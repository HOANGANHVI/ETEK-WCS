namespace WCS.Domain.Rcs;

/// <summary>
/// CR-065: the one RCS map imported for monitoring. Display data only: it is never used for dispatch, locations or inventory.
/// The XML does not state its unit, axis direction or heading convention, so those are settings an Admin checks against a real AMR and confirms.
/// </summary>
public class RcsMap
{
    /// <summary>There is at most one current map; a new import replaces it.</summary>
    public const int CurrentId = 1;

    private RcsMap() { }
    public RcsMap(DateTimeOffset now) { Id = CurrentId; ImportedAt = now; }

    public int Id { get; private set; }
    public string MapId { get; private set; } = "";
    public string MapName { get; private set; } = "";
    public string? MapQrCode { get; private set; }
    public string? MapType { get; private set; }
    public string? RowCount { get; private set; }
    public string? ColCount { get; private set; }
    public string? Width { get; private set; }
    public string? Height { get; private set; }
    public string FileName { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public int PointCount { get; private set; }
    public int EdgeCount { get; private set; }
    public string GeometryJson { get; private set; } = "";
    /// <summary>The file as imported, so that it can be read again by a later version.</summary>
    public string SourceXml { get; private set; } = "";
    public DateTimeOffset ImportedAt { get; private set; }
    public string? ImportedBy { get; private set; }

    /// <summary>Millimetres (the unit of the RCS posX / posY) per unit of xpos / ypos in the file.</summary>
    public double MmPerUnit { get; private set; } = 1000;
    public bool YAxisUp { get; private set; } = true;
    public double HeadingOffsetDeg { get; private set; }
    public bool HeadingClockwise { get; private set; }
    /// <summary>Further mapCode values the RCS reports for this map, separated by commas. The MapID of the file always matches.</summary>
    public string RcsMapCodes { get; private set; } = "";
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public string? ConfirmedBy { get; private set; }
    public uint Version { get; private set; }

    public void Replace(string mapId, string mapName, string? qr, string? type, string? rows, string? cols, string? width, string? height, string fileName, string sha256,
        int points, int edges, string geometryJson, string sourceXml, string? by, DateTimeOffset now)
    {
        if (!string.Equals(MapId, mapId, StringComparison.Ordinal))
        {   // another map: nothing of the old calibration applies
            MmPerUnit = 1000; YAxisUp = true; HeadingOffsetDeg = 0; HeadingClockwise = false; RcsMapCodes = "";
        }
        MapId = mapId; MapName = mapName; MapQrCode = qr; MapType = type; RowCount = rows; ColCount = cols; Width = width; Height = height;
        FileName = fileName; Sha256 = sha256; PointCount = points; EdgeCount = edges; GeometryJson = geometryJson; SourceXml = sourceXml;
        ImportedBy = by; ImportedAt = now; ConfirmedAt = null; ConfirmedBy = null;   // new geometry: to be checked again
    }

    public void Calibrate(double mmPerUnit, bool yAxisUp, double headingOffsetDeg, bool headingClockwise, string rcsMapCodes, bool confirmed, string? by, DateTimeOffset now)
    {
        MmPerUnit = mmPerUnit; YAxisUp = yAxisUp; HeadingOffsetDeg = headingOffsetDeg; HeadingClockwise = headingClockwise; RcsMapCodes = rcsMapCodes;
        ConfirmedAt = confirmed ? now : null; ConfirmedBy = confirmed ? by : null;
    }
}
