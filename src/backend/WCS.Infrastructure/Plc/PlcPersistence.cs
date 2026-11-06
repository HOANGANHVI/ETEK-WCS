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
        b.Property(x => x.Encoding).HasMaxLength(40);
        b.Property(x => x.ApprovedBy).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne<PlcConnection>().WithMany().HasForeignKey(x => x.PlcId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PlcStation>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        // Identity of a signal: PLC_ID + Station_ID + Signal_Name.
        b.HasIndex(x => new { x.PlcId, x.StationId, x.SignalName }).IsUnique();
        // Address namespace is per PLC: the same confirmed address may not be given twice on one PLC (but may repeat on another PLC).
        b.HasIndex(x => new { x.PlcId, x.ActualAddress }).IsUnique().HasFilter("\"ActualAddress\" IS NOT NULL");
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
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.PlcId, x.OccurredAt });
    }
}

internal sealed class PlcConfigRepository(WcsDbContext db) : IPlcConfigRepository
{
    public async Task<IReadOnlyList<PlcConnection>> ListConnectionsAsync(CancellationToken ct) => await db.PlcConnections.OrderBy(x => x.Id).ToListAsync(ct);
    public Task<PlcConnection?> GetConnectionAsync(string plcId, CancellationToken ct) => db.PlcConnections.FirstOrDefaultAsync(x => x.Id == plcId, ct);
    public void AddConnection(PlcConnection c) => db.PlcConnections.Add(c);
    public async Task<IReadOnlyList<PlcStation>> ListStationsAsync(CancellationToken ct) => await db.PlcStations.OrderBy(x => x.PlcId).ThenBy(x => x.Order).ToListAsync(ct);
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

    public static PlcCatalog LoadEmbedded()
    {
        var asm = typeof(PlcCatalog).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("plc-io-catalog.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<PlcCatalog>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

public sealed class CatalogSource { public string File { get; set; } = ""; public string Sha256 { get; set; } = ""; }
public sealed class CatalogPlc { public string PlcId { get; set; } = ""; public string IpAddress { get; set; } = ""; public List<string> Stations { get; set; } = []; }
public sealed class CatalogStation { public string StationId { get; set; } = ""; public string PlcId { get; set; } = ""; public int Order { get; set; } }
public sealed class CatalogSignal
{
    public int Seq { get; set; } public string Group { get; set; } = ""; public string SignalName { get; set; } = ""; public string Direction { get; set; } = "";
    public string DataType { get; set; } = ""; public string TemplateAddressReference { get; set; } = ""; public string Description { get; set; } = "";
}
public sealed class CatalogHandshakeRow
{
    public string StationId { get; set; } = ""; public string SignalName { get; set; } = ""; public string? Owner { get; set; }
    public string? ApprovalStatus { get; set; } public string? Trigger { get; set; } public string? SetCondition { get; set; } public string? ResetCondition { get; set; }
}

public sealed record SeedResult(int PlcsAdded, int StationsAdded, int PointsAdded, int PlcsTotal, int StationsTotal, int PointsTotal);

/// <summary>
/// Loads the catalog into the database. Idempotent and additive: existing rows are never modified (so configuration entered by an Admin
/// is safe), missing rows are created, Actual_PLC_Address is never set.
/// </summary>
public sealed class PlcCatalogSeeder(WcsDbContext db, IClock clock, ILogger<PlcCatalogSeeder> logger)
{
    public async Task<SeedResult> SeedAsync(CancellationToken ct = default) => await SeedAsync(PlcCatalog.LoadEmbedded(), ct);

    public async Task<SeedResult> SeedAsync(PlcCatalog catalog, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var plcs = await db.PlcConnections.ToDictionaryAsync(x => x.Id, ct);
        var stations = await db.PlcStations.ToDictionaryAsync(x => x.Id, ct);
        var pointKeys = (await db.StationIoPoints.Select(x => new { x.PlcId, x.StationId, x.SignalName }).ToListAsync(ct))
            .Select(x => (x.PlcId, x.StationId, x.SignalName)).ToHashSet();

        int pa = 0, sa = 0, ia = 0;
        foreach (var p in catalog.Plcs.Where(p => !plcs.ContainsKey(p.PlcId)))
        { var e = new PlcConnection(p.PlcId, p.PlcId, p.IpAddress, now); db.PlcConnections.Add(e); plcs[p.PlcId] = e; pa++; }
        foreach (var s in catalog.Stations.Where(s => !stations.ContainsKey(s.StationId)))
        { var e = new PlcStation(s.StationId, s.PlcId, s.Order, now); db.PlcStations.Add(e); stations[s.StationId] = e; sa++; }
        // Parents first so the foreign keys exist when the points are inserted.
        if (pa + sa > 0) await db.SaveChangesAsync(ct);

        foreach (var st in catalog.Stations)
            foreach (var sig in catalog.Template)
            {
                if (pointKeys.Contains((st.PlcId, st.StationId, sig.SignalName))) continue;
                db.StationIoPoints.Add(new StationIoPoint(st.PlcId, st.StationId, sig.Seq, sig.Group, sig.SignalName,
                    Enum.Parse<PlcSignalDirection>(sig.Direction), Enum.Parse<PlcDataType>(sig.DataType), sig.TemplateAddressReference, sig.Description, now));
                ia++;
            }
        if (ia > 0) await db.SaveChangesAsync(ct);
        if (pa + sa + ia > 0) logger.LogInformation("PLC catalog seeded: +{Plcs} PLC, +{Stations} stations, +{Points} IO points", pa, sa, ia);
        return new SeedResult(pa, sa, ia, plcs.Count, stations.Count, await db.StationIoPoints.CountAsync(ct));
    }
}
