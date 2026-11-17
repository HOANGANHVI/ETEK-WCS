using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-008B-5: AGV dispatch hold (D-7, P2) after an outcome whose physical state is not proven (D-5/D-6), released only by an Admin with two confirmations (D-8).</summary>
[Collection("Database")]
public class Cr008b5AgvDispatchHoldTests(DatabaseFixture fixture)
{
    private static async Task<Harness> ReadyAsync(DatabaseFixture f)
    {
        var h = await Harness.CreateAsync(f);
        await h.SeedStandardAsync(bufferCapacity: 4, stationCapacity: 3);
        await h.StockAsync("A1", RackKind.Full, 1);
        await h.StockAsync("A2", RackKind.Full, 2);
        await h.StockAsync("F3", RackKind.Empty, 1);                   // BP-02 source (AGV2)
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        h.SchedulerOptions.RcsRetryMaxDelayMs = 4000;
        h.SchedulerOptions.RcsMaxAttempts = 4;
        return h;
    }

    private static Task CancelAsync(Harness h, Guid id) =>
        h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(id, "cr008b5", "tester"));

    private static async Task PickAsync(Harness h, Guid id)
    {
        await h.AdvanceUntilAsync(id, TaskState.Executing);
        await h.SignalAsync(id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(id, SignalKind.PlcPermissionGranted);
        await h.SignalAsync(id, SignalKind.PickConfirmed);
    }

    /// <summary>First task executing on AGV1 and a second one queued behind it, then the first one is interrupted.</summary>
    private static async Task<(TransportTask First, TransportTask Second)> HeldByInterruptAsync(Harness h)
    {
        var first = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var second = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await h.AdvanceUntilAsync(first.Id, TaskState.Executing);
        await h.SignalAsync(first.Id, SignalKind.RcsInterrupted);
        return (await h.GetTaskAsync(first.Id), await h.GetTaskAsync(second.Id));
    }

    private sealed record Api(HttpClient Admin, HttpClient Operator);
    private static async Task<Api> ApiAsync(DbApiFactory f)
    {
        using (var scope = f.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            ctx.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); ctx.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await ctx.SaveChangesAsync();
        }
        var admin = f.CreateClient().WithClientHeader(); Assert.Equal(200, await admin.LoginAsync("admin1"));
        var op = f.CreateClient().WithClientHeader(); Assert.Equal(200, await op.LoginAsync("op1"));
        return new Api(admin, op);
    }
    private static async Task<JsonElement> DataAsync(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    // ------------------------------------------------------------------------------------------------ 1-7 dispatch hold
    [Fact]
    public async Task A_held_agv_takes_no_new_or_queued_task_while_the_other_agv_keeps_working_and_nothing_is_moved_between_agvs()
    {
        await using var h = await ReadyAsync(fixture);
        var (first, second) = await HeldByInterruptAsync(h);
        var agv1 = await h.AgvAsync("AGV1");

        Assert.Equal(TaskState.Failed, first.State);
        Assert.True(agv1.DispatchHold);                                           // 1
        Assert.Equal(first.Id, agv1.DispatchHoldTaskId);
        Assert.NotNull(agv1.DispatchHoldAt);
        Assert.False(string.IsNullOrWhiteSpace(agv1.DispatchHoldReason));
        Assert.True(agv1.Enabled);                                                // the hold is NOT Enabled=false
        Assert.Equal(TaskState.Queued, second.State);                             // 2: the queued task is not dispatched

        for (var i = 0; i < 5; i++) { h.Clock.Advance(TimeSpan.FromSeconds(30)); await h.TickAsync(); }
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(second.Id)).State);

        var third = (await h.CreateTaskAsync("BP01", "A2")).Task;                 // 4: a new task is accepted into the queue
        Assert.Equal("Queued", third.State);
        Assert.Equal(agv1.Id, (await h.GetTaskAsync(third.Id)).AgvId);            // 5: still AGV1
        Assert.Equal(agv1.Id, (await h.GetTaskAsync(second.Id)).AgvId);
        Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));

        var other = (await h.CreateTaskAsync("BP02", "A2")).Task;                 // 3: AGV2 is unaffected
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(other.Id)).State);
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV2"));
        Assert.False((await h.AgvAsync("AGV2")).DispatchHold);
    }

    [Fact]
    public async Task The_hold_is_set_in_the_same_transaction_as_the_terminal_transition_so_no_parallel_dispatch_can_slip_through()
    {
        await using var h = await ReadyAsync(fixture);
        var first = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var second = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await h.AdvanceUntilAsync(first.Id, TaskState.Executing);
        var agvId = (await h.AgvAsync("AGV1")).Id;

        var work = new List<Task> { h.SignalAsync(first.Id, SignalKind.RcsInterrupted) };
        for (var i = 0; i < 8; i++)
        {
            work.Add(h.Scoped(async sp => { await sp.GetRequiredService<ITaskOrchestrator>().TryDispatchNextAsync(agvId); return 0; }));
            work.Add(h.TickAsync());
        }
        try { await Task.WhenAll(work); } catch (Exception ex) when (ex is not Xunit.Sdk.XunitException) { /* a lost optimistic-concurrency race is acceptable: the invariant below is what matters */ }

        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(second.Id)).State);
        Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));
    }

    [Fact]
    public async Task The_hold_survives_a_backend_restart()
    {
        await using var h = await ReadyAsync(fixture);
        var (_, second) = await HeldByInterruptAsync(h);
        await h.RestartAsync();
        h.Clock.Advance(TimeSpan.FromMinutes(5)); await h.TickAsync(); await h.TickAsync();

        var agv1 = await h.AgvAsync("AGV1");
        Assert.True(agv1.DispatchHold);
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(second.Id)).State);
    }

    // ------------------------------------------------------------------------------------------------ 8-15 outcomes (D-5 / D-6)
    [Fact]
    public async Task Interrupted_marks_rack_unknown_and_location_flagged_keeps_the_rack_location_and_the_reservation()
    {
        await using var h = await ReadyAsync(fixture);
        var (first, _) = await HeldByInterruptAsync(h);

        var rack = await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == first.RackId));
        Assert.Equal(RackStatus.Unknown, rack.Status);
        Assert.Equal((await h.LocationAsync("A1")).Id, rack.CurrentLocationId);   // 16: the WCS location is not changed
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.Contains(await h.ActiveReservationsAsync(), r => r.RackId == first.RackId);
    }

    [Fact]
    public async Task Completed_while_cancel_pending_without_a_confirmed_drop_marks_everything_uncertain_and_holds_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.SetStatus(t.RcsRequestCode, RcsTaskStatus.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();

        Assert.Equal(TaskState.Failed, (await h.GetTaskAsync(t.Id)).State);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(RackStatus.Unknown, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == t.RackId))).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
    }

    [Fact]
    public async Task A_completion_with_a_confirmed_drop_and_an_rcs_confirmed_cancel_neither_mark_uncertainty_nor_hold_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        var done = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(done.Id, TaskState.Completed);
        Assert.False((await h.AgvAsync("AGV1")).DispatchHold);                       // 10

        var cancelled = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await CancelAsync(h, cancelled.Id);
        h.Rcs.ConfirmCancel(cancelled.RcsRequestCode);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();
        Assert.Equal(TaskState.Cancelled, (await h.GetTaskAsync(cancelled.Id)).State);
        Assert.False((await h.AgvAsync("AGV1")).DispatchHold);                       // 11
        Assert.Equal(0, await h.Db(db => db.Racks.CountAsync(r => r.Status == RackStatus.Unknown)));
        Assert.Equal(0, await h.Db(db => db.Locations.CountAsync(l => l.ReconciliationRequired)));
    }

    [Fact]
    public async Task Rcs_forgetting_an_accepted_task_holds_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.Forget(t.RcsRequestCode);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();

        Assert.Equal("RCS_NOTFOUND_AFTER_ACCEPT", (await h.GetTaskAsync(t.Id)).StateReason);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(RackStatus.Unknown, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == t.RackId))).Status);
    }

    [Fact]
    public async Task The_cancel_retry_limit_keeps_the_reservation_marks_uncertainty_and_holds_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.Unreachable = true;
        for (var i = 0; i < 30; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Contains(await h.ActiveReservationsAsync(), r => r.RackId == task.RackId);
        Assert.Equal(RackStatus.Unknown, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == task.RackId))).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
    }

    [Fact]
    public async Task A_dispatch_whose_outcome_stays_unknown_marks_uncertainty_and_holds_the_agv()
    {
        await using var h = await ReadyAsync(fixture);
        h.SchedulerOptions.MaxDispatchAttempts = 3;
        h.Rcs.Unreachable = true;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        for (var i = 0; i < 12; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        var failed = await h.GetTaskAsync(t.Id);
        Assert.Contains("RCS_DISPATCH_UNCERTAIN", failed.StateReason);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(RackStatus.Unknown, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == failed.RackId))).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.Contains(await h.ActiveReservationsAsync(), r => r.RackId == failed.RackId);
    }

    [Fact]
    public async Task Rcs_ended_without_a_drop_marks_uncertainty_and_holds_the_agv_but_a_later_plc_drop_is_still_accepted()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await PickAsync(h, t.Id);
        await h.SignalAsync(t.Id, SignalKind.RcsEnded);

        var mid = await h.GetTaskAsync(t.Id);
        Assert.False(mid.IsTerminal);                                              // the task is still the active task of the AGV (PER_AGV unchanged)
        Assert.True(mid.RequiresReconciliation);
        var agv = await h.AgvAsync("AGV1");
        Assert.True(agv.DispatchHold);
        Assert.Equal(t.Id, agv.DispatchHoldTaskId);
        Assert.Equal(RackStatus.Unknown, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == t.RackId))).Status);
        Assert.NotEmpty(await h.ActiveReservationsAsync());

        await h.SignalAsync(t.Id, SignalKind.RcsEnded);                            // repeated signal: still one hold
        Assert.Single((await h.GetTaskAsync(t.Id)).History, x => x.EventName == "AGV_DISPATCH_HOLD");

        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);                      // the PLC drop evidence arrives later: it is accepted and the rack is placed
        Assert.Equal(TaskState.Completed, (await h.GetTaskAsync(t.Id)).State);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);                      // D-8: only an Admin lifts the hold
    }

    [Fact]
    public async Task A_rack_and_location_that_need_reconciliation_are_not_allocated_to_a_new_task()
    {
        await using var h = await ReadyAsync(fixture);
        var (first, _) = await HeldByInterruptAsync(h);
        var again = (await h.CreateTaskAsync("BP01", "A1")).Task;                  // A1 needs reconciliation: nothing may be reserved there
        Assert.DoesNotContain(await h.ActiveReservationsAsync(), r => r.TaskId == again.Id && r.RackId == first.RackId);
    }

    // ------------------------------------------------------------------------------------------------ 18-23 Admin release (D-8)
    [Fact]
    public async Task Only_an_admin_with_both_confirmations_releases_the_hold_and_the_release_is_audited_and_dispatches_the_next_task()
    {
        await using var h = await ReadyAsync(fixture);
        var (first, second) = await HeldByInterruptAsync(h);
        var agvId = (await h.AgvAsync("AGV1")).Id;
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);
        var url = $"/api/agvs/{agvId}/release-hold";

        Assert.Equal(HttpStatusCode.Forbidden, (await api.Operator.PostAsJsonAsync(url, new { inventoryReconciled = true, agvPhysicallyVerified = true, note = "ok ok" })).StatusCode);   // 18
        Assert.NotEqual(HttpStatusCode.OK, (await api.Admin.PostAsJsonAsync(url, new { inventoryReconciled = false, agvPhysicallyVerified = true, note = "ok ok" })).StatusCode);       // 19
        Assert.NotEqual(HttpStatusCode.OK, (await api.Admin.PostAsJsonAsync(url, new { inventoryReconciled = true, agvPhysicallyVerified = false, note = "ok ok" })).StatusCode);       // 20
        Assert.NotEqual(HttpStatusCode.OK, (await api.Admin.PostAsJsonAsync(url, new { note = "ok ok" })).StatusCode);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);

        var ok = await api.Admin.PostAsJsonAsync(url, new { inventoryReconciled = true, agvPhysicallyVerified = true, note = "rack found at A1, AGV1 checked on site" });   // 21
        Assert.True(ok.StatusCode == HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        Assert.False((await DataAsync(ok)).GetProperty("dispatchHold").GetBoolean());
        Assert.False((await h.AgvAsync("AGV1")).DispatchHold);

        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "AGV_DISPATCH_HOLD_RELEASE" && a.Actor == "admin1").ToListAsync());   // 22
        var success = Assert.Single(audit, a => a.Result == AuditResult.Success);
        var after = success.AfterJson ?? "";
        Assert.Contains(first.Id.ToString(), after);
        Assert.Contains("inventoryReconciled", after);
        Assert.Contains("agvPhysicallyVerified", after);
        Assert.Contains((await h.GetTaskAsync(first.Id)).History, x => x.EventName == "AGV_DISPATCH_HOLD_RELEASED");

        h.Clock.Advance(TimeSpan.FromMinutes(1)); await h.TickAsync();                // 23: after the release the scheduler dispatches the queued task
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(second.Id)).State);
    }

    [Fact]
    public async Task Finishing_the_inventory_reconciliation_alone_does_not_lift_the_hold()
    {
        await using var h = await ReadyAsync(fixture);
        var (first, second) = await HeldByInterruptAsync(h);
        await h.Scoped(async sp => { await sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(first.Id, "A1", "admin1", "rack found at A1"); return 0; });

        h.Clock.Advance(TimeSpan.FromMinutes(1)); await h.TickAsync();
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
        Assert.Equal(TaskState.Queued, (await h.GetTaskAsync(second.Id)).State);
    }

    [Fact]
    public async Task The_hold_cannot_be_released_while_the_task_that_caused_it_is_still_active()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await PickAsync(h, t.Id);
        await h.SignalAsync(t.Id, SignalKind.RcsEnded);
        var agvId = (await h.AgvAsync("AGV1")).Id;

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => h.Scoped(async sp =>
        { await sp.GetRequiredService<WCS.Application.Services.AgvHoldService>().ReleaseAsync(agvId, true, true, "check", "admin1"); return 0; }));
        Assert.Contains("still active", ex.Message);
        Assert.True((await h.AgvAsync("AGV1")).DispatchHold);
    }

    [Fact]
    public async Task The_agv_listing_exposes_the_hold_and_its_reason()
    {
        await using var h = await ReadyAsync(fixture);
        var (first, _) = await HeldByInterruptAsync(h);
        await using var f = new DbApiFactory(fixture);
        var api = await ApiAsync(f);

        var list = await DataAsync(await api.Operator.GetAsync("/api/agvs"));    // operators may read it
        var a1 = list.EnumerateArray().Single(e => e.GetProperty("code").GetString() == "AGV1");
        Assert.True(a1.GetProperty("dispatchHold").GetBoolean());
        Assert.Equal(first.Id, a1.GetProperty("dispatchHoldTaskId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(a1.GetProperty("dispatchHoldReason").GetString()));
        Assert.False(list.EnumerateArray().Single(e => e.GetProperty("code").GetString() == "AGV2").GetProperty("dispatchHold").GetBoolean());
    }
}
