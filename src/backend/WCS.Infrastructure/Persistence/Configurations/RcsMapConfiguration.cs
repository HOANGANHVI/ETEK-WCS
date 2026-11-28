using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WCS.Application.RcsMaps;
using WCS.Domain.Rcs;

namespace WCS.Infrastructure.Persistence.Configurations;

/// <summary>CR-065: one new table; nothing existing changes.</summary>
internal sealed class RcsMapConfiguration : IEntityTypeConfiguration<RcsMap>
{
    public void Configure(EntityTypeBuilder<RcsMap> b)
    {
        b.ToTable("rcs_maps");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.MapId).HasMaxLength(64).IsRequired();
        b.Property(x => x.MapName).HasMaxLength(200).IsRequired();
        foreach (var p in new[] { nameof(RcsMap.MapQrCode), nameof(RcsMap.MapType), nameof(RcsMap.RowCount), nameof(RcsMap.ColCount), nameof(RcsMap.Width), nameof(RcsMap.Height), nameof(RcsMap.Sha256) })
            b.Property(p).HasMaxLength(64);
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.RcsMapCodes).HasMaxLength(500);
        b.Property(x => x.ImportedBy).HasMaxLength(100);
        b.Property(x => x.ConfirmedBy).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
    }
}

/// <summary>CR-066: one new table; nothing existing changes.</summary>
internal sealed class RcsMapPointConfiguration : IEntityTypeConfiguration<RcsMapPoint>
{
    public void Configure(EntityTypeBuilder<RcsMapPoint> b)
    {
        b.ToTable("rcs_map_points");
        b.HasKey(x => new { x.MapId, x.PointId });
        b.Property(x => x.MapId).HasMaxLength(64);
        b.Property(x => x.PointId).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
    }
}

internal sealed class RcsMapRepository(WcsDbContext db) : IRcsMapRepository
{
    public Task<List<RcsMapPoint>> ListPointsAsync(string mapId, CancellationToken ct) => db.Set<RcsMapPoint>().Where(x => x.MapId == mapId).ToListAsync(ct);
    public void Add(RcsMapPoint point) => db.Set<RcsMapPoint>().Add(point);
    public void Remove(RcsMapPoint point) => db.Set<RcsMapPoint>().Remove(point);

    public Task<RcsMap?> GetAsync(CancellationToken ct) => db.Set<RcsMap>().FirstOrDefaultAsync(x => x.Id == RcsMap.CurrentId, ct);
    public void Add(RcsMap map) => db.Set<RcsMap>().Add(map);
}
