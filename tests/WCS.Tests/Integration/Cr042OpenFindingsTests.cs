using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CHARACTERIZATION of two open findings of the CR-042 review. They describe what the system does NOW; neither is an approved rule. When a correction is approved, the matching
/// test is turned round. Real PostgreSQL, SIMULATOR only.
/// </summary>
[Collection("Database")]
public class Cr042OpenFindingsTests(DatabaseFixture fixture)
{
    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<ProcessAdminService>()));

    /// <summary>
    /// PER_AGV across the two engines (Legacy dispatch): the route engine looks at the tasks of an AGV before it gives the AGV to a leg, but the Phase 1 task engine does not look at
    /// the legs. A task created while a leg of the same AGV is active is dispatched.
    /// </summary>
    [Fact]
    public async Task Current_behaviour_a_Phase_1_task_is_dispatched_to_an_AGV_that_already_carries_a_leg()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-LEG", RequestType.PickupRequest, RackKind.Full, ["A2"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A2", RackKind.Full, 1); await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = (await kit.StartAsync("leg", "A2", RequestType.PickupRequest, RackKind.Full, "T-LEG")).Order;
        var leg = Assert.Single((await kit.OrderAsync(order.Id)).Legs);
        Assert.Equal(LegState.InProgress, leg.State); Assert.NotNull(leg.AgvId);

        var task = (await kit.H.CreateTaskAsync("Bp01", "A1", "task")).Task;
        var stored = await kit.H.GetTaskAsync(task.Id);
        Assert.Equal(leg.AgvId, stored.AgvId);
        Assert.NotEqual(TaskState.Queued, stored.State);
        Assert.Contains(stored.State, TaskStateMachine.AgvOccupyingStates);
        var still = Assert.Single((await kit.OrderAsync(order.Id)).Legs);
        Assert.Equal((LegState.InProgress, leg.AgvId), (still.State, still.AgvId));   // two active jobs on one AGV at the same moment
    }

    /// <summary>
    /// OQ-54: separate queue scopes do not remove every circular wait. A delivery of a third scope reserves the slot the moment it is freed, although its AGV is carrying the rack
    /// that waits for exactly that slot; neither can move.
    /// </summary>
    [Fact]
    public async Task Current_behaviour_a_reservation_of_a_leg_without_a_free_AGV_blocks_the_rack_that_AGV_carries()
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
        await kit.TickAsync();                        // the third scope reserves it at once; its AGV (empty racks) carries order 2
        await kit.PickAsync((await Leg(o2.Id)).Id);   // order 2 now holds a rack and needs A1
        await kit.TickAsync(); await kit.TickAsync();
        await kit.DropAsync((await Leg(o1.Id)).Id);   // order 1 finishes: C1 was freed by order 2
        for (var i = 0; i < 5; i++) await kit.TickAsync();

        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(o1.Id)).State);
        var waiting = await Leg(o2.Id);
        Assert.Equal((WaitKind.DestinationUnavailable_AgvHoldingRack, (string?)null), (waiting.WaitKind, waiting.DestinationLocationCode));
        var blocked = await Leg(o3.Id);
        Assert.Equal(("A1", LegState.Ready, "AGV_BUSY"), (blocked.DestinationLocationCode, blocked.State, blocked.StateReason));
        Assert.Equal((await kit.H.AgvAsync("AGV2")).Id, waiting.AgvId);   // the AGV the third order needs is the one carrying the second rack
        Assert.Equal(RackKind.Empty, blocked.RackKind);

        // only a person gets out of it: the order that holds the rack cannot be cancelled, cancelling the third one frees the slot
        Task<OrderDto> Cancel(Guid id) => kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(id, "test", "admin"));
        Assert.Equal("ORDER_NOT_CANCELLABLE", (await Assert.ThrowsAsync<AppException>(() => Cancel(o2.Id))).Code);
        await Cancel(o3.Id);
        await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal("A1", (await Leg(o2.Id)).DestinationLocationCode);
    }
}
