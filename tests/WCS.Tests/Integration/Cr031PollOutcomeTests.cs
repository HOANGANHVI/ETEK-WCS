using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using WCS.Api.Logging;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Domain.Common;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-031 fix: every RCS fleet poll ends with a log line - also when the RCS answers HTTP 200 with a business code other than "0" (RCS_CODE_NOT_SUCCESS:1) - and the line carries the endpoint, the HTTP status, the
/// RCS code and message, the duration, the request id and the consecutive failures. The events go through the REAL log pipeline filter (the 60-second throttle of the logger) to prove they are not swallowed.
/// MOCK RCS only: nothing here was observed on a real RCS-2000.
/// </summary>
[Collection("Database")]
public class Cr031PollOutcomeTests(DatabaseFixture fixture)
{
    private sealed class Events : ILogEventSink { public List<LogEvent> All { get; } = []; public void Emit(LogEvent e) { lock (All) All.Add(e); } public List<LogEvent> Snapshot() { lock (All) return [.. All]; } }

    private sealed class FakeRcs : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public Func<(int Status, string Body, int DelayMs)> Respond { get; set; } = () => (200, Ok(), 0);
        public int Port { get; }
        private FakeRcs(WebApplication app, int port) { _app = app; Port = port; }
        public static async Task<FakeRcs> StartAsync()
        {
            var b = WebApplication.CreateBuilder(); b.WebHost.UseUrls("http://127.0.0.1:0"); b.Logging.ClearProviders();
            var app = b.Build(); FakeRcs? self = null;
            app.MapPost("/{**path}", async (HttpContext c) =>
            {
                var (status, text, delay) = self!.Respond();
                if (delay > 0) await Task.Delay(delay);
                c.Response.StatusCode = status; c.Response.ContentType = "application/json"; await c.Response.WriteAsync(text);
            });
            await app.StartAsync();
            return self = new FakeRcs(app, new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port);
        }
        public async ValueTask DisposeAsync() { try { await _app.StopAsync(); } catch { } await _app.DisposeAsync(); }
    }

    private static string Ok(params string[] codes) => JsonSerializer.Serialize(new { code = "0", message = "successful", reqCode = "x", data = codes.Select(c => new Dictionary<string, string> { ["robotCode"] = c, ["battery"] = "80", ["posX"] = "1", ["posY"] = "2" }) });
    private static string Code1(string msg = "task failed") => JsonSerializer.Serialize(new { code = "1", message = msg, reqCode = "x" });

    private async Task<(Harness H, Events Log)> SetupAsync(FakeRcs rcs, int timeoutSeconds = 2)
    {
        var events = new Events();
        var serilog = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new ThrottlingSink(events, TimeSpan.FromSeconds(60))).CreateLogger();   // the same filter the application puts in front of its sinks
        var h = await Harness.CreateAsync(fixture, s => { s.AddLogging(b => b.AddProvider(new SerilogLoggerProvider(serilog))); s.Configure<RcsFleetOptions>(o => o.TimeoutSeconds = timeoutSeconds); },
            new Dictionary<string, string?> { ["Rcs:Fleet:Source"] = "Real" });
        await h.Scoped(async sp => { await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(true, new RcsSettings("RCS", "127.0.0.1", rcs.Port, null, ConnectionOptions.AuthNone, null, 5000), null, false, null, false, "test")); return 0; });
        return (h, events);
    }

    private static Task<bool> Poll(Harness h, bool advance = true) { if (advance) h.Clock.Advance(TimeSpan.FromSeconds(5)); return h.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force: false)); }
    private static Task<FleetView> View(Harness h) => h.Scoped(sp => sp.GetRequiredService<FleetService>().GetAsync(true));
    private static List<string> Lines(Events e, string code) => e.Snapshot().Where(x => x.RenderMessage().Contains(code)).Select(x => x.RenderMessage()).ToList();
    private static bool Has(LogEvent e, string prop) => e.Properties.ContainsKey(prop);
    private static List<LogEvent> Ev(Events e, string code) => e.Snapshot().Where(x => x.RenderMessage().Contains(code)).ToList();

    // ================================================================================================ 1-2: HTTP 200 with a business code
    [Fact]
    public async Task HTTP_200_with_RCS_code_1_is_a_failed_poll_with_a_log_line_that_says_what_the_RCS_answered_and_the_data_is_not_updated()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, Code1("task failed"), 0);
        await Poll(h, advance: false);

        var v = await View(h);
        Assert.Equal("RCS_CODE_NOT_SUCCESS:1", v.LastErrorCode); Assert.Equal(FleetStates.Disconnected, v.Connection); Assert.Equal("task failed", v.LastErrorMessage);
        Assert.Null(v.LastSuccessAt); Assert.Equal(0, v.Count);                                       // never a success: no time, no AMR
        var failed = Assert.Single(Lines(log, "RCS_FLEET_POLL_FAILED"));
        Assert.Contains("RCS_CODE_NOT_SUCCESS:1", failed); Assert.Contains("HTTP 200", failed); Assert.Contains("RCS code 1", failed); Assert.Contains("RCS message task failed", failed);
        Assert.Contains($"endpoint 127.0.0.1:{rcs.Port}/rcms-dps/rest/queryAgvStatus", failed); Assert.Matches(@"request [0-9a-f]{32}", failed); Assert.Matches(@"\d+ ms", failed); Assert.Contains("consecutive failures 1", failed);
        Assert.Empty(Lines(log, "RCS_FLEET_POLL_SUCCESS")); Assert.Empty(Lines(log, "RCS_FLEET_SNAPSHOT_UPDATED"));   // it is NOT counted as a successful poll
        Assert.Equal(LogEventLevel.Warning, Ev(log, "RCS_FLEET_POLL_FAILED").Single().Level);
    }

    [Fact]
    public async Task HTTP_200_with_RCS_code_0_is_a_success_with_endpoint_request_and_robot_codes_in_the_log()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, Ok("2048", "1357"), 0);
        await Poll(h, advance: false);
        var ok = Assert.Single(Lines(log, "RCS_FLEET_POLL_SUCCESS"));
        Assert.Contains("HTTP 200", ok); Assert.Contains("2 AMR(s) [2048,1357]", ok); Assert.Contains($"endpoint 127.0.0.1:{rcs.Port}/rcms-dps/rest/queryAgvStatus", ok); Assert.Matches(@"request [0-9a-f]{32}", ok); Assert.Contains("last successful poll", ok);
        Assert.Single(Lines(log, "RCS_FLEET_SNAPSHOT_UPDATED")); Assert.Empty(Lines(log, "RCS_FLEET_POLL_FAILED"));
        Assert.NotNull((await View(h)).LastSuccessAt);
    }

    // ================================================================================================ 3-6: the five error classes
    [Fact]
    public async Task A_timeout_is_RCS_TIMEOUT_and_logged()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs, timeoutSeconds: 1); await using var _h = h;
        rcs.Respond = () => (200, Ok(), 3000);
        await Poll(h, advance: false);
        Assert.Equal("RCS_TIMEOUT", (await View(h)).LastErrorCode);
        Assert.Contains("RCS_TIMEOUT", Assert.Single(Lines(log, "RCS_FLEET_POLL_FAILED")));
    }

    [Fact]
    public async Task A_refused_connection_is_RCS_CONNECTION_FAILED_and_logged()
    {
        var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs, timeoutSeconds: 8); await using var _h = h;   // a refused loopback connection takes about 2 s on Windows
        await rcs.DisposeAsync();                                                                     // nothing listens on the port any more
        await Poll(h, advance: false);
        var v = await View(h);
        Assert.Equal("RCS_CONNECTION_FAILED", v.LastErrorCode); Assert.Equal(FleetStates.Disconnected, v.Connection);
        var line = Assert.Single(Lines(log, "RCS_FLEET_POLL_FAILED"));
        Assert.Contains("RCS_CONNECTION_FAILED", line); Assert.Contains("HTTP null", line);             // no HTTP status: the connection was never made
    }

    [Theory]
    [InlineData(404)] [InlineData(500)] [InlineData(503)]
    public async Task An_HTTP_error_status_is_RCS_HTTP_ERROR_with_the_status_in_the_log(int status)
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (status, "<html>do not log this body</html>", 0);
        await Poll(h, advance: false);
        Assert.Equal($"RCS_HTTP_ERROR:{status}", (await View(h)).LastErrorCode);
        var line = Assert.Single(Lines(log, "RCS_FLEET_POLL_FAILED"));
        Assert.Contains($"RCS_HTTP_ERROR:{status}", line); Assert.Contains($"HTTP {status}", line); Assert.DoesNotContain("do not log this body", line);
    }

    [Theory]
    [InlineData("not json at all secret-body-content")] [InlineData("[]")] [InlineData("{\"message\":\"no code\"}")] [InlineData("{\"code\":\"0\",\"data\":\"not a list\"}")]
    public async Task An_invalid_response_is_RCS_RESPONSE_INVALID_and_the_body_is_never_logged(string body)
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, body, 0);
        await Poll(h, advance: false);
        Assert.Equal("RCS_RESPONSE_INVALID", (await View(h)).LastErrorCode);
        var line = Assert.Single(Lines(log, "RCS_FLEET_POLL_FAILED"));
        Assert.Contains("RCS_RESPONSE_INVALID", line); Assert.DoesNotContain("secret-body-content", line); Assert.DoesNotContain("not a list", line.Replace("'data' is not a list.", ""));
    }

    // ================================================================================================ 7-10: repeated failures, stale data, never succeeded, every cycle
    [Fact]
    public async Task Repeated_failures_are_all_logged_not_throttled_and_the_consecutive_counter_grows()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, Code1(), 0);
        for (var i = 0; i < 4; i++) await Poll(h);
        var lines = Lines(log, "RCS_FLEET_POLL_FAILED");
        Assert.Equal(4, lines.Count);                                                                  // identical failures within 60 s: the throttle of the log pipeline must NOT hide them
        for (var i = 0; i < 4; i++) Assert.Contains($"consecutive failures {i + 1}", lines[i]);
        Assert.Equal(4, (await View(h)).ConsecutiveFailures);
        Assert.DoesNotContain("suppressed", string.Join("|", lines));
    }

    [Fact]
    public async Task A_failure_after_a_success_keeps_the_old_snapshot_and_its_time_and_the_data_becomes_stale_never_live()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, Ok("2048"), 0);
        await Poll(h, advance: false);
        var good = await View(h); var lastOk = good.LastSuccessAt;
        Assert.Equal(FleetStates.Fresh, good.Amrs.Single().State);

        rcs.Respond = () => (200, Code1(), 0);
        await Poll(h);
        var bad = await View(h);
        Assert.Equal(lastOk, bad.LastSuccessAt);                                                       // the time of the last SUCCESS is untouched
        Assert.Equal("2048", bad.Amrs.Single().RobotCode); Assert.Equal("80", bad.Amrs.Single().Battery);   // the last good snapshot is kept
        Assert.Equal("RCS_CODE_NOT_SUCCESS:1", bad.LastErrorCode); Assert.Equal(FleetStates.Disconnected, bad.Connection);
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.NotEqual(FleetStates.Fresh, (await View(h)).Amrs.Single().State);                      // not Live any more
    }

    [Fact]
    public async Task Without_any_success_the_API_has_no_update_time_so_the_screen_cannot_show_a_made_up_one()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, _) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, Code1(), 0);
        await Poll(h, advance: false); await Poll(h);
        var v = await View(h);
        Assert.Null(v.LastSuccessAt); Assert.NotNull(v.LastAttemptAt); Assert.Empty(v.Amrs); Assert.Equal(FleetStates.Disconnected, v.Connection);
        // the screen shows "Chưa nhận được dữ liệu AMR từ RCS" (agv.fleetDisconnectedNever) when lastSuccessAt is null: no dash, no time
        var panel = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "frontend", "wcs-web", "src", "components", "FleetPanel.tsx"));
        Assert.Contains("fleet.lastSuccessAt ? t('agv.fleetDisconnected'", panel); Assert.Contains("t('agv.fleetDisconnectedNever')", panel);
    }

    [Fact]
    public async Task Every_poll_cycle_ends_with_exactly_one_SUCCESS_or_FAILED_line()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs, timeoutSeconds: 1); await using var _h = h;
        var script = new Queue<(int, string, int)>([(200, Ok("1"), 0), (200, Code1(), 0), (500, "x", 0), (200, "garbage", 0), (200, Ok("1", "2"), 0), (200, Ok(), 2500)]);
        rcs.Respond = () => script.Dequeue();
        for (var i = 0; i < 6; i++) await Poll(h, advance: i > 0);
        Assert.Equal(6, Lines(log, "RCS_FLEET_POLL_STARTED").Count);
        Assert.Equal(2, Lines(log, "RCS_FLEET_POLL_SUCCESS").Count); Assert.Equal(4, Lines(log, "RCS_FLEET_POLL_FAILED").Count);
    }

    // ================================================================================================ the database copy is rate-limited, the stdout line is not
    [Fact]
    public async Task The_stored_copy_is_limited_but_the_console_line_is_written_for_every_poll()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        rcs.Respond = () => (200, Code1(), 0);
        for (var i = 0; i < 3; i++) await Poll(h);
        var failed = Ev(log, "RCS_FLEET_POLL_FAILED");
        Assert.Equal(3, failed.Count);                                                                 // all three reach the sinks (stdout / file)
        Assert.All(failed, e => Assert.True(Has(e, "NoThrottle")));
        Assert.Equal(2, failed.Count(e => Has(e, "NoPersist")));                                       // ...but only the first is also stored in system_log
        Assert.All(Ev(log, "RCS_FLEET_POLL_STARTED"), e => Assert.True(Has(e, "NoPersist")));
    }
}
