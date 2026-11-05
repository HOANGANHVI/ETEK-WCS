using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";
    /// <summary>Seed on startup. Default false; enabled only in Development configuration.</summary>
    public bool Enabled { get; set; }
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
}
