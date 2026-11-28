using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.RcsMaps;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-066: a name and a WCS location on a map point; a new import keeps them only while the point is where it was.</summary>
[Collection("Database")]
public class Cr066MapPointTests(DatabaseFixture fixture)
{
    private static string Map(string x2) => $"<MapCfg><MapID>M1</MapID><MapName>N</MapName><PointInfo><id>1</id><xpos>1</xpos><ypos>1</ypos></PointInfo><PointInfo><id>2</id><xpos>{x2}</xpos><ypos>1</ypos></PointInfo></MapCfg>";
    private static Task<T> Maps<T>(RoutingKit kit, Func<RcsMapService, Task<T>> call) => kit.With(sp => call(sp.GetRequiredService<RcsMapService>()));

    [Fact]
    public async Task A_point_is_named_linked_unlinked_and_not_moved_to_another_place_by_a_new_import()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var sha = (await Maps(kit, s => s.ImportAsync("m.xml", Map("2"), "t"))).Info.Sha256;
        await Maps(kit, s => s.SetPointAsync(1, new(sha, "Gate A1", "A1"), "t"));
        var taken = await Assert.ThrowsAsync<AppException>(() => Maps(kit, s => s.SetPointAsync(2, new(sha, null, "A1"), "t")));
        Assert.Equal("RCS_MAP_LOCATION_LINKED", taken.Code);
        var points = await Maps(kit, s => s.SetPointAsync(2, new(sha, "Gate A2", "A2"), "t"));
        Assert.Equal([("A1", true), ("A2", true)], points.Select(p => (p.LocationCode!, p.Current)));
        Assert.Equal("RCS_MAP_CHANGED", (await Assert.ThrowsAsync<AppException>(() => Maps(kit, s => s.SetPointAsync(1, new("old", "x", null), "t")))).Code);

        var sha2 = (await Maps(kit, s => s.ImportAsync("m2.xml", Map("5"), "t"))).Info.Sha256;           // point 2 moved
        var view = (await Maps(kit, s => s.GetAsync()))!;
        Assert.Equal([(1L, true), (2L, false)], view.Points.Select(p => (p.PointId, p.Current)));
        await Maps(kit, s => s.SetPointAsync(2, new(sha2, null, "A2"), "t"));                            // set again where it is now
        var left = await Maps(kit, s => s.SetPointAsync(1, new(sha2, "Gate A1", null), "t"));            // unlink, the name stays
        Assert.Equal(("Gate A1", (string?)null), (left[0].Name, left[0].LocationCode));
        Assert.Single(await Maps(kit, s => s.RemovePointAsync(1)));
    }
}
