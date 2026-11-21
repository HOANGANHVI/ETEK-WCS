using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Fleet;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-028: RcsManaged dispatch on the RCS SIMULATOR - WCS sends the task without a robot code, the simulated RCS chooses the AMR, WCS learns it from queryTaskStatus / the callback.
/// Nothing here was verified against a real RCS-2000 (the real create-task adapter is not implemented: no location / task-type mapping is confirmed).
/// </summary>
[Collection("Database")]
public class Cr028DynamicDispatchTests(DatabaseFixture fixture)
{
    private static Dictionary<string, string?> ManagedConfig(int maxInFlight = 0) => new()
    {
        ["Rcs:Dispatch:Mode"] = "RcsManaged", ["Rcs:Dispatch:MaxInFlightTasks"] = maxInFlight.ToString(), ["Rcs:Dispatch:AssignmentCheckSeconds"] = "5", ["Rcs:Dispatch:AssignedRecheckSeconds"] = "60",
    };

    /// <summary>Locations only - NO AGV row at all (the fleet is whatever the RCS has; WCS needs no AGV1 / AGV2).</summary>
    private async Task<Harness> ManagedAsync(int amrs, int maxInFlight = 0, bool withAgvRows = false, int capacity = 20)
    {
        var h = await Harness.CreateAsync(fixture, null, ManagedConfig(maxInFlight));
        if (withAgvRows) await h.SeedStandardAsync(bufferCapacity: capacity, stationCapacity: 1);
        else
            await h.Db(async db =>
            {
                foreach (var c in new[] { "A1", "A2", "B1", "B2" }) db.Locations.Add(new Location(c, c, LocationType.Station, 1, AllowedRackKind.Any, true, h.Clock.UtcNow));
                foreach (var c in new[] { "F1", "F2" }) db.Locations.Add(new Location(c, c, LocationType.Buffer, capacity, AllowedRackKind.Full, false, h.Clock.UtcNow));
                foreach (var c in new[] { "F3", "F4" }) db.Locations.Add(new Location(c, c, LocationType.Buffer, capacity, AllowedRackKind.Empty, false, h.Clock.UtcNow));
                db.Locations.Add(new Location("F5", "F5", LocationType.ManualBuffer, capacity, AllowedRackKind.Any, false, h.Clock.UtcNow));
                await db.SaveChangesAsync(); return 0;
            });
        await h.StockAsync("F1", RackKind.Full, capacity);
        h.Rcs.SetFleetSize(amrs);
        return h;
    }

