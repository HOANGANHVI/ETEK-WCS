using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Application.RealDispatch;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Rcs;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using Xunit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-038 hardening: tasks created before CR-038, the simulator lifecycle with its messages, every communication path of the monitor (query, cancel, callback, unlinked), recovery after a restart
/// with an unknown outcome, mode changes racing task creation, and the leg readiness of configurable processes. Real = the CR-030 MOCK RCS only.
/// </summary>
[Collection("Database")]
public class Cr038HardeningTests(DatabaseFixture fixture)
{
    private const string Token = "tok-123", CbSecret = "cb-secret";

    private sealed class FakeFleet(FleetView? view) : IFleetStatusReader { public Task<FleetView?> ReadAsync(CancellationToken ct = default) => Task.FromResult(view); }
    private static readonly FleetView ReadyFleet = new("Real", false, "Connected", null, null, null, null, 0, 5, 30, 2, 2, []);

    private static Dictionary<string, string?> RealCfg(int timeout = 1, bool legTemplate = false, string? withoutLocation = null)
    {
        var d = new Dictionary<string, string?>
        {
            ["Rcs:RealDispatch:Enabled"] = "true", ["Rcs:RealDispatch:TimeoutSeconds"] = timeout.ToString(), ["Rcs:RealDispatch:ClientCode"] = "WCS", ["Rcs:Dispatch:Mode"] = "RcsManaged",
            ["Rcs:RealDispatch:Callback:Enabled"] = "true", ["Rcs:RealDispatch:Callback:SharedSecret"] = CbSecret,
        };
        foreach (var c in new[] { "A1", "A2", "B1", "B2", "F1", "F2", "F3", "F4", "F5" })
            if (c != withoutLocation) { d[$"Rcs:RealDispatch:Locations:{c}:PositionCode"] = "P-" + c; d[$"Rcs:RealDispatch:Locations:{c}:Type"] = "00"; }
        foreach (var f in new[] { "Bp01", "Bp02", "Bp03", "Bp05", "Bp06" })
        {
            d[$"Rcs:RealDispatch:Processes:{f}:TaskTyp"] = "F01"; d[$"Rcs:RealDispatch:Processes:{f}:PositionPath:0"] = "Source"; d[$"Rcs:RealDispatch:Processes:{f}:PositionPath:1"] = "Request";
        }
        if (legTemplate) { d["Rcs:RealDispatch:LegTemplate:TaskTyp"] = "TPL-LEG"; d["Rcs:RealDispatch:LegTemplate:PositionPath:0"] = "Source"; d["Rcs:RealDispatch:LegTemplate:PositionPath:1"] = "Destination"; }
        return d;
    }

