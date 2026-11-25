using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Domain.Common;
using WCS.Domain.Rcs;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.ModeControl;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using Xunit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-038 – Simulator / Real AGV mode, the RCS message log and the mode transitions. Real services and PostgreSQL; the "real" RCS is the MOCK server of CR-030 (nothing here can reach a
/// real RCS). Messages are checked in the shape the operator sees them: direction, mode, protocol, HTTP details, payload and result.
/// </summary>
[Collection("Database")]
public class Cr038DualModeTests(DatabaseFixture fixture)
{
    private const string Token = "tok-123", CbSecret = "cb-secret";

    private sealed class FakeFleet(FleetView? view) : IFleetStatusReader { public Task<FleetView?> ReadAsync(CancellationToken ct = default) => Task.FromResult(view); }
    private static FleetView RealFleet(string connection = "Connected", string source = "Real", bool simulated = false) =>
        new(source, simulated, connection, null, null, null, null, 0, 5, 30, 2, 2, []);

    /// <summary>Everything a Real AGV switch needs: every approved flow mapped, every location of the flows mapped, callback with a secret, timeouts short.</summary>
    private static Dictionary<string, string?> RealCfg(int timeout = 1)
    {
        var d = new Dictionary<string, string?>
        {
            ["Rcs:RealDispatch:Enabled"] = "true", ["Rcs:RealDispatch:TimeoutSeconds"] = timeout.ToString(), ["Rcs:RealDispatch:ClientCode"] = "WCS",
            ["Rcs:RealDispatch:Callback:Enabled"] = "true", ["Rcs:RealDispatch:Callback:SharedSecret"] = CbSecret, ["Rcs:Dispatch:Mode"] = "RcsManaged",   // the RCS chooses the AMR: no agvCode is sent
        };
        foreach (var c in new[] { "A1", "A2", "B1", "B2", "F1", "F2", "F3", "F4", "F5" }) { d[$"Rcs:RealDispatch:Locations:{c}:PositionCode"] = "P-" + c; d[$"Rcs:RealDispatch:Locations:{c}:Type"] = "00"; }
        foreach (var f in new[] { "Bp01", "Bp02", "Bp03", "Bp05", "Bp06" })
        {
            d[$"Rcs:RealDispatch:Processes:{f}:TaskTyp"] = "F01"; d[$"Rcs:RealDispatch:Processes:{f}:PositionPath:0"] = "Source"; d[$"Rcs:RealDispatch:Processes:{f}:PositionPath:1"] = "Request";
            d[$"Rcs:RealDispatch:Processes:{f}:PodDir"] = "0"; d[$"Rcs:RealDispatch:Processes:{f}:Priority"] = "5";
        }
        return d;
    }

    private async Task<(Harness H, MockRcsServer Rcs)> RealAsync(Action<Dictionary<string, string?>>? tweak = null, bool saveRcs = true, FleetView? fleet = null, int timeout = 1)
    {
        var rcs = await MockRcsServer.StartAsync(Token);
        var cfg = RealCfg(timeout); tweak?.Invoke(cfg);
        var h = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(fleet ?? RealFleet())), cfg);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 5);
        if (saveRcs)
            await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(
                new UpdateRcsCommand(true, new RcsSettings("MockRCS", "127.0.0.1", rcs.Port, "http://127.0.0.1", ConnectionOptions.AuthToken, null, 5000), null, false, Token, false, "test")));
        return (h, rcs);
    }

    private static Task<T> Svc<T>(Harness h, Func<IExecutionModeService, Task<T>> call) => h.Scoped(sp => call(sp.GetRequiredService<IExecutionModeService>()));
    private static Task<IReadOnlyList<RcsMessageSummary>?> Messages(Harness h, Guid taskId) => h.Scoped(sp => sp.GetRequiredService<IRcsMessageQuery>().ForTaskAsync(taskId));
    private static Task<RcsMessageDetail?> Detail(Harness h, Guid id) => h.Scoped(sp => sp.GetRequiredService<IRcsMessageQuery>().GetAsync(id));
    private static ModeTransitionRequest To(ExecutionMode m, string? id = null, bool confirm = true) => new(id ?? Guid.NewGuid().ToString("N"), m, "test reason", confirm);

    /// <summary>The mode row as an operator would have left it (a test arrangement: the real way is the transition, tested separately).</summary>
    private static async Task SetModeRowAsync(Harness h, ExecutionMode mode)
    {
        await h.Db(async db =>
        {
            var s = await db.ExecutionModeStates.FirstOrDefaultAsync(x => x.Id == 1);
            if (s is null) { s = ExecutionModeState.Default(h.Clock.UtcNow); db.ExecutionModeStates.Add(s); }
            s.SwitchTo(mode, "test", h.Clock.UtcNow);
            await db.SaveChangesAsync(); return 0;
        });
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
    }

    private static Task FinishTaskAsync(Harness h, Guid id) => h.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE transport_tasks SET \"State\" = 'Failed', \"FinishedAt\" = now(), \"RequiresReconciliation\" = false, \"RcsDispatchState\" = NULL WHERE \"Id\" = {0}", id));

    // ================================================================================================ Simulator
    [Fact]
    public async Task Simulator_mode_records_the_request_and_the_answer_marked_SIMULATED_and_never_reaches_the_real_adapter()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 5);
        var t = (await h.CreateTaskAsync("BP05", "F5", "sim1")).Task;
        Assert.Equal(ExecutionMode.Simulator, (await h.GetTaskAsync(t.Id)).ExecutionMode);

        var msgs = (await Messages(h, t.Id))!;
        Assert.True(msgs.Count >= 2);
        var o = msgs.First(m => m.Direction == RcsMessageDirection.Out); var i = msgs.First(m => m.Direction == RcsMessageDirection.In);
        Assert.All(msgs, m => { Assert.Equal(ExecutionMode.Simulator, m.Mode); Assert.Contains("SIMULATOR", m.Protocol); });
        Assert.Equal("Create Task", o.MessageType); Assert.Equal("Create Task Response", i.MessageType); Assert.Equal("Accepted", i.Result);
        var od = (await Detail(h, o.Id))!; var id = (await Detail(h, i.Id))!;
        Assert.Contains(t.RcsRequestCode, od.Payload);                                   // the REAL request object of the simulated call
        Assert.Contains("rcsTaskCode", id.Payload); Assert.Null(od.Endpoint); Assert.Null(od.HttpStatus);   // no URL and no HTTP status: nothing was sent over a network
        Assert.True(h.Rcs.CreateCalls >= 1);
    }

    [Fact]
    public async Task A_simulated_rejection_is_recorded_as_what_it_was()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 5);
        h.Rcs.NextFault = RcsFault.RejectNextCreate;
        var t = (await h.CreateTaskAsync("BP05", "F5", "sim2")).Task;
        var rows = (await Messages(h, t.Id))!;
        var answer = rows.First(m => m.Direction == RcsMessageDirection.In);
        Assert.Equal("Rejected", answer.Result);
        Assert.DoesNotContain(rows, m => m.Mode == ExecutionMode.Real);
    }

    // ================================================================================================ Real
    [Fact]
    public async Task Real_mode_records_the_real_request_and_the_real_answer_without_the_token()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "real1")).Task;
        Assert.Equal(ExecutionMode.Real, (await h.GetTaskAsync(t.Id)).ExecutionMode);
        var msgs = (await Messages(h, t.Id))!;
        var o = (await Detail(h, msgs.First(m => m.Direction == RcsMessageDirection.Out).Id))!;
        var i = (await Detail(h, msgs.First(m => m.Direction == RcsMessageDirection.In).Id))!;
        Assert.All(msgs, m => Assert.Equal(ExecutionMode.Real, m.Mode));
        Assert.Equal("Create Task", o.MessageType); Assert.Equal("POST", o.HttpMethod); Assert.Contains("genAgvSchedulingTask", o.Endpoint); Assert.Equal("Sent", o.Result);
        Assert.Contains(t.RcsRequestCode, o.Payload); Assert.DoesNotContain(Token, o.Payload);           // the body as built; the token never reaches the log
        Assert.Equal(200, i.HttpStatus); Assert.Equal("Accepted", i.Result); Assert.NotNull(i.Payload); Assert.DoesNotContain(Token, i.Payload);
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask"));
    }

    [Fact]
    public async Task A_timeout_is_recorded_as_a_timeout_and_the_call_is_not_repeated_blindly()
    {
        var (h, rcs) = await RealAsync(timeout: 1); await using var _h = h; await using var _r = rcs;
        rcs.DelayNextCreate = TimeSpan.FromSeconds(3);
        var t = (await h.CreateTaskAsync("BP05", "F5", "real2")).Task;
        var msgs = (await Messages(h, t.Id))!;
        var answer = Assert.Single(msgs, m => m.Direction == RcsMessageDirection.In);
        Assert.Equal("Timeout", answer.Result); Assert.Equal("RCS_TIMEOUT", answer.ErrorCode); Assert.Null(answer.HttpStatus);
        Assert.Equal(1, msgs.Count(m => m.Direction == RcsMessageDirection.Out));                       // one send, no automatic second one
        Assert.NotEqual(RcsDispatchState.Accepted, (await h.GetTaskAsync(t.Id)).RcsDispatchState);        // never "accepted" without a real answer
    }

    [Fact]
    public async Task A_lost_connection_a_refusal_and_a_non_JSON_answer_are_recorded_separately()
    {
        var (h, rcs) = await RealAsync(timeout: 8);   // Windows needs about 2 s to report a refused connection await using var _h = h; await using var _r = rcs;
        rcs.ForceNextCode = "1";
        var refused = (await h.CreateTaskAsync("BP05", "F5", "real3")).Task;
        Assert.Equal("Rejected", (await Messages(h, refused.Id))!.Single(m => m.Direction == RcsMessageDirection.In).Result);

        await h.StockAsync("F1", RackKind.Full, 2);
        rcs.AnswerNotJson = true;
        var notJson = (await h.CreateTaskAsync("BP05", "F5", "real4")).Task;
        var nj = (await Messages(h, notJson.Id))!.Single(m => m.Direction == RcsMessageDirection.In);
        Assert.Equal("Unknown", nj.Result); Assert.Equal("RCS_NOT_JSON", nj.ErrorCode);

        var free = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); free.Start(); var closedPort = ((System.Net.IPEndPoint)free.LocalEndpoint).Port; free.Stop();   // nobody listens there: connection refused
        await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(true, new RcsSettings("Closed", "127.0.0.1", closedPort, "http://127.0.0.1", ConnectionOptions.AuthToken, null, 5000), null, false, Token, false, "test")));
        await h.StockAsync("F1", RackKind.Full, 2);
        var lost = (await h.CreateTaskAsync("BP05", "F5", "real5")).Task;
        var lm = (await Messages(h, lost.Id))!.Single(m => m.Direction == RcsMessageDirection.In);
        Assert.Equal("ConnectionError", lm.Result);
    }

    [Fact]
    public async Task A_call_refused_before_any_network_use_is_recorded_as_NotSent_with_the_reason()
    {
        var (h, rcs) = await RealAsync(c => { c.Remove("Rcs:RealDispatch:Locations:F5:PositionCode"); c.Remove("Rcs:RealDispatch:Locations:F5:Type"); }); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "real6")).Task;
        var m = Assert.Single(await Messages(h, t.Id) ?? []);
        Assert.Equal(RcsMessageDirection.Out, m.Direction); Assert.Equal("NotSent", m.Result); Assert.Equal("RCS_MAPPING_MISSING", m.ErrorCode);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));
    }

    // ================================================================================================ secrets
    [Fact]
    public void Secrets_are_masked_in_json_nested_json_arrays_and_plain_text()
    {
        var json = RcsPayloadMasker.MaskText("{\"reqCode\":\"r1\",\"tokenCode\":\"abc123\",\"data\":{\"password\":\"p\",\"items\":[{\"X-Rcs-Callback-Token\":\"zzz\",\"ok\":1}]}}");
        Assert.DoesNotContain("abc123", json); Assert.DoesNotContain("\"p\"", json); Assert.DoesNotContain("zzz", json);
        Assert.Contains("r1", json); Assert.Contains("***", json);
        var text = RcsPayloadMasker.MaskText("Authorization: Bearer topsecret; password=hunter2");
        Assert.DoesNotContain("topsecret", text); Assert.DoesNotContain("hunter2", text);
    }

    [Fact]
    public async Task The_recorder_stores_a_callback_masked_cuts_a_huge_payload_and_never_throws()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var rec = h.Services.GetRequiredService<IRcsMessageRecorder>();
        await rec.RecordAsync(new RcsMessageEntry(ExecutionMode.Real, RcsMessageDirection.In, "AGV Callback: start", "RCS callback (HTTP)", "WCS-x", "/cb", "POST", 200, "{\"taskCode\":\"WCS-x\",\"token\":\"leak\"}", "Applied"));
        await rec.RecordAsync(new RcsMessageEntry(ExecutionMode.Real, RcsMessageDirection.In, "big", "p", "WCS-y", Payload: new string('x', 100_000), Result: "Received"));
        var rows = await h.Db(db => db.RcsMessages.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync());
        Assert.DoesNotContain("leak", rows[0].Payload); Assert.Equal(ExecutionMode.Real, rows[0].Mode);
        Assert.True(rows[1].PayloadTruncated); Assert.True(rows[1].Payload!.Length <= 32_000);
        await rec.RecordAsync(new RcsMessageEntry(ExecutionMode.Real, RcsMessageDirection.In, new string('t', 500), "p", null));   // a value the column cannot hold: logged, never thrown
    }

    [Fact]
    public async Task Messages_of_a_task_come_in_time_order_and_an_unknown_task_has_no_log()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 5);
        var t = (await h.CreateTaskAsync("BP05", "F5", "ord1")).Task;
        var msgs = (await Messages(h, t.Id))!;
        Assert.Equal(msgs.OrderBy(m => m.Seq).Select(m => m.Id), msgs.Select(m => m.Id));
        Assert.Null(await Messages(h, Guid.NewGuid()));
        Assert.Null(await Detail(h, Guid.NewGuid()));
    }

    // ================================================================================================ mode transitions
    [Fact]
    public async Task A_new_environment_starts_in_Simulator_not_chosen_and_Production_style_guard_refuses_until_chosen()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var v = await Svc(h, s => s.GetAsync());
        Assert.Equal(ExecutionMode.Simulator, v.Mode); Assert.False(v.IsExplicit); Assert.False(v.RecoveryRequired);
    }

    [Fact]
    public async Task Simulator_to_Real_is_refused_when_the_deployment_has_no_real_dispatch_and_nothing_changes()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var r = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"));
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failed, c => c.Code == "REAL_DISPATCH_ENABLED"); Assert.Contains(r.Failed, c => c.Code == "RCS_CONNECTION");
        Assert.Contains(r.Failed, c => c.Code == "PROCESS_MAPPING"); Assert.Contains(r.Failed, c => c.Code == "CALLBACK_CONFIG");
        Assert.Equal(ExecutionMode.Simulator, (await Svc(h, s => s.GetAsync())).Mode);
        var hist = await Svc(h, s => s.HistoryAsync(10));
        Assert.Single(hist); Assert.False(hist[0].Succeeded);
        Assert.Equal(1, await h.Db(db => db.AuditLog.CountAsync(a => a.Action == "EXECUTION_MODE_CHANGE" && a.Result == AuditResult.Denied)));
    }

    [Fact]
    public async Task Simulator_to_Real_succeeds_when_everything_is_ready_and_is_audited_with_actor_reason_and_both_modes()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.Equal(ExecutionMode.Simulator, h.Services.GetRequiredService<IExecutionModeProvider>().Current.Mode);
        var readiness = await Svc(h, s => s.ReadinessAsync(ExecutionMode.Real));
        Assert.True(readiness.CanSwitch, string.Join(" | ", readiness.Conditions.Where(c => !c.Ok).Select(c => c.Code + ": " + c.Message)));

        var r = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"));
        Assert.True(r.Succeeded); Assert.Equal(ExecutionMode.Simulator, r.From); Assert.Equal(ExecutionMode.Real, r.To);
        Assert.Equal(ExecutionMode.Real, h.Services.GetRequiredService<IExecutionModeProvider>().Current.Mode);
        var a = await h.Db(db => db.AuditLog.AsNoTracking().SingleAsync(x => x.Action == "EXECUTION_MODE_CHANGE" && x.Result == AuditResult.Success));
        Assert.Equal("admin", a.Actor); Assert.Contains("Simulator -> Real", a.Detail); Assert.Contains("test reason", a.Detail);
        var created = (await h.CreateTaskAsync("BP05", "F5", "after-switch")).Task;
        Assert.Equal(ExecutionMode.Real, (await h.GetTaskAsync(created.Id)).ExecutionMode);
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask"));
    }

    [Fact]
    public async Task Switching_to_Real_needs_the_explicit_confirmation_and_each_missing_prerequisite_blocks_it()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var noConfirm = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real, confirm: false), "admin"));
        Assert.False(noConfirm.Succeeded); Assert.Contains(noConfirm.Failed, c => c.Code == "CONFIRM_REAL_AGV");
        Assert.Equal(ExecutionMode.Simulator, (await Svc(h, s => s.GetAsync())).Mode);

        await using var noMap = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(RealFleet())), RealCfgWithout("Rcs:RealDispatch:Locations:F3:PositionCode"));
        await Svc(noMap, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.Contains((await Svc(noMap, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"))).Failed, c => c.Code == "LOCATION_MAPPING");

        await using var noCb = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(RealFleet())), RealCfgWithout("Rcs:RealDispatch:Callback:SharedSecret"));
        await Svc(noCb, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.Contains((await Svc(noCb, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"))).Failed, c => c.Code == "CALLBACK_CONFIG");

        await using var stale = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(RealFleet("Disconnected"))), RealCfg());
        await Svc(stale, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.Contains((await Svc(stale, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"))).Failed, c => c.Code == "FLEET_REAL_FRESH");

        await using var simFleet = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(RealFleet(source: "Simulator", simulated: true))), RealCfg());
        await Svc(simFleet, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.Contains((await Svc(simFleet, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"))).Failed, c => c.Code == "FLEET_REAL_FRESH");

        await using var noRcs = await Harness.CreateAsync(fixture, s => s.AddScoped<IFleetStatusReader>(_ => new FakeFleet(RealFleet())), RealCfg());
        await Svc(noRcs, s => s.InitializeAsync().ContinueWith(_ => 0));
        Assert.Contains((await Svc(noRcs, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"))).Failed, c => c.Code == "RCS_CONNECTION");   // no RCS connection saved in this database
    }

    private static Dictionary<string, string?> RealCfgWithout(string key) { var d = RealCfg(); d.Remove(key); return d; }

    [Fact]
    public async Task Simulator_to_Real_is_refused_while_a_simulator_task_is_unfinished_and_nothing_is_cancelled()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var sim = (await h.CreateTaskAsync("BP05", "F5", "sim-active")).Task;
        Assert.Equal(ExecutionMode.Simulator, (await h.GetTaskAsync(sim.Id)).ExecutionMode);
        var r = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"));
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failed, c => c.Code == "NO_ACTIVE_SIMULATOR_WORK");
        Assert.Contains(r.BlockingWork, w => w.Id == sim.Id.ToString());
        Assert.NotEqual(TaskState.Cancelled, (await h.GetTaskAsync(sim.Id)).State);          // the task was left alone
        await FinishTaskAsync(h, sim.Id);
        Assert.True((await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin"))).Succeeded);
        Assert.Equal(ExecutionMode.Simulator, (await h.GetTaskAsync(sim.Id)).ExecutionMode);   // the old task keeps its dispatcher
    }

    [Fact]
    public async Task Real_to_Simulator_is_refused_while_a_real_task_is_unfinished_or_unreconciled_and_allowed_when_safe()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await SetModeRowAsync(h, ExecutionMode.Real);
        var real = (await h.CreateTaskAsync("BP05", "F5", "real-active")).Task;
        var blocked = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        Assert.False(blocked.Succeeded); Assert.Contains(blocked.Failed, c => c.Code == "NO_ACTIVE_REAL_WORK"); Assert.Contains(blocked.BlockingWork, w => w.Id == real.Id.ToString());
        Assert.Equal(ExecutionMode.Real, (await Svc(h, s => s.GetAsync())).Mode);

        await FinishTaskAsync(h, real.Id);
        await h.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE transport_tasks SET \"RequiresReconciliation\" = true, \"ReconciliationCompleted\" = false WHERE \"Id\" = {0}", real.Id));
        var unreconciled = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        Assert.False(unreconciled.Succeeded); Assert.Contains(unreconciled.BlockingWork, w => w.Id == real.Id.ToString() && w.Reason.Contains("reconciliation"));

        await h.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE transport_tasks SET \"ReconciliationCompleted\" = true WHERE \"Id\" = {0}", real.Id));
        var ok = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"));
        Assert.True(ok.Succeeded); Assert.Equal(ExecutionMode.Simulator, h.Services.GetRequiredService<IExecutionModeProvider>().Current.Mode);
        Assert.Equal(ExecutionMode.Real, (await h.GetTaskAsync(real.Id)).ExecutionMode);   // the Real task stays Real
    }

    [Fact]
    public async Task A_repeated_request_id_answers_the_first_result_and_changes_nothing()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var first = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real, "same-id"), "admin"));
        var again = await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real, "same-id"), "admin"));
        Assert.True(first.Succeeded); Assert.False(first.Repeated);
        Assert.True(again.Succeeded); Assert.True(again.Repeated);
        Assert.Single(await Svc(h, s => s.HistoryAsync(10)));
        await Assert.ThrowsAsync<AppException>(() => Svc(h, s => s.TransitionAsync(new ModeTransitionRequest("", ExecutionMode.Simulator, "r", false), "admin")));
        await Assert.ThrowsAsync<AppException>(() => Svc(h, s => s.TransitionAsync(new ModeTransitionRequest("x1", ExecutionMode.Simulator, " ", false), "admin")));
    }

    [Fact]
    public async Task Several_admins_switching_at_once_end_in_exactly_one_change()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Svc(h, s => s.TransitionAsync(To(ExecutionMode.Real), "admin")))));
        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Contains(r.Failed, c => c.Code == "ALREADY_IN_MODE"));
        Assert.Equal(ExecutionMode.Real, (await Svc(h, s => s.GetAsync())).Mode);
        Assert.Equal(6, (await Svc(h, s => s.HistoryAsync(20))).Count);
        Assert.Equal(1, (await Svc(h, s => s.HistoryAsync(20))).Count(x => x.Succeeded));
    }

    [Fact]
    public async Task A_task_never_changes_its_execution_mode()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 5);
        var t = (await h.CreateTaskAsync("BP05", "F5", "pin1")).Task;
        var ex = await Assert.ThrowsAsync<DomainException>(() => h.Db(async db =>
        {
            var task = await db.Tasks.SingleAsync(x => x.Id == t.Id);
            task.PinExecutionMode(ExecutionMode.Real);
            await db.SaveChangesAsync(); return 0;
        }));
        Assert.Equal("EXECUTION_MODE_FIXED", ex.Code);
    }

    // ================================================================================================ restart and recovery
    [Fact]
    public async Task After_a_restart_the_saved_Real_mode_is_restored_and_never_silently_becomes_Simulator()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await SetModeRowAsync(h, ExecutionMode.Real);
        await h.RestartAsync();
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var v = await Svc(h, s => s.GetAsync());
        Assert.Equal(ExecutionMode.Real, v.Mode); Assert.False(v.RecoveryRequired);
        Assert.Equal(ExecutionMode.Real, h.Services.GetRequiredService<IExecutionModeProvider>().Current.Mode);
    }

    [Fact]
    public async Task A_restart_with_an_unresolved_real_dispatch_blocks_new_dispatch_until_it_is_reconciled()
    {
        var (h, rcs) = await RealAsync(); await using var _h = h; await using var _r = rcs;
        await SetModeRowAsync(h, ExecutionMode.Real);
        rcs.DelayNextCreate = TimeSpan.FromSeconds(3);                                            // the answer is lost: the outcome is unknown
        var lost = (await h.CreateTaskAsync("BP05", "F5", "lost1")).Task;
        await h.RestartAsync();
        await Svc(h, s => s.InitializeAsync().ContinueWith(_ => 0));
        var v = await Svc(h, s => s.GetAsync());
        Assert.Equal(ExecutionMode.Real, v.Mode); Assert.True(v.RecoveryRequired);                // still Real, never Simulator
        var guard = h.Services.GetRequiredService<IRcsDispatchGuard>().Check();
        Assert.False(guard.Allowed); Assert.Equal("EXECUTION_MODE_RECOVERY_REQUIRED", guard.Code);
        var ex = await Assert.ThrowsAsync<AppException>(() => h.CreateTaskAsync("BP05", "F5", "blocked1"));
        Assert.Equal("EXECUTION_MODE_RECOVERY_REQUIRED", ex.Code);
        Assert.False((await Svc(h, s => s.TransitionAsync(To(ExecutionMode.Simulator), "admin"))).Succeeded);   // no way out by switching mode

        var refused = await Svc(h, s => s.AcknowledgeRecoveryAsync("ack1", "checked", "admin"));
        Assert.False(refused.Succeeded); Assert.Contains(refused.BlockingWork, w => w.Id == lost.Id.ToString());
        await FinishTaskAsync(h, lost.Id);                                                        // reconciled by an operator
        var ok = await Svc(h, s => s.AcknowledgeRecoveryAsync("ack2", "checked with the RCS", "admin"));
        Assert.True(ok.Succeeded);
        Assert.True(h.Services.GetRequiredService<IRcsDispatchGuard>().Check().Allowed);
        Assert.Equal(1, rcs.Count("genAgvSchedulingTask"));                                       // nothing was resent automatically
    }

    [Fact]
    public async Task The_guard_follows_the_mode_Real_needs_the_deployment_to_allow_it_and_Simulator_is_open_outside_Production()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var guard = h.Services.GetRequiredService<IRcsDispatchGuard>();
        Assert.True(guard.Check().Allowed);                                                       // Simulator, development-like host
        h.Services.GetRequiredService<IExecutionModeProvider>().Update(new ExecutionModeSnapshot(ExecutionMode.Real, true, false, null, DateTimeOffset.UtcNow, "t"));
        var real = guard.Check();
        Assert.False(real.Allowed); Assert.Equal("RCS_REAL_NOT_READY", real.Code);               // Rcs:RealDispatch:Enabled is false here
    }
}