    private static async Task<TransportTask[]> CreateAsync(Harness h, int n)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < n; i++) { ids.Add((await h.CreateTaskAsync("BP05", "F5", $"k{i}")).Task.Id); h.Clock.Advance(TimeSpan.FromMilliseconds(10)); }   // distinct creation times: the FIFO order of the queue is by creation time
        return await Task.WhenAll(ids.Select(h.GetTaskAsync));
    }

    // ================================================================================================ the RCS chooses, whatever the number of AMRs
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task Tasks_are_sent_without_a_robot_and_each_gets_the_AMR_the_RCS_chose_for_1_2_3_and_N_AMRs_with_no_AGV_row_at_all(int amrs)
    {
        await using var h = await ManagedAsync(amrs);
        Assert.Equal(0, await h.Db(db => db.Agvs.CountAsync()));                                  // no AGV1 / AGV2, no role, nothing configured
        var created = await CreateAsync(h, amrs * 2);
        Assert.All(created, t => { Assert.Null(t.AgvId); Assert.Equal(TaskState.Accepted, t.State); Assert.Equal(RcsDispatchState.Accepted, t.RcsDispatchState); Assert.Null(t.RobotCode); });   // sent and accepted - NOT assigned
        Assert.Equal(amrs * 2, h.Rcs.TaskCount);

        await h.TickAsync();                                                                      // "which AMR?" (queryTaskStatus)
        var after = await Task.WhenAll(created.Select(t => h.GetTaskAsync(t.Id)));
        Assert.All(after, t => { Assert.Equal(RcsDispatchState.Assigned, t.RcsDispatchState); Assert.StartsWith("SIM-", t.RobotCode); Assert.NotNull(t.AssignedAt); Assert.Null(t.AgvId); });
        var perRobot = after.GroupBy(t => t.RobotCode).ToDictionary(g => g.Key!, g => g.Count());
        Assert.Equal(amrs, perRobot.Count);                                                       // the RCS spread the tasks over the whole fleet ...
        Assert.All(perRobot.Values, n => Assert.Equal(2, n));                                      // ... (least loaded first): WCS did not queue them per AGV
    }

    [Fact]
    public async Task One_AMR_may_have_several_active_tasks_because_PER_AGV_is_not_applied_to_RcsManaged_tasks()
    {
        await using var h = await ManagedAsync(1);
        var created = await CreateAsync(h, 4);
        Assert.All(created, t => Assert.Equal(TaskState.Accepted, t.State));                       // no AGV queue: nothing waits behind "the" AGV
        await h.TickAsync();
        Assert.Equal(4, (await Task.WhenAll(created.Select(t => h.GetTaskAsync(t.Id)))).Count(t => t.RobotCode == "SIM-001"));
    }

    [Fact]
    public async Task The_robot_code_is_really_chosen_by_the_RCS_even_when_business_AGV_rows_exist()
    {
        await using var h = await ManagedAsync(2, withAgvRows: true);                              // AGV1 (robot R-FULL) and AGV2 (R-EMPTY) exist ...
        var t = (await CreateAsync(h, 1)).Single();
        await h.TickAsync();
        var done = await h.GetTaskAsync(t.Id);
        Assert.StartsWith("SIM-", done.RobotCode);                                                  // ... but the robot is the RCS's choice, never R-FULL / AGV1
        Assert.NotEqual("R-FULL", done.RobotCode);
        Assert.Equal("SIM-001", h.Rcs.RobotOf(done.RcsRequestCode));
        Assert.Null(done.AgvId);                                                                    // SIM-001 is not a business AGV row: AgvId stays empty (never AGV1)
    }

    // ================================================================================================ sent is not assigned
    [Fact]
    public async Task A_task_that_the_RCS_accepted_but_did_not_assign_waits_as_awaiting_assignment_and_is_never_attributed_to_an_AMR()
    {
        await using var h = await ManagedAsync(0);                                                 // the RCS has no AMR
        var t = (await CreateAsync(h, 1)).Single();
        Assert.Equal(RcsDispatchState.Accepted, t.RcsDispatchState);
        await h.TickAsync();
        var waiting = await h.GetTaskAsync(t.Id);
        Assert.Equal(RcsDispatchState.AwaitingAssignment, waiting.RcsDispatchState);
        Assert.Null(waiting.RobotCode); Assert.Null(waiting.AgvId); Assert.Null(waiting.AssignedAt);
        Assert.Equal(TaskState.Accepted, waiting.State);                                            // the 13 task states are untouched

        h.Rcs.SetFleetSize(2); h.Rcs.AssignPending(); h.Clock.Advance(TimeSpan.FromSeconds(6));
        await h.TickAsync();
        var assigned = await h.GetTaskAsync(t.Id);
        Assert.Equal(RcsDispatchState.Assigned, assigned.RcsDispatchState); Assert.Equal("SIM-001", assigned.RobotCode);
    }

    [Fact]
    public async Task A_failing_status_query_is_a_synchronisation_error_not_a_failed_task_and_recovers()
    {
        await using var h = await ManagedAsync(2);
        var t = (await CreateAsync(h, 1)).Single();
        h.Rcs.TaskQueryFailure = true;
        await h.TickAsync();
        var err = await h.GetTaskAsync(t.Id);
        Assert.Equal(RcsDispatchState.SyncError, err.RcsDispatchState); Assert.Equal(TaskState.Accepted, err.State); Assert.Null(err.RobotCode);
        Assert.Contains(err.History, x => x.EventName == "RCS_ASSIGNMENT_SYNC_ERROR");
        h.Rcs.TaskQueryFailure = false; h.Clock.Advance(TimeSpan.FromSeconds(6));
        await h.TickAsync();
        Assert.Equal(RcsDispatchState.Assigned, (await h.GetTaskAsync(t.Id)).RcsDispatchState);
    }

    // ================================================================================================ robot code mapping
    [Fact]
    public async Task A_robot_code_that_is_not_in_the_fleet_read_model_is_kept_raw_and_flagged_without_creating_an_AGV()
    {
        await using var h = await ManagedAsync(2);                                                 // the fleet read model is empty: FleetService never ran
        var t = (await CreateAsync(h, 1)).Single();
        await h.TickAsync();
        var done = await h.GetTaskAsync(t.Id);
        Assert.Equal("SIM-001", done.RobotCode);
        Assert.Contains(done.History, x => x.EventName == "RCS_ROBOT_NOT_IN_FLEET");
        Assert.Equal(0, await h.Db(db => db.Agvs.CountAsync()));                                    // no AGV invented

        await h.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force: true));   // now the fleet is known
        var t2 = (await h.CreateTaskAsync("BP05", "F5", "known")).Task;
        await h.TickAsync();
        Assert.DoesNotContain((await h.GetTaskAsync(t2.Id)).History, x => x.EventName == "RCS_ROBOT_NOT_IN_FLEET");
    }

    [Fact]
    public async Task A_robot_code_that_belongs_to_a_business_AGV_row_sets_AgvId_and_nothing_else_does()
    {
        await using var h = await ManagedAsync(2);
        await h.Db(async db => { db.Agvs.Add(new Agv("LEG-1", AgvRole.FullRack, "SIM-001")); await db.SaveChangesAsync(); return 0; });
        var created = await CreateAsync(h, 2);
        await h.TickAsync();
        var after = await Task.WhenAll(created.Select(t => h.GetTaskAsync(t.Id)));
        var legacyId = (await h.AgvAsync("LEG-1")).Id;
        Assert.Equal(legacyId, after.Single(t => t.RobotCode == "SIM-001").AgvId);
        Assert.Null(after.Single(t => t.RobotCode == "SIM-002").AgvId);
    }

    [Fact]
    public async Task When_the_RCS_changes_the_robot_WCS_follows_and_records_it()
    {
        await using var h = await ManagedAsync(2);
        var t = (await CreateAsync(h, 1)).Single();
        await h.TickAsync();
        Assert.Equal("SIM-001", (await h.GetTaskAsync(t.Id)).RobotCode);
        h.Rcs.ReassignRobot(t.RcsRequestCode, "SIM-002"); h.Clock.Advance(TimeSpan.FromSeconds(61));
        await h.TickAsync();
        var changed = await h.GetTaskAsync(t.Id);
        Assert.Equal("SIM-002", changed.RobotCode);
        Assert.Contains(changed.History, x => x.EventName == "RCS_ROBOT_CHANGED");
    }

    [Fact]
    public async Task The_callback_carries_the_robot_and_the_task_completes_with_its_lifecycle_unchanged()
    {
        await using var h = await ManagedAsync(3);
        var t = (await CreateAsync(h, 1)).Single();
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);                                      // AgvStarted ... DropConfirmed, each callback names the robot
        var done = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Completed, done.State); Assert.Equal("SIM-001", done.RobotCode); Assert.Equal(RcsDispatchState.Assigned, done.RcsDispatchState);
        Assert.Contains(done.History, x => x.EventName == "RCS_ASSIGNED_AMR");
        Assert.True(await h.Db(db => db.InventoryTransactions.AnyAsync(x => x.TaskId == t.Id)));   // the confirmed pick / drop updated the inventory exactly as in Legacy
    }

    // ================================================================================================ timeout, retry, no duplicates
    [Theory]
    [InlineData(RcsFault.TimeoutAfterCreate, 1)]
    [InlineData(RcsFault.TimeoutBeforeCreate, 2)]
    public async Task A_timeout_is_not_a_certain_failure_the_retry_keeps_the_request_code_and_never_creates_a_second_RCS_task(RcsFault fault, int expectedCreateCalls)
    {
        await using var h = await ManagedAsync(2);
        h.Rcs.NextFault = fault;
        var t = (await h.CreateTaskAsync("BP05", "F5", "timeout")).Task;
        var first = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Dispatching, first.State);                                          // not failed
        Assert.Equal(RcsDispatchState.Sending, first.RcsDispatchState);                             // the outcome is unknown
        Assert.Null(first.RcsTaskCode);

        await h.TickAsync();                                                                        // retry: the status is queried first, a create is sent only when the RCS does not know the task
        var retried = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Accepted, retried.State); Assert.Equal(RcsDispatchState.Accepted, retried.RcsDispatchState);
        Assert.Equal(t.RcsRequestCode, retried.RcsRequestCode);                                     // the same request code (reqCode)
        Assert.Equal(1, h.Rcs.TaskCount);                                                           // exactly one task at the RCS
        Assert.Equal(expectedCreateCalls, h.Rcs.CreateCalls);
        Assert.Equal(1, await h.Db(db => db.Tasks.CountAsync()));                                   // and one WCS task
    }

    [Fact]
    public async Task Creating_the_same_request_twice_returns_the_same_task_and_sends_nothing_new()
    {
        await using var h = await ManagedAsync(2);
        var a = await h.CreateTaskAsync("BP05", "F5", "same");
        var b = await h.CreateTaskAsync("BP05", "F5", "same");
        Assert.True(a.Created); Assert.False(b.Created); Assert.Equal(a.Task.Id, b.Task.Id);
        Assert.Equal(1, h.Rcs.CreateCalls);
    }

    // ================================================================================================ fleet-level limit
    [Fact]
    public async Task MaxInFlightTasks_limits_the_fleet_in_FIFO_order_and_frees_the_next_task_when_one_finishes()
    {
        await using var h = await ManagedAsync(3, maxInFlight: 2);
        var created = await CreateAsync(h, 4);
        Assert.Equal([TaskState.Accepted, TaskState.Accepted, TaskState.Queued, TaskState.Queued], created.Select(t => t.State).ToArray());
        Assert.Equal(2, h.Rcs.TaskCount);                                                           // the queued ones were not sent
        Assert.Contains((await h.GetTaskAsync(created[2].Id)).History, x => x.DetailCode == "QUEUED_FLEET_LIMIT");

        await h.AdvanceUntilAsync(created[0].Id, TaskState.Completed);
        var third = await h.GetTaskAsync(created[2].Id); var fourth = await h.GetTaskAsync(created[3].Id);
        Assert.Equal(TaskState.Accepted, third.State);                                              // the oldest queued task went first
        Assert.Equal(TaskState.Queued, fourth.State);
        Assert.Equal(3, h.Rcs.TaskCount);
    }

    [Fact]
    public async Task Without_a_limit_nothing_is_queued_by_WCS()
    {
        await using var h = await ManagedAsync(2, maxInFlight: 0);
        var created = await CreateAsync(h, 6);
        Assert.All(created, t => Assert.Equal(TaskState.Accepted, t.State));
    }

    // ================================================================================================ Legacy is not affected
    [Fact]
    public async Task Legacy_dispatch_is_unchanged_the_AGV_is_chosen_by_role_PER_AGV_queues_and_the_robot_code_is_sent()
    {
        await using var h = await Harness.CreateAsync(fixture);                                    // no Rcs:Dispatch setting = Legacy
        await h.SeedStandardAsync(bufferCapacity: 5); await h.StockAsync("F1", RackKind.Full, 2);
        var first = (await h.CreateTaskAsync("BP05", "F5")).Task; var second = (await h.CreateTaskAsync("BP05", "F5")).Task;
        var a = await h.GetTaskAsync(first.Id); var b = await h.GetTaskAsync(second.Id);
        Assert.Equal((await h.AgvAsync("AGV1")).Id, a.AgvId);
        Assert.Null(a.RcsDispatchState); Assert.Null(a.RobotCode); Assert.False(a.IsRcsManaged);
        Assert.Equal("R-FULL", h.Rcs.RobotOf(a.RcsRequestCode));                                    // WCS sent its AGV's robot code, as before
        Assert.Equal(TaskState.Accepted, a.State); Assert.Equal(TaskState.Queued, b.State);          // PER_AGV
    }

    [Fact]
    public async Task The_PER_AGV_index_still_protects_Legacy_tasks_and_does_not_apply_to_RcsManaged_tasks()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(); var agv = await h.AgvAsync("AGV1"); var a1 = await h.LocationAsync("A1");
        TransportTask Active(string key, bool managed)
        {
            var t = TransportTask.Create(key, ProcessFlow.Bp01, RackKind.Full, agv.Id, a1.Id, "t", h.Clock.UtcNow, rcsManaged: managed);
            t.TransitionTo(TaskState.Validating, "v", "t", h.Clock.UtcNow); t.TransitionTo(TaskState.Dispatching, "d", "t", h.Clock.UtcNow);
            return t;
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db(async db => { db.Tasks.AddRange(Active("l1", false), Active("l2", false)); await db.SaveChangesAsync(); return 0; }));
        await h.Db(async db => { db.Tasks.AddRange(Active("m1", true), Active("m2", true)); await db.SaveChangesAsync(); return 0; });   // two RcsManaged tasks on one robot: allowed
        Assert.Equal(2, await h.Db(db => db.Tasks.CountAsync()));
    }

    [Fact]
    public async Task An_invalid_dispatch_mode_stops_the_start_instead_of_silently_choosing_one()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Harness.CreateAsync(fixture, null, new Dictionary<string, string?> { ["Rcs:Dispatch:Mode"] = "Bogus" }));
    }

    // ================================================================================================ monitoring and statistics
    [Fact]
    public async Task The_fleet_view_links_tasks_by_robot_code_and_counts_the_tasks_that_wait_for_an_AMR()
    {
        await using var h = await ManagedAsync(2);
        var assigned = (await CreateAsync(h, 1)).Single();
        await h.TickAsync();                                                                        // SIM-001 gets it
        h.Rcs.SetFleetSize(2);
        await h.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force: true));
        h.Rcs.AutoAssign = false; await h.CreateTaskAsync("BP05", "F5", "unassigned");              // accepted by the RCS, no AMR yet
        var v = await h.Scoped(sp => sp.GetRequiredService<FleetService>().GetAsync(true));
        Assert.Equal("RcsManaged", v.DispatchMode);
        Assert.Equal(assigned.Id, v.Amrs.Single(a => a.RobotCode == "SIM-001").ActiveTaskId);
        Assert.Null(v.Amrs.Single(a => a.RobotCode == "SIM-002").ActiveTaskId);
        Assert.Equal(1, v.TasksAwaitingAssignment); Assert.Equal(2, v.TasksInFlight);
    }

    [Fact]
    public async Task Statistics_by_robot_never_attribute_a_task_without_a_robot_to_any_AMR_and_history_keeps_its_AGV()
    {
        await using var h = await ManagedAsync(2, withAgvRows: true);
        h.Rcs.AutoAssign = false;
        var unassigned = await CreateAsync(h, 2);                                                   // two tasks without a robot
        h.Rcs.AutoAssign = true; h.Rcs.AssignPending(); h.Clock.Advance(TimeSpan.FromSeconds(6)); await h.TickAsync();
        var perRobot = await h.Db(db => db.Tasks.AsNoTracking().Where(t => t.RobotCode != null).GroupBy(t => t.RobotCode).Select(g => new { g.Key, n = g.Count() }).ToListAsync());
        Assert.Equal(2, perRobot.Sum(x => x.n));
        // before the RCS answered nothing was attributed: the tasks above only got a robot from the RCS
        Assert.All(unassigned, t => Assert.Null(t.RobotCode));
        // the business AGV rows are untouched and no task points to AGV1 / AGV2
        Assert.Equal(2, await h.Db(db => db.Agvs.CountAsync()));
        Assert.Equal(0, await h.Db(db => db.Tasks.CountAsync(t => t.AgvId != null)));
    }
}