    private async Task<(Harness H, MockRcsServer Rcs)> RealAsync(int timeout = 1)
    {
        var rcs = await MockRcsServer.StartAsync(Token);
        var h = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(ReadyFleet)), RealCfg(timeout));
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 8);
        await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(
            new UpdateRcsCommand(true, new RcsSettings("MockRCS", "127.0.0.1", rcs.Port, "http://127.0.0.1", ConnectionOptions.AuthToken, null, 5000), null, false, Token, false, "test")));
        return (h, rcs);
    }

    private async Task<Harness> SimAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 8);
        return h;
    }

    private static Task<IReadOnlyList<RcsMessageSummary>?> Msgs(Harness h, Guid id) => h.Scoped(sp => sp.GetRequiredService<IRcsMessageQuery>().ForTaskAsync(id));
    private static Task<T> Svc<T>(Harness h, Func<IExecutionModeService, Task<T>> f) => h.Scoped(sp => f(sp.GetRequiredService<IExecutionModeService>()));
    private static Task Init(Harness h) => Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
    private static Task<int> Sql(Harness h, string sql, params object[] p) => h.Db(db => db.Database.ExecuteSqlRawAsync(sql, p));
    private static ModeTransitionRequest To(ExecutionMode m, bool confirm = true) => new(Guid.NewGuid().ToString("N"), m, "test", confirm);
    private static IRcsClient Client(Harness h) => h.Services.GetRequiredService<IRcsClient>();
    private static async Task SetRowAsync(Harness h, ExecutionMode mode)
    {
        await h.Db(async db =>
        {
            var s = await db.ExecutionModeStates.FirstOrDefaultAsync(x => x.Id == 1);
            if (s is null) { s = ExecutionModeState.Default(h.Clock.UtcNow); db.ExecutionModeStates.Add(s); }
            s.SwitchTo(mode, "test", h.Clock.UtcNow); await db.SaveChangesAsync(); return 0;
        });
        await Init(h);
    }

    // ================================================================================================ 1. tasks created before CR-038
    [Fact]
    public async Task A_task_without_a_pinned_mode_is_sent_nowhere_even_while_Real_dispatch_is_enabled()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "legacy1")).Task;
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask"));
        await Sql(h, "UPDATE transport_tasks SET \"ExecutionMode\" = NULL WHERE \"Id\" = {0}", t.Id);   // what a row created before CR-038 looks like
        var (creates, queries, simCalls) = (rcs.Count("genAgvSchedulingTask"), rcs.Count("queryTaskStatus"), h.Rcs.CreateCalls);

        var create = await Client(h).CreateTaskAsync(new RcsCreateTaskRequest(t.RcsRequestCode, ProcessFlow.Bp05, RackKind.Full, null, "F1", "F5"));
        var cont = await Client(h).ContinueTaskAsync(t.RcsRequestCode, t.RcsTaskCode, "F5");
        var cancel = await Client(h).CancelTaskAsync(t.RcsRequestCode, t.RcsTaskCode);
        var query = await Client(h).QueryTaskAsync(t.RcsRequestCode);
        Assert.All([create, cont, cancel], r => { Assert.Equal(RcsCallOutcome.Unknown, r.Outcome); Assert.Contains("LEGACY_TASK_MODE_UNKNOWN", r.Message); });   // an unknown outcome, never "rejected" and never "accepted"
        Assert.Equal(RcsTaskStatus.Unknown, query.Status);
        for (var i = 0; i < 3; i++) await h.TickAsync();
        Assert.Equal((creates, queries), (rcs.Count("genAgvSchedulingTask"), rcs.Count("queryTaskStatus")));   // nothing reached the real RCS: no create, no continue, no cancel, not even a query
        Assert.Equal(0, rcs.Count("continueTask")); Assert.Equal(0, rcs.Count("cancelTask"));
        Assert.Equal(simCalls, h.Rcs.CreateCalls);                                                         // and nothing reached the simulator
    }

    [Fact]
    public async Task Unpinned_work_blocks_both_mode_changes_and_the_Admin_can_adopt_it_as_Simulator_but_never_as_Real()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await Init(h);
        var a = (await h.CreateTaskAsync("BP05", "F5", "legacyA")).Task;
        var b = (await h.CreateTaskAsync("BP05", "F5", "legacyB")).Task;
        await Sql(h, "UPDATE transport_tasks SET \"ExecutionMode\" = NULL, \"RcsTaskCode\" = 'SIM-00001' WHERE \"Id\" = {0}", a.Id);        // looks like a simulator task
        await Sql(h, "UPDATE transport_tasks SET \"ExecutionMode\" = NULL, \"RcsTaskCode\" = {1} WHERE \"Id\" = {0}", b.Id, b.RcsRequestCode);   // an RCS code that is not a simulator code

        var toReal = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"));
        Assert.False(toReal.Succeeded);
        Assert.Contains(toReal.BlockingWork, w => w.Id == a.Id.ToString() && w.Reason.StartsWith("LEGACY")); Assert.Contains(toReal.BlockingWork, w => w.Id == b.Id.ToString());

        var adopt = await Svc(h, s => s.AdoptLegacyAsSimulatorAsync("old simulator work", "admin"));
        Assert.Equal(1, adopt.AdoptedTasks);
        Assert.Contains(adopt.NotAdopted, w => w.Id == b.Id.ToString());                                  // may exist in the real RCS: not adopted
        Assert.Equal(ExecutionMode.Simulator, (await h.GetTaskAsync(a.Id)).ExecutionMode);
        Assert.Null((await h.GetTaskAsync(b.Id)).ExecutionMode);                                          // never pinned to Real by this tool
        Assert.Equal(1, await h.Db(db => db.AuditLog.CountAsync(x => x.Action == "EXECUTION_MODE_ADOPT_LEGACY")));
        await Assert.ThrowsAsync<AppException>(() => Svc(h, s => s.AdoptLegacyAsSimulatorAsync(" ", "admin")));

        await SetRowAsync(h, ExecutionMode.Real);                                                        // the other direction is blocked by unpinned work too
        var toSim = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        Assert.False(toSim.Succeeded); Assert.Contains(toSim.BlockingWork, w => w.Id == b.Id.ToString());
    }

    [Fact]
    public async Task The_simulator_never_drives_a_Real_task_or_an_unpinned_task()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "drv1")).Task;
        var ex = await Assert.ThrowsAsync<AppException>(() => h.Scoped(sp => sp.GetRequiredService<SimulationDriver>().AdvanceAsync(t.Id)));
        Assert.Equal("SIMULATION_NOT_ALLOWED_FOR_TASK", ex.Code);
        await Sql(h, "UPDATE transport_tasks SET \"ExecutionMode\" = NULL WHERE \"Id\" = {0}", t.Id);
        await Assert.ThrowsAsync<AppException>(() => h.Scoped(sp => sp.GetRequiredService<SimulationDriver>().AdvanceAsync(t.Id)));
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);                              // no signal was injected
    }

    // ================================================================================================ 2. simulator lifecycle
    [Fact]
    public async Task A_simulated_task_runs_from_create_to_completed_with_every_step_recorded_and_the_rack_where_it_belongs()
    {
        await using var h = await SimAsync();
        var t = (await h.CreateTaskAsync("BP05", "F5", "life1")).Task;
        var steps = await h.Scoped(sp => sp.GetRequiredService<SimulationDriver>().RunAsync(t.Id));
        var done = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Completed, done.State); Assert.Equal(ExecutionMode.Simulator, done.ExecutionMode);
        Assert.NotNull(done.PickConfirmedAt); Assert.NotNull(done.DropConfirmedAt);
        Assert.Single(await h.RacksAtAsync("F5")); Assert.Equal(7, (await h.RacksAtAsync("F1")).Count);   // inventory agrees with the task: one rack moved F1 -> F5

        var types = (await Msgs(h, t.Id))!.OrderBy(m => m.Seq).Select(m => (m.Direction, m.MessageType)).ToList();
        Assert.Equal((RcsMessageDirection.Out, "Create Task"), types[0]); Assert.Equal((RcsMessageDirection.In, "Create Task Response"), types[1]);
        var events = types.Where(x => x.MessageType.StartsWith("Simulated AGV Event")).Select(x => x.MessageType).ToList();
        Assert.True(events.IndexOf("Simulated AGV Event: AgvStarted") < events.IndexOf("Simulated AGV Event: PickConfirmed") && events.IndexOf("Simulated AGV Event: PickConfirmed") < events.IndexOf("Simulated AGV Event: DropConfirmed"), string.Join(",", events));
        Assert.Equal(steps.Count(s => s.Signal is "AgvStarted" or "PickConfirmed" or "DropConfirmed"), events.Count);   // one message per step the simulator really performed
        Assert.All((await Msgs(h, t.Id))!, m => Assert.Equal(ExecutionMode.Simulator, m.Mode));
    }

    [Fact]
    public async Task A_simulated_task_that_is_not_driven_stays_where_it_is_nothing_is_faked()
    {
        await using var h = await SimAsync();
        var t = (await h.CreateTaskAsync("BP05", "F5", "life2")).Task;
        var after = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Accepted, after.State); Assert.Null(after.PickConfirmedAt); Assert.Null(after.DropConfirmedAt);
        Assert.DoesNotContain((await Msgs(h, t.Id))!, m => m.MessageType.StartsWith("Simulated AGV Event"));   // no pickup / drop message without the step
        Assert.Empty(await h.RacksAtAsync("F5"));
    }

    [Fact]
    public async Task A_simulated_cancel_is_recorded_both_ways_and_ends_cancelled()
    {
        await using var h = await SimAsync();
        var t = (await h.CreateTaskAsync("BP05", "F5", "life3")).Task;
        await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t.Id, "test", "admin"));
        await h.Scoped(sp => sp.GetRequiredService<SimulationDriver>().RunAsync(t.Id));
        Assert.Equal(TaskState.Cancelled, (await h.GetTaskAsync(t.Id)).State);
        var m = (await Msgs(h, t.Id))!;
        Assert.Contains(m, x => x.MessageType == "Cancel Task" && x.Direction == RcsMessageDirection.Out);
        Assert.Contains(m, x => x.MessageType == "Cancel Task Response" && x.Direction == RcsMessageDirection.In && x.Result == "Accepted");
        Assert.Contains(m, x => x.MessageType == "Simulated AGV Event: RcsCancelConfirmed");
    }

    // ================================================================================================ 3. communication monitor
    [Fact]
    public async Task Queries_are_recorded_only_when_the_answer_changes_in_Real_and_in_Simulator()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "q1")).Task;
        int Queries(IReadOnlyList<RcsMessageSummary> m) => m.Count(x => x.MessageType == "Query Task Status");
        await Client(h).QueryTaskAsync(t.RcsRequestCode);
        var baseline = Queries((await Msgs(h, t.Id))!);
        for (var i = 0; i < 4; i++) await Client(h).QueryTaskAsync(t.RcsRequestCode);
        Assert.Equal(baseline, Queries((await Msgs(h, t.Id))!));                                         // the same answer five times: no new message
        rcs.SetStatus(t.RcsRequestCode, "2");
        await Client(h).QueryTaskAsync(t.RcsRequestCode);
        var rows = (await Msgs(h, t.Id))!;
        Assert.Equal(baseline + 1, Queries(rows));                                                       // a change: one new OUT, and its IN
        Assert.Contains(rows, m => m.MessageType == "Query Task Status Response" && m.Mode == ExecutionMode.Real && m.HttpStatus == 200);

        await using var sim = await SimAsync();
        var s = (await sim.CreateTaskAsync("BP05", "F5", "q2")).Task;
        await Client(sim).QueryTaskAsync(s.RcsRequestCode);
        var simBase = (await Msgs(sim, s.Id))!.Count(x => x.MessageType == "Query Task Status");
        for (var i = 0; i < 4; i++) await Client(sim).QueryTaskAsync(s.RcsRequestCode);
        Assert.Equal(simBase, (await Msgs(sim, s.Id))!.Count(x => x.MessageType == "Query Task Status"));
        sim.Rcs.SetStatus(s.RcsRequestCode, RcsTaskStatus.Executing);
        await Client(sim).QueryTaskAsync(s.RcsRequestCode);
        Assert.Equal(simBase + 1, (await Msgs(sim, s.Id))!.Count(x => x.MessageType == "Query Task Status"));
    }

    [Fact]
    public async Task A_real_cancel_is_recorded_request_and_response()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "c1")).Task;
        await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(t.Id, "test", "admin"));
        Assert.Equal(1, rcs.Count("cancelTask"));
        var m = (await Msgs(h, t.Id))!;
        var o = Assert.Single(m, x => x.MessageType == "Cancel Task");
        Assert.Equal(ExecutionMode.Real, o.Mode);
        Assert.Contains(m, x => x.MessageType == "Cancel Task Response" && x.HttpStatus == 200);
    }

    [Fact]
    public async Task A_callback_is_linked_to_its_task_by_task_code_masked_and_never_leaks_into_another_task_or_an_unknown_one_is_shown_unlinked()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        var a = (await h.CreateTaskAsync("BP05", "F5", "cbA")).Task;
        var b = (await h.CreateTaskAsync("BP05", "F5", "cbB")).Task;
        string Cb(string code, string method = "start") => $$"""{"reqCode":"rq-{{method}}-{{code}}","method":"{{method}}","robotCode":"R001","taskCode":"{{code}}","token":"leak-me"}""";
        var applied = await h.Scoped(sp => sp.GetRequiredService<RcsCallbackService>().HandleAsync(Cb(a.RcsRequestCode)));
        var unknown = await h.Scoped(sp => sp.GetRequiredService<RcsCallbackService>().HandleAsync(Cb("WCS-no-such-task")));

        var ma = (await Msgs(h, a.Id))!; var mb = (await Msgs(h, b.Id))!;
        var cb = Assert.Single(ma, x => x.MessageType == "AGV Callback: start");
        Assert.Equal(ExecutionMode.Real, cb.Mode); Assert.Equal(RcsMessageDirection.In, cb.Direction); Assert.Equal(applied.Status.ToString(), cb.Result);
        var detail = (await h.Scoped(sp => sp.GetRequiredService<IRcsMessageQuery>().GetAsync(cb.Id)))!;
        Assert.Equal(a.Id, detail.TaskId); Assert.Equal(a.RequestKey, detail.RequestKey); Assert.DoesNotContain("leak-me", detail.Payload); Assert.Contains(a.RcsRequestCode, detail.Payload);
        Assert.DoesNotContain(mb, x => x.MessageType.StartsWith("AGV Callback"));                         // task B got nothing of A's
        Assert.DoesNotContain(mb, x => x.RequestCode == a.RcsRequestCode); Assert.DoesNotContain(ma, x => x.RequestCode == b.RcsRequestCode);
        Assert.DoesNotContain(ma.Concat(mb), x => x.RequestCode == "WCS-no-such-task");                   // the unknown code was attached to nobody
        var unlinked = await h.Scoped(sp => sp.GetRequiredService<IRcsMessageQuery>().UnlinkedAsync(50));
        var u = Assert.Single(unlinked, x => x.RequestCode == "WCS-no-such-task");
        Assert.Equal(unknown.Status.ToString(), u.Result);
        Assert.Null((await h.Scoped(sp => sp.GetRequiredService<IRcsMessageQuery>().GetAsync(u.Id)))!.TaskId);
    }

    [Fact]
    public async Task A_callback_after_a_restart_is_applied_and_recorded_and_the_earlier_history_is_still_there()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await SetRowAsync(h, ExecutionMode.Real);
        var t = (await h.CreateTaskAsync("BP05", "F5", "cbR")).Task;
        var before = (await Msgs(h, t.Id))!.Count;
        await h.RestartAsync(); await Init(h);
        Assert.False((await Svc(h, s => s.GetAsync())).RecoveryRequired);                                // the RCS had answered: nothing is unresolved
        var r = await h.Scoped(sp => sp.GetRequiredService<RcsCallbackService>().HandleAsync($$"""{"reqCode":"rq1","method":"start","robotCode":"R001","taskCode":"{{t.RcsRequestCode}}"}"""));
        Assert.Equal(RcsCallbackStatus.Applied, r.Status);
        var rows = (await Msgs(h, t.Id))!;
        Assert.Equal(before + 1, rows.Count);                                                            // nothing lost, the callback added
        Assert.Contains(rows, m => m.MessageType == "AGV Callback: start");
    }

    [Fact]
    public async Task A_late_answer_after_the_timeout_adds_nothing_and_the_task_is_not_created_twice()
    {
        var (h, rcs) = await RealAsync(timeout: 1); await using var _h = h; await using var _r = rcs;
        rcs.DelayNextCreate = TimeSpan.FromSeconds(3);                                                    // the RCS stores the task and answers LATE
        var t = (await h.CreateTaskAsync("BP05", "F5", "late1")).Task;
        await Task.Delay(TimeSpan.FromSeconds(3.5));                                                      // the late answer arrives (and is discarded)
        var m = (await Msgs(h, t.Id))!;
        Assert.Equal(1, m.Count(x => x.Direction == RcsMessageDirection.Out && x.MessageType == "Create Task"));
        Assert.Equal("Timeout", Assert.Single(m, x => x.MessageType == "Create Task Response").Result);   // the timeout stays the recorded outcome
        for (var i = 0; i < 3; i++) await h.TickAsync();
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask")); Assert.Equal(1, rcs.TaskCount);              // the retry asked the RCS first and found the task: no second create
    }

    // ================================================================================================ 4. restart and recovery
    [Theory]
    [InlineData(true)]    // the RCS stored the task but the answer was lost
    [InlineData(false)]   // the RCS never stored it
    public async Task After_a_restart_an_unknown_dispatch_is_never_resent_by_itself_and_the_history_survives(bool rcsStoredIt)
    {
        var (h, rcs) = await RealAsync(timeout: 1); await using var _h = h; await using var _r = rcs;
        await SetRowAsync(h, ExecutionMode.Real);
        if (rcsStoredIt) rcs.DelayNextCreate = TimeSpan.FromSeconds(3); else rcs.FailNextCreateBeforeStoring = true;
        var t = (await h.CreateTaskAsync("BP05", "F5", $"rec-{rcsStoredIt}")).Task;
        var before = (await Msgs(h, t.Id))!.Select(x => x.Id).ToHashSet();
        var requests = rcs.Count("genAgvSchedulingTask");
        await h.RestartAsync(); await Init(h);
        Assert.True((await Svc(h, s => s.GetAsync())).RecoveryRequired);
        Assert.Equal(ExecutionMode.Real, (await Svc(h, s => s.GetAsync())).Mode);                         // not Simulator
        for (var i = 0; i < 3; i++) await h.TickAsync();
        Assert.Equal(requests, rcs.Count("genAgvSchedulingTask"));                                        // nothing resent: recovery blocks every new dispatch
        Assert.True((await Msgs(h, t.Id))!.Select(x => x.Id).ToHashSet().IsSupersetOf(before));          // and no message was lost
        Assert.Equal(rcsStoredIt ? 1 : 0, rcs.TaskCount);
    }

    [Fact]
    public async Task A_mode_change_racing_task_creation_never_leaves_Simulator_work_behind_a_successful_switch()
    {
        for (var round = 0; round < 3; round++)
        {
            var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
            await Init(h);
            var creates = Enumerable.Range(0, 4).Select(i => Task.Run(async () => { try { return (await h.CreateTaskAsync("BP05", "F5", $"race-{round}-{i}")).Task.Id; } catch (AppException) { return Guid.Empty; } })).ToList();
            var switchTask = Task.Run(() => Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin")));
            await Task.WhenAll(creates.Append(switchTask.ContinueWith(_ => Guid.Empty)));
            var sw = await switchTask;
            var tasks = await h.Db(db => db.Tasks.AsNoTracking().ToListAsync());
            Assert.All(tasks, x => Assert.NotNull(x.ExecutionMode));                                      // every task got a mode
            var openSim = tasks.Where(x => x.ExecutionMode == ExecutionMode.Simulator && x.State is not (TaskState.Completed or TaskState.Failed or TaskState.Cancelled)).ToList();
            if (sw.Succeeded) Assert.Empty(openSim);                                                      // a successful switch left no unfinished Simulator task behind
            else Assert.Contains(sw.Failed, c => c.Code == "NO_ACTIVE_SIMULATOR_WORK");                   // the refusal names the work that raced in
            Assert.Equal(sw.Succeeded ? ExecutionMode.Real : ExecutionMode.Simulator, (await Svc(h, s => s.GetAsync())).Mode);
        }
    }

    // ================================================================================================ 6. configurable processes (Q-MLEG-1)
    [Fact]
    public void The_leg_template_is_checked_before_any_leg_is_sent()
    {
        Assert.NotNull(RealDispatchReadiness.LegTemplateProblem(new RealDispatchOptions()));
        Assert.NotNull(RealDispatchReadiness.LegTemplateProblem(new RealDispatchOptions { LegTemplate = new ProcessMapping { TaskTyp = "T", PositionPath = ["Request"] } }));   // only Source / Destination exist for a leg
        Assert.NotNull(RealDispatchReadiness.LegTemplateProblem(new RealDispatchOptions { LegTemplate = new ProcessMapping { TaskTyp = "", PositionPath = ["Source", "Destination"] } }));
        Assert.Null(RealDispatchReadiness.LegTemplateProblem(new RealDispatchOptions { LegTemplate = new ProcessMapping { TaskTyp = "TPL", PositionPath = ["Source", "Destination"] } }));
        var o = new RealDispatchOptions(); o.Locations["F1"] = new LocationMapping { PositionCode = "P1", Type = "00" };
        Assert.Single(RealDispatchReadiness.LegLocationProblems(o, ["F1", "F2", "  ", null, "f1"]));        // only F2 is missing; blanks and case duplicates are ignored
    }

    [Fact]
    public async Task A_Real_order_is_refused_up_front_when_a_leg_could_not_be_sent_so_nothing_is_half_sent()
    {
        foreach (var (template, without, expect) in new[] { (false, (string?)null, "template"), (true, "F2", "positionCode") })
        {
            await using var kit = await RoutingKit.CreateAsync(fixture, extraConfig: RealCfg(legTemplate: template, withoutLocation: without), more: s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(ReadyFleet)));
            var (_, ver) = await kit.CreateProcessAsync("T-REAL-LEG", RequestType.PickupRequest, RackKind.Full, ["A1", "A2"], RoutingKit.OneLeg("Request", RoutingKit.Dest(1, "B1"), RoutingKit.Dest(2, "F2")));
            await kit.PublishAsync(ver);
            var ex = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("k-" + expect, "A1", RequestType.PickupRequest, RackKind.Full, "T-REAL-LEG"));
            Assert.Equal("REAL_LEG_MAPPING_INCOMPLETE", ex.Code); Assert.Contains(expect, ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await kit.H.Db(db => db.TransportOrders.CountAsync()));                           // no order, no leg, nothing sent
            var readiness = await kit.H.Scoped(sp => sp.GetRequiredService<IExecutionModeService>().ReadinessAsync(ExecutionMode.Real));
            Assert.Contains(readiness.Conditions, c => c.Code == (template ? "PROCESS_LEG_LOCATIONS" : "LEG_TEMPLATE") && !c.Ok);   // the mode switch says the same before anyone tries
        }
    }

    [Fact]
    public async Task A_Real_order_with_a_template_and_every_location_mapped_is_accepted_and_the_readiness_agrees()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture, extraConfig: RealCfg(legTemplate: true), more: s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(ReadyFleet)));
        var (_, ver) = await kit.CreateProcessAsync("T-REAL-OK", RequestType.PickupRequest, RackKind.Full, ["A1", "A2"], RoutingKit.OneLeg("Request", RoutingKit.Dest(1, "B1"), RoutingKit.Dest(2, "F1")));
        await kit.PublishAsync(ver);
        var readiness = await kit.H.Scoped(sp => sp.GetRequiredService<IExecutionModeService>().ReadinessAsync(ExecutionMode.Real));
        Assert.Contains(readiness.Conditions, c => c.Code == "LEG_TEMPLATE" && c.Ok); Assert.Contains(readiness.Conditions, c => c.Code == "PROCESS_LEG_LOCATIONS" && c.Ok);
        var started = await kit.StartAsync("k-ok", "A1", RequestType.PickupRequest, RackKind.Full, "T-REAL-OK");
        Assert.Equal(ExecutionMode.Real, (await kit.H.Db(db => db.TransportOrders.AsNoTracking().SingleAsync())).ExecutionMode);
        Assert.True(started.Created);
    }
}
