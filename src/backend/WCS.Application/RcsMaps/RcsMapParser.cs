using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using WCS.Application.Common;

namespace WCS.Application.RcsMaps;

public sealed record MapPoint(long Id, double X, double Y, string? Value, string? StorageType);
/// <param name="Ctrl">Intermediate positions of a curved path, as [x, y], in file order. Empty = a straight connection.</param>
public sealed record MapEdge(long From, long To, double? Distance, IReadOnlyList<double[]> Ctrl);
public sealed record MapGeometry(IReadOnlyList<MapPoint> Points, IReadOnlyList<MapEdge> Edges);
public sealed record ParsedRcsMap(string MapId, string MapName, string? MapQrCode, string? MapType, string? Row, string? Col, string? Width, string? Height,
    MapGeometry Geometry, int UnknownNeighbours);

/// <summary>
/// Reads the map XML exported by the RCS: <c>MapCfg</c> with MapID, MapName and one <c>PointInfo</c> per point (id, xpos, ypos, NeighbInfo). In NeighbInfo every
/// <c>id</c> starts the next neighbour; its distance and CtrlPoint follow. Nothing is converted here. DTDs and external references are refused.
/// </summary>
public static class RcsMapParser
{
    public const int MaxCharacters = 10_000_000;
    public const int MaxPoints = 100_000;

    public static ParsedRcsMap Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw Invalid("The file is empty.");
        if (xml.Length > MaxCharacters) throw Invalid("The file is larger than 10 MB.");
        XDocument doc;
        try
        {
            using var text = new StringReader(xml.TrimStart('﻿'));
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxCharacters });
            doc = XDocument.Load(reader);
        }
        catch (XmlException e) { throw Invalid("The file is not valid XML: " + e.Message); }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "MapCfg") throw Invalid("This is not an RCS map: the root element must be MapCfg.");
        var mapId = Text(root, "MapID") ?? throw Invalid("MapID is missing.");
        var mapName = Text(root, "MapName") ?? throw Invalid("MapName is missing.");
        if (mapId.Length > 64 || mapName.Length > 200) throw Invalid("MapID or MapName is too long.");

        var points = new List<MapPoint>(); var edges = new List<MapEdge>(); var ids = new HashSet<long>();
        foreach (var p in root.Elements("PointInfo"))
        {
            if (points.Count >= MaxPoints) throw Invalid($"The map has more than {MaxPoints} points.");
            var id = Integer(p.Element("id")?.Value) ?? throw Invalid($"PointInfo number {points.Count + 1} has no numeric id.");
            if (!ids.Add(id)) throw Invalid($"Point id {id} appears twice.");
            var x = Number(p.Element("xpos")?.Value) ?? throw Invalid($"Point {id} has no numeric xpos.");
            var y = Number(p.Element("ypos")?.Value) ?? throw Invalid($"Point {id} has no numeric ypos.");
            points.Add(new MapPoint(id, x, y, Short(p.Element("value")?.Value), Short(p.Element("StorageType")?.Value)));
            foreach (var block in p.Elements("NeighbInfo")) ReadNeighbours(id, block, edges);
        }
        if (points.Count == 0) throw Invalid("The map has no PointInfo.");
        var known = edges.Where(e => ids.Contains(e.To)).ToList();
        return new ParsedRcsMap(mapId, mapName, Short(Text(root, "MapQRCode")), Short(Text(root, "MapType")), Short(Text(root, "Row")), Short(Text(root, "Col")),
            Short(Text(root, "Width")), Short(Text(root, "Height")), new MapGeometry(points, known), edges.Count - known.Count);
    }

    private static void ReadNeighbours(long from, XElement block, List<MapEdge> edges)
    {
        long? to = null; double? distance = null; var ctrl = new List<double[]>();
        void Close() { if (to is { } t) edges.Add(new MapEdge(from, t, distance, ctrl)); to = null; distance = null; ctrl = []; }
        foreach (var e in block.Elements())
        {
            switch (e.Name.LocalName)
            {
                case "id": Close(); to = Integer(e.Value) ?? throw Invalid($"Point {from} has a neighbour without a numeric id."); break;
                case "distance": distance = Number(e.Value); break;
                case "CtrlPoint":
                    foreach (var pos in e.Elements("Pos"))
                    {
                        var x = Number((string?)pos.Attribute("xpos")); var y = Number((string?)pos.Attribute("ypos"));
                        if (x is null || y is null) throw Invalid($"A control point of point {from} has no numeric position.");
                        ctrl.Add([x.Value, y.Value]);
                    }
                    break;
            }
        }
        Close();
    }

    private static string? Text(XElement root, string name) => root.Element(name)?.Value.Trim() is { Length: > 0 } v ? v : null;
    private static string? Short(string? v) => v?.Trim() is { Length: > 0 } t ? (t.Length > 64 ? t[..64] : t) : null;
    private static long? Integer(string? v) => long.TryParse(v?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static double? Number(string? v) => double.TryParse(v?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    private static AppException Invalid(string message) => AppException.Validation("RCS_MAP_INVALID", message);
}
