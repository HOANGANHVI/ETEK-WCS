using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Plc;
using WCS.Domain.Routing;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-040: the official queue (D-12 admission, D-13 strict FIFO per scope, Held), the Admin PLC simulation (PLC → WCS signals through the one request intake) and guarded rack records.
/// Real PostgreSQL, real engine, SIMULATOR dispatcher. Nothing talks to a PLC or an RCS.
/// </summary>
[Collection("Database")]
public class Cr040QueueSimulationRackTests(DatabaseFixture fixture)
{
    private static readonly Dictionary<string, string?> Queue = new() { ["RouteEngine:NewPublicationsRequireReadiness"] = "true" };

    private async Task<RoutingKit> KitAsync() => await WithoutPlcPermissionAsync(await RoutingKit.CreateAsync(fixture, seedPorts: false, extraConfig: Queue));

    /// <summary>The queue is not about the PLC: the test stations need no port here (a station that needs PLC permission and has no port cannot be set Ready, tested in Cr039).</summary>
    private static async Task<RoutingKit> WithoutPlcPermissionAsync(RoutingKit kit)
    {
        await kit.H.Db(async db => { foreach (var l in await db.Locations.ToListAsync()) l.Configure(l.Name, l.RackCapacity, l.AllowedRackKind, true, false, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        return kit;
    }

    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<ProcessAdminService>()));

    /// <summary>A published, Ready, queued process "pick at the request station, deliver to <paramref name="destinations"/> (chosen before pick)".</summary>
    private static async Task<(Guid ProcessId, Guid VersionId)> ProcessAsync(RoutingKit kit, string code, string trigger, string? scope, bool afterPick, params string[] destinations)
    {
        var step = Move("s1", 1, "Pickup", "Request", "Candidates", "done", destinations.Select((d, i) => Dest(i + 1, d)).ToArray(), afterPick ? "AfterPick" : "BeforePick");
        var (p, v) = await kit.CreateProcessAsync(code, RequestType.PickupRequest, RackKind.Full, [trigger], new("s1", [step, Done()]));
        await kit.PublishAsync(v);
        if (scope is not null) await Admin(kit, s => s.SetQueueScopeAsync(p, scope, "test scope"));
        await Admin(kit, s => s.SetReadinessAsync(v, "Ready", "test", "admin"));
        return (p, v);
    }

    private static async Task<OrderDto> RequestAsync(RoutingKit kit, string key, string at, string code) => (await kit.StartAsync(key, at, RequestType.PickupRequest, RackKind.Full, code)).Order;
    private static async Task<int> LegsAsync(RoutingKit kit, Guid order) => (await kit.OrderAsync(order)).Legs.Count;
    private static async Task FinishAsync(RoutingKit kit, Guid order)
    {
        var leg = (await kit.OrderAsync(order)).Legs.Single(l => !l.IsTerminal);
        await kit.PickAsync(leg.Id); await kit.TickAsync(); await kit.DropAsync(leg.Id); await kit.TickAsync();
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(order)).State);
    }
    private static Task MoveRacksOutAsync(RoutingKit kit, string location) => kit.H.Db(async db =>
    {
        var id = (await db.Locations.SingleAsync(l => l.Code == location)).Id;
        db.Racks.RemoveRange(await db.Racks.Where(r => r.CurrentLocationId == id).ToListAsync()); await db.SaveChangesAsync(); return 0;
    });

    // ================================================================== admission and scope

    [Fact]
    public async Task Only_a_Published_and_Ready_process_enters_the_queue_and_gets_one_position()
    {
        await using var kit = await KitAsync();
        var (p, v) = await kit.CreateProcessAsync("Q-A", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var refused = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("n", "A1", RequestType.PickupRequest, RackKind.Full, "Q-A"));
        Assert.Equal("PROCESS_NOT_READY", refused.Code);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));

        await Admin(kit, s => s.SetQueueScopeAsync(p, "area-a", "test"));
        await Admin(kit, s => s.SetReadinessAsync(v, "Ready", "test", "admin"));
        var first = await RequestAsync(kit, "k1", "A1", "Q-A");
        var again = await RequestAsync(kit, "k1", "A1", "Q-A");   // the same request key: the same request, the same position
        Assert.Equal(first.Id, again.Id); Assert.Equal(first.QueueSequence, again.QueueSequence);
        Assert.NotNull(first.QueueSequence); Assert.Equal("AREA-A", first.QueueScope); Assert.Equal(1, await LegsAsync(kit, first.Id));
    }

    [Fact]
    public async Task Without_a_declared_scope_a_request_waits_and_nothing_is_reserved_then_it_runs_once_the_scope_is_declared()
    {
        await using var kit = await KitAsync();
        var (p, _) = await ProcessAsync(kit, "Q-S", "A1", null, false, "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await RequestAsync(kit, "k", "A1", "Q-S");
        await kit.TickAsync(); await kit.TickAsync();
        var waiting = await kit.OrderAsync(order.Id);
        Assert.Equal(OrderState.Waiting, waiting.State); Assert.Equal(WaitKind.QueueScopeUndetermined, waiting.WaitKind); Assert.Empty(waiting.Legs); Assert.Null(waiting.QueueScope);
        Assert.Equal(0, await kit.H.Db(d => d.Reservations.CountAsync()));
        var startable = (await Admin(kit, s => s.StartableAsync())).Single();
        Assert.True(startable.Queued); Assert.Null(startable.QueueScope);

        await Admin(kit, s => s.SetQueueScopeAsync(p, "S", "approved"));
        await kit.TickAsync();
        var run = await kit.OrderAsync(order.Id);
        Assert.Equal("S", run.QueueScope); Assert.Single(run.Legs); Assert.Equal(order.QueueSequence, run.QueueSequence);
    }

    // ================================================================== strict FIFO

    [Fact]
    public async Task In_one_scope_a_newer_request_never_takes_the_freed_slot_ahead_of_an_older_one()
    {
        await using var kit = await KitAsync();
        await ProcessAsync(kit, "Q-1", "A1", "S", false, "C1");
        await ProcessAsync(kit, "Q-2", "A2", "S", false, "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);   // C1 is full
        var older = await RequestAsync(kit, "older", "A1", "Q-1");
        var newer = await RequestAsync(kit, "newer", "A2", "Q-2");
        Assert.Equal(0, await LegsAsync(kit, older.Id));

        await MoveRacksOutAsync(kit, "C1");   // the slot frees up
        var third = await RequestAsync(kit, "third", "A2", "Q-2");   // a request that arrives right now is evaluated before any scheduler pass
        Assert.Equal(0, await LegsAsync(kit, third.Id));
        await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal(1, await LegsAsync(kit, older.Id));
        Assert.Equal(0, await LegsAsync(kit, newer.Id));
        Assert.Equal(0, await LegsAsync(kit, third.Id));
        Assert.Equal("C1", (await kit.OrderAsync(older.Id)).Legs.Single().DestinationLocationCode);
        var behind = await kit.OrderAsync(newer.Id);
        Assert.Equal(OrderState.Waiting, behind.State);

        await FinishAsync(kit, older.Id);
        await MoveRacksOutAsync(kit, "C1"); await kit.TickAsync();
        Assert.Equal(1, await LegsAsync(kit, newer.Id)); Assert.Equal(0, await LegsAsync(kit, third.Id));
    }

    [Fact]
    public async Task A_blocked_head_makes_the_rest_of_its_scope_wait_even_when_they_could_run_while_another_scope_runs()
    {
        await using var kit = await KitAsync();
        await ProcessAsync(kit, "Q-1", "A1", "S", false, "C1");
        await ProcessAsync(kit, "Q-2", "A2", "S", false, "F1");   // free destination, but the same scope
        await ProcessAsync(kit, "Q-3", "B1", "T", false, "F2");   // another scope
        foreach (var l in new[] { "A1", "A2", "B1", "C1" }) await kit.H.StockAsync(l, RackKind.Full, 1);
        var head = await RequestAsync(kit, "head", "A1", "Q-1");
        var second = await RequestAsync(kit, "second", "A2", "Q-2");
        var other = await RequestAsync(kit, "other", "B1", "Q-3");
        await kit.TickAsync();
        Assert.Equal(0, await LegsAsync(kit, head.Id));
        Assert.Equal(0, await LegsAsync(kit, second.Id));
        Assert.Equal(WaitKind.QueueBehind, (await kit.OrderAsync(second.Id)).WaitKind);
        Assert.Equal(1, await LegsAsync(kit, other.Id));

        await kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(head.Id, "test", "admin"));   // a cancelled head leaves the queue
        await kit.TickAsync();
        Assert.Equal(1, await LegsAsync(kit, second.Id));
    }

    [Fact]
    public async Task The_source_rack_goes_to_the_oldest_request_first()
    {
        await using var kit = await KitAsync();
        var (p, v) = await kit.CreateProcessAsync("Q-D", RequestType.DeliveryRequest, RackKind.Full, ["A1", "A2"], new("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "F1")]), Done()]));
        await kit.PublishAsync(v); await Admin(kit, s => s.SetQueueScopeAsync(p, "S", "t")); await Admin(kit, s => s.SetReadinessAsync(v, "Ready", "t", "admin"));
        var older = (await kit.StartAsync("o", "A1", RequestType.DeliveryRequest, RackKind.Full, "Q-D")).Order;
        var newer = (await kit.StartAsync("n", "A2", RequestType.DeliveryRequest, RackKind.Full, "Q-D")).Order;
        await kit.H.StockAsync("F1", RackKind.Full, 1);   // one rack appears
        var late = (await kit.StartAsync("late", "A2", RequestType.DeliveryRequest, RackKind.Full, "Q-D")).Order;
        await kit.TickAsync();
        Assert.Equal(1, await LegsAsync(kit, older.Id)); Assert.Equal(0, await LegsAsync(kit, newer.Id)); Assert.Equal(0, await LegsAsync(kit, late.Id));
    }

    [Fact]
    public async Task After_pick_the_destination_slot_also_goes_to_the_oldest_and_the_holder_is_not_sent_to_reconciliation()
    {
        await using var kit = await KitAsync();
        await ProcessAsync(kit, "Q-1", "A1", "S", true, "C1");
        await ProcessAsync(kit, "Q-2", "A2", "S", true, "F1");
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var older = await RequestAsync(kit, "o", "A1", "Q-1");
        var newer = await RequestAsync(kit, "n", "A2", "Q-2");
        await kit.TickAsync();
        Assert.Equal(1, await LegsAsync(kit, older.Id));
        Assert.Equal(0, await LegsAsync(kit, newer.Id));   // the older one has no destination yet: nothing newer is given a resource
        var leg = (await kit.OrderAsync(older.Id)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.TickAsync();
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(older.Id)).State);
        Assert.Equal(0, await LegsAsync(kit, newer.Id));
        await MoveRacksOutAsync(kit, "C1"); await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal("C1", (await kit.OrderAsync(older.Id)).Legs.Single().DestinationLocationCode);
        Assert.Equal(1, await LegsAsync(kit, newer.Id));
    }

    [Fact]
    public async Task Concurrent_requests_of_one_scope_still_receive_resources_in_queue_order()
    {
        await using var kit = await KitAsync();
        var (p, v) = await kit.CreateProcessAsync("Q-C", RequestType.DeliveryRequest, RackKind.Full, ["A1", "A2", "B1", "B2"], new("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "F1")]), Done()]));
        await kit.PublishAsync(v); await Admin(kit, s => s.SetQueueScopeAsync(p, "S", "t")); await Admin(kit, s => s.SetReadinessAsync(v, "Ready", "t", "admin"));
        await kit.H.StockAsync("F1", RackKind.Full, 2);
        await Task.WhenAll(new[] { "A1", "A2", "B1", "B2" }.Select(at => Task.Run(() => kit.StartAsync("c-" + at, at, RequestType.DeliveryRequest, RackKind.Full, "Q-C"))));
        for (var i = 0; i < 3; i++) await kit.TickAsync();
        var orders = await kit.H.Db(d => d.TransportOrders.Include(o => o.Legs).AsNoTracking().OrderBy(o => o.QueueSequence).ToListAsync());
        Assert.Equal([1, 1, 0, 0], orders.Select(o => o.Legs.Count).ToArray());
        Assert.Equal(4, orders.Select(o => o.QueueSequence).Distinct().Count());
    }

    // ================================================================== Held

    [Fact]
    public async Task A_held_request_keeps_its_position_blocks_nothing_outside_its_scope_and_returns_to_its_turn()
    {
        await using var kit = await KitAsync();
        var (_, v1) = await ProcessAsync(kit, "Q-1", "A1", "S", false, "C1");
        await ProcessAsync(kit, "Q-2", "A2", "S", false, "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var older = await RequestAsync(kit, "o", "A1", "Q-1");
        var newer = await RequestAsync(kit, "n", "A2", "Q-2");

        await Admin(kit, s => s.SetReadinessAsync(v1, "Suspended", "station maintenance", "admin"));
        await kit.TickAsync();
        var held = await kit.OrderAsync(older.Id);
        Assert.True(held.QueueHeld); Assert.Equal(WaitKind.QueueHeld, held.WaitKind); Assert.Equal(older.QueueSequence, held.QueueSequence);
        await MoveRacksOutAsync(kit, "C1"); await kit.TickAsync();
        Assert.Equal(0, await LegsAsync(kit, older.Id));
        Assert.Equal(0, await LegsAsync(kit, newer.Id));   // OQ-58 undecided: the Held head is not bypassed

        await Admin(kit, s => s.SetReadinessAsync(v1, "Ready", "back", "admin"));
        await kit.TickAsync();
        var released = await kit.OrderAsync(older.Id);
        Assert.False(released.QueueHeld); Assert.Equal(older.QueueSequence, released.QueueSequence); Assert.Empty(released.Legs);   // never dispatched in the pass that releases it
        await kit.TickAsync();
        Assert.Equal(1, await LegsAsync(kit, older.Id)); Assert.Equal(0, await LegsAsync(kit, newer.Id));
        var kinds = await kit.H.Db(d => d.RoutingEvents.AsNoTracking().Where(e => e.OrderId == older.Id).Select(e => e.Kind).ToListAsync());
        Assert.Contains("QUEUE_HELD", kinds); Assert.Contains("QUEUE_RESUMED", kinds);
    }

    [Fact]
    public async Task A_new_published_version_does_not_move_a_queued_request_off_its_pinned_version()
    {
        await using var kit = await KitAsync();
        var (p, v1) = await ProcessAsync(kit, "Q-P", "A1", "S", false, "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var order = await RequestAsync(kit, "o", "A1", "Q-P");
        var v2 = await Admin(kit, async s => { var nv = await s.NewVersionAsync(p, v1, "admin"); await s.SaveDefinitionAsync(nv.Id, new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "F1")]), Done()])); return nv.Id; });
        await kit.PublishAsync(v2);
        await MoveRacksOutAsync(kit, "C1"); await kit.TickAsync();
        var run = await kit.OrderAsync(order.Id);
        Assert.Equal(1, run.ProcessVersionNumber); Assert.Equal("C1", run.Legs.Single().DestinationLocationCode);
    }

    // ================================================================== PLC simulation through the one intake

    [Fact]
    public async Task The_simulation_needs_only_the_Admins_confirmation_and_sends_PLC_signals_through_the_same_intake()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);   // no real RCS, no deployment switch
        await kit.H.Db(async db =>
        {   // the PLC → WCS points the intake reads, on port A1 (test data, approved like the other test point of the port; nothing is written to a PLC)
            var i = 10;
            foreach (var (name, type) in new[] { ("Pickup_Request", PlcDataType.Bool), ("Request_ID", PlcDataType.String), ("Rack_Full", PlcDataType.Int), ("Station_Ready", PlcDataType.Bool) })
            {
                var point = new StationIoPoint("PLC-T1", "A1", i++, "Request", name, PlcSignalDirection.PlcToSoftware, type, "ref", "test", kit.H.Clock.UtcNow);
                point.ConfigureAddress($"D{1500 + i}", type == PlcDataType.String ? 10 : null, null, kit.H.Clock.UtcNow); point.Approve("engineer", kit.H.Clock.UtcNow); db.StationIoPoints.Add(point);
            }
            await db.SaveChangesAsync(); return 0;
        });
        var (_, v) = await kit.CreateProcessAsync("T-SIM", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Task<T> Sim<T>(Func<PlcSimulationService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<PlcSimulationService>()));
        var service = await Sim(s => Task.FromResult(s));
        kit.Link.Offline.Add("PLC-T1");   // the physical PLC of this port is not connected

        Assert.Equal("SIMULATION_NOT_ACTIVE", (await Assert.ThrowsAsync<AppException>(() => Sim(s => s.SetSignalAsync("A1", "Pickup_Request", "on")))).Code);
        Assert.Equal("SIMULATION_CONFIRMATION_REQUIRED", Assert.Throws<AppException>(() => service.Activate("admin", false)).Code);
        Assert.True(service.Activate("admin", true).Active);
        Assert.Equal("SIGNAL_NOT_A_PLC_INPUT", (await Assert.ThrowsAsync<AppException>(() => Sim(s => s.SetSignalAsync("A1", "Request_Ack", "on")))).Code);   // a WCS → PLC signal cannot be set
        await Sim(s => s.SetSignalAsync("A1", "Pickup_Request", "ON"));
        await Sim(s => s.SetSignalAsync("A1", "Request_ID", "SIM-1"));
        var station = await Sim(s => s.SetSignalAsync("A1", "Rack_Full", "1"));
        Assert.Equal("true", station.Signals.Single(x => x.Signal == "Pickup_Request").Value);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));   // setting signals starts nothing
        Assert.Equal("SAFETY_ACK_REQUIRED", (await Assert.ThrowsAsync<AppException>(() => Sim(s => s.EvaluateStationAsync("A1", false)))).Code);

        var result = await Sim(s => s.EvaluateStationAsync("A1", true));
        Assert.True(result.Outcome == "Accepted", result.Code + ": " + result.Message);
        Assert.Equal("PLC-T1:SIM-1", result.Order!.RequestKey);
        Assert.Equal("Simulator", result.Order.ExecutionMode);
        var again = await Sim(s => s.EvaluateStationAsync("A1", true));   // the same read twice: the intake's own duplicate rule
        Assert.Equal("Duplicate", again.Outcome);
        Assert.Equal(1, await kit.H.Db(d => d.TransportOrders.CountAsync()));
        var listed = (await Sim(s => s.ListStationsAsync())).Single(x => x.StationId == "A1");
        Assert.NotNull(listed.ActiveTransaction);

        // a physical caller of the intake gets exactly the same answer for the same signals
        var physical = await kit.PlcRequestAsync("PLC-T1", "A1", "SIM-1", true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.Duplicate, physical.Outcome);

        kit.Link.Offline.Remove("PLC-T1");
        // CR-050 (D-01): in the simulator PLC runtime a connected PLC is a simulated one and does not block (the refusal for a physical PLC: Cr050SimulatorTests)
        await Sim(s => s.SetSignalAsync("A1", "Pickup_Request", "off"));
        service.Deactivate();
        Assert.All((await Sim(s => s.ListStationsAsync())).Single(x => x.StationId == "A1").Signals, s => Assert.Null(s.Value));
    }

    // ================================================================== racks

    private static Task<T> Racks<T>(RoutingKit kit, Func<RackAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<RackAdminService>()));
    private static async Task<string> CodeOf(Func<Task> f) => (await Assert.ThrowsAsync<AppException>(f)).Code;

    [Fact]
    public async Task A_rack_can_be_created_corrected_moved_and_removed_and_its_history_stays()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        Assert.Equal("REASON_REQUIRED", await CodeOf(() => Racks(kit, s => s.CreateAsync(new("F1", RackKind.Full, "R-1", null, " "), "admin"))));
        var rack = await Racks(kit, s => s.CreateAsync(new("F1", RackKind.Full, "R-1", null, "found on site"), "admin"));
        Assert.Equal("RACK_ID_IN_USE", await CodeOf(() => Racks(kit, s => s.CreateAsync(new("F2", RackKind.Full, "R-1", null, "dup"), "admin"))));
        Assert.Equal("RACK_KIND_NOT_ALLOWED", await CodeOf(() => Racks(kit, s => s.CreateAsync(new("F3", RackKind.Full, null, null, "x"), "admin"))));
        await kit.H.StockAsync("C1", RackKind.Full, 1);
        Assert.Equal("CAPACITY_EXCEEDED", await CodeOf(() => Racks(kit, s => s.UpdateAsync(rack.Id, new("C1", null, null, "move"), "admin"))));
        var moved = await Racks(kit, s => s.UpdateAsync(rack.Id, new("F2", null, "R-2", "put on F2"), "admin"));
        Assert.Equal("R-2", moved.ExternalRackId); Assert.Equal((await kit.H.LocationAsync("F2")).Id, moved.CurrentLocationId);
        await Racks(kit, s => s.DeleteAsync(rack.Id, "scrapped", "admin"));
        Assert.Null(await kit.H.Db(d => d.Racks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rack.Id)));
        var history = await kit.H.Db(d => d.InventoryTransactions.AsNoTracking().Where(t => t.RackId == rack.Id).Select(t => t.Note).ToListAsync());
        Assert.Equal(3, history.Count); Assert.Contains(history, n => n!.Contains("removed"));
    }

    [Fact]
    public async Task A_reserved_moving_or_order_bound_rack_cannot_be_changed_and_the_reason_is_said()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-RK", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var id = (await kit.RackAtAsync("A1"))!.Id;
        var order = (await kit.StartAsync("k", "A1", RequestType.PickupRequest, RackKind.Full, "T-RK")).Order;
        Assert.Equal("RACK_RESERVED", await CodeOf(() => Racks(kit, s => s.DeleteAsync(id, "x", "admin"))));
        Assert.Equal("CAPACITY_EXCEEDED", await CodeOf(() => Racks(kit, s => s.CreateAsync(new("C1", RackKind.Full, null, null, "the slot is reserved"), "admin"))));
        await kit.PickAsync(order.Legs.Single().Id);
        Assert.Equal("RACK_IN_TRANSPORT", await CodeOf(() => Racks(kit, s => s.UpdateAsync(id, new("F1", null, null, "x"), "admin"))));
        await kit.DropAsync(order.Legs.Single().Id);
        await Racks(kit, s => s.UpdateAsync(id, new(null, RackKind.Empty, null, "emptied"), "admin"));   // free again once the order is finished
    }

    // ================================================================== migration

    private static WcsDbContext Context(string cs) => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
    private static async Task<string?> ScalarAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    [Fact]
    public async Task The_new_migrations_go_down_and_up_again_and_refuse_to_drop_an_active_queue()
    {
        var scratch = new DatabaseFixture();
        await scratch.InitializeAsync();
        try
        {
            const string beforeBoth = "20261125085557_AddExecutionModeAndRcsMessages";
            var cs = scratch.ConnectionString;
            Guid orderId, version;
            await using (var kit = await WithoutPlcPermissionAsync(await RoutingKit.CreateAsync(scratch, seedPorts: false, extraConfig: Queue)))
            {
                var (_, v) = await ProcessAsync(kit, "Q-M", "A1", null, false, "C1");
                version = v;
                orderId = (await RequestAsync(kit, "m", "A1", "Q-M")).Id;
                var refused = await Assert.ThrowsAnyAsync<Exception>(() => Context(cs).GetService<IMigrator>().MigrateAsync(beforeBoth));
                Assert.Contains("ROLLBACK_REFUSED", refused.ToString());
                await Context(cs).Database.MigrateAsync();   // CR-041: later migrations were already rolled back before the refusal; bring them back
                await kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(orderId, "rollback", "admin"));
                await Admin(kit, s => s.DisableAsync(version));
            }
            await Context(cs).GetService<IMigrator>().MigrateAsync(beforeBoth);
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM information_schema.columns WHERE column_name IN ('QueueScope','QueueSequence','QueueHeld','RequiresReadiness')"));
            Assert.Equal("1", await ScalarAsync(cs, "SELECT COUNT(*) FROM transport_orders"));   // the history stays
            await Context(cs).Database.MigrateAsync();
            Assert.Equal("5", await ScalarAsync(cs, "SELECT COUNT(*) FROM information_schema.columns WHERE column_name IN ('QueueScope','QueueSequence','QueueHeld','RequiresReadiness')"));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
