using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Routing;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-057: a location is removed only when nothing depends on it any more; past records keep showing it.</summary>
[Collection("Database")]
public class Cr057LocationRemovalTests(DatabaseFixture fixture)
{
    private static Task<T> Config<T>(RoutingKit kit, Func<OperationalConfigService, Task<T>> call) => kit.With(sp => call(sp.GetRequiredService<OperationalConfigService>()));
    private static Task NewAsync(RoutingKit kit, string code) =>
        Config(kit, s => s.SaveLocationAsync(new SaveLocationCommand(code, "QA " + code, LocationType.Buffer, 2, AllowedRackKind.Any, true, false), true));
    private static async Task<string[]> BlockersAsync(RoutingKit kit, string code) => [.. (await Config(kit, s => s.RemovalAsync(code))).Blockers.Select(b => b.Code)];
    private static async Task<string> RefusedAsync(RoutingKit kit, string code, string? reason = "x", string? confirm = null) =>
        (await Assert.ThrowsAsync<AppException>(() => Config(kit, s => s.DeleteLocationAsync(code, reason, confirm ?? code)))).Code;
    private static Task<bool> ExistsAsync(RoutingKit kit, string code) => kit.H.Db(d => d.Locations.AnyAsync(l => l.Code == code && l.RemovedAt == null));

    [Fact]
    public async Task A_location_nothing_depends_on_is_deleted_after_the_code_and_a_reason()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await NewAsync(kit, "Q9");
        var plan = await Config(kit, s => s.RemovalAsync("Q9"));
        Assert.True(plan.CanDelete);
        Assert.Empty(plan.Blockers);
        Assert.Equal("REASON_REQUIRED", await RefusedAsync(kit, "Q9", " "));
        Assert.Equal("LOCATION_CODE_CONFIRMATION", await RefusedAsync(kit, "Q9", "x", "Q8"));
        Assert.True(await ExistsAsync(kit, "Q9"));
        Assert.Equal("Q9", await Config(kit, s => s.DeleteLocationAsync("Q9", "entered by mistake", "q9")));
        Assert.False(await ExistsAsync(kit, "Q9"));
        var removed = await kit.H.Db(d => d.Locations.SingleAsync(l => l.Code == "Q9"));            // the row stays, marked
        Assert.False(removed.Enabled);
        Assert.Contains(await kit.With(sp => sp.GetRequiredService<WcsQueries>().ListRemovedLocationsAsync()), r => r.Id == removed.Id && r.Code == "Q9");
        Assert.DoesNotContain(await kit.With(sp => sp.GetRequiredService<WcsQueries>().ListLocationsAsync()), l => l.Code == "Q9");
        await Assert.ThrowsAsync<AppException>(() => Config(kit, s => s.RemovalAsync("Q9")));       // not found any more
        await NewAsync(kit, "Q9");                                                                  // the code is free again
        Assert.True(await ExistsAsync(kit, "Q9"));
        Assert.Equal(2, await kit.H.Db(d => d.Locations.CountAsync(l => l.Code == "Q9")));
    }

    [Fact]
    public async Task Racks_a_process_definition_and_unfinished_work_each_keep_a_location()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await NewAsync(kit, "Q1"); await NewAsync(kit, "Q2");
        await kit.H.StockAsync("Q1", RackKind.Full, 1);
        Assert.Equal(["LOCATION_DELETE_RACKS"], await BlockersAsync(kit, "Q1"));                    // the stock declaration is a finished record
        Assert.Equal("LOCATION_DELETE_RACKS", await RefusedAsync(kit, "Q1"));
        var (_, v) = await kit.CreateProcessAsync("T-LOC", RequestType.PickupRequest, RackKind.Full, ["Q1"], OneLeg("Request", Dest(1, "Q2")));
        var named = (await Config(kit, s => s.RemovalAsync("Q2"))).Blockers.Single();
        Assert.Equal(("LOCATION_DELETE_PROCESS", "T-LOC"), (named.Code, named.Detail));                  // a draft counts as well
        Assert.Contains("LOCATION_DELETE_PROCESS", await BlockersAsync(kit, "Q1"));                     // as a trigger location
        await kit.PublishAsync(v);
        var order = (await kit.StartAsync("R-1", "Q1", RequestType.PickupRequest, RackKind.Full)).Order.Id;
        await kit.TickAsync();
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        Assert.Contains("LOCATION_DELETE_OPEN_WORK", await BlockersAsync(kit, "Q2"));                  // an order is on its way there
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id);
        await kit.TickAsync();
        Assert.Equal(["LOCATION_DELETE_PROCESS"], await BlockersAsync(kit, "Q1"));                      // finished: only the definition still names it
        Assert.True(await ExistsAsync(kit, "Q1"));
    }

    [Fact]
    public async Task A_linked_port_the_deployment_configuration_and_protected_stations_keep_a_location()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var a1 = await BlockersAsync(kit, "A1");
        Assert.Contains("LOCATION_DELETE_CONFIGURED", a1);
        Assert.Contains("LOCATION_DELETE_PORT", a1);
        await NewAsync(kit, "A3");
        Assert.Contains("LOCATION_DELETE_PROTECTED", await BlockersAsync(kit, "A3"));
        Assert.Equal("LOCATION_DELETE_PROTECTED", await RefusedAsync(kit, "A3"));
        await Assert.ThrowsAsync<AppException>(() => Config(kit, s => s.RemovalAsync("NOPE")));
    }
}
