using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>Idempotency, handshake rules, RCS faults, cancel, failure and reconciliation.</summary>
[Collection("Database")]
public class RobustnessTests(DatabaseFixture fixture)
{
    private static async Task<Harness> Bp01ReadyAsync(DatabaseFixture f, int buffer = 3)
    {
        var h = await Harness.CreateAsync(f);
        await h.SeedStandardAsync(buffer);
        await h.StockAsync("A1", RackKind.Full, 1);
        return h;
    }

    // ------------------------------------------------------------------------------------------ idempotency
    [Fact]
    public async Task Same_request_key_returns_the_same_task_even_when_sent_in_parallel()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => h.CreateTaskAsync("BP01", "A1", "same-key")));

        Assert.Single(results.Select(r => r.Task.Id).Distinct());
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Equal(1, h.Rcs.CreatedTaskCount);
    }

    [Fact]
    public async Task Repeated_signals_are_idempotent()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);

        Assert.Equal(SignalOutcome.Applied, (await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested, "e1")).Outcome);
        Assert.Equal(SignalOutcome.Duplicate, (await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested, "e1")).Outcome); // same event key
        Assert.Equal(SignalOutcome.Applied, (await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted, "e2")).Outcome);

        Assert.Equal(SignalOutcome.Applied, (await h.SignalAsync(t.Id, SignalKind.PickConfirmed, "e3")).Outcome);
        Assert.Equal(SignalOutcome.Duplicate, (await h.SignalAsync(t.Id, SignalKind.PickConfirmed, "e3")).Outcome);
        Assert.Equal(SignalOutcome.Duplicate, (await h.SignalAsync(t.Id, SignalKind.PickConfirmed, "other-key")).Outcome); // natural idempotency

        Assert.Single((await h.TransactionsAsync(t.Id)), x => x.EventType == InventoryEventType.PickConfirmed);
    }

    [Fact]
    public async Task Parallel_duplicate_callbacks_apply_the_pick_exactly_once()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => h.SignalAsync(t.Id, SignalKind.PickConfirmed, "cb-1")));

        Assert.Equal(1, results.Count(r => r.Outcome == SignalOutcome.Applied));
        Assert.All(results.Where(r => r.Outcome != SignalOutcome.Applied), r => Assert.Equal(SignalOutcome.Duplicate, r.Outcome));
        Assert.Single((await h.TransactionsAsync(t.Id)), x => x.EventType == InventoryEventType.PickConfirmed);
    }

    [Fact]
    public async Task Events_for_unknown_tasks_or_wrong_robot_are_rejected()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;

        var unknown = await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>()
            .HandleSignalAsync(new TaskSignal(SignalKind.AgvStarted, RcsTaskCode: "NOPE")));
        Assert.Equal(SignalOutcome.Rejected, unknown.Outcome);

        var wrongRobot = await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>()
            .HandleSignalAsync(new TaskSignal(SignalKind.AgvStarted, t.Id, RobotCode: "SOME-OTHER-ROBOT")));
        Assert.Equal(SignalOutcome.Rejected, wrongRobot.Outcome);
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);

        var right = await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>()
            .HandleSignalAsync(new TaskSignal(SignalKind.AgvStarted, RcsTaskCode: t.RcsTaskCode, RobotCode: "R-FULL")));
        Assert.Equal(SignalOutcome.Applied, right.Outcome);
    }

    // ------------------------------------------------------------------------------------------ handshake / completion rules
    [Fact]
    public async Task Pick_is_refused_until_the_plc_has_granted_permission()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;       // A1 requires PLC permission
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);

        var early = await h.SignalAsync(t.Id, SignalKind.PickConfirmed);
        Assert.Equal(SignalOutcome.Rejected, early.Outcome);
        Assert.Single(await h.RacksAtAsync("A1"));                    // inventory untouched
        Assert.Null((await h.GetTaskAsync(t.Id)).PickConfirmedAt);

        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        Assert.Equal(TaskState.WaitingForPlc, (await h.GetTaskAsync(t.Id)).State);
        Assert.Contains(h.Stations.PermissionRequests, p => p.Station == "A1" && p.TaskId == t.Id);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);
        Assert.Equal(SignalOutcome.Applied, (await h.SignalAsync(t.Id, SignalKind.PickConfirmed)).Outcome);
    }

    [Fact]
    public async Task Drop_before_pick_is_rejected_and_rcs_end_alone_never_completes_a_task()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);

        Assert.Equal(SignalOutcome.Rejected, (await h.SignalAsync(t.Id, SignalKind.DropConfirmed)).Outcome);

        var end = await h.SignalAsync(t.Id, SignalKind.RcsEnded);
        Assert.Equal(SignalOutcome.Applied, end.Outcome);
        var after = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Executing, after.State);               // NOT completed
        Assert.True(after.RequiresReconciliation);
        Assert.Single(await h.RacksAtAsync("A1"));
    }

    [Fact]
    public async Task Events_arriving_after_a_final_state_are_ignored_and_logged()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);

        var late = await h.SignalAsync(t.Id, SignalKind.AgvStarted);
        Assert.Equal(SignalOutcome.Ignored, late.Outcome);
        var final = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Completed, final.State);
        Assert.Contains(final.History, x => x.EventName == "LATE_EVENT_IGNORED");
    }

    // ------------------------------------------------------------------------------------------ RCS faults
    [Fact]
    public async Task Timeout_after_rcs_created_the_task_does_not_create_a_duplicate()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        h.Rcs.NextFault = RcsFault.TimeoutAfterCreate;

        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Dispatching", t.State);                          // outcome unknown: still holding the AGV
        Assert.Equal(1, h.Rcs.CreatedTaskCount);

        await h.TickAsync();                                           // recovery queries first, then maps the existing task
        var recovered = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Accepted, recovered.State);
        Assert.NotNull(recovered.RcsTaskCode);
        Assert.Equal(1, h.Rcs.CreatedTaskCount);
        Assert.Equal(1, h.Rcs.CreateCalls);                            // the query found it, no second create
        Assert.True(h.Rcs.QueryCalls >= 1);
    }

    [Fact]
    public async Task Timeout_before_creation_is_retried_after_a_query_confirms_nothing_exists()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        h.Rcs.NextFault = RcsFault.TimeoutBeforeCreate;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Dispatching", t.State);

        await h.TickAsync();
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);
        Assert.Equal(1, h.Rcs.CreatedTaskCount);
        Assert.Equal(2, h.Rcs.CreateCalls);
    }

    [Fact]
    public async Task Rcs_rejection_fails_the_task_releases_the_resources_and_frees_the_agv()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        h.Rcs.NextFault = RcsFault.RejectNextCreate;

        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var failed = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Contains("RCS_REJECTED", failed.StateReason);
        Assert.False(failed.RequiresReconciliation);                   // nothing ever happened physically
        Assert.Empty(await h.ActiveReservationsAsync());
        Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));

        var retry = (await h.CreateTaskAsync("BP01", "A1")).Task;      // the rack is available again
        Assert.Equal("Accepted", retry.State);
    }

    [Fact]
    public async Task Continue_timeout_queries_rcs_before_retrying()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);

        h.Rcs.NextFault = RcsFault.TimeoutNextContinue;
        await h.SignalAsync(t.Id, SignalKind.PickConfirmed);
        Assert.Equal(TaskState.Continuing, (await h.GetTaskAsync(t.Id)).State);
        var queriesBefore = h.Rcs.QueryCalls;

        await h.TickAsync();                                           // retry: continue applied this time
        Assert.Equal(TaskState.Executing, (await h.GetTaskAsync(t.Id)).State);
        Assert.True(h.Rcs.QueryCalls > queriesBefore);
    }

    [Fact]
    public async Task Continue_rejection_fails_the_task_and_keeps_the_rack_for_reconciliation()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);

        h.Rcs.NextFault = RcsFault.RejectNextContinue;
        await h.SignalAsync(t.Id, SignalKind.PickConfirmed);

        var failed = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.True(failed.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());            // not auto-released
    }

    // ------------------------------------------------------------------------------------------ cancel
    [Fact]
    public async Task Cancelling_a_queued_task_releases_its_rack_without_touching_rcs()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        await h.StockAsync("A2", RackKind.Full, 1);
        await h.CreateTaskAsync("BP01", "A1");
        var queued = (await h.CreateTaskAsync("BP01", "A2")).Task;
        Assert.Equal("Queued", queued.State);

        var cancelled = await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(queued.Id, "no longer needed", "tester"));
        Assert.Equal("Cancelled", cancelled.State);
        Assert.Equal(0, h.Rcs.CancelCalls);
        Assert.Single(await h.ActiveReservationsAsync());              // only the running task's rack reservation remains
    }

    [Fact]
    public async Task Cancelling_an_active_task_waits_for_rcs_confirmation_and_then_requires_reconciliation()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;

        var pending = await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t.Id, "operator stop", "tester"));
        Assert.Equal("CancelPending", pending.State);                  // never CANCELLED without RCS confirmation
        Assert.Equal(1, h.Rcs.CancelCalls);
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));         // still occupies the AGV

        await h.TickAsync();                                           // RCS has not confirmed yet -> still pending
        Assert.Equal(TaskState.CancelPending, (await h.GetTaskAsync(t.Id)).State);

        h.Rcs.ConfirmCancel(pending.RcsRequestCode);
        await h.TickAsync();
        var cancelled = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Cancelled, cancelled.State);
        Assert.True(cancelled.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());            // held until reconciled (inventory 4.4)
        Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));         // terminal -> AGV free
    }

    [Fact]
    public async Task Cancelling_twice_is_idempotent_and_a_completed_task_cannot_be_cancelled()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var orchestrator = (Func<Task<TaskDto>>)(() => h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t.Id, "x", "u")));
        await orchestrator();
        h.Rcs.ConfirmCancel(t.RcsRequestCode);
        await h.TickAsync();
        Assert.Equal("Cancelled", (await orchestrator()).State);       // second cancel is a no-op

        var done = (await h.CreateTaskAsync("BP05", "F5", "x2")).Task; // F1/F2 empty -> fails, terminal
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(done.Id, "x", "u")));
        Assert.Equal("TASK_ALREADY_FINISHED", ex.Code);
    }

    // ------------------------------------------------------------------------------------------ failure & reconciliation
    [Fact]
    public async Task Interruption_after_pick_marks_the_rack_unknown_and_reconciliation_restores_it()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);
        await h.SignalAsync(t.Id, SignalKind.PickConfirmed);

        var failed = await h.SignalAsync(t.Id, SignalKind.RcsInterrupted);
        Assert.Equal(SignalOutcome.Applied, failed.Outcome);
        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.True(task.RequiresReconciliation);

        var rack = Assert.Single(await h.Db(db => db.Racks.AsNoTracking().ToListAsync()));
        Assert.Equal(RackStatus.Unknown, rack.Status);                 // not "at source", not "at destination"
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.False((await h.LocationAsync("A1")).HasFreeSlot(0, 0)); // blocked for other tasks while unknown

        // Operator confirms the rack is physically at F1.
        await h.RunAsync(sp => sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(t.Id, "F1", "supervisor", "found at F1"));

        var reconciled = await h.GetTaskAsync(t.Id);
        Assert.False(reconciled.RequiresReconciliation);
        var fixedRack = Assert.Single(await h.RacksAtAsync("F1"));
        Assert.Equal(RackStatus.Full, fixedRack.Status);
        Assert.False((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.Empty(await h.ActiveReservationsAsync());
        Assert.Contains(await h.TransactionsAsync(t.Id), x => x.EventType == InventoryEventType.Reconciliation && x.UserName == "supervisor");
    }

    [Fact]
    public async Task Reconciliation_is_only_possible_for_finished_tasks_that_need_it()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            h.RunAsync(sp => sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(t.Id, "F1", "u", null)));
        Assert.Equal("TASK_NOT_FINISHED", ex.Code);
    }

    [Fact]
    public async Task Failed_task_frees_the_agv_so_the_next_queued_task_starts()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        await h.StockAsync("A2", RackKind.Full, 1);
        var first = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var second = (await h.CreateTaskAsync("BP01", "A2")).Task;
        Assert.Equal("Queued", second.State);

        await h.AdvanceUntilAsync(first.Id, TaskState.Executing);
        await h.SignalAsync(first.Id, SignalKind.RcsInterrupted);

        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(second.Id)).State);
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));
    }

    // ------------------------------------------------------------------------------------------ timeouts / queue policy
    [Fact]
    public async Task Configured_timeout_requests_cancel_but_is_off_by_default()
    {
        await using var h = await Bp01ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;

        h.Clock.Advance(TimeSpan.FromHours(5));
        await h.TickAsync();
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);   // no timeout configured

        h.SchedulerOptions.StateTimeoutsSeconds["Accepted"] = 60;
        await h.TickAsync();
        var timedOut = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.CancelPending, timedOut.State);
        Assert.Equal("TIMEOUT_Accepted", timedOut.StateReason);
        Assert.Equal(1, h.Rcs.CancelCalls);
    }

    [Fact]
    public async Task Waiting_for_destination_never_times_out_unless_explicitly_configured()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 1);
        await h.StockAsync("A1", RackKind.Full, 1);
        foreach (var l in new[] { "B1", "B2", "F1", "F2", "F5" }) await h.StockAsync(l, RackKind.Full, 1);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);
        await h.SignalAsync(t.Id, SignalKind.PickConfirmed);

        h.Clock.Advance(TimeSpan.FromDays(2));
        await h.TickAsync();
        Assert.Equal(TaskState.WaitingForDestination, (await h.GetTaskAsync(t.Id)).State);
    }

    [Fact]
    public async Task Queue_is_fifo_by_creation_time_pending_confirmation_of_a_priority_policy()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 10);
        await h.StockAsync("F1", RackKind.Full, 3);

        var a = (await h.CreateTaskAsync("BP05", "F5", "a")).Task;
        var b = (await h.CreateTaskAsync("BP05", "F5", "b")).Task;
        var c = (await h.CreateTaskAsync("BP05", "F5", "c")).Task;
        await h.AdvanceUntilAsync(a.Id, TaskState.Completed);
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(b.Id)).State);
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(c.Id)).State);
    }
}
