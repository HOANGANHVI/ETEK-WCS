using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Domain.Common;
using WCS.Domain.Fleet;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-027 Phase 1: the AMR fleet comes from the RCS (queryAgvStatus), N AMRs, shown as fresh / stale / missing, never replaced by invented AMRs. Only the SIMULATOR and a local test server
/// that answers like the API document were used: nothing here was verified against a real RCS-2000.
/// </summary>
[Collection("Database")]
public class Cr027FleetTests(DatabaseFixture fixture)
{
    private static Task<bool> Refresh(Harness h, bool force = true) => h.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force));
    private static Task<FleetView> View(Harness h, bool admin = true) => h.Scoped(sp => sp.GetRequiredService<FleetService>().GetAsync(admin));

    // ================================================================================================ N AMRs, 0..many
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(25)]
    public async Task The_fleet_has_exactly_the_AMRs_the_RCS_reports_whatever_their_number(int n)
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(n);
        Assert.True(await Refresh(h));
        var v = await View(h);
        Assert.Equal(n, v.Count); Assert.Equal(n, v.FreshCount);
        Assert.Equal(FleetStates.Connected, v.Connection);                                   // a successful answer with 0 AMRs is "connected", not "no data"
        Assert.True(v.SimulatedData); Assert.Equal("Simulator", v.Source);
        Assert.All(v.Amrs, a => { Assert.StartsWith("SIM-", a.RobotCode); Assert.Equal(FleetStates.Fresh, a.State); });
        Assert.Equal(n, await h.Db(db => db.RcsAmrSnapshots.CountAsync()));
    }

    [Fact]
    public async Task Before_any_query_there_is_no_data_and_nothing_is_invented()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var v = await View(h);
        Assert.Equal(FleetStates.NoData, v.Connection); Assert.Empty(v.Amrs); Assert.Null(v.LastSuccessAt);
    }

    [Fact]
    public async Task A_new_AMR_appears_and_one_that_disappears_is_kept_as_missing_then_fresh_again_when_it_returns()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(2); await Refresh(h);
        h.Clock.Advance(TimeSpan.FromSeconds(6));
        h.Rcs.SetFleetSize(3); await Refresh(h);                                                // SIM-003 is new
        Assert.Equal(["SIM-001", "SIM-002", "SIM-003"], (await View(h)).Amrs.Select(a => a.RobotCode).ToArray());

        h.Clock.Advance(TimeSpan.FromSeconds(6));
        h.Rcs.SetFleet(new[] { "SIM-001", "SIM-003" }.Select(c => new RcsFleetAmr(c, "0", null, "80", "1", "2", "M", "0", "4", "0", "0", null, null, [])));
        await Refresh(h);                                                                       // SIM-002 is gone from the response
        var v = await View(h);
        Assert.Equal(3, v.Count);                                                               // its row is kept (no deletion)
        var gone = v.Amrs.Single(a => a.RobotCode == "SIM-002");
        Assert.Equal(FleetStates.Missing, gone.State);
        Assert.Equal(2, v.FreshCount);
        Assert.True(gone.LastSeenAt < v.LastSuccessAt);                                         // shows WHEN it was last seen

        h.Clock.Advance(TimeSpan.FromSeconds(6));
        h.Rcs.SetFleetSize(3); await Refresh(h);
        Assert.All((await View(h)).Amrs, a => Assert.Equal(FleetStates.Fresh, a.State));
    }

    // ================================================================================================ failures, stale, offline
    [Fact]
    public async Task A_failing_query_keeps_the_last_data_but_marks_it_stale_and_never_adds_or_removes_an_AMR()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(2); await Refresh(h);
        var before = (await View(h)).Amrs.Select(a => (a.RobotCode, a.Battery, a.LastSeenAt)).ToList();

        foreach (var failure in new[] { RcsFleetResult.Fail(FleetErrorCodes.Timeout, "t"), RcsFleetResult.Fail(FleetErrorCodes.InvalidResponse, "bad"), RcsFleetResult.Fail($"{FleetErrorCodes.Http}:500", "HTTP 500") })
        {
            h.Clock.Advance(TimeSpan.FromSeconds(6));
            h.Rcs.FleetFailure = failure;
            Assert.True(await Refresh(h));
            var v = await View(h);
            Assert.Equal(FleetStates.Disconnected, v.Connection);
            Assert.Equal(failure.ErrorCode, v.LastErrorCode);
            Assert.Equal(2, v.Count);                                                           // the last data is still there ...
            Assert.All(v.Amrs, a => Assert.Equal(FleetStates.Stale, a.State));                  // ... and is NOT shown as current
            Assert.Equal(before, v.Amrs.Select(a => (a.RobotCode, a.Battery, a.LastSeenAt)).ToList());
        }
        Assert.Equal(3, (await View(h)).ConsecutiveFailures);
        h.Rcs.FleetFailure = null; h.Rcs.Unreachable = true;                                    // the simulated outage switch is a failure too
        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);
        Assert.Equal(FleetErrorCodes.Unreachable, (await View(h)).LastErrorCode);

        h.Rcs.Unreachable = false; h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);   // recovery
        var ok = await View(h);
        Assert.Equal(FleetStates.Connected, ok.Connection); Assert.Equal(0, ok.ConsecutiveFailures); Assert.All(ok.Amrs, a => Assert.Equal(FleetStates.Fresh, a.State));
    }

    [Fact]
    public async Task The_very_first_query_failing_leaves_an_empty_fleet_not_an_invented_one()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(5); h.Rcs.FleetFailure = RcsFleetResult.Fail(FleetErrorCodes.Timeout, "t");
        await Refresh(h);
        var v = await View(h);
        Assert.Equal(FleetStates.Disconnected, v.Connection); Assert.Empty(v.Amrs);
        Assert.Equal(0, await h.Db(db => db.RcsAmrSnapshots.CountAsync()));
    }

    [Fact]
    public async Task Data_that_is_not_refreshed_becomes_stale_with_time_even_without_a_failed_query()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(1); await Refresh(h);
        Assert.Equal(FleetStates.Fresh, (await View(h)).Amrs.Single().State);
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(FleetStates.Fresh, (await View(h)).Amrs.Single().State);                    // inside 3 intervals (15 s)
        h.Clock.Advance(TimeSpan.FromSeconds(10));                                               // 20 s without an update
        var v = await View(h);
        Assert.Equal(FleetStates.Stale, v.Amrs.Single().State); Assert.Equal(FleetStates.Disconnected, v.Connection);
    }

    [Fact]
    public async Task The_query_interval_is_applied_and_a_query_that_never_answers_is_a_timeout()
    {
        await using var h = await Harness.CreateAsync(fixture);
        h.Rcs.SetFleetSize(1);
        Assert.True(await Refresh(h, force: false));
        Assert.False(await Refresh(h, force: false));                                           // too early: no second query
        Assert.Equal(1, h.Rcs.FleetQueryCalls);
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await Refresh(h, force: false));
        Assert.Equal(2, h.Rcs.FleetQueryCalls);

        await using var slow = await Harness.CreateAsync(fixture, s =>
        {
            s.Configure<RcsFleetOptions>(o => o.TimeoutSeconds = 1);
            s.AddScoped<IRcsFleetQuery, NeverAnswers>();
        });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Refresh(slow);
        Assert.InRange(sw.Elapsed.TotalSeconds, 0.8, 8);
        var v = await View(slow);
        Assert.Equal(FleetErrorCodes.Timeout, v.LastErrorCode); Assert.Equal(FleetStates.Disconnected, v.Connection); Assert.Empty(v.Amrs);
    }

    private sealed class NeverAnswers : IRcsFleetQuery
    {
        public FleetSource Source => FleetSource.Simulator;
        public async Task<RcsFleetResult> QueryAsync(CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return RcsFleetResult.Ok([]); }
    }

    // ================================================================================================ simulator and real never mix
    [Fact]
    public async Task Simulator_and_real_rows_are_stored_apart_and_a_real_row_is_never_shown_as_simulated()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.Db(async db =>
        {
            var real = new RcsAmrSnapshot(FleetSource.Real, "1001", h.Clock.UtcNow); real.Apply("0", null, "55", "1", "2", "M1", "0", "2", "0", "0", null, null, null, h.Clock.UtcNow);
            db.RcsAmrSnapshots.Add(real);
            var st = new RcsFleetState(FleetSource.Real); st.Succeeded(h.Clock.UtcNow, 1, 5); db.RcsFleetStates.Add(st);
            await db.SaveChangesAsync(); return 0;
        });
        h.Rcs.SetFleetSize(2); await Refresh(h);
        var sim = await View(h);
        Assert.Equal(["SIM-001", "SIM-002"], sim.Amrs.Select(a => a.RobotCode).ToArray());      // the Real row 1001 is not in the simulator view
        Assert.True(sim.SimulatedData);
        Assert.Equal(1, await h.Db(db => db.RcsAmrSnapshots.CountAsync(x => x.Source == FleetSource.Real)));
        Assert.Equal(2, await h.Db(db => db.RcsAmrSnapshots.CountAsync(x => x.Source == FleetSource.Simulator)));
    }

    // ================================================================================================ the parser (API document 5.2.14)
    private const string DocumentSample = """
        { "code": "0", "message": "successful", "reqCode": "1541954B96B1112",
          "data": [
            { "robotCode": "1001", "robotDir": "180", "robotIp": "", "battery": "80", "posX": "1.0", "posY": "2.0", "mapCode": "", "speed": "", "status": "1", "exclType": "0", "stop": "1",
              "podCode": "200001", "podDir": "90", "path": ["[10000,20000,90]", "[20000,30000,-90]", "[20000,30000,180]", "[30000,40000,0]"] },
            { "robotCode": "1001", "robotDir": "180", "robotIp": "", "battery": "80", "posX": "1.0", "posY": "2.0", "mapCode": "", "speed": "", "status": "1", "exclType": "0", "stop": "1",
              "podCode": "200001", "podDir": "90", "path": [] } ] }
        """;

    [Fact]
    public void The_parser_reads_the_document_sample_field_by_field_and_ignores_the_repeated_robotCode()
    {
        var r = HikQueryAgvStatusParser.Parse(DocumentSample);
        Assert.True(r.Success);
        var a = Assert.Single(r.Amrs);                                                          // the sample repeats robotCode 1001: the first entry stays
        Assert.Equal(1, r.Skipped);
        Assert.Equal(("1001", "180", "", "80", "1.0", "2.0", "", "", "1", "0", "1", "200001", "90"),
            (a.RobotCode, a.RobotDir, a.RobotIp, a.Battery, a.PosX, a.PosY, a.MapCode, a.Speed, a.Status, a.ExclType, a.Stop, a.PodCode, a.PodDir));
        Assert.Equal(["[10000,20000,90]", "[20000,30000,-90]", "[20000,30000,180]", "[30000,40000,0]"], a.Path);
    }

    [Theory]
    [InlineData("not json", FleetErrorCodes.InvalidResponse)]
    [InlineData("[]", FleetErrorCodes.InvalidResponse)]
    [InlineData("{\"message\":\"x\"}", FleetErrorCodes.InvalidResponse)]
    [InlineData("{\"code\":\"0\",\"data\":{\"robotCode\":\"1\"}}", FleetErrorCodes.InvalidResponse)]
    [InlineData("{\"code\":\"1\",\"message\":\"Failed\"}", FleetErrorCodes.Rejected + ":1")]
    [InlineData("{\"code\":\"99\",\"message\":\"Unknown error, try again.\"}", FleetErrorCodes.Rejected + ":99")]
    public void A_response_that_is_not_a_success_in_the_documented_shape_is_a_failure_without_any_AMR(string json, string code)
    {
        var r = HikQueryAgvStatusParser.Parse(json);
        Assert.False(r.Success); Assert.Equal(code, r.ErrorCode); Assert.Empty(r.Amrs);
    }

    [Fact]
    public void Zero_AMRs_missing_ids_and_numeric_values_are_handled_without_inventing_anything()
    {
        Assert.Empty(HikQueryAgvStatusParser.Parse("{\"code\":\"0\",\"data\":[]}").Amrs);
        Assert.Empty(HikQueryAgvStatusParser.Parse("{\"code\":\"0\"}").Amrs);
        var r = HikQueryAgvStatusParser.Parse("{\"code\":0,\"data\":[{\"robotCode\":\"7\",\"battery\":55,\"status\":2},{\"battery\":\"10\"},{\"robotCode\":\" \"},\"x\"]}");
        Assert.True(r.Success); Assert.Equal(3, r.Skipped);
        var a = Assert.Single(r.Amrs); Assert.Equal(("7", "55", "2", null), (a.RobotCode, a.Battery, a.Status, a.PodCode));
    }

    // ================================================================================================ the real client against a local server that answers like the document
    private sealed class FakeRcs : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public List<(string Path, string Body)> Requests { get; } = [];
        public Func<(int Status, string Body, int DelayMs)> Respond { get; set; } = () => (200, DocumentSample, 0);
        public int Port { get; }

        private FakeRcs(WebApplication app, int port) { _app = app; Port = port; }

        public static async Task<FakeRcs> StartAsync()
        {
            var b = WebApplication.CreateBuilder(); b.WebHost.UseUrls("http://127.0.0.1:0"); b.Logging.ClearProviders();
            var app = b.Build(); FakeRcs? self = null;
            app.MapPost("/{**path}", async (HttpContext c) =>
            {
                using var r = new StreamReader(c.Request.Body); var body = await r.ReadToEndAsync();
                lock (self!.Requests) self.Requests.Add((c.Request.Path.Value!, body));
                var (status, text, delay) = self.Respond();
                if (delay > 0) await Task.Delay(delay);
                c.Response.StatusCode = status; c.Response.ContentType = "application/json"; await c.Response.WriteAsync(text);
            });
            await app.StartAsync();
            var port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
            return self = new FakeRcs(app, port);
        }

        public async ValueTask DisposeAsync() { await _app.StopAsync(); await _app.DisposeAsync(); }
    }

    private async Task<Harness> RealHarnessAsync(FakeRcs rcs, int timeoutSeconds = 2)
    {
        var h = await Harness.CreateAsync(fixture, s => s.Configure<RcsFleetOptions>(o => { o.TimeoutSeconds = timeoutSeconds; o.Real.Port = rcs.Port; }),
            new Dictionary<string, string?> { ["Rcs:Fleet:Source"] = "Real", ["Rcs:Fleet:Real:Port"] = rcs.Port.ToString() });
        await h.Scoped(async sp =>
        {
            await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(true, new RcsSettings("HIK RCS-2000", "127.0.0.1", rcs.Port, null, ConnectionOptions.AuthNone, null, 3000), null, false, null, false, "test"));
            return 0;
        });
        return h;
    }

    [Fact]
    public async Task The_real_client_sends_one_read_only_queryAgvStatus_and_stores_the_answer_as_Real()
    {
        await using var rcs = await FakeRcs.StartAsync(); await using var h = await RealHarnessAsync(rcs);
        Assert.True(await Refresh(h));
        var req = Assert.Single(rcs.Requests);
        Assert.Equal("/rcms-dps/rest/queryAgvStatus", req.Path);                                // the endpoint of API document 5.2.14
        using var body = JsonDocument.Parse(req.Body);
        Assert.Equal(["mapCode", "reqCode", "reqTime"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());   // request id, time and the MAP ID (mapCode): no robot list, no task command
        Assert.Equal(32, body.RootElement.GetProperty("reqCode").GetString()!.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", body.RootElement.GetProperty("reqTime").GetString()!);

        var v = await View(h);
        Assert.Equal("Real", v.Source); Assert.False(v.SimulatedData); Assert.Equal(FleetStates.Connected, v.Connection);
        var a = Assert.Single(v.Amrs); Assert.Equal("1001", a.RobotCode); Assert.Equal("80", a.Battery); Assert.Equal(4, a.Path.Count);
        Assert.Equal(1, await h.Db(db => db.RcsAmrSnapshots.CountAsync(x => x.Source == FleetSource.Real)));
        Assert.Equal(0, await h.Db(db => db.RcsAmrSnapshots.CountAsync(x => x.Source == FleetSource.Simulator)));
        Assert.Equal(0, h.Rcs.CreateCalls + h.Rcs.ContinueCalls + h.Rcs.CancelCalls);           // no task call of any kind
        Assert.Null((await View(h, admin: false)).Amrs.Single().RobotIp); Assert.Empty((await View(h, admin: false)).Amrs.Single().Path);   // an Operator does not get the network parameters / path
    }

    [Fact]
    public async Task The_real_client_reports_server_error_timeout_refusal_and_unreachable_as_failures_and_keeps_the_data()
    {
        await using var rcs = await FakeRcs.StartAsync(); await using var h = await RealHarnessAsync(rcs, timeoutSeconds: 1);
        await Refresh(h);
        async Task<string?> Failure(Func<(int, string, int)> respond)
        {
            rcs.Respond = respond; h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);
            var v = await View(h);
            Assert.Equal(FleetStates.Disconnected, v.Connection); Assert.Equal(FleetStates.Stale, v.Amrs.Single().State);   // the AMR is kept, marked stale
            return v.LastErrorCode;
        }
        Assert.Equal($"{FleetErrorCodes.Http}:500", await Failure(() => (500, "boom", 0)));
        Assert.Equal(FleetErrorCodes.Timeout, await Failure(() => (200, DocumentSample, 3000)));
        Assert.Equal($"{FleetErrorCodes.Rejected}:99", await Failure(() => (200, "{\"code\":\"99\",\"message\":\"Unknown error, try again.\"}", 0)));
        Assert.Equal(FleetErrorCodes.InvalidResponse, await Failure(() => (200, "<html>", 0)));
        rcs.Respond = () => (200, DocumentSample, 0); h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);
        Assert.Equal(FleetStates.Connected, (await View(h)).Connection);

        await rcs.DisposeAsync();                                                               // nothing listens any more
        h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);
        Assert.Contains((await View(h)).LastErrorCode, new[] { FleetErrorCodes.Unreachable, FleetErrorCodes.Timeout });   // a closed loopback port is refused at once on Linux but after about 2 s on Windows (then it is a timeout)
        Assert.Equal(FleetStates.Stale, (await View(h)).Amrs.Single().State);
    }

    [Fact]
    public async Task The_real_client_does_nothing_until_the_RCS_connection_is_saved_and_enabled()
    {
        await using var rcs = await FakeRcs.StartAsync();
        await using var h = await Harness.CreateAsync(fixture, null, new Dictionary<string, string?> { ["Rcs:Fleet:Source"] = "Real", ["Rcs:Fleet:Real:Port"] = rcs.Port.ToString() });
        await Refresh(h);
        Assert.Equal(FleetErrorCodes.NotConfigured, (await View(h)).LastErrorCode);
        Assert.Empty(rcs.Requests);                                                             // no network call without a saved, enabled connection
        Assert.Empty((await View(h)).Amrs);
    }

    // ================================================================================================ regression: nothing of the business changes
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(40)]
    public async Task The_transport_statistics_are_exactly_the_baseline_whatever_the_number_of_AMRs_the_RCS_reports(int amrs)
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var step = 0; var last = 0;
        async Task Between() { last = amrs == 40 ? (step++ % 2 == 0 ? 40 : 0) : amrs; kit.H.Rcs.SetFleetSize(last); kit.H.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(kit.H); }   // the fleet is polled all the way through the scenario
        var stats = await StatsScenario.RunAsync(kit, Between);
        Assert.Equal(Cr027StatisticsBaselineTests.Golden, stats);                               // task counts, trips (legs), per-AGV figures, inventory, reservations, AGV rows: all as before CR-027
        var fleet = await View(kit.H);
        Assert.Equal(last, fleet.FreshCount);                                                    // the AMRs the RCS reports now ...
        Assert.Equal(amrs, fleet.Count);                                                         // ... and the ones that left its answer are kept (missing), never deleted                          // and the fleet itself was really polled
    }

    [Fact]
    public async Task The_fleet_never_touches_the_agv_rows_the_assignment_by_role_or_the_task_history()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5); await h.StockAsync("F1", RackKind.Full, 2);
        var agvsBefore = await h.Db(db => db.Agvs.AsNoTracking().OrderBy(a => a.Code).Select(a => new { a.Id, a.Code, a.Role, a.RcsRobotCode, a.Enabled, a.ConnectionStatus, a.DispatchHold }).ToListAsync());
        h.Rcs.SetFleetSize(10); await Refresh(h);                                                // ten AMRs, none of them AGV1 / AGV2
        var created = await h.CreateTaskAsync("BP05", "F5");
        Assert.Equal(agvsBefore.Single(a => a.Code == "AGV1").Id, (await h.GetTaskAsync(created.Task.Id)).AgvId);   // AgvId is still assigned by role (PER_AGV unchanged)
        Assert.Equal(agvsBefore, await h.Db(db => db.Agvs.AsNoTracking().OrderBy(a => a.Code).Select(a => new { a.Id, a.Code, a.Role, a.RcsRobotCode, a.Enabled, a.ConnectionStatus, a.DispatchHold }).ToListAsync()));
        Assert.Equal(2, await h.Db(db => db.Agvs.CountAsync()));
        h.Rcs.SetFleetSize(0); h.Clock.Advance(TimeSpan.FromSeconds(6)); await Refresh(h);
        Assert.Equal(1, await h.Db(db => db.Tasks.CountAsync()));                                // the number of AMRs never changes the number of tasks
    }

    [Fact]
    public async Task An_AMR_whose_code_is_the_robot_code_of_a_business_AGV_shows_that_AGV_and_its_active_task_read_only()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.SeedStandardAsync(bufferCapacity: 5); await h.StockAsync("F1", RackKind.Full, 2);
        var task = (await h.CreateTaskAsync("BP05", "F5")).Task;
        h.Rcs.SetFleet([new RcsFleetAmr("R-FULL", "0", null, "70", "1", "2", "M", "0", "2", "0", "0", "RACK-9", "0", []), new RcsFleetAmr("OTHER-1", "0", null, "90", "1", "2", "M", "0", "4", "0", "0", null, null, [])]);
        await Refresh(h);
        var v = await View(h);
        var linked = v.Amrs.Single(a => a.RobotCode == "R-FULL");
        Assert.Equal("AGV1", linked.LinkedAgvCode); Assert.Equal(task.Id, linked.ActiveTaskId); Assert.Equal("RACK-9", linked.PodCode);
        var other = v.Amrs.Single(a => a.RobotCode == "OTHER-1");
        Assert.Null(other.LinkedAgvCode); Assert.Null(other.ActiveTaskId);
    }

    // ================================================================================================ the API
    [Fact]
    public async Task The_fleet_endpoint_is_open_to_admin_and_operator_and_only_the_admin_sees_the_network_parameters()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator)); await db.SaveChangesAsync();
            f.Services.GetRequiredService<WCS.Infrastructure.Simulation.RcsSimulator>().SetFleet([new RcsFleetAmr("A-1", "90", "10.0.0.5", "60", "1", "2", "M", "5", "2", "0", "0", null, null, ["[1,2,3]"])]);
            await scope.ServiceProvider.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force: true);
        }
        Assert.Equal(401, (await f.CreateClient().WithClientHeader().SendJsonAsync(HttpMethod.Get, "/api/agvs/fleet")).Status);
        var op = f.CreateClient().WithClientHeader(); Assert.Equal(200, await op.LoginAsync("op1"));
        var admin = f.CreateClient().WithClientHeader(); Assert.Equal(200, await admin.LoginAsync("admin1"));
        var o = (await op.SendJsonAsync(HttpMethod.Get, "/api/agvs/fleet")).Root.GetProperty("data");
        var a = (await admin.SendJsonAsync(HttpMethod.Get, "/api/agvs/fleet")).Root.GetProperty("data");
        Assert.Equal(1, o.GetProperty("count").GetInt32()); Assert.True(o.GetProperty("simulatedData").GetBoolean());
        Assert.Equal(JsonValueKind.Null, o.GetProperty("amrs")[0].GetProperty("robotIp").ValueKind);
        Assert.Equal("10.0.0.5", a.GetProperty("amrs")[0].GetProperty("robotIp").GetString());
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/agvs")).Status);         // the existing AGV endpoint is untouched
    }
}
