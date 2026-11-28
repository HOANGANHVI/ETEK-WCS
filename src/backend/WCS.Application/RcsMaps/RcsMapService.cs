using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Rcs;

namespace WCS.Application.RcsMaps;

public interface IRcsMapRepository
{
    Task<RcsMap?> GetAsync(CancellationToken ct = default);
    void Add(RcsMap map);
}

public sealed record RcsMapInfo(string MapId, string MapName, string? MapQrCode, string? MapType, string? Row, string? Col, string? Width, string? Height, string FileName, string Sha256,
    int PointCount, int EdgeCount, DateTimeOffset ImportedAt, string? ImportedBy, double MmPerUnit, bool YAxisUp, double HeadingOffsetDeg, bool HeadingClockwise,
    IReadOnlyList<string> RcsMapCodes, DateTimeOffset? ConfirmedAt, string? ConfirmedBy);
public sealed record RcsMapView(RcsMapInfo Info, MapGeometry Geometry);
public sealed record RcsMapImportResult(RcsMapInfo Info, bool Replaced, int UnknownNeighbours);
public sealed record RcsMapSettings(double MmPerUnit, bool YAxisUp, double HeadingOffsetDeg, bool HeadingClockwise, IReadOnlyList<string>? RcsMapCodes, bool Confirmed);

/// <summary>CR-065: import and read the monitoring map. It reads and writes only its own record: nothing is sent to the RCS or to an AMR.</summary>
public sealed class RcsMapService(IUnitOfWork uow, IRcsMapRepository repo, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<RcsMapView?> GetAsync(CancellationToken ct = default) => uow.ExecuteAsync(async t =>
        await repo.GetAsync(t) is { } map ? new RcsMapView(Info(map), JsonSerializer.Deserialize<MapGeometry>(map.GeometryJson, Json)!) : null, ct);

    public async Task<RcsMapImportResult> ImportAsync(string? fileName, string? xml, string? user, CancellationToken ct = default)
    {
        var parsed = RcsMapParser.Parse(xml);   // checked completely before anything is stored
        var name = Path.GetFileName((fileName ?? "").Trim());
        if (name.Length is 0 or > 200) name = "map.xml";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml!))).ToLowerInvariant();
        return await uow.ExecuteAsync(async t =>
        {
            var map = await repo.GetAsync(t); var replaced = map is not null;
            if (map is null) { map = new RcsMap(clock.UtcNow); repo.Add(map); }
            map.Replace(parsed.MapId, parsed.MapName, parsed.MapQrCode, parsed.MapType, parsed.Row, parsed.Col, parsed.Width, parsed.Height, name, hash,
                parsed.Geometry.Points.Count, parsed.Geometry.Edges.Count, JsonSerializer.Serialize(parsed.Geometry, Json), xml!, user, clock.UtcNow);
            return new RcsMapImportResult(Info(map), replaced, parsed.UnknownNeighbours);
        }, ct);
    }

    public Task<RcsMapInfo> SaveSettingsAsync(RcsMapSettings s, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (!double.IsFinite(s.MmPerUnit) || s.MmPerUnit is <= 0 or > 1_000_000) throw AppException.Validation("RCS_MAP_SETTINGS", "Millimetres per map unit must be greater than 0 and at most 1000000.");
        if (!double.IsFinite(s.HeadingOffsetDeg) || s.HeadingOffsetDeg is < -360 or > 360) throw AppException.Validation("RCS_MAP_SETTINGS", "The heading offset must be between -360 and 360 degrees.");
        var codes = (s.RcsMapCodes ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var joined = string.Join(",", codes);
        if (codes.Count > 20 || joined.Length > 500 || codes.Any(c => c.Contains(','))) throw AppException.Validation("RCS_MAP_SETTINGS", "At most 20 map codes, without commas.");
        var map = await repo.GetAsync(t) ?? throw AppException.NotFound("RCS_MAP_NOT_FOUND", "No map has been imported.");
        map.Calibrate(s.MmPerUnit, s.YAxisUp, s.HeadingOffsetDeg, s.HeadingClockwise, joined, s.Confirmed, user, clock.UtcNow);
        return Info(map);
    }, ct);

    private static RcsMapInfo Info(RcsMap m) => new(m.MapId, m.MapName, m.MapQrCode, m.MapType, m.RowCount, m.ColCount, m.Width, m.Height, m.FileName, m.Sha256, m.PointCount, m.EdgeCount,
        m.ImportedAt, m.ImportedBy, m.MmPerUnit, m.YAxisUp, m.HeadingOffsetDeg, m.HeadingClockwise,
        m.RcsMapCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), m.ConfirmedAt, m.ConfirmedBy);
}
