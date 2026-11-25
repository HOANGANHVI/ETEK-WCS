using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Application.RealDispatch;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Infrastructure.RealDispatch;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-030 – the REAL RCS dispatch adapter (genAgvSchedulingTask, queryTaskStatus, continueTask, cancelTask, agvCallback) verified against a MOCK of the RCS-2000 API V3.3 (<see cref="MockRcsServer"/>).
/// These are MOCK tests: they prove the adapter follows the API document, fails closed and keeps the CR-028 rules. They prove NOTHING about a real RCS-2000, a real AMR or real mappings.
/// All mapping values in this file (positionCode "P-F1", taskTyp "F01", type "00") are TEST values, not business data.
/// </summary>
[Collection("Database")]
public class Cr030RealRcsDispatchTests(DatabaseFixture fixture)
{
    private const string Token = "tok-123", CbSecret = "cb-secret", CbPath = "/rcs/agv/agvCallbackService/agvCallback";

    private static Dictionary<string, string?> Cfg(bool managed = true, int timeout = 1)
    {
        var d = new Dictionary<string, string?>
        {
            ["Rcs:RealDispatch:Enabled"] = "true", ["Rcs:RealDispatch:TimeoutSeconds"] = timeout.ToString(), ["Rcs:RealDispatch:ClientCode"] = "WCS",
            ["Rcs:RealDispatch:Locations:F1:PositionCode"] = "P-F1", ["Rcs:RealDispatch:Locations:F1:Type"] = "00",
            ["Rcs:RealDispatch:Locations:F2:PositionCode"] = "P-F2", ["Rcs:RealDispatch:Locations:F2:Type"] = "00",
            ["Rcs:RealDispatch:Locations:F5:PositionCode"] = "P-F5", ["Rcs:RealDispatch:Locations:F5:Type"] = "00",
            ["Rcs:RealDispatch:Processes:Bp05:TaskTyp"] = "F01", ["Rcs:RealDispatch:Processes:Bp05:PositionPath:0"] = "Source", ["Rcs:RealDispatch:Processes:Bp05:PositionPath:1"] = "Request",
            ["Rcs:RealDispatch:Processes:Bp05:PodDir"] = "0", ["Rcs:RealDispatch:Processes:Bp05:Priority"] = "5",
            ["Rcs:RealDispatch:Callback:Enabled"] = "true", ["Rcs:RealDispatch:Callback:SharedSecret"] = CbSecret,
        };
        if (managed) d["Rcs:Dispatch:Mode"] = "RcsManaged";
        return d;
    }

