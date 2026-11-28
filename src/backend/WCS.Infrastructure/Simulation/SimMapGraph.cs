using WCS.Application.RcsMaps;

namespace WCS.Infrastructure.Simulation;

/// <summary>
/// CR-069: the imported RCS map as the simulator drives on it. Positions are millimetres (the unit the RCS reports), connections are exactly the NeighbInfo entries of the XML
/// (in their direction, along their control points). Nothing is added: no connection means no route.
/// </summary>
public sealed class SimMapGraph
{
    private sealed record Edge(long To, IReadOnlyList<(double X, double Y)> Line, double Length);

    private readonly Dictionary<long, (double X, double Y)> _points;
    private readonly Dictionary<long, List<Edge>> _edges = [];
    private readonly Dictionary<string, long> _locations = new(StringComparer.OrdinalIgnoreCase);

    public string Key { get; }
    public string MapId { get; }
    public string MapCode { get; }
    private readonly bool _yUp, _clockwise;
    private readonly double _offset;
    /// <summary>Where idle simulated AMRs are placed: parking points of the Excel data, then charging places, then the other points by id.</summary>
    public IReadOnlyList<long> Homes { get; }

    public SimMapGraph(RcsMapView map)
    {
        var i = map.Info; var scale = i.MmPerUnit;
        Key = $"{i.Sha256}|{i.ImportedAt:O}|{i.ConfirmedAt:O}|{scale}|{string.Join(";", map.Points.Select(p => $"{p.PointId}:{p.LocationCode}:{p.Current}"))}";
        MapId = i.MapId; MapCode = string.IsNullOrWhiteSpace(i.MapQrCode) ? i.MapId : i.MapQrCode!;
        _yUp = i.YAxisUp; _clockwise = i.HeadingClockwise; _offset = i.HeadingOffsetDeg;
        _points = map.Geometry.Points.ToDictionary(p => p.Id, p => (p.X * scale, p.Y * scale));
        foreach (var e in map.Geometry.Edges)
        {
            if (!_points.TryGetValue(e.From, out var a) || !_points.TryGetValue(e.To, out var b)) continue;
            var line = new List<(double X, double Y)> { a };
            line.AddRange(e.Ctrl.Select(c => (c[0] * scale, c[1] * scale)));
            line.Add(b);
            if (!_edges.TryGetValue(e.From, out var list)) _edges[e.From] = list = [];
            list.Add(new Edge(e.To, line, Length(line)));
        }
        // a WCS link (set by an Admin) decides; otherwise the one Excel row whose call code is exactly the location code
        var rows = map.Geometry.MapData?.Rows.Where(r => r.PointId is not null && r.CallCode is not null).ToList() ?? [];
        foreach (var g in rows.GroupBy(r => r.CallCode!, StringComparer.OrdinalIgnoreCase)) if (g.Count() == 1) _locations[g.Key] = g.First().PointId!.Value;
        foreach (var p in map.Points) if (p.Current && p.LocationCode is not null && !p.LocationRemoved) _locations[p.LocationCode] = p.PointId;
        var type = map.Geometry.MapData?.Rows.Where(r => r.PointId is not null).GroupBy(r => r.PointId!.Value).ToDictionary(g => g.Key, g => g.First().DataType ?? "") ?? [];
        Homes = _points.Keys.OrderBy(id => type.GetValueOrDefault(id) switch { "Parking Point" => 0, "Charging Area" => 1, "Passing Point" => 2, _ => 3 }).ThenBy(id => id).ToList();
    }

    public long? PointOf(string? locationCode) => locationCode is not null && _locations.TryGetValue(locationCode, out var id) && _points.ContainsKey(id) ? id : null;
    public (double X, double Y) Position(long id) => _points[id];
    public static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    public static double Length(IReadOnlyList<(double X, double Y)> line) { var s = 0.0; for (var i = 1; i < line.Count; i++) s += Distance(line[i - 1], line[i]); return s; }

    public (long Id, double Distance) Nearest((double X, double Y) at)
    {
        var best = _points.MinBy(p => Distance(p.Value, at));
        return (best.Key, Distance(best.Value, at));
    }

    /// <summary>The shortest route along the connections; null = the map has none.</summary>
    public List<(double X, double Y)>? Route(long from, long to) => Search(from, id => id == to);

    /// <summary>The route to the closest point (along the connections) that is farther than <paramref name="radius"/> from <paramref name="from"/>: how an AMR leaves a station.</summary>
    public List<(double X, double Y)>? Leave(long from, double radius) => Search(from, id => Distance(_points[id], _points[from]) > radius);

    private List<(double X, double Y)>? Search(long from, Func<long, bool> goal)
    {
        var cost = new Dictionary<long, double> { [from] = 0 }; var back = new Dictionary<long, (long From, Edge Edge)>();
        var queue = new PriorityQueue<long, double>(); queue.Enqueue(from, 0);
        while (queue.TryDequeue(out var at, out var c))
        {
            if (c > cost[at]) continue;
            if (goal(at))
            {
                var edges = new List<Edge>();
                for (var n = at; n != from; n = back[n].From) edges.Add(back[n].Edge);
                edges.Reverse();
                var line = new List<(double X, double Y)> { _points[from] };
                foreach (var e in edges) line.AddRange(e.Line.Skip(1));
                return line;
            }
            foreach (var e in _edges.GetValueOrDefault(at) ?? [])
            {
                var next = c + e.Length;
                if (cost.TryGetValue(e.To, out var old) && old <= next) continue;
                cost[e.To] = next; back[e.To] = (at, e); queue.Enqueue(e.To, next);
            }
        }
        return null;
    }

    /// <summary>The robotDir the simulator reports for a movement, in the convention the map settings read it back with.</summary>
    public double DirectionOf(double dx, double dy)
    {
        var screen = Math.Atan2(_yUp ? -dy : dy, dx) * 180 / Math.PI;      // clockwise on the screen, 0 = to the right
        var dir = _clockwise ? screen + _offset : -screen - _offset;
        dir %= 360; if (dir > 180) dir -= 360; if (dir <= -180) dir += 360;
        return Math.Round(dir, 1);
    }
}
