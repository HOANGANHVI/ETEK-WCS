using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using WCS.Application.Fleet;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Persistence;
using WCS.Runtime;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-028 follow-up: (1) legs / RouteEngine in RcsManaged mode never choose an AGV by role, (2) the rollback of AddRcsManagedDispatch stops instead of forcing data, (3) the dispatch mode
/// is not changed while the old mode still has active work and a changed configuration is reported as "restart pending". RCS SIMULATOR only.
/// </summary>
[Collection("Database")]
public class Cr028ModeAndLegTests(DatabaseFixture fixture)
{
    private static readonly string[] A = ["A1", "A2"];
    private static Dictionary<string, string?> Managed => new() { ["Rcs:Dispatch:Mode"] = "RcsManaged" };

    private sealed class RecordingDispatcher : ILegDispatcher
    {
        public List<LegDispatchRequest> Requests { get; } = [];
        public List<(string RequestCode, string? TaskCode, string Destination)> Continues { get; } = [];
        public Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default)
        { lock (Requests) Requests.Add(request); return Task.FromResult(new LegDispatchResult(LegDispatchOutcome.Accepted, $"SIM-{request.RequestCode}", null)); }
        public Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default)
        { lock (Continues) Continues.Add((requestCode, agvTaskCode, destinationLocationCode)); return Task.FromResult(new LegDispatchResult(LegDispatchOutcome.Accepted, agvTaskCode, null)); }
    }

    private static async Task<(RoutingKit Kit, RecordingDispatcher Rec)> KitAsync(DatabaseFixture f, bool managed, bool agvRows)
    {
        var rec = new RecordingDispatcher();
        var kit = await RoutingKit.CreateAsync(f, more: s => s.AddSingleton<ILegDispatcher>(rec), extraConfig: managed ? Managed : null);
        if (!agvRows) await kit.H.Db(db => db.Agvs.ExecuteDeleteAsync());
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);
        return (kit, rec);
    }

    // ================================================================================================ 1. legs: the RCS chooses, WCS never picks by role
    [Fact]
    public async Task RcsManaged_legs_are_sent_without_a_robot_with_no_AGV_row_at_all_and_two_legs_are_not_serialised_per_AGV()
    {
        var (kit, rec) = await KitAsync(fixture, managed: true, agvRows: false);
        await using var _ = kit;
        Assert.Equal(0, await kit.H.Db(db => db.Agvs.CountAsync()));                              // no AGV1 / AGV2, so no role could be looked up

        var o1 = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var o2 = (await kit.StartAsync("o2", "A2", RequestType.PickupRequest, RackKind.Full)).Order;
        foreach (var id in new[] { o1.Id, o2.Id })
        {
            var leg = (await kit.OrderAsync(id)).Legs.Single();
            Assert.Equal(LegState.InProgress, leg.State);                                          // both are on their way at the same time (PER_AGV does not apply)
            Assert.NotNull(leg.AgvTaskCode); Assert.Null(leg.AgvId);                               // sent to the RCS; WCS chose no AGV
            Assert.NotEqual("AGV_UNAVAILABLE", leg.StateReason); Assert.NotEqual("AGV_BUSY", leg.StateReason);
        }
        Assert.Equal(2, rec.Requests.Count);
        Assert.All(rec.Requests, r => Assert.Null(r.RobotCode));                                    // no agvCode in the request
        Assert.All((await kit.TransactionsAsync()).Where(t => t.IsActive), t => { Assert.Null(t.AgvId); Assert.True(t.State < StationTransactionState.AgvAssigned); });   // no AMR is known: not "AgvAssigned"
    }

    [Fact]
    public async Task RcsManaged_legs_ignore_a_DispatchHold_and_an_AGV_that_is_busy_in_Legacy_terms()
    {
        var (kit, rec) = await KitAsync(fixture, managed: true, agvRows: true);
        await using var _ = kit;
        await kit.H.Db(async db => { foreach (var a in await db.Agvs.ToListAsync()) a.PlaceDispatchHold("test", Guid.NewGuid(), kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var leg = (await kit.OrderAsync(o.Id)).Legs.Single();
        Assert.Equal(LegState.InProgress, leg.State); Assert.Null(leg.AgvId);                      // never AGV1, whatever the rack kind
        Assert.Null(rec.Requests.Single().RobotCode);                                               // not R-FULL
    }

    [Fact]
    public async Task RcsManaged_multi_leg_order_completes_and_no_leg_ever_gets_an_AGV()
    {
        var (kit, rec) = await KitAsync(fixture, managed: true, agvRows: false);
        await using var _ = kit;
        await kit.H.StockAsync("B1", RackKind.Full, 1);                                             // B1 is full: leg 1 goes to the buffer F1, leg 2 retrieves it when B1 has room
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var l1 = (await kit.OrderAsync(o.Id)).Legs.Single();
        Assert.Equal("F1", l1.DestinationLocationCode);
        await kit.PickAsync(l1.Id); await kit.DropAsync(l1.Id);
        var after = await kit.OrderAsync(o.Id);
        Assert.Equal(OrderState.Waiting, after.State);                                              // waiting for room at B1, rack stored at the buffer
        Assert.Single(after.Legs); Assert.Null(after.Legs.Single().AgvId);
        Assert.All(rec.Requests, r => Assert.Null(r.RobotCode));
    }

    [Fact]
    public async Task RcsManaged_AfterPick_destination_is_continued_by_task_code_without_any_AGV()
    {
        var (kit, rec) = await KitAsync(fixture, managed: true, agvRows: false);
        await using var _ = kit;
        var (_, v) = await kit.CreateProcessAsync("T-ONLY-B1", RequestType.PickupRequest, RackKind.Full, ["A1"], new VersionDefinition("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "B1")], "AfterPick"), Done()]));
        await kit.PublishAsync(v);
        var o = (await kit.StartAsync("x1", "A1", RequestType.PickupRequest, RackKind.Full, "T-ONLY-B1")).Order;
        var leg = (await kit.OrderAsync(o.Id)).Legs.Single();
        Assert.Null(leg.DestinationLocationCode); Assert.Null(leg.AgvId);
        await kit.PickAsync(leg.Id);                                                                 // the rack is on the AMR; B1 is free, so the destination is chosen now
        var done = (await kit.OrderAsync(o.Id)).Legs.Single();
        Assert.Equal("B1", done.DestinationLocationCode); Assert.Null(done.AgvId);
        var cont = Assert.Single(rec.Continues);                                                     // continueTask goes by the task code of the leg - no robot, no role
        Assert.Equal(done.AgvTaskCode, cont.TaskCode); Assert.Equal("B1", cont.Destination);
    }

    [Fact]
    public async Task Legacy_legs_are_unchanged_AGV_by_role_robot_code_sent_and_PER_AGV_queue()
    {
        var (kit, rec) = await KitAsync(fixture, managed: false, agvRows: true);
        await using var _ = kit;
        var agv1 = await kit.H.AgvAsync("AGV1");
        var o1 = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var o2 = (await kit.StartAsync("o2", "A2", RequestType.PickupRequest, RackKind.Full)).Order;
        var first = (await kit.OrderAsync(o1.Id)).Legs.Single(); var second = (await kit.OrderAsync(o2.Id)).Legs.Single();
        Assert.Equal(agv1.Id, first.AgvId); Assert.Equal("R-FULL", rec.Requests.Single().RobotCode);
        Assert.Equal(LegState.Ready, second.State); Assert.Equal("AGV_BUSY", second.StateReason);   // PER_AGV queue
        Assert.All((await kit.TransactionsAsync()).Where(t => t.LegId == first.Id), t => Assert.True(t.State >= StationTransactionState.AgvAssigned));
    }

    // ================================================================================================ 2. migration rollback guard
    private static WcsDbContext Context(string cs) => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
    private const string Previous = "20261119120653_AddRcsFleetSnapshots";

    private static async Task<int> ScalarAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rollback_of_AddRcsManagedDispatch_stops_while_tasks_have_no_AGV_or_are_active_and_works_when_nothing_blocks_it()
    {
        var scratch = new DatabaseFixture();                                                         // its own scratch database: the shared test database is never rolled back
        await scratch.InitializeAsync();
        try
        {
            await using (var h = await Harness.CreateAsync(scratch, null, Managed))
            {
                await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
                await h.StockAsync("F1", RackKind.Full, 5);
                h.Rcs.SetFleetSize(1);
                var task = (await h.CreateTaskAsync("BP05", "F5", "rb1")).Task;                      // an RcsManaged task, active, AgvId NULL (not assigned)
                Assert.Null((await h.GetTaskAsync(task.Id)).AgvId);

                // (a) active task without AGV: refused, nothing changed
                var ex = await Assert.ThrowsAnyAsync<Exception>(() => Context(scratch.ConnectionString).GetService<IMigrator>().MigrateAsync(Previous));
                Assert.Contains("ROLLBACK_REFUSED", ex.ToString());
                Assert.Contains("1 task(s) have no AgvId and 1 RcsManaged task(s) are still active", ex.ToString());
                Assert.Equal(1, await ScalarAsync(scratch.ConnectionString, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='transport_tasks' AND column_name='RcsDispatchState'"));
                Assert.Equal(1, await ScalarAsync(scratch.ConnectionString, "SELECT COUNT(*) FROM transport_tasks"));      // the task and its history are still there
                Assert.True(await ScalarAsync(scratch.ConnectionString, "SELECT COUNT(*) FROM task_history") > 0);

                // (b) finished but still without an AGV: still refused (WCS does not invent AGV1 / AGV2)
                await scratch.ExecAsync("UPDATE transport_tasks SET \"State\" = 'Completed'");
                var ex2 = await Assert.ThrowsAnyAsync<Exception>(() => Context(scratch.ConnectionString).GetService<IMigrator>().MigrateAsync(Previous));
                Assert.Contains("1 task(s) have no AgvId and 0 RcsManaged task(s) are still active", ex2.ToString());
                Assert.Equal(1, await ScalarAsync(scratch.ConnectionString, "SELECT COUNT(*) FROM transport_tasks WHERE \"AgvId\" IS NULL"));

                // (c) the owner decided what the AGV of that finished task is (here: set explicitly by hand) -> the rollback is possible and keeps every row
                await scratch.ExecAsync("UPDATE transport_tasks SET \"AgvId\" = (SELECT \"Id\" FROM agvs ORDER BY \"Code\" LIMIT 1)");
                await Context(scratch.ConnectionString).GetService<IMigrator>().MigrateAsync(Previous);
                Assert.Equal(0, await ScalarAsync(scratch.ConnectionString, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='transport_tasks' AND column_name='RcsDispatchState'"));
                Assert.Equal("NO", await ScalarStringAsync(scratch.ConnectionString, "SELECT is_nullable FROM information_schema.columns WHERE table_name='transport_tasks' AND column_name='AgvId'"));
                Assert.Equal(1, await ScalarAsync(scratch.ConnectionString, "SELECT COUNT(*) FROM transport_tasks"));

                // and forward again
                await Context(scratch.ConnectionString).Database.MigrateAsync();
                Assert.Equal("YES", await ScalarStringAsync(scratch.ConnectionString, "SELECT is_nullable FROM information_schema.columns WHERE table_name='transport_tasks' AND column_name='AgvId'"));
            }
        }
        finally { await scratch.DisposeAsync(); }
    }

    private static async Task<string> ScalarStringAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return (string)(await cmd.ExecuteScalarAsync())!;
    }

    // ================================================================================================ 3. mode switch
    [Fact]
    public async Task Switching_to_RcsManaged_is_refused_while_Legacy_tasks_are_active_and_allowed_after_they_finish()
    {
        await using var h = await Harness.CreateAsync(fixture);                                      // Legacy
        await h.SeedStandardAsync(bufferCapacity: 10, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 3);
        var task = (await h.CreateTaskAsync("BP05", "F5", "m1")).Task;

        var refused = await h.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("RcsManaged"));
        Assert.False(refused.Allowed); Assert.Equal(1, refused.ActiveTasks); Assert.Equal("Legacy", refused.OtherMode);
        Assert.Contains("DISPATCH_MODE_CHANGE_REFUSED", refused.Message); Assert.Contains("'RcsManaged'", refused.Message); Assert.Contains("'Legacy'", refused.Message);
        Assert.True((await h.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("Legacy"))).Allowed);   // staying in Legacy needs no check

        await h.AdvanceUntilAsync(task.Id, TaskState.Completed);
        Assert.True((await h.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("RcsManaged"))).Allowed);
    }

    [Fact]
    public async Task Switching_back_to_Legacy_is_refused_while_RcsManaged_tasks_or_legs_are_active()
    {
        await using var h = await Harness.CreateAsync(fixture, null, Managed);
        await h.SeedStandardAsync(bufferCapacity: 10, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 3);
        h.Rcs.SetFleetSize(1);
        var task = (await h.CreateTaskAsync("BP05", "F5", "m2")).Task;                               // RcsManaged, no AGV
        var refused = await h.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("Legacy"));
        Assert.False(refused.Allowed); Assert.Equal(1, refused.ActiveTasks); Assert.Equal("RcsManaged", refused.OtherMode);
        Assert.Contains("DISPATCH_MODE_CHANGE_REFUSED", refused.Message);
        Assert.Equal(1, await h.Db(db => db.Tasks.CountAsync(t => t.AgvId == null)));              // nothing was converted or assigned by the check
        await h.TickAsync();
        await h.AdvanceUntilAsync(task.Id, TaskState.Completed);
        Assert.True((await h.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("Legacy"))).Allowed);
    }

    [Fact]
    public async Task A_leg_on_its_way_blocks_the_switch_in_both_directions()
    {
        var (kit, _) = await KitAsync(fixture, managed: true, agvRows: false);
        await using (kit)
        {
            await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full);              // RcsManaged leg, sent, no AGV
            var back = await kit.H.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("Legacy"));
            Assert.False(back.Allowed); Assert.Equal(1, back.ActiveLegs);
        }
        var (kit2, _) = await KitAsync(fixture, managed: false, agvRows: true);
        await using (kit2)
        {
            await kit2.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full);             // Legacy leg with AGV1
            var fwd = await kit2.H.Scoped(sp => sp.GetRequiredService<DispatchModeGuard>().CheckAsync("RcsManaged"));
            Assert.False(fwd.Allowed); Assert.Equal(1, fwd.ActiveLegs);
        }
    }

    [Fact]
    public async Task Startup_check_refuses_to_start_in_a_mode_while_the_other_mode_has_active_work()
    {
        await using var h = await Harness.CreateAsync(fixture);                                      // an active Legacy task exists ...
        await h.SeedStandardAsync(bufferCapacity: 10, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 3);
        await h.CreateTaskAsync("BP05", "F5", "m3");
        var scopes = h.Services.GetRequiredService<IServiceScopeFactory>();
        // ... and the process now starts with Rcs:Dispatch:Mode = RcsManaged
        var managedState = new DispatchModeState(Options.Create(new DispatchOptions { Mode = "RcsManaged" }), new FakeMonitor(new DispatchOptions { Mode = "RcsManaged" }), NullLogger<DispatchModeState>.Instance);
        var ex = await Assert.ThrowsAsync<DispatchModeChangeRefusedException>(() => new DispatchModeStartupCheck(scopes, managedState, NullLogger<DispatchModeStartupCheck>.Instance).StartAsync(CancellationToken.None));
        Assert.Contains("DISPATCH_MODE_CHANGE_REFUSED", ex.Message);
        // started in Legacy (the mode of the active work): fine
        var legacyState = new DispatchModeState(Options.Create(new DispatchOptions()), new FakeMonitor(new DispatchOptions()), NullLogger<DispatchModeState>.Instance);
        await new DispatchModeStartupCheck(scopes, legacyState, NullLogger<DispatchModeStartupCheck>.Instance).StartAsync(CancellationToken.None);
    }

    private sealed class FakeMonitor(DispatchOptions initial) : IOptionsMonitor<DispatchOptions>
    {
        private Action<DispatchOptions, string?>? _listener;
        public DispatchOptions CurrentValue { get; private set; } = initial;
        public DispatchOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<DispatchOptions, string?> listener) { _listener = listener; return null; }
        public void Change(DispatchOptions next) { CurrentValue = next; _listener?.Invoke(next, null); }
    }

    [Fact]
    public void A_configuration_changed_while_WCS_runs_is_reported_as_restart_pending_and_the_running_mode_is_kept()
    {
        var monitor = new FakeMonitor(new DispatchOptions { Mode = "Legacy" });
        var state = new DispatchModeState(Options.Create(new DispatchOptions { Mode = "Legacy" }), monitor, NullLogger<DispatchModeState>.Instance);
        Assert.False(state.RestartPending); Assert.Equal("Legacy", state.EffectiveMode);
        monitor.Change(new DispatchOptions { Mode = "RcsManaged" });                                  // somebody edits appsettings while WCS runs
        Assert.True(state.RestartPending);
        Assert.Equal("Legacy", state.EffectiveMode);                                                  // the runtime keeps the mode it started with ...
        Assert.Equal("RcsManaged", state.ConfiguredMode);                                             // ... and says what is configured (shown in the fleet view)
        monitor.Change(new DispatchOptions { Mode = "Legacy" });
        Assert.False(state.RestartPending);                                                           // changed back: nothing pending
    }
}
