using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Plc;

internal sealed class PlcConnectionConfiguration : IEntityTypeConfiguration<PlcConnection>
{
    public void Configure(EntityTypeBuilder<PlcConnection> b)
    {
        b.ToTable("plc_connections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(64).IsRequired();
        b.Property(x => x.Protocol).HasMaxLength(60);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class PlcStationConfiguration : IEntityTypeConfiguration<PlcStation>
{
    public void Configure(EntityTypeBuilder<PlcStation> b)
    {
        b.ToTable("plc_stations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(20);
        b.Property(x => x.PlcId).HasMaxLength(20).IsRequired();
        b.Property(x => x.LocationCode).HasMaxLength(50);
        b.Property(x => x.Role).HasMaxLength(30).IsRequired();
        b.Property(x => x.RetiredAt);
        b.HasOne<PlcConnection>().WithMany().HasForeignKey(x => x.PlcId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PlcId, x.Order }).IsUnique();
    }
}

internal sealed class StationIoPointConfiguration : IEntityTypeConfiguration<StationIoPoint>
{
    public void Configure(EntityTypeBuilder<StationIoPoint> b)
    {
        b.ToTable("station_io_points");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PlcId).HasMaxLength(20).IsRequired();
        b.Property(x => x.StationId).HasMaxLength(20).IsRequired();
        b.Property(x => x.Group).HasMaxLength(60).IsRequired();
        b.Property(x => x.SignalName).HasMaxLength(60).IsRequired();
        b.Property(x => x.TemplateAddressReference).HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.ActualAddress).HasMaxLength(20);
        b.Property(x => x.AddressOrigin).HasConversion<string>().HasMaxLength(20).HasDefaultValue(IoAddressOrigin.None);
        b.Property(x => x.SharedWith).HasMaxLength(200);
        b.Property(x => x.Encoding).HasMaxLength(40);
        b.Property(x => x.ApprovedBy).HasMaxLength(100);
        b.Property(x => x.WriteApproval).HasConversion<string>().HasMaxLength(20).HasDefaultValue(WriteApprovalStatus.NotApproved);
        b.Property(x => x.WriteDecidedBy).HasMaxLength(100);
        b.Property(x => x.WriteDecisionNote).HasMaxLength(200);
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne<PlcConnection>().WithMany().HasForeignKey(x => x.PlcId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PlcStation>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        // Identity of a signal: PLC_ID + Station_ID + Signal_Name.
        b.HasIndex(x => new { x.PlcId, x.StationId, x.SignalName }).IsUnique();
        // Address namespace is per PLC: the same confirmed address may not be given twice on one PLC (but may repeat on another PLC).
        // CR-048: except for the signals the IO map puts in one register; for those the same signal still may not be given one address twice.
        b.HasIndex(x => new { x.PlcId, x.ActualAddress }).IsUnique().HasFilter("\"ActualAddress\" IS NOT NULL AND \"SharedWith\" IS NULL");
        b.HasIndex(x => new { x.PlcId, x.ActualAddress, x.SignalName }).IsUnique().HasFilter("\"ActualAddress\" IS NOT NULL AND \"SharedWith\" IS NOT NULL");
    }
}

internal sealed class RetiredIoPointConfiguration : IEntityTypeConfiguration<RetiredIoPoint>
{
    public void Configure(EntityTypeBuilder<RetiredIoPoint> b)
    {
        b.ToTable("retired_io_points");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CatalogSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.PlcId).HasMaxLength(20).IsRequired();
        b.Property(x => x.StationId).HasMaxLength(20).IsRequired();
        b.Property(x => x.SignalName).HasMaxLength(60).IsRequired();
        b.Property(x => x.ActualAddress).HasMaxLength(20);
        b.Property(x => x.SnapshotJson).IsRequired();
        b.HasIndex(x => new { x.PlcId, x.StationId });
    }
}

internal sealed class PlcCommLogConfiguration : IEntityTypeConfiguration<PlcCommLogEntry>
{
    public void Configure(EntityTypeBuilder<PlcCommLogEntry> b)
    {
        b.ToTable("plc_comm_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PlcId).HasMaxLength(20);
        b.Property(x => x.StationId).HasMaxLength(20);
        b.Property(x => x.Event).HasMaxLength(60).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DiagCode).HasMaxLength(60);
        b.Property(x => x.Stage).HasMaxLength(20);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.PlcId, x.OccurredAt });
        b.HasIndex(x => new { x.PlcId, x.Stage, x.OccurredAt });   // diagnostics: "PLC-01, stage TCP, last hour"
    }
}

