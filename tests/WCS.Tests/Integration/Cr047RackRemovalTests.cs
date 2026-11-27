using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-047 (hotfix): removing a rack record. A free rack is removed and its history stays; a rack held by a task or a transport order is refused, the answer names the job, and removing it
/// becomes possible once that job is ended by its own rules. Nothing of a transport order or of the history is ever deleted. Real PostgreSQL, SIMULATOR only.
/// </summary>
[Collection("Database")]
public class Cr047RackRemovalTests(DatabaseFixture fixture)
{
    private static Task<T> Racks<T>(RoutingKit kit, Func<RackAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<RackAdminService>()));
    private static Task<WCS.Application.RackDto> AddAsync(RoutingKit kit, string location, string id) =>
        Racks(kit, s => s.CreateAsync(new RackCreateCommand(location, RackKind.Full, id, null, "test"), "admin"));
    private static Task<int> CountAsync(RoutingKit kit, Guid rack) => kit.H.Db(db => db.Racks.CountAsync(r => r.Id == rack));

    private static async Task<(RoutingKit Kit, Guid OrderId)> WaitingOrderAsync(DatabaseFixture fixture)
    {
        var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-WAIT", RequestType.PickupRequest, RackKind.Full, ["A2"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.CreateTaskAsync("Bp01", "A1", "busy-agv");                     // the AGV is busy, so the order's leg is not sent
        var order = (await kit.StartAsync("waits", "A2", RequestType.PickupRequest, RackKind.Full, "T-WAIT")).Order;
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(order.Id)).State);  // no rack at A2 yet
        return (kit, order.Id);
    }

    [Fact]
    public async Task A_free_rack_is_removed_and_its_history_stays()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var rack = await AddAsync(kit, "A2", "FREE-1");
        var check = await Racks(kit, s => s.CheckEditableAsync(rack.Id));
        Assert.True(check.Editable);
        Assert.Null(check.Blockers);
        await Racks(kit, s => s.DeleteAsync(rack.Id, "wrong record", "admin"));
        Assert.Equal(0, await CountAsync(kit, rack.Id));
        Assert.Equal(2, await kit.H.Db(db => db.InventoryTransactions.CountAsync(t => t.RackId == rack.Id)));   // created and removed
    }

    [Fact]
    public async Task A_rack_taken_by_a_waiting_order_names_the_order_and_can_be_removed_after_the_order_is_cancelled()
    {
        var (kit, orderId) = await WaitingOrderAsync(fixture);
        await using var _ = kit;
        var rack = await AddAsync(kit, "A2", "TAKEN-1");
        await kit.TickAsync();                                                       // the waiting order reserves the new rack

        var check = await Racks(kit, s => s.CheckEditableAsync(rack.Id));
        Assert.Equal((false, "RACK_RESERVED"), (check.Editable, check.Code));
        var blocker = Assert.Single(check.Blockers!);
        Assert.Equal(("Order", orderId, true, false), (blocker.Kind, blocker.Id, blocker.CanCancel, blocker.RacksHeld));
        Assert.Equal("RACK_RESERVED", (await Assert.ThrowsAsync<AppException>(() => Racks(kit, s => s.DeleteAsync(rack.Id, "x", "admin")))).Code);
        Assert.Equal(1, await CountAsync(kit, rack.Id));

        await kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(orderId, "not needed", "admin"));
        Assert.True((await Racks(kit, s => s.CheckEditableAsync(rack.Id))).Editable);
        await Racks(kit, s => s.DeleteAsync(rack.Id, "wrong record", "admin"));
        Assert.Equal(0, await CountAsync(kit, rack.Id));
        var order = await kit.OrderAsync(orderId);                                   // the order, its leg and the history are all still there
        Assert.Equal(OrderState.Cancelled, order.State);
        Assert.Equal(rack.Id, Assert.Single(order.Legs).RackId);
        Assert.True(await kit.H.Db(db => db.RoutingEvents.AnyAsync(e => e.OrderId == orderId)));
    }

    [Fact]
    public async Task A_rack_of_a_task_or_on_the_AGV_is_refused_and_says_who_holds_it()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var rack = await AddAsync(kit, "A1", "TASK-1");
        var task = (await kit.H.CreateTaskAsync("Bp01", "A1", "holds")).Task;
        var check = await Racks(kit, s => s.CheckEditableAsync(rack.Id));
        Assert.Equal((false, "RACK_RESERVED"), (check.Editable, check.Code));
        var blocker = Assert.Single(check.Blockers!);
        Assert.Equal(("Task", task.Id, false), (blocker.Kind, blocker.Id, blocker.CanCancel));
        Assert.StartsWith("NV-", blocker.Reference);
        Assert.Equal(1, await CountAsync(kit, rack.Id));
    }
}
