using Microsoft.Extensions.DependencyInjection;
using WCS.Application.RcsMaps;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-069: the route the simulator drives is made of the connections of the map only.</summary>
public class Cr069SimMapGraphTests
{
    private static SimMapGraph Graph() => new(new RcsMapView(
        new RcsMapInfo("M", "M", "CC", null, null, null, null, null, "m.xml", "h", 4, 3, DateTimeOffset.UnixEpoch, null, 1000, true, 0, false, [], null, null),
        new MapGeometry([new(1, 0, 0, null, null), new(2, 1, 0, null, null), new(3, 1, 1, null, null), new(4, 5, 5, null, null)],
            [new(1, 2, 1, []), new(2, 3, 1, [[1.2, 0.5]]), new(3, 1, 1.4, [])],
            new MapDataSet("d", "h", "s", [new(2, "x", "A1", "Working Area", null, null, null, null, null, null, null, "Matched", 1), new(3, "y", "B1", "Working Area", null, null, null, null, null, null, null, "Matched", 3)])),
        []));

    [Fact]
    public void A_route_follows_the_connections_in_their_direction_and_there_is_none_without_them()
    {
        var g = Graph();
        Assert.Equal((1L, 3L), (g.PointOf("a1")!.Value, g.PointOf("B1")!.Value));
        Assert.Null(g.PointOf("Z9"));
        var route = g.Route(1, 3)!;
        Assert.Equal([(0d, 0d), (1000d, 0d), (1200d, 500d), (1000d, 1000d)], route);       // through point 2 and its control point, never straight across
        Assert.Equal(2, g.Route(3, 1)!.Count);
        Assert.Null(g.Route(1, 4));
        Assert.Equal(90, g.DirectionOf(0, 1));                                             // +Y of an upward map = 90 degrees
    }
}