internal sealed class PlcExchangeLogConfiguration : IEntityTypeConfiguration<PlcExchangeLogEntry>
{
    public void Configure(EntityTypeBuilder<PlcExchangeLogEntry> b)
    {
        b.ToTable("plc_exchange_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PlcId).HasMaxLength(20).IsRequired();
        b.Property(x => x.Operation).HasMaxLength(10).IsRequired();
        b.Property(x => x.Command).HasMaxLength(12).IsRequired();
        b.Property(x => x.Device).HasMaxLength(10).IsRequired();
        b.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        b.Property(x => x.Error).HasMaxLength(300);
        b.Property(x => x.RequestHex).HasMaxLength(PlcExchangeLogEntry.MaxHex);
        b.Property(x => x.ResponseHex).HasMaxLength(PlcExchangeLogEntry.MaxHex);
        b.HasIndex(x => new { x.PlcId, x.OccurredAt });
    }
}

internal sealed class PlcConfigRepository(WcsDbContext db) : IPlcConfigRepository
{
    public async Task<IReadOnlyList<PlcConnection>> ListConnectionsAsync(CancellationToken ct) => await db.PlcConnections.OrderBy(x => x.Id).ToListAsync(ct);
    public Task<PlcConnection?> GetConnectionAsync(string plcId, CancellationToken ct) => db.PlcConnections.FirstOrDefaultAsync(x => x.Id == plcId, ct);
    public void AddConnection(PlcConnection c) => db.PlcConnections.Add(c);
    public async Task<IReadOnlyList<PlcStation>> ListStationsAsync(CancellationToken ct) => await db.PlcStations.Where(x => x.RetiredAt == null).OrderBy(x => x.PlcId).ThenBy(x => x.Order).ToListAsync(ct);
    public Task<PlcStation?> GetStationAsync(string stationId, CancellationToken ct) => db.PlcStations.FirstOrDefaultAsync(x => x.Id == stationId, ct);
    public void AddStation(PlcStation s) => db.PlcStations.Add(s);

    public async Task<IReadOnlyList<StationIoPoint>> ListPointsAsync(string? plcId, string? stationId, CancellationToken ct)
    {
        var q = db.StationIoPoints.AsQueryable();
        if (plcId is not null) q = q.Where(x => x.PlcId == plcId);
        if (stationId is not null) q = q.Where(x => x.StationId == stationId);
        return await q.OrderBy(x => x.PlcId).ThenBy(x => x.StationId).ThenBy(x => x.Seq).ToListAsync(ct);
    }

    public Task<StationIoPoint?> GetPointAsync(Guid id, CancellationToken ct) => db.StationIoPoints.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<StationIoPoint?> FindPointAsync(string plcId, string stationId, string signal, CancellationToken ct) =>
        db.StationIoPoints.FirstOrDefaultAsync(x => x.PlcId == plcId && x.StationId == stationId && x.SignalName == signal, ct);
    public void AddPoint(StationIoPoint p) => db.StationIoPoints.Add(p);
    public void AddLog(PlcCommLogEntry e) => db.PlcCommLog.Add(e);

    public async Task<IReadOnlyList<PlcCommLogEntry>> QueryLogsAsync(string? plcId, string? stationId, PlcLogLevel? minLevel, int skip, int take, CancellationToken ct)
    {
        var q = db.PlcCommLog.AsNoTracking().AsQueryable();
        if (plcId is not null) q = q.Where(x => x.PlcId == plcId);
        if (stationId is not null) q = q.Where(x => x.StationId == stationId);
        if (minLevel is { } lv) q = q.Where(x => x.Level >= lv);
        return await q.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Skip(Math.Max(0, skip)).Take(take).ToListAsync(ct);
    }

    public Task<int> PurgeLogsAsync(DateTimeOffset olderThan, CancellationToken ct) => db.PlcCommLog.Where(x => x.OccurredAt < olderThan).ExecuteDeleteAsync(ct);
}

// ------------------------------------------------------------------------------------------------------------------------------------
// IO catalog (derived from the controlled workbook by scripts/generate-plc-io-catalog.mjs) and its idempotent seeder.
// ------------------------------------------------------------------------------------------------------------------------------------

public sealed class PlcCatalog
{
    public int SchemaVersion { get; set; }
    public CatalogSource Source { get; set; } = new();
    public List<CatalogPlc> Plcs { get; set; } = [];
    public List<CatalogStation> Stations { get; set; } = [];
    public List<CatalogSignal> Template { get; set; } = [];
    public List<CatalogHandshakeRow> Handshake { get; set; } = [];

