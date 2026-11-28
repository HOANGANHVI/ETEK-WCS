using System.Globalization;
using System.Text.RegularExpressions;
using WCS.Application.Common;

namespace WCS.Application.RcsMaps;

/// <summary>One worksheet as plain text cells; the first row is the header.</summary>
public sealed record MapSheet(string Name, IReadOnlyList<IReadOnlyList<string?>> Rows);

/// <summary>Reads the Excel file (.xls / .xlsx) the RCS exports. Adapter of the infrastructure; the file is only read.</summary>
public interface IMapDataReader
{
    IReadOnlyList<MapSheet> Read(byte[] content);
}

/// <summary>How an Excel row relates to the XML of the same import.</summary>
public static class MapDataStatus
{
    public const string Matched = "Matched";
    /// <summary>The Map Data ID carries another map QR code: the row belongs to another map and is never attached to this one.</summary>
    public const string OtherMap = "OtherMap";
    /// <summary>This map's QR code, but no point stands at the coded position.</summary>
    public const string NoPoint = "NoPoint";
    /// <summary>The same Map Data ID appears on several rows: none of them is attached.</summary>
    public const string Duplicate = "Duplicate";
    /// <summary>Several points of the XML stand at the coded position.</summary>
    public const string Ambiguous = "Ambiguous";
    /// <summary>No Map Data ID, or not of the form digits + QR code + digits.</summary>
    public const string Invalid = "Invalid";
}

/// <summary>One row exactly as the file states it, with the result of the matching. Nothing of the file is changed and no row is left out.</summary>
public sealed record MapDataRow(int RowNumber, string? MapDataId, string? CallCode, string? DataType, string? LinkedLocation, string? LinkedArea, string? LinkedRackType,
    string? LinkedDeviceType, string? RackNo, string? FixRackNo, string? MachineDataNumber, string Status, long? PointId);
public sealed record MapDataSet(string FileName, string Sha256, string Sheet, IReadOnlyList<MapDataRow> Rows);

/// <summary>
/// Matches the Excel rows to the points of the XML by the Map Data ID only: X in millimetres, the MapQRCode of the XML, Y in millimetres
/// (for example 123500CC129324 is xpos 123.5, ypos 129.324 of map CC). Row order and similar names are never used.
/// </summary>
public static partial class RcsMapDataMatcher
{
    public const int MaxRows = 100_000;
    /// <summary>Map units of the XML to the millimetres of a Map Data ID (the file's positions are metres with at most three decimals).</summary>
    public const double CodeUnitsPerMapUnit = 1000;
    private static readonly string[] Required = ["map data id", "call code", "data type"];

    [GeneratedRegex(@"^(\d{1,9})([A-Za-z]+)(\d{1,9})$")]
    private static partial Regex Code();

    public static MapDataSet Match(IReadOnlyList<MapSheet> sheets, string fileName, string sha256, string? mapQrCode, IReadOnlyList<MapPoint> points)
    {
        var sheet = sheets.FirstOrDefault(s => s.Rows.Count > 0 && Required.All(r => Header(s).ContainsKey(r)))
            ?? throw Invalid("No worksheet has the columns Map Data ID, Call code and Data type.");
        if (sheet.Rows.Count - 1 > MaxRows) throw Invalid($"The worksheet has more than {MaxRows} rows.");
        if (string.IsNullOrWhiteSpace(mapQrCode)) throw AppException.Validation("RCS_MAPDATA_MISMATCH", "The XML has no MapQRCode: the Excel rows cannot be matched to it.");
        var columns = Header(sheet);

        // points by their coded position; a position with more than three decimals cannot be written in a Map Data ID and matches nothing
        var byPosition = points.Select(p => (p.Id, X: Millimetres(p.X), Y: Millimetres(p.Y))).Where(p => p.X is not null && p.Y is not null)
            .GroupBy(p => (p.X!.Value, p.Y!.Value)).ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

        var rows = new List<MapDataRow>();
        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var cells = sheet.Rows[i];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;     // an empty line carries no data
            string? Cell(string name) => columns.TryGetValue(name, out var c) && c < cells.Count && cells[c]?.Trim() is { Length: > 0 } v ? (v.Length > 200 ? v[..200] : v) : null;
            var id = Cell("map data id");
            var status = MapDataStatus.Invalid; long? point = null;
            if (id is not null && Code().Match(id) is { Success: true } m)
            {
                if (!string.Equals(m.Groups[2].Value, mapQrCode, StringComparison.OrdinalIgnoreCase)) status = MapDataStatus.OtherMap;
                else if (!byPosition.TryGetValue((long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)), out var ids)) status = MapDataStatus.NoPoint;
                else if (ids.Count > 1) status = MapDataStatus.Ambiguous;
                else { status = MapDataStatus.Matched; point = ids[0]; }
            }
            rows.Add(new MapDataRow(i + 1, id, Cell("call code"), Cell("data type"), Cell("linked location"), Cell("linked area"), Cell("linked rack type"),
                Cell("linked device type"), Cell("rack no."), Cell("fix rack no."), Cell("machine data number"), status, point));
        }
        var repeated = rows.Where(r => r.MapDataId is not null).GroupBy(r => r.MapDataId!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        rows = rows.Select(r => r.MapDataId is not null && repeated.Contains(r.MapDataId) ? r with { Status = MapDataStatus.Duplicate, PointId = null } : r).ToList();
        if (!rows.Any(r => r.Status == MapDataStatus.Matched))
            throw AppException.Validation("RCS_MAPDATA_MISMATCH", $"No row of the Excel file matches a point of this map (MapQRCode {mapQrCode}): the two files do not belong together.");
        return new MapDataSet(fileName, sha256, sheet.Name, rows);
    }

    private static long? Millimetres(double mapUnits)
    {
        var mm = mapUnits * CodeUnitsPerMapUnit; var whole = Math.Round(mm);
        return Math.Abs(mm - whole) < 1e-6 ? (long)whole : null;
    }

    private static Dictionary<string, int> Header(MapSheet sheet)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < sheet.Rows[0].Count; i++)
            if (sheet.Rows[0][i]?.Trim().ToLowerInvariant() is { Length: > 0 } name) map.TryAdd(name, i);
        return map;
    }

    private static AppException Invalid(string message) => AppException.Validation("RCS_MAPDATA_INVALID", message);
}
