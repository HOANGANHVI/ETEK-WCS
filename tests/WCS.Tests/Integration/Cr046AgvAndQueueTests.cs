using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-046: the corrections of the two findings of the CR-042 review (F1: PER_AGV across the two engines; F2: a destination reserved by an order whose AGV is busy). These tests replace the
/// characterization tests of the findings. Real PostgreSQL, SIMULATOR only, Legacy dispatch.
/// </summary>
[Collection("Database")]
public class Cr046AgvAndQueueTests(DatabaseFixture fixture)
{
    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<ProcessAdminService>()));

    private async Task<RoutingKit> PickupFromA2Async()
    {
        var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-LEG", RequestType.PickupRequest, RackKind.Full, ["A2"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A2", RackKind.Full, 1); await kit.H.StockAsync("A1", RackKind.Full, 1);
        return kit;
    }

    [Fact]
    public async Task F1_a_Phase_1_task_waits_while_the_AGV_carries_a_leg_and_starts_when_the_leg_is_done()
    {
        await using var kit = await PickupFromA2Async();
        var order = (await kit.StartAsync("leg", "A2", RequestType.PickupRequest, RackKind.Full, "T-LEG")).Order;
        var leg = Assert.Single((await kit.OrderAsync(order.Id)).Legs);
        Assert.Equal(LegState.InProgress, leg.State);

        var task = (await kit.H.CreateTaskAsync("Bp01", "A1", "task")).Task;
        var stored = await kit.H.GetTaskAsync(task.Id);
        Assert.Equal((leg.AgvId, TaskState.Queued), (stored.AgvId, stored.State));
        await kit.H.TickAsync(); await kit.H.TickAsync();
        Assert.Equal(TaskState.Queued, (await kit.H.GetTaskAsync(task.Id)).State);     // the scheduler does not give the AGV away either

        await kit.PickAsync(leg.Id);                                                    // rack on the AGV: still busy
        await kit.H.TickAsync();
        Assert.Equal(TaskState.Queued, (await kit.H.GetTaskAsync(task.Id)).State);
        await kit.DropAsync(leg.Id);
        await kit.H.TickAsync();
        Assert.Contains((await kit.H.GetTaskAsync(task.Id)).State, TaskStateMachine.AgvOccupyingStates);
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(order.Id)).State);
    }

    [Fact]
    public async Task F1_a_task_and_an_order_created_at_the_same_moment_never_give_one_AGV_two_jobs()
    {
        await using var kit = await PickupFromA2Async();
        await Task.WhenAll(
            Task.Run(() => kit.H.CreateTaskAsync("Bp01", "A1", "task")),
            Task.Run(() => kit.StartAsync("leg", "A2", RequestType.PickupRequest, RackKind.Full, "T-LEG")));
        for (var i = 0; i < 3; i++) { await kit.TickAsync(); await kit.H.TickAsync(); }
        var agv = (await kit.H.AgvAsync("AGV1")).Id;
        var occupying = TaskStateMachine.AgvOccupyingStates.ToArray();
        var jobs = await kit.H.Db(async db => await db.Tasks.CountAsync(t => t.AgvId == agv && occupying.Contains(t.State))
                                             + await db.TransportLegs.CountAsync(l => l.AgvId == agv && (l.State == LegState.InProgress || l.State == LegState.Waiting)));
        Assert.Equal(1, jobs);                                                           // exactly one of the two, whichever came first
    }

    /// <summary>
    /// The chain of the finding: a delivery of a third queue scope used to reserve the slot the moment it was freed although its AGV was carrying the rack waiting for exactly that slot.
    /// </summary>
    [Fact]
    public async Task F2_an_order_whose_AGV_is_busy_reserves_nothing_so_the_rack_on_that_AGV_finds_its_slot()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture, seedPorts: false, extraConfig: new Dictionary<string, string?> { ["RouteEngine:NewPublicationsRequireReadiness"] = "true" });
        await kit.H.Db(async db => { foreach (var l in await db.Locations.ToListAsync()) l.Configure(l.Name, 1, AllowedRackKind.Any, true, false, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        async Task QueuedAsync(string code, RequestType type, RackKind kind, string trigger, string scope, VersionDefinition definition)
        {
            var (p, v) = await kit.CreateProcessAsync(code, type, kind, [trigger], definition);
            await kit.PublishAsync(v);
            await Admin(kit, s => s.SetQueueScopeAsync(p, scope, "test"));
            await Admin(kit, s => s.SetReadinessAsync(v, "Ready", "test", "admin"));
        }
        VersionDefinition PickThenChoose(string destination) => new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, destination)], resolution: "AfterPick"), Done()]);
        await QueuedAsync("Q-OUT", RequestType.PickupRequest, RackKind.Full, "A1", "OUT", PickThenChoose("C1"));
        await QueuedAsync("Q-BACK", RequestType.PickupRequest, RackKind.Empty, "C1", "BACK", PickThenChoose("A1"));
        await QueuedAsync("Q-CAP", RequestType.DeliveryRequest, RackKind.Empty, "A1", "CAP", new("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "F3")]), Done()]));
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Empty, 1); await kit.H.StockAsync("F3", RackKind.Empty, 1);

        var o1 = (await kit.StartAsync("out", "A1", RequestType.PickupRequest, RackKind.Full, "Q-OUT")).Order;
        var o2 = (await kit.StartAsync("back", "C1", RequestType.PickupRequest, RackKind.Empty, "Q-BACK")).Order;
        var o3 = (await kit.StartAsync("cap", "A1", RequestType.DeliveryRequest, RackKind.Empty, "Q-CAP")).Order;
        async Task<TransportLeg> Leg(Guid id) => (await kit.OrderAsync(id)).Legs.Single();

        await kit.PickAsync((await Leg(o1.Id)).Id);   // A1 is free
        await kit.TickAsync();
        var third = await kit.OrderAsync(o3.Id);      // the AGV of the third order (empty racks) carries order 2: nothing is reserved
        Assert.Equal((OrderState.Waiting, WaitKind.AgvBusy), (third.State, third.WaitKind));
        Assert.Empty(third.Legs);

        await kit.PickAsync((await Leg(o2.Id)).Id);   // order 2 holds a rack and needs A1: it gets it
        await kit.TickAsync();
        Assert.Equal("A1", (await Leg(o2.Id)).DestinationLocationCode);
        await kit.DropAsync((await Leg(o1.Id)).Id);
        await kit.DropAsync((await Leg(o2.Id)).Id);
        for (var i = 0; i < 4; i++) await kit.TickAsync();

        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(o1.Id)).State);
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(o2.Id)).State);
        // A1 now holds the rack order 2 brought: the third order waits for a slot (or its rack), holding nothing
        var last = await kit.OrderAsync(o3.Id);
        Assert.Equal(OrderState.Waiting, last.State);
        Assert.Empty(last.Legs);
    }

    /// <summary>Two processes served by the same AGV: the first chooses its destination after the pick, the second before it.</summary>
    private async Task<RoutingKit> TwoPickupsAsync(bool rule)
    {
        var kit = await RoutingKit.CreateAsync(fixture, extraConfig: new Dictionary<string, string?> { ["RouteEngine:DeferDestinationWhileAgvNeedsOne"] = rule ? "true" : "false" });
        var (_, later) = await kit.CreateProcessAsync("T-LATER", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")], resolution: "AfterPick"), Done()]));
        var (_, first) = await kit.CreateProcessAsync("T-FIRST", RequestType.PickupRequest, RackKind.Full, ["A2"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "B1")]), Done()]));
        await kit.PublishAsync(later); await kit.PublishAsync(first);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);
        return kit;
    }

    [Fact]
    public async Task F2_an_order_reserves_nothing_while_its_AGV_has_a_leg_without_destination_and_goes_on_once_that_leg_has_one()
    {
        await using var kit = await TwoPickupsAsync(rule: true);
        var one = (await kit.StartAsync("one", "A1", RequestType.PickupRequest, RackKind.Full, "T-LATER")).Order;
        var two = (await kit.StartAsync("two", "A2", RequestType.PickupRequest, RackKind.Full, "T-FIRST")).Order;
        await kit.TickAsync();
        var waiting = await kit.OrderAsync(two.Id);
        Assert.Equal((OrderState.Waiting, WaitKind.AgvBusy, 0), (waiting.State, waiting.WaitKind, waiting.Legs.Count));
        Assert.Equal(0, await kit.H.Db(db => db.Reservations.CountAsync(r => r.Kind == ReservationKind.DestinationLocation && r.Status == ReservationStatus.Active)));

        await kit.PickAsync((await kit.OrderAsync(one.Id)).Legs.Single().Id);     // the first leg gets its destination
        await kit.TickAsync(); await kit.TickAsync();
        var leg = Assert.Single((await kit.OrderAsync(two.Id)).Legs);
        Assert.Equal(("B1", LegState.Ready, "AGV_BUSY"), (leg.DestinationLocationCode, leg.State, leg.StateReason));   // queued behind the AGV as before

        var leg1 = (await kit.OrderAsync(one.Id)).Legs.Single();
        await kit.DropAsync(leg1.Id);                                              // the AGV is free: the second order goes on by itself
        await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal(LegState.InProgress, Assert.Single((await kit.OrderAsync(two.Id)).Legs).State);
    }

    [Fact]
    public async Task F2_the_rule_can_be_switched_off_and_the_earlier_behaviour_returns()
    {
        await using var kit = await TwoPickupsAsync(rule: false);
        await kit.StartAsync("one", "A1", RequestType.PickupRequest, RackKind.Full, "T-LATER");
        var two = (await kit.StartAsync("two", "A2", RequestType.PickupRequest, RackKind.Full, "T-FIRST")).Order;
        var leg = Assert.Single((await kit.OrderAsync(two.Id)).Legs);
        Assert.Equal(("B1", LegState.Ready, "AGV_BUSY"), (leg.DestinationLocationCode, leg.State, leg.StateReason));
    }
}