    private static PlcCatalog? _testOverride;

    /// <summary>The catalog in use: the embedded one (the standard IO Map, CR-046).</summary>
    public static PlcCatalog Current => _testOverride ?? LoadEmbedded();

    /// <summary>Test assembly only: lets the tests of the PLC mechanics run on a fixture catalog. No product code and no setting reaches it.</summary>
    internal static void UseForTests(PlcCatalog? catalog) => _testOverride = catalog;

    public List<CatalogPlcSignal> PlcSignals { get; set; } = [];
    /// <summary>CR-047: the IO Matrix sheet as it is (every column and row, merged cells) and the sequence sheet, for display. Null in a catalog without them.</summary>
    public CatalogSheet? Sheet { get; set; }
    public CatalogSequence? Sequence { get; set; }

    public static PlcCatalog LoadEmbedded()
    {
        var asm = typeof(PlcCatalog).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("plc-io-catalog.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<PlcCatalog>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

public sealed class CatalogSource { public string File { get; set; } = ""; public string Sha256 { get; set; } = ""; }
public sealed class CatalogPlc { public string PlcId { get; set; } = ""; public string? DocumentLabel { get; set; } public string IpAddress { get; set; } = ""; public List<string> Stations { get; set; } = []; }
public sealed class CatalogStation
{
    public string StationId { get; set; } = ""; public string PlcId { get; set; } = ""; public int Order { get; set; }
    /// <summary>Signal name -> address written in the IO map for THIS station: the default address of the point (CR-048).</summary>
    public Dictionary<string, string> Addresses { get; set; } = [];
}
public sealed class CatalogSheet { public string Name { get; set; } = ""; public string Title { get; set; } = ""; public List<string> Columns { get; set; } = []; public List<CatalogSheetRow> Rows { get; set; } = []; }
/// <summary><see cref="Cells"/>: the eleven columns A..K as written (a merged cell repeats its value). <see cref="Merged"/>: column letter -> first cell of the merged range the row belongs to.</summary>
public sealed class CatalogSheetRow { public int Row { get; set; } public List<string> Cells { get; set; } = []; public Dictionary<string, string> Merged { get; set; } = []; }
public sealed class CatalogSequence { public string Name { get; set; } = ""; public List<CatalogSequenceRow> Rows { get; set; } = []; }
public sealed class CatalogSequenceRow { public int Row { get; set; } public Dictionary<string, string> Cells { get; set; } = []; public string? Arrow { get; set; } }
/// <summary>A row of the IO map that belongs to a PLC, not to a station (heartbeat, spare). Shown for reference; not implemented.</summary>
public sealed class CatalogPlcSignal
{
    public string PlcId { get; set; } = ""; public string SignalName { get; set; } = ""; public string? Direction { get; set; } public string DataType { get; set; } = "";
    public string Address { get; set; } = ""; public string Values { get; set; } = ""; public string Meaning { get; set; } = ""; public bool Spare { get; set; }
}
public sealed class CatalogSignal
{
    public int Seq { get; set; } public string Group { get; set; } = ""; public string SignalName { get; set; } = ""; public string Direction { get; set; } = "";
    public string DataType { get; set; } = ""; public string TemplateAddressReference { get; set; } = ""; public string Description { get; set; } = "";
    public string? Values { get; set; } public List<string> SharedRegisterWith { get; set; } = [];
}
public sealed class CatalogHandshakeRow
{
    public string StationId { get; set; } = ""; public string SignalName { get; set; } = ""; public string? Owner { get; set; }
    public string? ApprovalStatus { get; set; } public string? Trigger { get; set; } public string? SetCondition { get; set; } public string? ResetCondition { get; set; }
}

public sealed record SeedResult(int PlcsAdded, int StationsAdded, int PointsAdded, int PlcsTotal, int StationsTotal, int PointsTotal,
    int DefaultsAssigned = 0, int DefaultsBlocked = 0, int PointsRetired = 0, int StationsRetired = 0);

/// <summary>
/// Brings the database in line with the catalog (CR-048). Idempotent:
///  - missing PLC, station and point rows are created;
///  - rows of signals that are not in the catalog are moved to <c>retired_io_points</c>, and a station that only had such rows is retired (kept, hidden, location link released);
///  - every point of the catalog that was never configured gets the address written in the IO map for it; a default follows the catalog;
///  - an address an Admin entered or removed is never changed.
/// An address never makes a point approved or writable.
/// </summary>
public sealed class PlcCatalogSeeder(WcsDbContext db, IClock clock, ILogger<PlcCatalogSeeder> logger, PlcCatalog? current = null)
{
    public async Task<SeedResult> SeedAsync(CancellationToken ct = default) => await SeedAsync(current ?? PlcCatalog.Current, ct);

    public async Task<SeedResult> SeedAsync(PlcCatalog catalog, CancellationToken ct = default)
    {
        // one transaction: an upgrade that fails half way leaves the database as it was
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var result = await ApplyAsync(catalog, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }

    private async Task<SeedResult> ApplyAsync(PlcCatalog catalog, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var plcs = await db.PlcConnections.ToDictionaryAsync(x => x.Id, ct);
        var stations = await db.PlcStations.ToDictionaryAsync(x => x.Id, ct);
        var retired = await RetireAsync(catalog, stations, now, ct);
        var pointKeys = (await db.StationIoPoints.Select(x => new { x.PlcId, x.StationId, x.SignalName }).ToListAsync(ct))
            .Select(x => (x.PlcId, x.StationId, x.SignalName)).ToHashSet();

        int pa = 0, sa = 0, ia = 0;
        foreach (var p in catalog.Plcs.Where(p => !plcs.ContainsKey(p.PlcId)))
        { var e = new PlcConnection(p.PlcId, p.PlcId, p.IpAddress, now); db.PlcConnections.Add(e); plcs[p.PlcId] = e; pa++; }
        foreach (var s in catalog.Stations.Where(s => !stations.ContainsKey(s.StationId)))
        {
            // CR-046: a database of an earlier catalog may already use this position (e.g. X6 holds the place X4 has in the new IO map): rows are never moved, the new station takes the next free one
            var used = stations.Values.Where(x => x.PlcId == s.PlcId).Select(x => x.Order).ToHashSet();
            var order = used.Contains(s.Order) ? used.Max() + 1 : s.Order;
            var e = new PlcStation(s.StationId, s.PlcId, order, now); db.PlcStations.Add(e); stations[s.StationId] = e; sa++;
        }
        // Parents first so the foreign keys exist when the points are inserted.
        foreach (var s in catalog.Stations)
            if (stations[s.StationId] is { RetiredAt: not null } back) back.Restore(now);
        await db.SaveChangesAsync(ct);

        foreach (var st in catalog.Stations)
            foreach (var sig in catalog.Template)
            {
                if (pointKeys.Contains((st.PlcId, st.StationId, sig.SignalName))) continue;
                db.StationIoPoints.Add(new StationIoPoint(st.PlcId, st.StationId, sig.Seq, sig.Group, sig.SignalName,
                    Enum.Parse<PlcSignalDirection>(sig.Direction), Enum.Parse<PlcDataType>(sig.DataType), st.Addresses.GetValueOrDefault(sig.SignalName) ?? sig.TemplateAddressReference, sig.Description, now));
                ia++;
            }
        if (ia > 0) await db.SaveChangesAsync(ct);
        var (assigned, blocked) = await AssignDefaultsAsync(catalog, now, ct);
        if (pa + sa + ia + retired.Points + retired.Stations + assigned + blocked > 0)
            logger.LogInformation("PLC catalog seeded: +{Plcs} PLC, +{Stations} stations, +{Points} IO points, {Assigned} default addresses assigned, {Blocked} blocked, {RetiredPoints} old points and {RetiredStations} old stations retired",
                pa, sa, ia, assigned, blocked, retired.Points, retired.Stations);
        return new SeedResult(pa, sa, ia, plcs.Count, stations.Count, await db.StationIoPoints.CountAsync(ct), assigned, blocked, retired.Points, retired.Stations);
    }

    private async Task<(int Points, int Stations)> RetireAsync(PlcCatalog catalog, Dictionary<string, PlcStation> stations, DateTimeOffset now, CancellationToken ct)
    {
        var names = catalog.Template.Select(t => t.SignalName).ToList();
        if (names.Count == 0) return (0, 0);   // a catalog without signals retires nothing
        var old = await db.StationIoPoints.Where(p => !names.Contains(p.SignalName)).ToListAsync(ct);
        if (old.Count == 0) return (0, 0);
        var oldIds = old.Select(p => p.Id).ToList();
        var oldStations = old.Select(p => p.StationId).Distinct().ToList();
        var stillUsed = (await db.StationIoPoints.Where(p => oldStations.Contains(p.StationId) && !oldIds.Contains(p.Id)).Select(p => p.StationId).Distinct().ToListAsync(ct)).ToHashSet();
        var inCatalog = catalog.Stations.Select(s => s.StationId).ToHashSet();
        var retiredStations = 0;
        foreach (var p in old)
            db.Set<RetiredIoPoint>().Add(new RetiredIoPoint(p.Id, now, catalog.Source.Sha256, p.PlcId, p.StationId, p.SignalName, p.ActualAddress, JsonSerializer.Serialize(new
            {
                p.Id, p.PlcId, p.StationId, p.Seq, p.SignalName, p.UpdatedAt, AddressOrigin = p.AddressOrigin.ToString(), p.SharedWith,   // every column of the row
                p.Group, Direction = p.Direction.ToString(), DataType = p.DataType.ToString(), p.Description, p.TemplateAddressReference, p.ActualAddress, p.WordLength, p.Encoding,
                Status = p.Status.ToString(), p.ApprovedBy, p.ApprovedAt, p.WriteAllowed, WriteApproval = p.WriteApproval.ToString(), p.WriteDecidedBy, p.WriteDecidedAt, p.WriteDecisionNote,
                StationLocation = stations.GetValueOrDefault(p.StationId)?.LocationCode,
            })));
        foreach (var id in oldStations.Where(id => !stillUsed.Contains(id) && !inCatalog.Contains(id)))
            if (stations.GetValueOrDefault(id) is { RetiredAt: null } s)
            {
                if (s.LocationCode is not null) logger.LogWarning("PLC station {Station} left the IO map: its link to location {Location} is released", s.Id, s.LocationCode);
                s.Retire(now); retiredStations++;
            }
        db.StationIoPoints.RemoveRange(old);
        await db.SaveChangesAsync(ct);   // the addresses of the old rows are free before the defaults are assigned
        return (old.Count, retiredStations);
    }

    private async Task<(int Assigned, int Blocked)> AssignDefaultsAsync(PlcCatalog catalog, DateTimeOffset now, CancellationToken ct)
    {
        var shared = catalog.Template.ToDictionary(t => t.SignalName, t => t.SharedRegisterWith.Count == 0 ? null : string.Join(',', t.SharedRegisterWith));
        var defaults = catalog.Stations.SelectMany(s => s.Addresses.Select(a => (Key: (s.PlcId, s.StationId, a.Key), Address: DeviceAddress.Normalize(a.Value))))
            .ToDictionary(x => x.Key, x => x.Address);
        var points = await db.StationIoPoints.OrderBy(p => p.PlcId).ThenBy(p => p.StationId).ThenBy(p => p.Seq).ToListAsync(ct);
        foreach (var p in points) if (shared.TryGetValue(p.SignalName, out var with)) p.ShareRegisterWith(with);
        // a default that changed in the catalog is released first, so that addresses may move between points
        var todo = new List<(StationIoPoint Point, string Address)>();
        foreach (var p in points)
        {
            if (!defaults.TryGetValue((p.PlcId, p.StationId, p.SignalName), out var address)) continue;
            if (p.AddressOrigin == IoAddressOrigin.Custom || (p.AddressOrigin == IoAddressOrigin.None && p.ActualAddress is not null)) continue;
            if (p.AddressOrigin == IoAddressOrigin.WorkbookDefault && p.ActualAddress == address) continue;
            todo.Add((p, address));
        }
        int assigned = 0, blocked = 0;
        foreach (var (p, address) in todo)
        {
            var holder = points.FirstOrDefault(x => x.Id != p.Id && x.PlcId == p.PlcId && x.ActualAddress == address && !todo.Any(t => t.Point.Id == x.Id && t.Address != address)
                && !(x.StationId == p.StationId && AddressValidator.MayShare(x.SignalName, x.SharedWith, p.SignalName, p.SharedWith)));
            if (holder is not null)
            {
                blocked++;
                logger.LogWarning("Default address {Address} of {Plc} {Station}/{Signal} is not assigned: {HolderStation}/{HolderSignal} holds it", address, p.PlcId, p.StationId, p.SignalName, holder.StationId, holder.SignalName);
                continue;
            }
            p.ApplyDefaultAddress(address, now); assigned++;
        }
        await db.SaveChangesAsync(ct);
        return (assigned, blocked);
    }
}
