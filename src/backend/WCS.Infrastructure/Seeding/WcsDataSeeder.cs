using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using System.Text.Json;
using WCS.Domain.Locations;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";
    /// <summary>Seed on startup. Default false; enabled only in Development configuration.</summary>
    public bool Enabled { get; set; }
    /// <summary>Insert-only seed of the approved default Phase 1 locations (<c>default-locations.json</c>). On by default so a new deployment is usable; never updates or deletes a row. Set false to switch it off.</summary>
    public bool DefaultLocations { get; set; } = true;
    /// <summary>Optional RCS robot code per AGV code (data, never hard-coded). NEEDS_CONFIRMATION.</summary>
    public Dictionary<string, string> AgvRobotCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SeedLocation> Locations { get; set; } = [];
}

public sealed class SeedLocation
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Buffer";
    /// <summary>NEEDS_CONFIRMATION: real capacities are not in the specification.</summary>
    public int Capacity { get; set; }
    public string AllowedRackKind { get; set; } = "Any";
    public bool RequiresPlcPermission { get; set; }
}

/// <summary>
/// Additive, idempotent seed. AGV1 (full racks) and AGV2 (empty racks) are defined by business-rules.md 1.2, so they are always
/// ensured. Locations come ONLY from configuration (the specification gives no capacities) and never overwrite existing rows.
/// </summary>
public sealed class WcsDataSeeder(WcsDbContext db, IClock clock, IOptions<SeedOptions> options, ILogger<WcsDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        foreach (var (code, role) in new[] { ("AGV1", AgvRole.FullRack), ("AGV2", AgvRole.EmptyRack) })
        {
            if (await db.Agvs.AnyAsync(a => a.Code == code, ct)) continue;
            db.Agvs.Add(new Agv(code, role, o.AgvRobotCodes.GetValueOrDefault(code)));
            logger.LogInformation("Seeded {Code}", code);
        }
        if (o.DefaultLocations) await SeedDefaultLocationsAsync(null, ct);
        if (!o.Enabled) { await db.SaveChangesAsync(ct); return; }

        foreach (var l in o.Locations)
        {
            if (await db.Locations.AnyAsync(x => x.Code == l.Code, ct)) continue;
            db.Locations.Add(new Location(l.Code, l.Name, Enum.Parse<LocationType>(l.Type, true), l.Capacity,
                Enum.Parse<AllowedRackKind>(l.AllowedRackKind, true), l.RequiresPlcPermission, clock.UtcNow));
            logger.LogInformation("Seeded location {Code}", l.Code);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Audit action that records "catalog version N has been applied to this database". It is written in the SAME transaction as the locations of that version.</summary>
    public const string CatalogAppliedAction = "SEED_LOCATION_CATALOG_APPLIED";
    public const string CatalogTargetType = "DefaultLocationCatalog";
    private const long SeedLockKey = 7370031;   // pg_advisory_xact_lock: instances starting together take turns

    /// <summary>
    /// Default Phase 1 locations from the embedded catalog, applied ONCE per catalog version. For every version 1..N that has no marker yet, the rows of that version (SinceVersion)
    /// whose code does not exist (ignoring case and spaces) are INSERTED; nothing is updated or deleted, and a location an Admin deleted later is NOT created again (its version is
    /// already marked). The locations, their audit rows and the version marker are saved in ONE transaction under an advisory lock: a failure rolls everything back and leaves no marker;
    /// instances that start together take turns and the second one finds the marker. Racks, reservations, tasks and PLC data are never touched.
    /// </summary>
    public async Task<int> SeedDefaultLocationsAsync(DefaultLocationCatalog? catalogOverride = null, CancellationToken ct = default)
    {
        var catalog = catalogOverride ?? DefaultLocationCatalog.Load();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7370031)", ct);
        var applied = (await db.AuditLog.AsNoTracking().Where(a => a.Action == CatalogAppliedAction && a.TargetType == CatalogTargetType).Select(a => a.TargetId).ToListAsync(ct))
            .Where(x => x is not null).Select(x => x!).ToHashSet();
        var existing = (await db.Locations.AsNoTracking().Select(x => x.Code).ToListAsync(ct)).Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
        var created = 0;
        for (var v = 1; v <= catalog.Version; v++)
        {
            if (applied.Contains(v.ToString())) continue;
            foreach (var l in catalog.Locations.Where(x => x.SinceVersion == v))
            {
                if (existing.Contains(l.Code.Trim().ToUpperInvariant())) continue;
                db.Locations.Add(new Location(l.Code, l.Name, Enum.Parse<LocationType>(l.Type, true), l.Capacity, Enum.Parse<AllowedRackKind>(l.AllowedRackKind, true), l.RequiresPlcPermission, clock.UtcNow));
                db.AuditLog.Add(new AuditEntry(clock.UtcNow, "SEED", "System", "SEED_LOCATION_CREATE", "Location", l.Code, AuditResult.Success,
                    $"Default location {l.Code} ({l.Type}, capacity {l.Capacity}, {l.AllowedRackKind}) created by default location catalog v{v}", null, null, null));
                logger.LogInformation("Default location seeded: {Code} ({Type}, capacity {Capacity}, {Kind}) catalog v{Version}", l.Code, l.Type, l.Capacity, l.AllowedRackKind, v);
                existing.Add(l.Code.Trim().ToUpperInvariant()); created++;
            }
            db.AuditLog.Add(new AuditEntry(clock.UtcNow, "SEED", "System", CatalogAppliedAction, CatalogTargetType, v.ToString(), AuditResult.Success,
                $"Default location catalog version {v} applied", null, null, null));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return created;
    }
}

/// <summary>The approved default location catalog (embedded <c>default-locations.json</c>): data, not logic.</summary>
public sealed class DefaultLocationCatalog
{
    /// <summary>Catalog version. Raise it (and give new rows the same SinceVersion) to add locations to deployments that already ran an older version.</summary>
    public int Version { get; set; }
    public List<DefaultLocationRow> Locations { get; set; } = [];

    public static DefaultLocationCatalog Load()
    {
        var asm = typeof(DefaultLocationCatalog).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("default-locations.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<DefaultLocationCatalog>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}

public sealed class DefaultLocationRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int Capacity { get; set; }
    public string AllowedRackKind { get; set; } = "Any";
    public bool RequiresPlcPermission { get; set; }
    /// <summary>The catalog version that introduced this row.</summary>
    public int SinceVersion { get; set; } = 1;
    public string Source { get; set; } = "";
}
