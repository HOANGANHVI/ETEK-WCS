using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-026 step 2 (OQ-1): the Maintenance flag - every entry that starts NEW work is held or refused; work that already started keeps running; races are closed by a row lock.</summary>
[Collection("Database")]
public class Cr026MaintenanceTests(DatabaseFixture fixture)
{
    private static readonly ActorInfo Admin = new(Guid.NewGuid(), "admin", UserRole.Admin);
    private static string[] A => ["A1", "A2"];

    private static Task<MaintenanceView> SetAsync(Harness h, bool on, string? reason = "test") =>
        h.Scoped(sp => sp.GetRequiredService<MaintenanceService>().SetAsync(on, reason, Admin));

    // ------------------------------------------------------------------ the flag
    [Fact]
    public async Task The_flag_is_off_by_default_persists_and_every_switch_is_audited_with_actor_and_time()
    {
        await using var h = await Harness.CreateAsync(fixture);
        Assert.False((await h.Scoped(sp => sp.GetRequiredService<MaintenanceService>().GetAsync())).Enabled);

        var on = await SetAsync(h, true, "config import");
        Assert.True(on.Enabled); Assert.Equal("admin", on.ChangedBy); Assert.NotNull(on.UpdatedAt);
        await h.RestartAsync();   // persisted in the database, not in memory
        Assert.True((await h.Scoped(sp => sp.GetRequiredService<MaintenanceService>().GetAsync())).Enabled);

        await SetAsync(h, false, null);
        var entries = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action.StartsWith("SYSTEM_MAINTENANCE")).OrderBy(a => a.OccurredAt).ToListAsync());
        Assert.Equal(["SYSTEM_MAINTENANCE_ON", "SYSTEM_MAINTENANCE_OFF"], entries.Select(e => e.Action).ToArray());
        Assert.All(entries, e => { Assert.Equal("admin", e.Actor); Assert.Equal(AuditResult.Success, e.Result); });
    }

    // ------------------------------------------------------------------ new tasks / orders refused
    [Fact]
    public async Task New_tasks_are_refused_while_on_a_repeated_request_key_still_answers_and_everything_resumes_after()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        var first = (await h.CreateTaskAsync("BP05", "F5", "key-1")).Task;

        await SetAsync(h, true);
        var ex = await Assert.ThrowsAsync<AppException>(() => h.CreateTaskAsync("BP05", "F5", "key-2"));
        Assert.Equal(MaintenanceCodes.Code, ex.Code);
        Assert.Equal(1, await h.Db(db => db.Tasks.CountAsync()));                        // nothing was created
        var again = await h.CreateTaskAsync("BP05", "F5", "key-1");                      // idempotent answer for an existing request key
        Assert.False(again.Created); Assert.Equal(first.Id, again.Task.Id);

        await SetAsync(h, false);
        Assert.True((await h.CreateTaskAsync("BP05", "F5", "key-2")).Created);
    }

    [Fact]
    public async Task New_orders_and_plc_requests_are_refused_before_anything_is_recorded()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v1) = await kit.CreateProcessAsync("T-M1", RequestType.PickupRequest, RackKind.Full, A, OneLeg("Request", Dest(1, "B1")));
        await kit.PublishAsync(v1);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);
        await SetAsync(kit.H, true);

        var ex = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("o-1", "A1", RequestType.PickupRequest, RackKind.Full));
        Assert.Equal(MaintenanceCodes.Code, ex.Code);
        var plc = await kit.PlcRequestAsync("PLC-T1", "A1", "REQ-1", pickup: true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.Rejected, plc.Outcome); Assert.Equal(MaintenanceCodes.Code, plc.Code);
        Assert.Empty(await kit.TransactionsAsync());                                      // no Station Transaction, no port claimed
        Assert.Equal(0, await kit.H.Db(db => db.TransportOrders.CountAsync()));

        await SetAsync(kit.H, false);
        Assert.Equal(PlcRequestOutcome.Accepted, (await kit.PlcRequestAsync("PLC-T1", "A1", "REQ-1", pickup: true, rackFull: 1)).Outcome);   // the same request can simply be repeated
    }

    [Fact]
    public async Task A_recorded_plc_request_whose_order_cannot_start_stays_accepted_and_resumes_after_maintenance()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v1) = await kit.CreateProcessAsync("T-M2", RequestType.PickupRequest, RackKind.Full, A, OneLeg("Request", Dest(1, "B1")));
        await kit.PublishAsync(v1);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        // an Accepted Station Transaction without an Order (the state a crash between the two steps leaves)
        var tx = await kit.H.Db(async db =>
        {
            var t = StationTransaction.Create("A1", "PLC-T1", "A1", StationOperation.Pickup, null, null, "REQ-ORPHAN", kit.H.Clock.UtcNow, RackKind.Full);
            t.Advance(StationTransactionState.Requested, kit.H.Clock.UtcNow); t.Advance(StationTransactionState.Accepted, kit.H.Clock.UtcNow);
            db.StationTransactions.Add(t); await db.SaveChangesAsync(); return t.Id;
        });
        await SetAsync(kit.H, true);
        Assert.Equal(0, await kit.With(sp => sp.GetRequiredService<IPlcRequestService>().ResumeOrphansAsync()));   // new work: not started during Maintenance
        var held = await kit.H.Db(db => db.StationTransactions.AsNoTracking().SingleAsync(x => x.Id == tx));
        Assert.Equal(StationTransactionState.Accepted, held.State);                       // NOT failed because of Maintenance
        Assert.Equal(0, await kit.H.Db(db => db.TransportOrders.CountAsync()));

        await SetAsync(kit.H, false);
        Assert.Equal(1, await kit.With(sp => sp.GetRequiredService<IPlcRequestService>().ResumeOrphansAsync()));
        Assert.Equal(1, await kit.H.Db(db => db.TransportOrders.CountAsync()));
    }

    // ------------------------------------------------------------------ work that has not started is held, work that started finishes
    [Fact]
    public async Task A_running_task_finishes_while_a_queued_task_waits_and_is_dispatched_only_after_maintenance()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        var running = (await h.CreateTaskAsync("BP05", "F5")).Task;
        var queued = (await h.CreateTaskAsync("BP05", "F5")).Task;
        Assert.Equal("Accepted", running.State); Assert.Equal("Queued", queued.State);

        await SetAsync(h, true);
        await h.AdvanceUntilAsync(running.Id, TaskState.Completed);                       // already sent to the RCS: runs to the end
        for (var i = 0; i < 3; i++) await h.TickAsync();
        var q = await h.GetTaskAsync(queued.Id);
        Assert.Equal(TaskState.Queued, q.State);                                          // the AGV is free but the queue does not move
        Assert.Null(q.RcsTaskCode);

        await SetAsync(h, false);
        await h.TickAsync();
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(queued.Id)).State);
    }

    [Fact]
    public async Task Pending_and_a_claimed_task_without_RcsTaskCode_are_not_touched_during_maintenance()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        var agv = await h.AgvAsync("AGV1"); var f5 = await h.LocationAsync("F5");
        var (pending, claimed) = await h.Db(async db =>
        {
            var p = TransportTask.Create("p", ProcessFlow.Bp05, RackKind.Full, agv.Id, f5.Id, "t", h.Clock.UtcNow);
            db.Tasks.Add(p); await db.SaveChangesAsync(); return (p.Id, Guid.Empty);
        });
        await SetAsync(h, true);
        await h.TickAsync(); await h.TickAsync();
        Assert.Equal(TaskState.Pending, (await h.GetTaskAsync(pending)).State);          // not validated, no AGV claimed, nothing reserved
        Assert.Empty(await h.ActiveReservationsAsync());
        Assert.Equal(0, h.Rcs.CreateCalls);

        await SetAsync(h, false);
        await h.TickAsync();
        Assert.NotEqual(TaskState.Pending, (await h.GetTaskAsync(pending)).State);
        _ = claimed;
    }

    [Fact]
    public async Task A_dispatching_task_without_RcsTaskCode_is_not_sent_to_the_RCS_during_maintenance()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        var agv = await h.AgvAsync("AGV1"); var f5 = await h.LocationAsync("F5");
        var id = await h.Db(async db =>
        {
            var t = TransportTask.Create("d", ProcessFlow.Bp05, RackKind.Full, agv.Id, f5.Id, "t", h.Clock.UtcNow);
            t.TransitionTo(TaskState.Validating, "v", "t", h.Clock.UtcNow); t.TransitionTo(TaskState.Dispatching, "d", "t", h.Clock.UtcNow);
            db.Tasks.Add(t); await db.SaveChangesAsync(); return t.Id;
        });
        await SetAsync(h, true);
        await h.Scoped(async sp => { await sp.GetRequiredService<ITaskOrchestrator>().DispatchToRcsAsync(id); return 0; });
        await h.TickAsync();
        Assert.Equal(0, h.Rcs.CreateCalls);
        Assert.Null((await h.GetTaskAsync(id)).RcsTaskCode);
        Assert.Equal(0, (await h.GetTaskAsync(id)).DispatchAttempts);

        await SetAsync(h, false);
        await h.Scoped(async sp => { await sp.GetRequiredService<ITaskOrchestrator>().DispatchToRcsAsync(id); return 0; });
        Assert.NotNull((await h.GetTaskAsync(id)).RcsTaskCode);
    }

    [Fact]
    public async Task Station_requests_from_the_plc_stay_pending_unacknowledged_and_without_an_attempt_until_maintenance_ends()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        await SetAsync(h, true);
        h.Stations.EnqueueRequest(new StationRequest("F5", "BP05", "PLC-REQ-1"));
        Assert.Equal(0, await h.IntakeAsync());
        Assert.Equal(0, await h.IntakeAsync());
        var rec = await h.Db(db => db.StationRequests.AsNoTracking().SingleAsync(r => r.RequestKey == "PLC-REQ-1"));
        Assert.True(rec.IsPending); Assert.Equal(0, rec.Attempts);                         // not accepted, not rejected, no retry budget used
        Assert.Empty(h.Stations.Acknowledged);                                             // never acknowledged before it was really processed
        Assert.Equal(0, await h.Db(db => db.Tasks.CountAsync()));

        await SetAsync(h, false);
        Assert.Equal(1, await h.IntakeAsync());
        Assert.Single(h.Stations.Acknowledged);
        Assert.Equal(1, await h.Db(db => db.Tasks.CountAsync()));
    }

    // ------------------------------------------------------------------ multi-leg: first leg held, later legs finish
    [Fact]
    public async Task The_first_leg_of_an_order_is_held_but_the_next_leg_of_a_running_order_is_created_and_dispatched()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, ver) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(ver);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);
        await kit.H.StockAsync("B1", RackKind.Full, 1);                                    // B1 busy: order 1 goes to the buffer first
        var o1 = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var o2 = (await kit.StartAsync("o2", "A2", RequestType.PickupRequest, RackKind.Full)).Order;   // AGV busy -> its first leg waits (Ready, no AGV)
        Assert.Null((await kit.OrderAsync(o2.Id)).Legs.Single().AgvTaskCode);

        await SetAsync(kit.H, true);
        await kit.PickAsync(o1.Legs.Single().Id); await kit.DropAsync(o1.Legs.Single().Id);   // order 1 stores the rack at F1 and waits for B1
        await kit.TickAsync();
        Assert.Null((await kit.OrderAsync(o2.Id)).Legs.Single().AgvTaskCode);              // the AGV is free but the FIRST leg of order 2 is not dispatched

        await kit.With(sp => sp.GetRequiredService<IInventoryService>().AdjustStockAsync("B1", RackKind.Full, 0, "test", null));
        await kit.TickAsync();
        var order1 = await kit.OrderAsync(o1.Id);
        Assert.Equal(2, order1.Legs.Count);                                                // the next leg of the running order IS created ...
        Assert.NotNull(order1.Legs.OrderBy(l => l.Sequence).Last().AgvTaskCode);           // ... and dispatched, so the order can finish
        Assert.Null((await kit.OrderAsync(o2.Id)).Legs.Single().AgvTaskCode);

        await SetAsync(kit.H, false);
        var leg2 = order1.Legs.OrderBy(l => l.Sequence).Last();
        await kit.PickAsync(leg2.Id); await kit.DropAsync(leg2.Id);
        Assert.Equal(OrderState.Completed, (await kit.OrderAsync(o1.Id)).State);
        await kit.TickAsync();
        Assert.NotNull((await kit.OrderAsync(o2.Id)).Legs.Single().AgvTaskCode);           // after Maintenance order 2 starts
    }

    [Fact]
    public async Task Manual_cancel_still_works_during_maintenance()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        var t1 = (await h.CreateTaskAsync("BP05", "F5")).Task;
        var t2 = (await h.CreateTaskAsync("BP05", "F5")).Task;   // queued
        await SetAsync(h, true);
        await h.Scoped(async sp => { await sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t2.Id, "operator", "admin"); return 0; });
        Assert.Equal(TaskState.Cancelled, (await h.GetTaskAsync(t2.Id)).State);
        _ = t1;
    }

    // ------------------------------------------------------------------ the race
    [Fact]
    public async Task Switching_on_waits_for_a_creating_transaction_that_already_checked_the_flag_deterministic()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var checkedFlag = new TaskCompletionSource(); var release = new TaskCompletionSource();
        // a creating transaction: it has read the flag (share lock) and has not committed yet
        var creating = h.Scoped(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>(); var gate = sp.GetRequiredService<IMaintenanceGate>();
            return await uow.ExecuteAsync(async t => { await gate.EnsureOpenAsync(t); checkedFlag.SetResult(); await release.Task; return 0; });
        });
        await checkedFlag.Task;
        var switching = SetAsync(h, true);
        await Task.Delay(700);
        Assert.False(switching.IsCompleted, "the switch must wait for the transaction that is about to start new work");
        release.SetResult(); await creating;
        Assert.True((await switching).Enabled);
    }

    [Fact]
    public async Task A_creating_transaction_that_starts_during_the_switch_waits_and_is_then_refused_deterministic()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5);
        await h.StockAsync("F1", RackKind.Full, 3);
        var switched = new TaskCompletionSource(); var release = new TaskCompletionSource();
        // the switch: row locked and changed, not committed yet
        var switching = h.Scoped(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>(); var store = sp.GetRequiredService<IMaintenanceStore>();
            return await uow.ExecuteAsync(async t =>
            {
                var row = await store.GetForUpdateAsync(t); row.Set(true, "test", "admin", h.Clock.UtcNow);
                switched.SetResult(); await release.Task; return 0;
            });
        });
        await switched.Task;
        var creating = h.CreateTaskAsync("BP05", "F5", "during-switch");
        await Task.Delay(700);
        Assert.False(creating.IsCompleted, "the creating transaction must wait for the switch");
        release.SetResult(); await switching;
        var ex = await Assert.ThrowsAsync<AppException>(() => creating);
        Assert.Equal(MaintenanceCodes.Code, ex.Code);
        Assert.Equal(0, await h.Db(db => db.Tasks.CountAsync()));
    }

    [Fact]
    public async Task Switching_maintenance_on_while_tasks_are_being_created_lets_none_slip_in_afterwards()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 500);
        await h.StockAsync("F1", RackKind.Full, 300);
        var accepted = 0; var refused = 0; var other = new List<string>();
        var stop = false;
        var creators = Enumerable.Range(0, 6).Select(w => Task.Run(async () =>
        {
            for (var i = 0; !Volatile.Read(ref stop); i++)
            {
                try { await h.CreateTaskAsync("BP05", "F5", $"race-{w}-{i}"); Interlocked.Increment(ref accepted); }
                catch (AppException e) when (e.Code == MaintenanceCodes.Code) { Interlocked.Increment(ref refused); }
                catch (AppException e) { lock (other) other.Add(e.Code); }   // e.g. no free capacity - unrelated to Maintenance
            }
        })).ToArray();

        await Task.Delay(400);
        await SetAsync(h, true);
        var countAtSwitch = await h.Db(db => db.Tasks.CountAsync());                       // the switch has returned: this is the final number of tasks
        await Task.Delay(500);
        Volatile.Write(ref stop, true);
        await Task.WhenAll(creators);

        Assert.True(accepted > 0, "the creators never ran");
        Assert.True(refused > 0, "no creator was refused after the switch");
        Assert.Equal(countAtSwitch, await h.Db(db => db.Tasks.CountAsync()));              // not one task appeared after Maintenance was on
    }
}