    private async Task<(Harness H, MockRcsServer Rcs)> SetupAsync(bool managed = true, int timeout = 1, Action<Dictionary<string, string?>>? tweak = null, bool saveRcs = true)
    {
        var rcs = await MockRcsServer.StartAsync(Token);
        var cfg = Cfg(managed, timeout); tweak?.Invoke(cfg);
        var h = await Harness.CreateAsync(fixture, null, cfg);
        await h.SeedStandardAsync(bufferCapacity: 20, stationCapacity: 1);
        await h.StockAsync("F1", RackKind.Full, 5);
        if (saveRcs)
            await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(
                new UpdateRcsCommand(true, new RcsSettings("MockRCS", "127.0.0.1", rcs.Port, "http://127.0.0.1", ConnectionOptions.AuthToken, null, 5000), null, false, Token, false, "test")));
        return (h, rcs);
    }

    private static string S(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
    private static IRcsClient Client(Harness h) => h.Services.GetRequiredService<IRcsClient>();
    private static RcsCreateTaskRequest Req(string code = "WCS-0123456789abcdef0123456789abcdef", ProcessFlow flow = ProcessFlow.Bp05, string? robot = null) => new(code, flow, RackKind.Full, robot, "F1", "F5");
    private static string Cb(string req, string method, string robot, string task) => $$"""{"reqCode":"{{req}}","method":"{{method}}","robotCode":"{{robot}}","taskCode":"{{task}}","currentPositionCode":"p02","mapCode":"AA"}""";

    // ================================================================================================ separation and defaults
    [Fact]
    public async Task Real_dispatch_is_off_by_default_the_simulator_stays_and_enabling_it_switches_the_client_and_blocks_legs()
    {
        await using (var off = await Harness.CreateAsync(fixture))
        {
            Assert.IsType<WCS.Infrastructure.ModeControl.RoutedRcsClient>(IRcsClientDecorator.Unwrap(off.Services.GetRequiredService<IRcsClient>()));   // CR-038: the routed client; Simulator is the default mode (exactly as before CR-030)
            Assert.Equal(WCS.Domain.Rcs.ExecutionMode.Simulator, off.Services.GetRequiredService<IExecutionModeProvider>().Current.Mode);
            Assert.False(off.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RealDispatchOptions>>().Value.Enabled);
            Assert.Empty(off.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RealDispatchOptions>>().Value.Locations);   // no mapping unless configured
            Assert.Empty(off.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RealDispatchOptions>>().Value.Processes);
        }
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        Assert.IsType<WCS.Infrastructure.ModeControl.RoutedRcsClient>(IRcsClientDecorator.Unwrap(Client(h)));   // CR-038: routed; this harness is in Real mode
        Assert.Equal(WCS.Domain.Rcs.ExecutionMode.Real, h.Services.GetRequiredService<IExecutionModeProvider>().Current.Mode);
        var leg = await h.Scoped(sp => sp.GetRequiredService<ILegDispatcher>().DispatchAsync(new LegDispatchRequest(Guid.NewGuid(), "LEG-x", null, RackKind.Full, "A1", "B1")));
        Assert.Equal(LegDispatchOutcome.Blocked, leg.Outcome);                                      // a leg is not "accepted" by a simulator and not sent without a confirmed mapping
        Assert.Contains("NOT_AVAILABLE_FOR_REAL_RCS", leg.Reason);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));
    }

    // ================================================================================================ genAgvSchedulingTask
    [Fact]
    public async Task A_task_is_created_without_an_AMR_with_the_documented_fields_and_sent_is_not_assigned()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "real1")).Task;
        var body = Assert.Single(rcs.Bodies("genAgvSchedulingTask")).RootElement;

        Assert.Equal(t.RcsRequestCode, S(body, "taskCode"));                                         // the WCS request code is the RCS task id
        Assert.True(S(body, "reqCode").Length is > 0 and <= 32);                                     // reqCode: max 32 characters
        Assert.Equal("F01", S(body, "taskTyp")); Assert.Equal("WCS", S(body, "clientCode")); Assert.Equal(Token, S(body, "tokenCode"));
        Assert.Equal("0", S(body, "podDir")); Assert.Equal("5", S(body, "priority"));
        Assert.False(body.TryGetProperty("agvCode", out _));                                         // the RCS chooses the AMR: no agvCode is sent
        Assert.False(body.TryGetProperty("wbCode", out _));
        Assert.Equal([("P-F1", "00"), ("P-F5", "00")], body.GetProperty("positionCodePath").EnumerateArray().Select(p => (S(p, "positionCode"), S(p, "type"))).ToArray());

        var after = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Accepted, after.State); Assert.Equal(RcsDispatchState.Accepted, after.RcsDispatchState);
        Assert.Null(after.RobotCode); Assert.Null(after.AgvId);                                      // the mock already chose R001, but WCS has not been told: sent is NOT assigned
        Assert.Equal(t.RcsRequestCode, after.RcsTaskCode);
        Assert.DoesNotContain(Token, string.Join("|", after.History.Select(x => x.Detail)));         // the token is never written to the history
    }

    [Fact]
    public async Task The_reqCode_is_deterministic_so_a_repeated_request_keeps_the_same_reqCode()
    {
        Assert.Equal(HikRcsClient.ReqCodeOf("WCS-0123456789abcdef0123456789abcdef"), HikRcsClient.ReqCodeOf("WCS-0123456789abcdef0123456789abcdef"));
        Assert.Equal(32, HikRcsClient.ReqCodeOf("WCS-0123456789abcdef0123456789abcdef").Length);
        Assert.Equal("short", HikRcsClient.ReqCodeOf("short"));
        Assert.NotEqual(HikRcsClient.ReqCodeOf("WCS-a-continue-F2"), HikRcsClient.ReqCodeOf("WCS-a-cancel"));
        await Task.CompletedTask;
    }

    // ================================================================================================ queryTaskStatus, RobotCode
    [Fact]
    public async Task Polling_queryTaskStatus_records_the_AMR_the_RCS_chose_on_the_task_and_follows_a_change()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "real2")).Task;
        await h.TickAsync();                                                                        // "which AMR?" (queryTaskStatus)
        var q = Assert.Single(rcs.Bodies("queryTaskStatus")).RootElement;
        Assert.Equal([t.RcsRequestCode], q.GetProperty("taskCodes").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(Token, S(q, "tokenCode")); Assert.True(S(q, "reqCode").Length <= 32);
        var assigned = await h.GetTaskAsync(t.Id);
        Assert.Equal("R001", assigned.RobotCode); Assert.Equal(RcsDispatchState.Assigned, assigned.RcsDispatchState); Assert.Null(assigned.AgvId);   // not a business AGV: no AGV1, no invention

        rcs.Assign(t.RcsRequestCode, "R002"); h.Clock.Advance(TimeSpan.FromSeconds(61)); await h.TickAsync();
        var changed = await h.GetTaskAsync(t.Id);
        Assert.Equal("R002", changed.RobotCode); Assert.Contains(changed.History, x => x.EventName == "RCS_ROBOT_CHANGED");
    }

    [Fact]
    public async Task A_task_the_RCS_has_not_assigned_yet_waits_and_an_unknown_answer_is_a_sync_error_not_a_failure()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        rcs.AutoAssign = false;
        var t = (await h.CreateTaskAsync("BP05", "F5", "real3")).Task;
        await h.TickAsync();
        var waiting = await h.GetTaskAsync(t.Id);
        Assert.Equal(RcsDispatchState.AwaitingAssignment, waiting.RcsDispatchState); Assert.Null(waiting.RobotCode);

        rcs.ForceNextCode = "99"; h.Clock.Advance(TimeSpan.FromSeconds(6)); await h.TickAsync();   // "99": unknown error, try again
        Assert.Equal(RcsDispatchState.SyncError, (await h.GetTaskAsync(t.Id)).RcsDispatchState);
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);

        rcs.Assign(t.RcsRequestCode, "R003"); h.Clock.Advance(TimeSpan.FromSeconds(6)); await h.TickAsync();
        Assert.Equal("R003", (await h.GetTaskAsync(t.Id)).RobotCode);                                // recovered
    }

    [Theory]
    [InlineData("1", RcsTaskStatus.Created)] [InlineData("3", RcsTaskStatus.Created)] [InlineData("6", RcsTaskStatus.Created)]
    [InlineData("2", RcsTaskStatus.Executing)] [InlineData("4", RcsTaskStatus.Cancelling)] [InlineData("5", RcsTaskStatus.Cancelled)]
    [InlineData("9", RcsTaskStatus.Completed)] [InlineData("10", RcsTaskStatus.Interrupted)] [InlineData("0", RcsTaskStatus.Unknown)] [InlineData("77", RcsTaskStatus.Unknown)] [InlineData(null, RcsTaskStatus.Unknown)]
    public void The_taskStatus_values_of_the_API_document_are_mapped(string? code, RcsTaskStatus expected) => Assert.Equal(expected, HikRcsClient.MapStatus(code));

    [Fact]
    public async Task A_query_distinguishes_unknown_task_from_an_RCS_that_cannot_be_reached()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        Assert.Equal(RcsTaskStatus.NotFound, (await Client(h).QueryTaskAsync("WCS-nobody")).Status);   // the RCS answered and does not know it
        rcs.AnswerNotJson = true;
        Assert.Equal(RcsTaskStatus.Unknown, (await Client(h).QueryTaskAsync("WCS-nobody")).Status);    // garbage: inconclusive, never "not found"
        rcs.AnswerNotJson = false; await rcs.StopAsync();
        Assert.Equal(RcsTaskStatus.Unknown, (await Client(h).QueryTaskAsync("WCS-nobody")).Status);    // unreachable: inconclusive
    }

    // ================================================================================================ fail closed: mapping, configuration
    [Fact]
    public async Task A_missing_location_mapping_sends_nothing_fails_the_call_clearly_and_never_shows_the_token()
    {
        var (h, rcs) = await SetupAsync(tweak: c => { c.Remove("Rcs:RealDispatch:Locations:F5:PositionCode"); c.Remove("Rcs:RealDispatch:Locations:F5:Type"); });
        await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "nomap")).Task;
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));                                          // NOTHING was sent
        var after = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, after.State);
        var text = string.Join("|", after.History.Select(x => x.Detail)) + after.StateReason;
        Assert.Contains("RCS_MAPPING_MISSING", text); Assert.Contains("F5", text); Assert.DoesNotContain(Token, text);

        var direct = await Client(h).CreateTaskAsync(Req(flow: ProcessFlow.Bp02));                   // Bp02 has no process mapping
        Assert.Equal(RcsCallOutcome.Rejected, direct.Outcome); Assert.Contains("Process Bp02", direct.Message); Assert.DoesNotContain(Token, direct.Message);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));
    }

    [Fact]
    public async Task Without_a_saved_RCS_connection_nothing_is_sent_and_an_AMR_code_longer_than_5_is_refused()
    {
        var (h, rcs) = await SetupAsync(saveRcs: false); await using var _h = h; await using var _r = rcs;
        var r = await Client(h).CreateTaskAsync(Req());
        Assert.Equal(RcsCallOutcome.Rejected, r.Outcome); Assert.Contains("RCS_NOT_CONFIGURED", r.Message);
        Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));

        await h.Scoped(sp => sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(
            new UpdateRcsCommand(true, new RcsSettings("MockRCS", "127.0.0.1", rcs.Port, "http://127.0.0.1", ConnectionOptions.AuthToken, null, 5000), null, false, Token, false, "test")));
        var tooLong = await Client(h).CreateTaskAsync(Req(robot: "R-FULL"));                         // agvCode is at most 5 characters (API document)
        Assert.Equal(RcsCallOutcome.Rejected, tooLong.Outcome); Assert.Equal(0, rcs.Count("genAgvSchedulingTask"));
    }

    [Fact]
    public void The_readiness_report_names_what_is_missing_and_never_generates_a_mapping()
    {
        var empty = new RealDispatchOptions { Enabled = true };
        var report = RealDispatchReadiness.Report(empty);
        Assert.Contains(report, x => x.Contains("Processes is empty")); Assert.Contains(report, x => x.Contains("Locations is empty"));
        Assert.Empty(empty.Locations); Assert.Empty(empty.Processes);
        Assert.NotNull(RealDispatchReadiness.LocationProblem("F1", new LocationMapping { PositionCode = "p", Type = "99" }));
        Assert.NotNull(RealDispatchReadiness.ProcessProblem(ProcessFlow.Bp01, new ProcessMapping { TaskTyp = "F01", PositionPath = ["Destination"] }));
        Assert.Null(RealDispatchReadiness.ProcessProblem(ProcessFlow.Bp01, new ProcessMapping { TaskTyp = "F01", PositionPath = ["Source"] }));
        Assert.Contains(RealDispatchReadiness.Report(new RealDispatchOptions { Enabled = true, Callback = new CallbackOptions { Enabled = true } }), x => x.Contains("every callback will be refused"));
    }

    // ================================================================================================ timeout, retry, duplicates, outage
    [Fact]
    public async Task A_timeout_after_the_RCS_created_the_task_is_not_a_failure_and_the_retry_never_creates_a_second_task()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        rcs.DelayNextCreate = TimeSpan.FromSeconds(3);                                               // the task IS stored, the answer arrives too late
        var t = (await h.CreateTaskAsync("BP05", "F5", "slow")).Task;
        var first = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Dispatching, first.State); Assert.Equal(RcsDispatchState.Sending, first.RcsDispatchState);   // unknown, not failed
        await h.TickAsync();                                                                         // the retry asks the RCS first
        var retried = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Accepted, retried.State); Assert.Equal(t.RcsRequestCode, retried.RcsRequestCode);
        Assert.Equal(1, rcs.TaskCount); Assert.Equal(1, rcs.Count("genAgvSchedulingTask"));
    }

    [Fact]
    public async Task A_failure_before_creation_is_retried_with_the_same_reqCode_and_one_RCS_task_results()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        rcs.FailNextCreateBeforeStoring = true;
        var t = (await h.CreateTaskAsync("BP05", "F5", "lost")).Task;
        Assert.Equal(TaskState.Dispatching, (await h.GetTaskAsync(t.Id)).State);
        await h.TickAsync();
        Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);
        var bodies = rcs.Bodies("genAgvSchedulingTask");
        Assert.Equal(2, bodies.Count);
        Assert.Equal(S(bodies[0].RootElement, "reqCode"), S(bodies[1].RootElement, "reqCode"));      // a repeated request keeps the SAME reqCode
        Assert.Equal(S(bodies[0].RootElement, "taskCode"), S(bodies[1].RootElement, "taskCode"));
        Assert.Equal(1, rcs.TaskCount);
    }

    [Fact]
    public async Task The_same_request_twice_is_answered_with_code_6_and_accepted_without_a_second_task()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var a = await Client(h).CreateTaskAsync(Req()); var b = await Client(h).CreateTaskAsync(Req());
        Assert.Equal(RcsCallOutcome.Accepted, a.Outcome); Assert.Equal(RcsCallOutcome.Accepted, b.Outcome);
        Assert.Equal(2, rcs.Count("genAgvSchedulingTask")); Assert.Equal(1, rcs.TaskCount);
        Assert.Equal(Req().RequestCode, b.RcsTaskCode);
    }

    [Fact]
    public async Task A_lost_connection_to_the_RCS_is_an_unknown_outcome_and_the_task_is_not_failed()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        await rcs.StopAsync();
        var t = (await h.CreateTaskAsync("BP05", "F5", "down")).Task;
        var after = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Dispatching, after.State); Assert.Equal(RcsDispatchState.Sending, after.RcsDispatchState); Assert.Null(after.RcsTaskCode);
    }

    [Fact]
    public async Task A_refusal_code_1_is_a_certain_failure_and_the_message_never_contains_the_token()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        rcs.Token = "another-token";                                                                 // the RCS refuses the token
        var r = await Client(h).CreateTaskAsync(Req());
        Assert.Equal(RcsCallOutcome.Rejected, r.Outcome); Assert.DoesNotContain(Token, r.Message ?? ""); Assert.DoesNotContain("another-token", r.Message ?? "");
    }

    // ================================================================================================ continueTask, cancelTask
    [Fact]
    public async Task Continue_and_cancel_follow_the_API_document_and_an_unmapped_destination_sends_nothing()
    {
        var (h, rcs) = await SetupAsync(tweak: c => c["Rcs:RealDispatch:CancelMode"] = "0"); await using var _h = h; await using var _r = rcs;
        var req = Req();
        Assert.Equal(RcsCallOutcome.Accepted, (await Client(h).CreateTaskAsync(req)).Outcome);

        var cont = await Client(h).ContinueTaskAsync(req.RequestCode, req.RequestCode, "F2");
        Assert.Equal(RcsCallOutcome.Accepted, cont.Outcome);
        var cb = Assert.Single(rcs.Bodies("continueTask")).RootElement;
        Assert.Equal(req.RequestCode, S(cb, "taskCode")); Assert.True(S(cb, "reqCode").Length <= 32); Assert.Equal(Token, S(cb, "tokenCode"));
        Assert.Equal("P-F2", S(cb.GetProperty("nextPositionCode"), "positionCode")); Assert.Equal("00", S(cb.GetProperty("nextPositionCode"), "type"));
        Assert.False(cb.TryGetProperty("agvCode", out _)); Assert.False(cb.TryGetProperty("wbCode", out _)); Assert.False(cb.TryGetProperty("podCode", out _));   // only ONE identifier
        Assert.Equal("2", rcs.TaskOf(req.RequestCode)!.Status);                                      // the mock moved the task on

        var unmapped = await Client(h).ContinueTaskAsync(req.RequestCode, req.RequestCode, "F4");
        Assert.Equal(RcsCallOutcome.Rejected, unmapped.Outcome); Assert.Equal(1, rcs.Count("continueTask"));   // nothing was sent for F4

        var cancel = await Client(h).CancelTaskAsync(req.RequestCode, req.RequestCode);
        Assert.Equal(RcsCallOutcome.Accepted, cancel.Outcome);
        var cc = Assert.Single(rcs.Bodies("cancelTask")).RootElement;
        Assert.Equal(req.RequestCode, S(cc, "taskCode")); Assert.Equal("0", S(cc, "forceCancel")); Assert.False(cc.TryGetProperty("agvCode", out _));   // by task id, never "free the AMR"
        Assert.Equal("5", rcs.TaskOf(req.RequestCode)!.Status);

        Assert.Equal(RcsCallOutcome.Rejected, (await Client(h).CancelTaskAsync("WCS-unknown", null)).Outcome);   // the RCS does not know the task: code 1
    }

    // ================================================================================================ agvCallback
    [Fact]
    public async Task Callback_authentication_fails_closed_and_every_configured_check_must_pass()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var ip = IPAddress.Parse("10.1.2.3");
        Assert.Equal(RcsCallbackStatus.Unauthorized, Auth(h, null, ip));
        Assert.Equal(RcsCallbackStatus.Unauthorized, Auth(h, "wrong", ip));
        Assert.Equal(RcsCallbackStatus.Applied, Auth(h, CbSecret, ip));                              // "Applied" = authenticated

        var (h2, rcs2) = await SetupAsync(tweak: c => { c.Remove("Rcs:RealDispatch:Callback:SharedSecret"); c["Rcs:RealDispatch:Callback:AllowedSources:0"] = "10.1.0.0/16"; });
        await using var _h2 = h2; await using var _r2 = rcs2;
        Assert.Equal(RcsCallbackStatus.Applied, Auth(h2, null, ip));
        Assert.Equal(RcsCallbackStatus.Applied, Auth(h2, null, IPAddress.Parse("::ffff:10.1.9.9")));
        Assert.Equal(RcsCallbackStatus.Unauthorized, Auth(h2, null, IPAddress.Parse("10.2.0.1")));
        Assert.Equal(RcsCallbackStatus.Unauthorized, Auth(h2, null, null));

        var (h3, rcs3) = await SetupAsync(tweak: c => c.Remove("Rcs:RealDispatch:Callback:SharedSecret"));   // enabled with NO authentication configured
        await using var _h3 = h3; await using var _r3 = rcs3;
        Assert.Equal(RcsCallbackStatus.Unauthorized, Auth(h3, "anything", ip));                      // refused: no mechanism is invented

        var (h4, rcs4) = await SetupAsync(tweak: c => c["Rcs:RealDispatch:Callback:Enabled"] = "false");
        await using var _h4 = h4; await using var _r4 = rcs4;
        Assert.Equal(RcsCallbackStatus.Disabled, Auth(h4, CbSecret, ip));
    }

    private static RcsCallbackStatus Auth(Harness h, string? header, IPAddress? ip) => h.Scoped(sp => Task.FromResult(sp.GetRequiredService<RcsCallbackService>().Authenticate(header, ip))).GetAwaiter().GetResult();
    private static Task<RcsCallbackResult> Handle(Harness h, string body) => h.Scoped(sp => sp.GetRequiredService<RcsCallbackService>().HandleAsync(body));

    [Fact]
    public async Task Callbacks_record_the_AMR_start_the_task_never_complete_it_and_a_repeated_reqCode_is_applied_once()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "cb1")).Task;

        var start = await Handle(h, Cb("cb-1", "start", "R002", t.RcsRequestCode));
        Assert.Equal(RcsCallbackStatus.Applied, start.Status); Assert.Equal("0", start.Code); Assert.Equal("cb-1", start.ReqCode);
        var running = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Executing, running.State); Assert.Equal("R002", running.RobotCode); Assert.Equal(RcsDispatchState.Assigned, running.RcsDispatchState);   // the callback named the robot

        var again = await Handle(h, Cb("cb-1", "start", "R002", t.RcsRequestCode));                  // the RCS repeats the request with the same reqCode
        Assert.Equal(RcsCallbackStatus.Duplicate, again.Status); Assert.Equal("0", again.Code);
        Assert.Single((await h.GetTaskAsync(t.Id)).History, x => x.EventName == "AGV_EXECUTING" || x.ToState == TaskState.Executing);

        var end = await Handle(h, Cb("cb-2", "end", "R002", t.RcsRequestCode));
        Assert.Equal("0", end.Code);
        var ended = await h.GetTaskAsync(t.Id);
        Assert.NotEqual(TaskState.Completed, ended.State);                                           // "end" does NOT complete the task without a confirmed drop
        Assert.False(await h.Db(db => db.InventoryTransactions.AnyAsync(x => x.TaskId == t.Id)));    // and changes no rack / inventory
    }

    [Fact]
    public async Task A_callback_that_arrives_out_of_order_or_is_malformed_changes_nothing_and_is_answered_clearly()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "cb2")).Task;

        var early = await Handle(h, Cb("cb-e", "end", "R001", t.RcsRequestCode));                    // "end" before "start"
        Assert.Equal("0", early.Code);
        Assert.NotEqual(TaskState.Completed, (await h.GetTaskAsync(t.Id)).State);
        Assert.Equal(RcsCallbackStatus.Applied, (await Handle(h, Cb("cb-s", "start", "R001", t.RcsRequestCode))).Status);   // and the normal order still works afterwards

        Assert.Equal(RcsCallbackStatus.Invalid, (await Handle(h, """{"reqCode":"x","method":"start","taskCode":"t"}""")).Status);          // robotCode missing
        Assert.Equal(RcsCallbackStatus.Invalid, (await Handle(h, Cb("cb-m", "explode", "R001", t.RcsRequestCode))).Status);                  // unknown method
        Assert.Equal(RcsCallbackStatus.Invalid, (await Handle(h, "not json")).Status);
        Assert.Equal("1", (await Handle(h, Cb("cb-u", "start", "R001", "WCS-unknown-task"))).Code);                                         // unknown task
        var ctu = await Handle(h, Cb("cb-c", "ctu", "R001", t.RcsRequestCode));                      // CTU-only method: acknowledged, not used
        Assert.Equal(RcsCallbackStatus.Ignored, ctu.Status); Assert.Equal("0", ctu.Code);
        var numeric = await Handle(h, $$"""{"reqCode":"cb-n","method":"outbin","robotCode":6001,"taskCode":"{{t.RcsRequestCode}}"}""");   // numbers are accepted as text
        Assert.Equal("0", numeric.Code);
    }

    private sealed class CallbackFactory(DatabaseFixture db, Dictionary<string, string?> extra) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WcsDatabase"] = db.ConnectionString, ["Database:Password"] = null, ["Logging:SystemLog:Enabled"] = "false",
            }).AddInMemoryCollection(extra));
            builder.ConfigureServices(s => s.RemoveAll<IHostedService>());
        }
    }

    [Fact]
    public async Task The_callback_endpoint_refuses_unauthenticated_calls_accepts_authenticated_ones_and_is_not_mapped_when_disabled()
    {
        var (h, rcs) = await SetupAsync(); await using var _h = h; await using var _r = rcs;
        var t = (await h.CreateTaskAsync("BP05", "F5", "http1")).Task;
        var cfg = Cfg(); cfg["Rcs:RealDispatch:Callback:HeaderName"] = "X-Rcs-Callback-Token";

        using (var f = new CallbackFactory(fixture, cfg))
        {
            using var c = f.CreateClient();
            HttpRequestMessage Post(string body, string? secret) { var m = new HttpRequestMessage(HttpMethod.Post, CbPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") }; if (secret is not null) m.Headers.Add("X-Rcs-Callback-Token", secret); return m; }

            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Post(Cb("h-1", "start", "R002", t.RcsRequestCode), null))).StatusCode);       // no credential
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Post(Cb("h-1", "start", "R002", t.RcsRequestCode), "wrong"))).StatusCode);
            Assert.Equal(TaskState.Accepted, (await h.GetTaskAsync(t.Id)).State);                    // nothing happened

            var ok = await c.SendAsync(Post(Cb("h-1", "start", "R002", t.RcsRequestCode), CbSecret));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);                                          // no browser client header and no cookie are needed: it is outside /api
            using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
            Assert.Equal("0", S(json.RootElement, "code")); Assert.Equal("h-1", S(json.RootElement, "reqCode"));
            Assert.Equal(TaskState.Executing, (await h.GetTaskAsync(t.Id)).State);
            Assert.Equal("R002", (await h.GetTaskAsync(t.Id)).RobotCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.SendAsync(Post(new string('x', 70000), CbSecret))).StatusCode);
        }

        var off = Cfg(); off["Rcs:RealDispatch:Callback:Enabled"] = "false";
        using var f2 = new CallbackFactory(fixture, off);
        using var c2 = f2.CreateClient();
        var r = await c2.PostAsync(CbPath, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);   // the route is not served
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    // ================================================================================================ Legacy
    [Fact]
    public async Task A_Legacy_task_with_the_real_adapter_sends_its_AGV_code_and_the_Legacy_dispatch_is_unchanged()
    {
        var (h, rcs) = await SetupAsync(managed: false); await using var _h = h; await using var _r = rcs;
        await h.Db(async db => { foreach (var a in await db.Agvs.ToListAsync()) a.SetRobotCode(a.Code == "AGV1" ? "RF1" : "RE1"); await db.SaveChangesAsync(); return 0; });
        var t = (await h.CreateTaskAsync("BP05", "F5", "legacy")).Task;
        var body = Assert.Single(rcs.Bodies("genAgvSchedulingTask")).RootElement;
        Assert.Equal("RF1", S(body, "agvCode"));                                                     // Legacy: WCS names the AGV of the role, as before
        var after = await h.GetTaskAsync(t.Id);
        Assert.NotNull(after.AgvId); Assert.Null(after.RcsDispatchState); Assert.Null(after.RobotCode);   // Legacy tasks have no RcsManaged tracking
        Assert.Equal(TaskState.Accepted, after.State);
    }
}
