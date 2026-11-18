using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-008B-4: when RCS reports Completed / Interrupted (or no record after accepting) and WCS cannot prove the physical result, rack and locations become uncertain.</summary>
[Collection("Database")]
public class Cr008b4InventoryUncertaintyTests(DatabaseFixture fixture)
{
    private static async Task<Harness> ReadyAsync(DatabaseFixture f, int a1Racks = 2)
    {
        var h = await Harness.CreateAsync(f);
        await h.SeedStandardAsync(bufferCapacity: 4, stationCapacity: 3);
        await h.StockAsync("A1", RackKind.Full, a1Racks);             // a second rack at A1 proves that the whole LOCATION is blocked, not only one rack
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        return h;
    }

    private static Task CancelAsync(Harness h, Guid id) =>
        h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(id, "cr008b4", "tester"));

    private static async Task PickAsync(Harness h, Guid id)
    {
        await h.AdvanceUntilAsync(id, TaskState.Executing);
        await h.SignalAsync(id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(id, SignalKind.PlcPermissionGranted);
        await h.SignalAsync(id, SignalKind.PickConfirmed);
    }

    private static async Task<List<Rack>> AllRacksAsync(Harness h) => await h.Db(db => db.Racks.AsNoTracking().ToListAsync());

    // ------------------------------------------------------------------------------------------------ 1 / 4: Completed
    [Fact]
    public async Task Rcs_completed_during_cancel_without_a_wcs_pick_marks_rack_and_location_uncertain_and_keeps_the_reservation()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.SetStatus(t.RcsRequestCode, RcsTaskStatus.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal((TaskState.Failed, true), (task.State, task.RequiresReconciliation));
        var reserved = Assert.Single(await AllRacksAsync(h), r => r.Id == task.RackId);
        Assert.Equal(RackStatus.Unknown, reserved.Status);                          // WCS no longer claims the rack is still a Full rack at the source
        Assert.Equal((await h.LocationAsync("A1")).Id, reserved.CurrentLocationId);  // its WCS-recorded location is kept: it still counts against capacity
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.Contains(await h.ActiveReservationsAsync(), r => r.RackId == task.RackId);   // reservation NOT released
    }

    [Fact]
    public async Task Rcs_completed_after_a_confirmed_pick_but_without_a_drop_marks_everything_uncertain()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await PickAsync(h, t.Id);
        await CancelAsync(h, t.Id);
        h.Rcs.SetStatus(t.RcsRequestCode, RcsTaskStatus.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(RackStatus.Unknown, Assert.Single(await AllRacksAsync(h), r => r.Id == task.RackId).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.True((await h.Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Id == task.DestinationLocationId))).ReconciliationRequired);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
    }

    // ------------------------------------------------------------------------------------------------ 2 / 3: Interrupted
    [Fact]
    public async Task Rcs_interrupted_before_the_pick_marks_the_rack_and_the_source_location_uncertain()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);

        await h.SignalAsync(t.Id, SignalKind.RcsInterrupted);

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal((TaskState.Failed, true), (task.State, task.RequiresReconciliation));
        Assert.Null(task.PickConfirmedAt);
        Assert.Equal(RackStatus.Unknown, Assert.Single(await AllRacksAsync(h), r => r.Id == task.RackId).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
    }

    [Fact]
    public async Task Rcs_interrupted_after_the_pick_marks_rack_source_and_destination_uncertain()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await PickAsync(h, t.Id);

        await h.SignalAsync(t.Id, SignalKind.RcsInterrupted);

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(RackStatus.Unknown, Assert.Single(await AllRacksAsync(h), r => r.Id == task.RackId).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        var dest = await h.Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Id == task.DestinationLocationId));
        Assert.True(dest.ReconciliationRequired);
        Assert.False(dest.HasFreeSlot(0, 0));                                       // a flagged location is never allocated
    }

    // ------------------------------------------------------------------------------------------------ 5: NotFound after accept
    [Fact]
    public async Task Rcs_forgetting_an_accepted_task_marks_the_resources_uncertain()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.Forget(t.RcsRequestCode);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal("RCS_NOTFOUND_AFTER_ACCEPT", task.StateReason);
        Assert.Equal(RackStatus.Unknown, Assert.Single(await AllRacksAsync(h), r => r.Id == task.RackId).Status);
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
    }

    // ------------------------------------------------------------------------------------------------ nothing uncertain is handed to a new task
    [Fact]
    public async Task An_uncertain_rack_and_location_are_not_allocated_to_a_new_task()
    {
        await using var h = await ReadyAsync(fixture);                              // two Full racks at A1
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.SetStatus(t.RcsRequestCode, RcsTaskStatus.Interrupted);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();

        var next = (await h.CreateTaskAsync("BP01", "A1")).Task;                    // the second rack is physically fine but its LOCATION needs reconciliation

        Assert.Null(next.RackId);
        Assert.NotEqual("Accepted", next.State);
        Assert.Single(await h.ActiveReservationsAsync());                           // only the held reservation of the first task
        Assert.Equal(1, await h.Db(db => db.Racks.CountAsync(r => r.Status == RackStatus.Unknown)));
    }

    // ------------------------------------------------------------------------------------------------ reconciliation restores a consistent state
    [Fact]
    public async Task Reconciliation_places_the_rack_once_releases_the_reservation_and_clears_the_flags()
    {
        await using var h = await ReadyAsync(fixture);
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.SetStatus(t.RcsRequestCode, RcsTaskStatus.Completed);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();
        var racksBefore = (await AllRacksAsync(h)).Count;

        await h.RunAsync(sp => sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(t.Id, "F1", "supervisor", "found at F1"));

        var task = await h.GetTaskAsync(t.Id);
        Assert.False(task.RequiresReconciliation);
        var racks = await AllRacksAsync(h);
        Assert.Equal(racksBefore, racks.Count);                                     // no rack appeared or disappeared
        var moved = Assert.Single(racks, r => r.Id == task.RackId);
        Assert.Equal((await h.LocationAsync("F1")).Id, moved.CurrentLocationId);    // exactly one current location
        Assert.Equal(RackStatus.Full, moved.Status);
        Assert.False((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.Empty(await h.ActiveReservationsAsync());
    }

    // ------------------------------------------------------------------------------------------------ what does NOT change
    [Fact]
    public async Task A_cancel_confirmed_by_rcs_and_a_normal_completion_do_not_mark_anything_uncertain()
    {
        await using var h = await ReadyAsync(fixture, a1Racks: 1);
        await h.StockAsync("A2", RackKind.Full, 1);
        var cancelled = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, cancelled.Id);
        h.Rcs.ConfirmCancel(cancelled.RcsRequestCode);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync();
        Assert.Equal(TaskState.Cancelled, (await h.GetTaskAsync(cancelled.Id)).State);
        Assert.Equal(0, await h.Db(db => db.Racks.CountAsync(r => r.Status == RackStatus.Unknown)));   // existing rule: held by reservation only
        Assert.False((await h.LocationAsync("A1")).ReconciliationRequired);

        var done = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await h.AdvanceUntilAsync(done.Id, TaskState.Completed);
        Assert.Equal(0, await h.Db(db => db.Locations.CountAsync(l => l.ReconciliationRequired)));
    }

    [Fact]
    public async Task The_cancel_retry_limit_marks_rack_and_location_uncertain_and_keeps_the_reservation_D5_D6()
    {
        await using var h = await ReadyAsync(fixture, a1Racks: 1);
        h.SchedulerOptions.RcsMaxAttempts = 3;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await CancelAsync(h, t.Id);
        h.Rcs.Unreachable = true;
        for (var i = 0; i < 20; i++) { h.Clock.Advance(TimeSpan.FromSeconds(20)); await h.TickAsync(); }

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(RackStatus.Unknown, Assert.Single(await AllRacksAsync(h)).Status);   // D-5/D-6 (CR-008B-5) supersede the narrower D-1 wording: no proof -> uncertain
        Assert.True((await h.LocationAsync("A1")).ReconciliationRequired);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
    }
}
