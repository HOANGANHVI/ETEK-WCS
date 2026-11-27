using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Rcs;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-050: the AMR simulation behind the simulated RCS executes transport work through the interfaces and entry points WCS already uses. Real PostgreSQL, real engines, no device.</summary>
[Collection("Database")]
public class Cr050SimulatorTests(DatabaseFixture fixture)
{
    private static AmrExecutionSimulator Sim(RoutingKit kit) => kit.H.Services.GetRequiredService<AmrExecutionSimulator>();

    private async Task<RoutingKit> KitAsync(string resolution = "BeforePick", IReadOnlyDictionary<string, string?>? config = null)
    {
        var kit = await RoutingKit.CreateAsync(fixture, extraConfig: config);
        kit.H.Rcs.SetFleetSize(2);
        var (_, v) = await kit.CreateProcessAsync("T-AMR", RequestType.PickupRequest, RackKind.Full, ["A1"],
            new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "B1"), Dest(1, "B2", order: 2)], resolution), Done()]));
        await kit.PublishAsync(v);
        return kit;
    }

    private static void On(RoutingKit kit, double speed = 5) => Sim(kit).Configure(true, speed);

    /// <summary>Engine passes and simulator steps of half a second until the condition holds.</summary>
    private static async Task<bool> RunAsync(RoutingKit kit, Func<Task<bool>> until, int steps = 150)
    {
        for (var i = 0; i < steps; i++)
        {
            if (await until()) return true;
            await kit.TickAsync();
            kit.H.Clock.Advance(TimeSpan.FromSeconds(1));
            await Sim(kit).StepAsync(TimeSpan.FromSeconds(0.5));
        }
        return await until();
    }

    private static async Task<Guid> StartAsync(RoutingKit kit, string key = "R1") => (await kit.StartAsync(key, "A1", RequestType.PickupRequest, RackKind.Full)).Order.Id;
    private static Func<Task<bool>> Completed(RoutingKit kit, Guid order) => async () => (await kit.OrderAsync(order)).State == OrderState.Completed;
    private static Func<Task<bool>> Released(RoutingKit kit) => async () => (await kit.TransactionsAsync()).All(t => !t.IsActive);
    private static Task<int> ActiveReservations(RoutingKit kit) => kit.H.Db(d => d.Reservations.CountAsync(r => r.Status == ReservationStatus.Active));
    private static Task<List<string>> Events(RoutingKit kit, Guid order) => kit.H.Db(d => d.RoutingEvents.Where(e => e.OrderId == order).Select(e => e.Kind).ToListAsync());

    [Theory]
    [InlineData("BeforePick")]
    [InlineData("AfterPick")]
    public async Task An_order_is_picked_dropped_and_both_ports_are_released_without_any_click_and_the_next_one_follows(string resolution)
    {
        await using var kit = await KitAsync(resolution);
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var first = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await Completed(kit, first)()) && await Released(kit)()), Describe(kit));
        Assert.NotNull(await kit.RackAtAsync("B1"));
        Assert.Equal(0, await ActiveReservations(kit));
        var inventory = await kit.H.Db(d => d.InventoryTransactions.Where(t => t.Source == ConfirmationSource.Simulator).Select(t => t.EventType).ToListAsync());
        Assert.Equal(1, inventory.Count(e => e == InventoryEventType.PickConfirmed));
        Assert.Equal(1, inventory.Count(e => e == InventoryEventType.DropConfirmed));
        var messages = await kit.H.Db(d => d.RcsMessages.Select(m => new { m.MessageType, m.Mode }).ToListAsync());
        Assert.All(messages, m => Assert.Equal(ExecutionMode.Simulator, m.Mode));
        foreach (var name in new[] { "PickConfirmed", "DropConfirmed", "AMR left port" }) Assert.Contains(messages, m => m.MessageType == $"Simulated AGV Event: {name}");

        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var second = await StartAsync(kit, "R2");
        Assert.True(await RunAsync(kit, Completed(kit, second)), Describe(kit));
        Assert.NotNull(await kit.RackAtAsync("B2"));
        Assert.True(await RunAsync(kit, Released(kit)), Describe(kit));
        Assert.All(Sim(kit).View().Robots, r => Assert.Equal("Idle", r.State));
        Assert.Equal(0, await kit.H.Db(d => d.PlcExchangeLog.CountAsync()));
    }

    [Fact]
    public async Task Switched_off_nothing_moves_and_work_sent_before_it_was_switched_on_is_never_taken_over()
    {
        await using var kit = await KitAsync();
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit);
        await kit.TickAsync(); await kit.TickAsync();
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        Assert.NotNull(leg.AgvTaskCode);
        for (var i = 0; i < 20; i++) await Sim(kit).StepAsync(TimeSpan.FromSeconds(1));
        On(kit, 50);
        Assert.False(await RunAsync(kit, async () => (await kit.OrderAsync(order)).Legs.Single().PickConfirmedAt is not null, 30));
        Assert.Empty(Sim(kit).View().Runs);

        // a port that was held before stays held: nothing is released by time
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id);
        Assert.False(await RunAsync(kit, Released(kit), 30));
        Assert.All((await kit.TransactionsAsync()).Where(t => t.IsActive), t => Assert.Equal(StationTransactionState.HandoverCompleted, t.State));
    }

    [Fact]
    public async Task A_rejected_dispatch_fails_the_order_as_a_rejection_of_the_AGV_system_does_and_a_timeout_is_retried()
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Sim(kit).Arm(AmrFault.RejectDispatch, 1, null);
        var rejected = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await kit.OrderAsync(rejected)).State == OrderState.Failed));
        Assert.Equal(0, await ActiveReservations(kit));
        Assert.All(await kit.TransactionsAsync(rejected), t => Assert.Equal(StationTransactionState.Failed, t.State));   // nothing was sent: the ports are free again

        Sim(kit).Arm(AmrFault.DispatchTimeout, 2, null);
        var late = await StartAsync(kit, "R2");
        Assert.True(await RunAsync(kit, Completed(kit, late)), Describe(kit));
        Assert.Contains("LEG_DISPATCH_PENDING", await Events(kit, late));
    }

    [Fact]
    public async Task An_order_created_before_the_simulation_was_switched_on_is_not_executed_even_when_its_leg_leaves_afterwards()
    {
        await using var kit = await KitAsync();
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var first = await StartAsync(kit);
        await kit.TickAsync(); await kit.TickAsync();
        var held = (await kit.OrderAsync(first)).Legs.Single();
        await kit.PickAsync(held.Id); await kit.DropAsync(held.Id);                       // done by hand: both ports stay held
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var waiting = await StartAsync(kit, "R2");
        await kit.TickAsync();
        Assert.All((await kit.OrderAsync(waiting)).Legs, l => Assert.Null(l.AgvTaskCode));
        kit.H.Clock.Advance(TimeSpan.FromSeconds(1));
        On(kit, 50);
        foreach (var t in (await kit.TransactionsAsync()).Where(t => t.IsActive).ToList())
            await kit.With(sp => sp.GetRequiredService<StationTransactionService>().ConfirmAgvExitedAsync(t.Id));
        Assert.True(await RunAsync(kit, async () => (await kit.OrderAsync(waiting)).Legs.SingleOrDefault()?.AgvTaskCode is not null, 30), Describe(kit));
        Assert.False(await RunAsync(kit, async () => (await kit.OrderAsync(waiting)).Legs.Single().PickConfirmedAt is not null, 30));
        Assert.Empty(Sim(kit).View().Runs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_rejection_never_frees_a_port_on_which_something_has_already_happened(bool picked)
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Sim(kit).Arm(AmrFault.DispatchTimeout, 20, null);                                  // sent, no answer: the leg waits without an AGV task
        var order = await StartAsync(kit);
        await kit.TickAsync(); await kit.TickAsync();
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        Assert.Null(leg.AgvTaskCode);
        var port = await kit.H.Db(d => d.PlcStations.Where(p => p.LocationCode == "A1" && p.RetiredAt == null).Select(p => p.Id).SingleAsync());
        var atSource = (await kit.TransactionsAsync(order)).Single(t => t.PortId == port);
        Sim(kit).ClearFaults();
        Sim(kit).Arm(AmrFault.RejectDispatch, 1, null);
        if (picked) await kit.PickAsync(leg.Id);                                            // confirmed by hand while the answer is still open
        else await kit.With(sp => sp.GetRequiredService<StationTransactionService>().AdvanceAsync(atSource.Id, StationTransactionState.AtStation));
        Assert.True(await RunAsync(kit, async () => (await kit.OrderAsync(order)).State == OrderState.Failed), Describe(kit));
        var after = await kit.TransactionsAsync(order);
        Assert.Equal(StationTransactionState.ReconciliationRequired, after.Single(t => t.Id == atSource.Id).State);
        Assert.All(after.Where(t => t.Id != atSource.Id), t => Assert.Equal(picked ? StationTransactionState.ReconciliationRequired : StationTransactionState.Failed, t.State));
        if (picked) Assert.Equal(RackStatus.InTransit, (await kit.RacksAsync()).Single().Status);
    }

    [Theory]
    [InlineData(AmrFault.MoveFailure, false, false)]
    [InlineData(AmrFault.PickFailure, false, false)]
    [InlineData(AmrFault.DropFailure, true, false)]
    public async Task A_failed_leg_is_recorded_changes_nothing_in_WCS_and_goes_on_after_the_recovery(AmrFault fault, bool picked, bool dropped)
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Sim(kit).Arm(fault, 1, null);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await Events(kit, order)).Contains("LEG_RCS_INTERRUPTED")), Describe(kit));
        await RunAsync(kit, () => Task.FromResult(false), 20);
        var o = await kit.OrderAsync(order); var leg = o.Legs.Single();
        Assert.Equal((OrderState.InProgress, LegState.InProgress), (o.State, leg.State));
        Assert.Equal((picked, dropped), (leg.PickConfirmedAt is not null, leg.DropConfirmedAt is not null));
        var rack = (await kit.RacksAsync()).Single();
        Assert.Equal(picked ? RackStatus.InTransit : RackStatus.Full, rack.Status);
        Assert.True(await ActiveReservations(kit) > 0);

        await Sim(kit).RecoverAsync(leg.AgvRequestCode);
        Assert.True(await RunAsync(kit, async () => (await Completed(kit, order)()) && await Released(kit)()), Describe(kit));
    }

    [Fact]
    public async Task A_stalled_AMR_a_late_answer_and_a_repeated_answer_leave_one_pick_and_one_drop()
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Sim(kit).Arm(AmrFault.ExecutionStall, 1, null);
        Sim(kit).Arm(AmrFault.ResponseDelay, 1, 5);
        Sim(kit).Arm(AmrFault.DuplicateResponse, 2, null);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, () => Task.FromResult(Sim(kit).View().Runs.Any(r => r.Stalled))));
        await RunAsync(kit, () => Task.FromResult(false), 20);
        var leg = (await kit.OrderAsync(order)).Legs.Single();
        Assert.Null(leg.PickConfirmedAt);
        await Sim(kit).RecoverAsync(leg.AgvRequestCode);
        Assert.True(await RunAsync(kit, Completed(kit, order)), Describe(kit));
        var events = Sim(kit).View().Runs.Single().Events;
        Assert.Contains(events, e => e.Contains("response delayed"));
        Assert.Equal(2, events.Count(e => e.Contains("sent again: Duplicate")));
        Assert.Equal(2, await kit.H.Db(d => d.InventoryTransactions.CountAsync(t => t.Source == ConfirmationSource.Simulator)));
    }

    [Fact]
    public async Task While_the_connection_is_lost_nothing_reaches_WCS_and_the_work_ends_after_it_is_back()
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, () => Task.FromResult(Sim(kit).View().Runs.Any(r => r.Phase == "ToSource" || r.Phase == "Picking"))));
        Sim(kit).SetConnected(false);
        await RunAsync(kit, () => Task.FromResult(false), 30);
        Assert.Null((await kit.OrderAsync(order)).Legs.Single().PickConfirmedAt);
        Sim(kit).SetConnected(true);
        Assert.True(await RunAsync(kit, async () => (await Completed(kit, order)()) && await Released(kit)()), Describe(kit));
    }

    [Fact]
    public async Task A_destination_trigger_lost_with_the_connection_is_repeated_only_on_request()
    {
        await using var kit = await KitAsync("AfterPick");
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.StockAsync("B1", RackKind.Full, 1); await kit.H.StockAsync("B2", RackKind.Full, 1);   // no destination at pick time
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, () => Task.FromResult(Sim(kit).View().Runs.Any(r => r.Phase == "WaitingForTrigger"))), Describe(kit));
        Sim(kit).SetConnected(false);
        await kit.H.Db(async d => { var b1 = await d.Locations.SingleAsync(l => l.Code == "B1"); d.Racks.RemoveRange(d.Racks.Where(r => r.CurrentLocationId == b1.Id)); await d.SaveChangesAsync(); return 0; });
        Assert.True(await RunAsync(kit, async () => (await kit.OrderAsync(order)).Legs.Single().DestinationLocationCode == "B1"));
        Sim(kit).SetConnected(true);
        Assert.False(await RunAsync(kit, Completed(kit, order), 30));   // WCS does not send the trigger again by itself
        await Sim(kit).RecoverAsync((await kit.OrderAsync(order)).Legs.Single().AgvRequestCode);
        Assert.True(await RunAsync(kit, Completed(kit, order)), Describe(kit));
    }

    [Fact]
    public async Task A_named_simulated_fleet_is_the_whole_fleet_and_carries_the_legs()
    {
        await using var kit = await KitAsync();
        var codes = await kit.H.Db(d => d.Agvs.OrderBy(a => a.Code).Select(a => a.RcsRobotCode!).ToListAsync());
        kit.H.Rcs.SetFleetSize(2, codes);
        On(kit);
        Assert.Equal(codes.Order(StringComparer.Ordinal), Sim(kit).View().Robots.Select(r => r.RobotCode));
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await Completed(kit, order)()) && await Released(kit)()), Describe(kit));
        Assert.Equal(2, Sim(kit).View().Robots.Count);
        Assert.Contains(Sim(kit).View().Runs.Single().RobotCode, codes);
    }

    [Fact]
    public async Task An_AMR_that_does_not_leave_keeps_the_destination_port_until_it_really_leaves()
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        Sim(kit).Arm(AmrFault.ExitStall, 1, null);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await Completed(kit, order)()) && Sim(kit).View().Runs.Any(r => r.Stalled)), Describe(kit));
        await RunAsync(kit, () => Task.FromResult(false), 30);                              // time alone releases nothing
        var b1 = await kit.H.Db(d => d.PlcStations.Where(p => p.LocationCode == "B1" && p.RetiredAt == null).Select(p => p.Id).SingleAsync());
        Assert.Equal(StationTransactionState.HandoverCompleted, (await kit.TransactionsAsync(order)).Single(t => t.PortId == b1).State);
        await Sim(kit).RecoverAsync((await kit.OrderAsync(order)).Legs.Single().AgvRequestCode);
        Assert.True(await RunAsync(kit, Released(kit)), Describe(kit));
    }

    [Fact]
    public async Task Switched_off_in_the_middle_the_work_is_finished_by_hand_and_is_not_taken_over_again()
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = await StartAsync(kit);
        Assert.True(await RunAsync(kit, async () => (await kit.OrderAsync(order)).Legs.SingleOrDefault()?.PickConfirmedAt is not null), Describe(kit));
        Sim(kit).Configure(false, 5);                                                       // what a restart does too: the runs are forgotten
        kit.H.Clock.Advance(TimeSpan.FromSeconds(1));
        On(kit, 50);
        Assert.False(await RunAsync(kit, Completed(kit, order), 30));
        Assert.Empty(Sim(kit).View().Runs);
        await kit.DropAsync((await kit.OrderAsync(order)).Legs.Single().Id);
        foreach (var x in (await kit.TransactionsAsync()).Where(x => x.IsActive).ToList())
            await kit.With(sp => sp.GetRequiredService<StationTransactionService>().ConfirmAgvExitedAsync(x.Id));
        Assert.True(await RunAsync(kit, async () => (await Completed(kit, order)()) && await Released(kit)()), Describe(kit));
        Assert.NotNull(await kit.RackAtAsync("B1"));
        Assert.Equal(0, await ActiveReservations(kit));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AmrFault.PickFailure)]
    public async Task A_task_of_the_task_engine_runs_to_its_end_and_an_interrupted_one_takes_the_approved_path(AmrFault? fault)
    {
        await using var kit = await KitAsync();
        On(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        if (fault is { } f) Sim(kit).Arm(f, 1, null);
        var task = (await kit.H.CreateTaskAsync("Bp01", "A1")).Task.Id;
        var wanted = fault is null ? TaskState.Completed : TaskState.Failed;
        var done = false;
        for (var i = 0; i < 200 && !done; i++)
        {
            await kit.H.TickAsync(); kit.H.Clock.Advance(TimeSpan.FromSeconds(1)); await Sim(kit).StepAsync(TimeSpan.FromSeconds(0.5));
            done = (await kit.H.GetTaskAsync(task)).State == wanted;
        }
        Assert.True(done, $"{(await kit.H.GetTaskAsync(task)).State}; {Describe(kit)}");
        var rack = (await kit.RacksAsync()).Single();
        if (fault is null) Assert.Equal((await kit.H.LocationAsync("B1")).Id, rack.CurrentLocationId);
        else { Assert.Equal(RackStatus.Unknown, rack.Status); Assert.True((await kit.H.LocationAsync("A1")).ReconciliationRequired); }
    }

    [Fact]
    public async Task The_simulation_is_refused_with_the_real_dispatch_and_faults_need_it_on()
    {
        await using var kit = await KitAsync(config: new Dictionary<string, string?> { ["Rcs:RealDispatch:Enabled"] = "true" });
        Assert.Equal("AMR_SIMULATION_REAL_DISPATCH", Assert.Throws<AppException>(() => Sim(kit).Configure(true, 1)).Code);
        Assert.Equal("AMR_SIMULATION_OFF", Assert.Throws<AppException>(() => Sim(kit).Arm(AmrFault.PickFailure, 1, null)).Code);
        Assert.False(Sim(kit).Enabled);
    }

    [Theory]
    [InlineData("Simulator", false)]
    [InlineData("RealReadOnly", true)]
    [InlineData("RealReadWrite", true)]
    public async Task The_PLC_simulation_is_refused_only_for_a_physical_PLC(string mode, bool refused)
    {
        await using var kit = await KitAsync(config: new Dictionary<string, string?> { ["Plc:Mode"] = mode });
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var service = kit.H.Services.GetRequiredService<PlcSimulationState>();
        service.Set(true, "admin", kit.H.Clock.UtcNow);
        Task<T> Call<T>(Func<PlcSimulationService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<PlcSimulationService>()));
        // the PLC of the port is "connected" (the fake link answers online)
        var station = (await Call(s => s.ListStationsAsync())).Single(x => x.StationId == "A1");
        Assert.Equal((true, refused), (station.PlcOnline == true, station.Blocked));
        if (refused)
            Assert.Equal("SIMULATION_PLC_ONLINE", (await Assert.ThrowsAsync<AppException>(() => Call(s => s.ResetStationAsync("A1")))).Code);
        else
            await Call(s => s.ResetStationAsync("A1"));
    }

    private static string Describe(RoutingKit kit) => string.Join(" | ", Sim(kit).View().Runs.Select(r => $"{r.RequestCode} {r.Phase} {r.Failure} [{string.Join("; ", r.Events.TakeLast(8))}]"));
}
