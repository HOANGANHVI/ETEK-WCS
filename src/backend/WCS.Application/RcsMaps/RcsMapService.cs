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
    // CR-066: names and location links of points
    Task<List<RcsMapPoint>> ListPointsAsync(string mapId, CancellationToken ct = default);
    void Add(RcsMapPoint point);
    void Remove(RcsMapPoint point);
}

public sealed record RcsMapInfo(string MapId, string MapName, string? MapQrCode, string? MapType, string? Row, string? Col, string? Width, string? Height, string FileName, string Sha256,
    int PointCount, int EdgeCount, DateTimeOffset ImportedAt, string? ImportedBy, double MmPerUnit, bool YAxisUp, double HeadingOffsetDeg, bool HeadingClockwise,
    IReadOnlyList<string> RcsMapCodes, DateTimeOffset? ConfirmedAt, string? ConfirmedBy);
public sealed record RcsMapView(RcsMapInfo Info, MapGeometry Geometry, IReadOnlyList<RcsMapPointView> Points);
/// <param name="Current">True = the point exists in the map shown, at the place it had when it was named. False = kept for review, shown on no point.</param>
public sealed record RcsMapPointView(long PointId, double X, double Y, string? Name, string? LocationCode, bool LocationRemoved, bool Current, DateTimeOffset UpdatedAt, string? UpdatedBy);
public sealed record RcsMapPointInput(string? MapSha256, string? Name, string? LocationCode);
public sealed record RcsMapImportResult(RcsMapInfo Info, bool Replaced, int UnknownNeighbours);
/// <summary>CR-067: what an import of these files would store. Nothing is saved.</summary>
public sealed record RcsMapPreview(string MapId, string MapName, string? MapQrCode, int PointCount, int EdgeCount, int UnknownNeighbours, MapDataSet? MapData, IReadOnlyList<long> PointsWithoutRow,
    string? CurrentMapId, int NamedPointsKept, int NamedPointsForReview);
/// <param name="MapDataBase64">The Excel file, base64.</param>
public sealed record RcsMapFiles(string? FileName, string? Content, string? MapDataFileName = null, string? MapDataBase64 = null);
public sealed record RcsMapSettings(double MmPerUnit, bool YAxisUp, double HeadingOffsetDeg, bool HeadingClockwise, IReadOnlyList<string>? RcsMapCodes, bool Confirmed);

