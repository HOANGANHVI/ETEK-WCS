using WCS.Application.Abstractions;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;

namespace WCS.Application.ConfigTransfer;

/// <summary>Persistence of the import bookkeeping (kept out of the Application so it does not know EF).</summary>
public interface IConfigTransferStore
{
    Task<ConfigTransferSession?> GetAsync(Guid id, CancellationToken ct = default);
    void Add(ConfigTransferSession session);
    /// <summary>The newest applied schema migration of this database (written into the manifest; an older target is told in the Preview).</summary>
    Task<string> LatestMigrationAsync(CancellationToken ct = default);
}

/// <summary>
/// CR-026 (OQ-2): the whole-system condition for Apply. Nothing is cancelled or completed to make it pass: when it fails, Apply is refused with the reasons.
/// </summary>
public sealed class ActiveWorkGate(IUnitOfWork uow, ITransportTaskRepository tasks, IRoutingRepository routing)
{
    private const int Cap = 501;

    public Task<IReadOnlyList<Blocker>> CheckAsync(CancellationToken ct = default) => uow.ExecuteAsync<IReadOnlyList<Blocker>>(async t =>
    {
        var list = new List<Blocker>();
        var open = Enum.GetValues<TaskState>().Where(s => !TaskStateMachine.IsTerminal(s)).ToList();
        var activeTasks = await tasks.ListByStatesAsync(open, Cap, t);
        if (activeTasks.Count > 0)
            list.Add(new Blocker("ACTIVE_TASKS", "Transport Tasks that are not finished", activeTasks.Count, activeTasks.Take(5).Select(x => $"{x.Id.ToString("N")[..8]} {x.State}").ToList()));

        var orders = new List<string>(); var orderCount = 0;
        foreach (var s in Enum.GetValues<OrderState>().Where(x => x is not (OrderState.Completed or OrderState.Failed or OrderState.Cancelled)))
        {
            var (items, total) = await routing.QueryOrdersAsync(s, 0, 5, t);
            orderCount += total; orders.AddRange(items.Select(o => $"{o.Id.ToString("N")[..8]} {o.State}"));
        }
        if (orderCount > 0) list.Add(new Blocker("ACTIVE_ORDERS", "Transport Orders that are not finished", orderCount, orders.Take(5).ToList()));

        var tx = await routing.ListTransactionsAsync(true, null, null, Cap, t);
        if (tx.Count > 0) list.Add(new Blocker("ACTIVE_STATION_TRANSACTIONS", "Station Transactions (PLC handshake) in progress", tx.Count, tx.Take(5).Select(x => $"{x.PortId} {x.State}").ToList()));
        return list;
    }, ct);
}

/// <summary>Checks that the persistent folder is usable and writes / verifies the backup. Nothing lives in a temporary directory.</summary>
public sealed class ConfigBackupService(Microsoft.Extensions.Options.IOptions<ConfigTransferOptions> options)
{
    private ConfigTransferOptions O => options.Value;

    /// <summary>Null when the folder can be used; otherwise why not (shown to the Admin, Apply is refused).</summary>
    public string? Problem()
    {
        var path = O.BackupPath;
        if (string.IsNullOrWhiteSpace(path)) return "ConfigTransfer:BackupPath is not configured (NEEDS_CONFIGURATION).";
        if (!Path.IsPathRooted(path)) return "ConfigTransfer:BackupPath must be an absolute path.";
        if (IsUnder(path, Path.GetTempPath())) return "ConfigTransfer:BackupPath points into a temporary folder; use a persistent folder (a Docker volume).";
        if (!Directory.Exists(path)) return "ConfigTransfer:BackupPath does not exist.";
        try
        {
            var probe = Path.Combine(path, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            Directory.CreateDirectory(Path.Combine(path, "incoming"));
            Directory.CreateDirectory(Path.Combine(path, "backups"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "ConfigTransfer:BackupPath is not writable."; }
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;
            if (free < (long)O.MinFreeMb * 1024 * 1024) return $"Less than {O.MinFreeMb} MB are free in ConfigTransfer:BackupPath.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { /* free space cannot be read on this file system: the write probe above already passed */ }
        return null;
    }

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public string BackupFile(Guid id) => $"backups/{id:N}.zip";
    public string Full(string relative) => Path.Combine(O.BackupPath!, relative);

    public void SaveIncoming(string relative, byte[] data) { var f = Full(relative); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllBytes(f, data); }
    public byte[] ReadIncoming(string relative) => File.ReadAllBytes(Full(relative));

    /// <summary>Writes the backup package, reads it back and verifies it (checksums, group counts, every expected key). Throws when anything is wrong, so nothing is written to the database.</summary>
    public string WriteAndVerify(Guid importId, PackageManifest manifest, IReadOnlyList<(string Group, string File, IReadOnlyList<ConfigRecord> Records)> groups, IReadOnlyDictionary<string, string> groupFiles)
    {
        var bytes = ConfigPackage.Write(manifest, groups);
        var relative = BackupFile(importId);
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        var back = ConfigPackage.Read(File.ReadAllBytes(full), groupFiles, new ConfigTransferOptions { MaxPackageMb = 1024, MaxUncompressedMb = 2048 }, (_, _) => []);
        foreach (var (group, _, records) in groups)
        {
            if (!back.Groups.TryGetValue(group, out var read) || read.Count != records.Count || records.Any(r => read.All(x => x.Key != r.Key)))
                throw new InvalidOperationException($"The backup of {group} failed its integrity check.");
        }
        return relative;
    }
}
