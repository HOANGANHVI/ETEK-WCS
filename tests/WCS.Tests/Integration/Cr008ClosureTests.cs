using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-008 closure: RcsEnded lifecycle, late Drop, CancelPending timeout and the dispatch-vs-hold race.</summary>
[Collection("Database")]
public class Cr008ClosureTests(DatabaseFixture fixture)
{
    private static async Task<Harness> ReadyAsync(DatabaseFixture f)
    {
        var h = await Harness.CreateAsync(f);
        await h.SeedStandardAsync(bufferCapacity: 4, stationCapacity: 3);
        await h.StockAsync("A1", RackKind.Full, 1);
        await h.StockAsync("A2", RackKind.Full, 2);
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        h.SchedulerOptions.RcsRetryMaxDelayMs = 4000;
        h.SchedulerOptions.RcsMaxAttempts = 4;
        return h;
    }

    private static Task CancelAsync(Harness h, Guid id) =>
        h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(id, "cr008-closure", "admin1"));

    private static async Task PickAsync(Harness h, Guid id)
    {
        await h.AdvanceUntilAsync(id, TaskState.Executing);
        await h.SignalAsync(id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(id, SignalKind.PlcPermissionGranted);
        await h.SignalAsync(id, SignalKind.PickConfirmed);
    }

    private static Task ReleaseAsync(Harness h, Guid agvId) =>
        h.Scoped(async sp => { await sp.GetRequiredService<AgvHoldService>().ReleaseAsync(agvId, true, true, "checked on site", "admin1"); return 0; });

    // ------------------------------------------------------------------------------------------------ C2: RcsEnded lifecycle
    [Fact]
    public async Task After_rcs_ended_the_admin_can_end_the_task_through_the_cancel_flow_reconcile_it_and_only_then_release_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var next = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await PickAsync(h, t.Id);
        await h.SignalAsync(t.Id, SignalKind.RcsEnded);
        var agvId = (await h.AgvAsync("AGV1")).Id;

        // RcsEnded alone neither completes nor ends the task, and the AGV stays held and busy
        Assert.False((await h.GetTaskAsync(t.Id)).IsTerminal);
        await Assert.ThrowsAnyAsync<Exception>(() => ReleaseAsync(h, agvId));      // cause task still active: no release

        // the Admin starts the (already existing) cancel flow; RCS answers "completed": the physical result stays unproven -> Failed + reconciliation
        await CancelAsync(h, t.Id);
        h.Rcs.SetStatus(t.RcsRequestCode, RcsTaskStatus.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();
        var ended = await h.GetTaskAsync(t.Id);
        Assert.Equal((TaskState.Failed, true), (ended.State, ended.RequiresReconciliation));
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(next.Id)).State);

        // inventory reconciliation alone does not lift the hold
        await h.Scoped(async sp => { await sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(t.Id, "F1", "admin1", "rack found at F1"); return 0; });
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);

        await ReleaseAsync(h, agvId);
        h.Clock.Advance(TimeSpan.FromMinutes(1)); await h.TickAsync();
        Assert.False((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(next.Id)).State);
    }

    [Fact]
    public async Task After_rcs_ended_an_unreachable_rcs_cannot_keep_the_agv_held_forever_the_retry_limit_ends_the_task_for_reconciliation()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await PickAsync(h, t.Id);
        await h.SignalAsync(t.Id, SignalKind.RcsEnded);
        await CancelAsync(h, t.Id);
        h.Rcs.Unreachable = true;
        for (var i = 0; i < 30; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);                                  // terminal: the Admin can now reconcile and release
        Assert.Contains(await h.ActiveReservationsAsync(), r => r.TaskId == t.Id);   // nothing released
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
    }

    // ------------------------------------------------------------------------------------------------ C3: late Drop
    [Fact]
    public async Task A_late_drop_after_rcs_ended_completes_the_inventory_but_not_the_hold_and_the_admin_still_reconciles_and_releases()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await PickAsync(h, t.Id);
        await h.SignalAsync(t.Id, SignalKind.RcsEnded);
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);

        var done = await h.GetTaskAsync(t.Id);
        var rack = await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == done.RackId));
        Assert.Equal(done.DestinationLocationId, rack.CurrentLocationId);            // placed once, at the destination of THIS task
        Assert.Equal(RackStatus.Full, rack.Status);
        Assert.Null(rack.CurrentTaskId);
        Assert.Equal(1, await h.Db(db => db.Racks.CountAsync(r => r.Id == done.RackId)));
        Assert.DoesNotContain(await h.ActiveReservationsAsync(), r => r.TaskId == t.Id);   // consumed by the confirmed drop
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);                        // D-8: only the Admin lifts it

        // a repeated drop signal is a duplicate, not a second placement
        var again = await h.SignalAsync(t.Id, SignalKind.DropConfirmed, eventKey: "late-dup");
        Assert.NotEqual(SignalOutcome.Applied, again.Outcome);
        Assert.Equal(done.DestinationLocationId, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == done.RackId))).CurrentLocationId);

        // the flags set when RcsEnded arrived are cleared only by the Admin reconciliation, and the hold only by the release
        var dest = await h.Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Id == done.DestinationLocationId));
        Assert.True(dest.ReconciliationRequired);
        await h.Scoped(async sp => { await sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(t.Id, dest.Code, "admin1", "rack confirmed at destination"); return 0; });
        Assert.False((await h.Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Id == dest.Id))).ReconciliationRequired);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        await ReleaseAsync(h, (await h.AgvAsync("AGV1")).Id);
        Assert.False((await h.AgvAsync("AGV1")).DispatchHold);
    }

    [Fact]
    public async Task A_drop_is_rejected_for_a_task_that_is_already_failed_or_never_picked()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        var early = await h.SignalAsync(t.Id, SignalKind.DropConfirmed);               // no pick yet
        Assert.NotEqual(SignalOutcome.Applied, early.Outcome);

        await h.SignalAsync(t.Id, SignalKind.RcsInterrupted);                           // task is now Failed, rack Unknown (never picked: it stays at A1)
        var rackBefore = await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == t.RackId));
        var late = await h.SignalAsync(t.Id, SignalKind.DropConfirmed, eventKey: "late-after-failed");
        Assert.NotEqual(SignalOutcome.Applied, late.Outcome);
        var rackAfter = await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == t.RackId));
        Assert.Equal((rackBefore.Status, rackBefore.CurrentLocationId), (rackAfter.Status, rackAfter.CurrentLocationId));
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
    }

    // ------------------------------------------------------------------------------------------------ C4: CancelPending timeout
    [Fact]
    public async Task A_cancel_pending_timeout_keeps_the_reservation_marks_uncertainty_and_holds_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var next = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.Unreachable = true;
        h.SchedulerOptions.StateTimeoutsSeconds["CancelPending"] = 60;                 // enabled for this test only; the default stays off
        h.Clock.Advance(TimeSpan.FromSeconds(120)); await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("TIMEOUT_CancelPending", task.StateReason);
        Assert.True(task.RequiresReconciliation);
        Assert.Contains(await h.ActiveReservationsAsync(), r => r.RackId == task.RackId);
        Assert.Equal(RackStatus.Unknown, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == task.RackId))).Status);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(next.Id)).State);
    }

    [Fact]
    public void The_default_configuration_has_no_state_timeouts()
    {
        Assert.Empty(new SchedulerOptions().StateTimeoutsSeconds.Where(kv => kv.Value > 0));
    }

    // ------------------------------------------------------------------------------------------------ C5: dispatch vs hold race
    [Fact]
    public async Task The_hold_flag_used_by_dispatch_is_a_fresh_read_not_the_tracked_entity()
    {
        await using var h = await ReadyAsync(fixture);
        var agvId = (await h.AgvAsync("AGV1")).Id;
        await h.Scoped(async sp =>
        {
            var repo = sp.GetRequiredService<IAgvRepository>();
            var tracked = await repo.GetAsync(agvId);                                    // the entity is loaded (and would stay stale) ...
            Assert.False(tracked!.DispatchHold);
            await h.Db(async db => { var a = await db.Agvs.SingleAsync(x => x.Id == agvId); a.PlaceDispatchHold("test", Guid.NewGuid(), DateTimeOffset.UtcNow); await db.SaveChangesAsync(); return 0; });
            Assert.False((await repo.GetAsync(agvId))!.DispatchHold);                    // ... the tracked copy is stale (this is the hazard)
            Assert.True(await repo.IsOnDispatchHoldAsync(agvId));                        // ... the dedicated read sees the committed hold
            return 0;
        });
    }

    [Fact]
    public async Task New_requests_racing_with_the_event_that_sets_the_hold_never_start_on_the_held_agv()
    {
        for (var round = 0; round < 6; round++)
        {
            await using var h = await ReadyAsync(fixture);
            await h.StockAsync("B1", RackKind.Full, 1);
            var first = (await h.CreateTaskAsync("BP01", "A1")).Task;
            await h.AdvanceUntilAsync(first.Id, TaskState.Executing);
            var agvId = (await h.AgvAsync("AGV1")).Id;

            var work = new List<Task> { h.SignalAsync(first.Id, SignalKind.RcsInterrupted) };
            for (var i = 0; i < 4; i++)
            {
                var key = $"race-{round}-{i}";
                work.Add(h.CreateTaskAsync("BP01", i % 2 == 0 ? "A2" : "B1", key));
                work.Add(h.Scoped(async sp => { await sp.GetRequiredService<ITaskOrchestrator>().TryDispatchNextAsync(agvId); return 0; }));
            }
            try { await Task.WhenAll(work); } catch (Exception ex) when (ex is not Xunit.Sdk.XunitException) { /* lost optimistic-concurrency races are acceptable; the invariant below is what matters */ }
            await h.TickAsync();

            Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
            var started = await h.Db(db => db.Tasks.AsNoTracking().Where(x => x.AgvId == agvId && x.Id != first.Id &&
                (x.State == TaskState.Dispatching || x.State == TaskState.Accepted || x.State == TaskState.Executing)).CountAsync());
            Assert.Equal(0, started);                                                    // nothing was started on the held AGV
            Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));
        }
    }
}