/// <summary>CR-065: import and read the monitoring map. It reads and writes only its own record: nothing is sent to the RCS or to an AMR.</summary>
public sealed class RcsMapService(IUnitOfWork uow, IRcsMapRepository repo, ILocationRepository locations, IClock clock, IMapDataReader mapDataReader)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A point moved by more than this (in map units) after a new import is not taken for the same point.</summary>
    public const double SamePlaceTolerance = 0.001;

    public Task<RcsMapView?> GetAsync(CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (await repo.GetAsync(t) is not { } map) return null;
        var geometry = JsonSerializer.Deserialize<MapGeometry>(map.GeometryJson, Json)!;
        return new RcsMapView(Info(map), geometry, await PointViewsAsync(map.MapId, geometry, t));
    }, ct);

    // ------------------------------------------------------------------ CR-066: names and location links

    /// <summary>
    /// Names a point and / or links it to a WCS location; empty name and location remove the record. One location stands on at most one point of the map.
    /// The caller says which imported file it was looking at: after another import nothing is saved against the wrong map.
    /// </summary>
    public async Task<IReadOnlyList<RcsMapPointView>> SetPointAsync(long pointId, RcsMapPointInput input, string? user, CancellationToken ct = default)
    {
        await uow.ExecuteAsync(async t =>
        {
        var map = await repo.GetAsync(t) ?? throw AppException.NotFound("RCS_MAP_NOT_FOUND", "No map has been imported.");
        if (!string.Equals(map.Sha256, input.MapSha256, StringComparison.OrdinalIgnoreCase)) throw AppException.Conflict("RCS_MAP_CHANGED", "Another map file was imported meanwhile: reload the page.");
        var geometry = JsonSerializer.Deserialize<MapGeometry>(map.GeometryJson, Json)!;
        var point = geometry.Points.FirstOrDefault(p => p.Id == pointId) ?? throw AppException.NotFound("RCS_MAP_POINT_NOT_FOUND", $"The map has no point {pointId}.");
        var name = input.Name?.Trim() is { Length: > 0 } n ? n : null;
        if (name is { Length: > 100 }) throw AppException.Validation("RCS_MAP_POINT_NAME", "The name has at most 100 characters.");
        var all = await repo.ListPointsAsync(map.MapId, t);
        var record = all.FirstOrDefault(p => p.PointId == pointId);
        Guid? locationId = null;
        if (input.LocationCode?.Trim() is { Length: > 0 } code)
        {
            // the link the point already has may stay, also when its location was removed meanwhile
            var location = await locations.GetByCodeAsync(code, t);
            if (location is null) throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location {code} not found.");
            var taken = all.FirstOrDefault(p => p.PointId != pointId && p.LocationId == location.Id && IsCurrent(p, geometry));
            if (taken is not null) throw AppException.Conflict("RCS_MAP_LOCATION_LINKED", $"{location.Code} is already linked to point {taken.PointId}: unlink it there first.");
            locationId = location.Id;
        }
        if (name is null && locationId is null) { if (record is not null) repo.Remove(record); }
        else
        {
            if (record is null) { record = new RcsMapPoint(map.MapId, pointId); repo.Add(record); }
            record.Set(point.X, point.Y, name, locationId, user, clock.UtcNow);
        }
            return true;
        }, ct);
        return (await GetAsync(ct))!.Points;
    }

    /// <summary>Removes the name and link of a point id, also one that no longer matches the map.</summary>
    public async Task<IReadOnlyList<RcsMapPointView>> RemovePointAsync(long pointId, CancellationToken ct = default)
    {
        await uow.ExecuteAsync(async t =>
        {
        var map = await repo.GetAsync(t) ?? throw AppException.NotFound("RCS_MAP_NOT_FOUND", "No map has been imported.");
        var record = (await repo.ListPointsAsync(map.MapId, t)).FirstOrDefault(p => p.PointId == pointId) ?? throw AppException.NotFound("RCS_MAP_POINT_NOT_FOUND", $"Point {pointId} has no name or link.");
        repo.Remove(record);
            return true;
        }, ct);
        return (await GetAsync(ct))!.Points;
    }

    private static bool IsCurrent(RcsMapPoint record, MapGeometry geometry) => geometry.Points.Any(p => p.Id == record.PointId
        && Math.Abs(p.X - record.X) <= SamePlaceTolerance && Math.Abs(p.Y - record.Y) <= SamePlaceTolerance);

    private async Task<IReadOnlyList<RcsMapPointView>> PointViewsAsync(string mapId, MapGeometry geometry, CancellationToken ct)
    {
        var records = await repo.ListPointsAsync(mapId, ct);
        var list = new List<RcsMapPointView>();
        foreach (var r in records.OrderBy(x => x.PointId))
        {
            var location = r.LocationId is { } id ? await locations.GetAsync(id, ct) : null;
            list.Add(new RcsMapPointView(r.PointId, r.X, r.Y, r.Name, location?.Code, location?.RemovedAt is not null, IsCurrent(r, geometry), r.UpdatedAt, r.UpdatedBy));
        }
        return list;
    }

    public const int MaxMapDataBytes = 5_000_000;

    /// <summary>Reads and matches both files completely; used by the preview and by the import, so both say the same.</summary>
    private (ParsedRcsMap Map, MapGeometry Geometry, string FileName, string Sha256) Read(RcsMapFiles files)
    {
        var parsed = RcsMapParser.Parse(files.Content);
        var name = FileName(files.FileName, "map.xml");
        var hash = Hash(Encoding.UTF8.GetBytes(files.Content!));
        MapDataSet? data = null;
        if (!string.IsNullOrWhiteSpace(files.MapDataBase64))
        {
            if (files.MapDataBase64.Length > MaxMapDataBytes * 4 / 3 + 8) throw AppException.Validation("RCS_MAPDATA_INVALID", "The Excel file is larger than 5 MB.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(files.MapDataBase64); }
            catch (FormatException) { throw AppException.Validation("RCS_MAPDATA_INVALID", "The Excel file was not sent completely."); }
            data = RcsMapDataMatcher.Match(mapDataReader.Read(bytes), FileName(files.MapDataFileName, "mapdata.xls"), Hash(bytes), parsed.MapQrCode, parsed.Geometry.Points);
        }
        return (parsed, parsed.Geometry with { MapData = data }, name, hash);
    }

    private static string FileName(string? given, string fallback)
    {
        var name = Path.GetFileName((given ?? "").Trim());
        return name.Length is 0 or > 200 ? fallback : name;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>CR-067: what the import would store, including which named points would stay where they are. Nothing is saved.</summary>
    public async Task<RcsMapPreview> PreviewAsync(RcsMapFiles files, CancellationToken ct = default)
    {
        var read = Read(files);
        return await uow.ExecuteAsync(async t =>
        {
            var current = await repo.GetAsync(t);
            var named = await repo.ListPointsAsync(read.Map.MapId, t);
            var kept = named.Count(r => IsCurrent(r, read.Geometry));
            var matched = read.Geometry.MapData?.Rows.Where(r => r.PointId is not null).Select(r => r.PointId!.Value).ToHashSet() ?? [];
            return new RcsMapPreview(read.Map.MapId, read.Map.MapName, read.Map.MapQrCode, read.Geometry.Points.Count, read.Geometry.Edges.Count, read.Map.UnknownNeighbours, read.Geometry.MapData,
                read.Geometry.MapData is null ? [] : read.Geometry.Points.Where(p => !matched.Contains(p.Id)).Select(p => p.Id).ToList(), current?.MapId, kept, named.Count - kept);
        }, ct);
    }

    public Task<RcsMapImportResult> ImportAsync(string? fileName, string? xml, string? user, CancellationToken ct = default) => ImportAsync(new RcsMapFiles(fileName, xml), user, ct);

    public async Task<RcsMapImportResult> ImportAsync(RcsMapFiles files, string? user, CancellationToken ct = default)
    {
        var (parsed, geometry, name, hash) = Read(files);   // checked completely before anything is stored
        return await uow.ExecuteAsync(async t =>
        {
            var map = await repo.GetAsync(t); var replaced = map is not null;
            if (map is null) { map = new RcsMap(clock.UtcNow); repo.Add(map); }
            map.Replace(parsed.MapId, parsed.MapName, parsed.MapQrCode, parsed.MapType, parsed.Row, parsed.Col, parsed.Width, parsed.Height, name, hash,
                geometry.Points.Count, geometry.Edges.Count, JsonSerializer.Serialize(geometry, Json), files.Content!, user, clock.UtcNow);
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
