using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WCS.Api.Api;
using WCS.Application.Config;
using WCS.Application.Fleet;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Fleet;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-031 – REAL RCS fleet monitoring: WCS itself calls POST /rcms-dps/rest/queryAgvStatus every interval, takes the number of AMRs from data[], invents nothing, never falls back to the simulator and
/// logs every poll. These tests use a MOCK server that answers like the API document 5.2.14 to prove the contract, the parsing and the behaviour on failure. The mock is NEVER operational data and
/// nothing here was verified against a real RCS-2000.
/// </summary>
[Collection("Database")]
public class Cr031RealFleetMonitoringTests(DatabaseFixture fixture)
{
    private const string Token = "tok-secret-xyz";

    private sealed class Capture : ILoggerProvider
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => new L(this);
        public void Dispose() { }
        private sealed class L(Capture c) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { lock (c.Lines) c.Lines.Add($"{logLevel}|{formatter(state, exception)}"); }
        }
        public List<string> Snapshot() { lock (Lines) return [.. Lines]; }
    }

    private sealed class FakeRcs : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public List<(string Path, string Body)> Requests { get; } = [];
        public Func<(int Status, string Body, int DelayMs)> Respond { get; set; } = () => (200, Fleet(), 0);
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

    /// <summary>The answer of queryAgvStatus (API 5.2.14): data[] has one object per AMR; only what is passed is present (nothing is filled in).</summary>
    private static string Fleet(params object[] amrs) => JsonSerializer.Serialize(new { code = "0", message = "successful", reqCode = "x", data = amrs });
    private static object Amr(string code, string? battery = "80", string? x = "1000", string? y = "2000", string? status = "4", string? stop = "0") =>
        new Dictionary<string, string?> { ["robotCode"] = code, ["battery"] = battery, ["posX"] = x, ["posY"] = y, ["mapCode"] = "AA", ["speed"] = "0", ["status"] = status, ["robotDir"] = "90", ["stop"] = stop, ["podCode"] = "100001", ["podDir"] = "0" }
            .Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);

    private async Task<(Harness H, Capture Log)> SetupAsync(FakeRcs rcs, int timeoutSeconds = 2, string auth = ConnectionOptions.AuthToken, bool withPort = true, Dictionary<string, string?>? extra = null)
    {
        var log = new Capture();
        var cfg = new Dictionary<string, string?> { ["Rcs:Fleet:Source"] = "Real" };   // NO Rcs:Fleet:Real:Port: the port comes from the saved RCS connection
        if (extra is not null) foreach (var kv in extra) cfg[kv.Key] = kv.Value;
        var h = await Harness.CreateAsync(fixture, s => { s.AddLogging(b => b.AddProvider(log)); s.Configure<RcsFleetOptions>(o => o.TimeoutSeconds = timeoutSeconds); }, cfg);
        await h.Scoped(async sp =>
        {
            await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(true,
                new RcsSettings("RCS 2000", "127.0.0.1", withPort ? rcs.Port : null, null, auth, auth == ConnectionOptions.AuthUserPassword ? "user" : null, 5000), auth == ConnectionOptions.AuthUserPassword ? "pw-secret-abc" : null, false,
                auth == ConnectionOptions.AuthToken ? Token : null, false, "test"));
            return 0;
        });
        return (h, log);
    }

    private static Task<bool> Refresh(Harness h, bool force = true) => h.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force));
    private static Task<FleetView> View(Harness h) => h.Scoped(sp => sp.GetRequiredService<FleetService>().GetAsync(true));

    // ================================================================================================ the count comes from data[]; WCS itself calls the RCS
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(5)]
    public async Task The_number_of_AMRs_is_the_number_of_elements_of_data_and_the_list_comes_from_the_real_RCS_only(int n)
    {
        await using var rcs = await FakeRcs.StartAsync();
        var codes = Enumerable.Range(0, n).Select(i => $"R{3000 + i * 7}").ToArray();
        rcs.Respond = () => (200, Fleet(codes.Select(c => Amr(c)).ToArray()), 0);
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        h.Rcs.SetFleetSize(3);                                                                       // the simulator HAS AMRs (SIM-001..): they must never appear

        Assert.True(await Refresh(h));
        var v = await View(h);
        Assert.Equal("Real", v.Source); Assert.False(v.SimulatedData); Assert.Equal(n, v.Count); Assert.Equal(codes.Order(), v.Amrs.Select(a => a.RobotCode).Order());
        Assert.DoesNotContain(v.Amrs, a => a.RobotCode.StartsWith("SIM-"));
        Assert.Equal(n == 0 ? FleetStates.Connected : FleetStates.Connected, v.Connection);
        var req = Assert.Single(rcs.Requests);
        Assert.Equal("/rcms-dps/rest/queryAgvStatus", req.Path);                                       // WCS sent the request (the RCS received it)
        Assert.Equal(0, await h.Db(db => db.Agvs.CountAsync()));                                      // no AGV1 / AGV2 or any business AGV was created
        Assert.Equal(0, await h.Db(db => db.RcsAmrSnapshots.CountAsync(x => x.Source == FleetSource.Simulator)));
    }

    [Fact]
    public async Task Only_the_values_the_RCS_returns_are_shown_and_a_change_of_battery_position_or_status_is_followed()
    {
        await using var rcs = await FakeRcs.StartAsync();
        rcs.Respond = () => (200, Fleet(Amr("2048", battery: "80", x: "1000", y: "2000", status: "4"), Amr("1357", battery: null, x: null, y: null, status: null, stop: null)), 0);
        var (h, _) = await SetupAsync(rcs); await using var _h = h;
        await Refresh(h);
        var v1 = await View(h);
        var a = v1.Amrs.Single(x => x.RobotCode == "2048"); var b = v1.Amrs.Single(x => x.RobotCode == "1357");
        Assert.Equal(("80", "1000", "2000", "AA", "4", "90", "0", "100001", "0"), (a.Battery, a.PosX, a.PosY, a.MapCode, a.Status, a.RobotDir, a.Stop, a.PodCode, a.PodDir));
        Assert.Null(b.Battery); Assert.Null(b.PosX); Assert.Null(b.Status); Assert.Null(b.Stop);   // nothing is invented for a missing field

        rcs.Respond = () => (200, Fleet(Amr("2048", battery: "64", x: "1500", y: "2500", status: "2"), Amr("1357")), 0);
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        await Refresh(h);
        var a2 = (await View(h)).Amrs.Single(x => x.RobotCode == "2048");
        Assert.Equal(("64", "1500", "2500", "2"), (a2.Battery, a2.PosX, a2.PosY, a2.Status));
    }

    [Fact]
    public async Task An_AMR_that_disappears_from_the_response_is_shown_as_missing_never_as_live_and_never_deleted()
    {
        await using var rcs = await FakeRcs.StartAsync();
        rcs.Respond = () => (200, Fleet(Amr("A1"), Amr("B2")), 0);
        var (h, _) = await SetupAsync(rcs); await using var _h = h;
        await Refresh(h);
        rcs.Respond = () => (200, Fleet(Amr("A1")), 0);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await Refresh(h);
        var v = await View(h);
        Assert.Equal(FleetStates.Fresh, v.Amrs.Single(x => x.RobotCode == "A1").State);
        Assert.Equal(FleetStates.Missing, v.Amrs.Single(x => x.RobotCode == "B2").State);
        Assert.Equal(2, v.Count);                                                                      // B2 keeps its row (history), flagged, not removed
    }

    // ================================================================================================ failures: timeout, loss and recovery, never the simulator
    [Fact]
    public async Task A_timeout_is_RCS_DISCONNECTED_with_the_last_good_time_and_the_old_data_becomes_stale_not_live()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs, timeoutSeconds: 1); await using var _h = h;
        await Refresh(h);
        var ok = (await View(h)); var lastOk = ok.LastSuccessAt;
        Assert.NotNull(lastOk);

        rcs.Respond = () => (200, Fleet(), 3000);                                                      // the RCS does not answer within the timeout
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await Refresh(h);
        var v = await View(h);
        Assert.Equal(FleetStates.Disconnected, v.Connection); Assert.Equal(FleetErrorCodes.Timeout, v.LastErrorCode); Assert.Equal(lastOk, v.LastSuccessAt);
        Assert.Equal(1, v.ConsecutiveFailures);
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.All((await View(h)).Amrs, a => Assert.NotEqual(FleetStates.Fresh, a.State));            // the old data is never shown as live
        Assert.False((await View(h)).SimulatedData);
        Assert.DoesNotContain((await View(h)).Amrs, a => a.RobotCode.StartsWith("SIM-"));              // no fallback to the simulator
    }

    [Fact]
    public async Task The_RCS_is_lost_and_comes_back_polling_continues_and_the_failures_are_reset()
    {
        await using var rcs = await FakeRcs.StartAsync();
        rcs.Respond = () => (200, Fleet(Amr("1001")), 0);
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        await Refresh(h);
        rcs.Respond = () => (503, "down", 0);
        for (var i = 0; i < 3; i++) { h.Clock.Advance(TimeSpan.FromSeconds(5)); await Refresh(h); }
        var down = await View(h);
        Assert.Equal(FleetStates.Disconnected, down.Connection); Assert.Equal(3, down.ConsecutiveFailures);
        Assert.StartsWith(FleetErrorCodes.Http, down.LastErrorCode);
        Assert.Equal(1, down.Count);                                                                   // the last real data is kept (stale), nothing is replaced

        rcs.Respond = () => (200, Fleet(Amr("1001"), Amr("1002")), 0);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await Refresh(h);
        var back = await View(h);
        Assert.Equal(FleetStates.Connected, back.Connection); Assert.Equal(0, back.ConsecutiveFailures); Assert.Equal(2, back.Amrs.Count(a => a.State == FleetStates.Fresh));
        var lines = log.Snapshot();
        Assert.Equal(5, lines.Count(l => l.Contains("RCS_FLEET_POLL_STARTED")));                       // one attempt per interval, also after the failures
        Assert.Equal(2, lines.Count(l => l.Contains("RCS_FLEET_POLL_SUCCESS"))); Assert.Equal(3, lines.Count(l => l.Contains("RCS_FLEET_POLL_FAILED")));
    }

    // ================================================================================================ configuration: no assumption, no simulator
    [Fact]
    public async Task A_switched_off_RCS_connection_is_a_configuration_error_nothing_is_sent_and_the_simulator_is_not_used()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, _) = await SetupAsync(rcs); await using var _h = h;
        await h.Scoped(async sp => { await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(false, new RcsSettings("RCS 2000", "127.0.0.1", rcs.Port, null, ConnectionOptions.AuthToken, null, 5000), null, false, null, false, "test")); return 0; });
        h.Rcs.SetFleetSize(3);
        await Refresh(h);
        var v = await View(h);
        Assert.Equal(FleetErrorCodes.NotConfigured, v.LastErrorCode);
        Assert.Empty(rcs.Requests); Assert.Equal(0, v.Count); Assert.Equal("Real", v.Source); Assert.False(v.SimulatedData);
    }

    [Fact]
    public async Task Without_a_saved_RCS_connection_the_state_is_NotConfigured_and_never_the_simulator()
    {
        var (h, _) = await SetupAsync(await FakeRcs.StartAsync()); await using var _h = h;
        await h.Db(async db => { db.ConnectionSettings.RemoveRange(db.ConnectionSettings); await db.SaveChangesAsync(); return 0; });
        h.Rcs.SetFleetSize(3);
        await Refresh(h);
        var v = await View(h);
        Assert.Equal(FleetErrorCodes.NotConfigured, v.LastErrorCode); Assert.Equal(0, v.Count); Assert.Equal(FleetStates.Disconnected, v.Connection); Assert.False(v.SimulatedData);
    }

    [Fact]
    public async Task The_query_is_sent_without_any_credential_whatever_the_saved_authentication_mode_and_no_secret_reaches_a_log()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        await Refresh(h);
        using (var body = JsonDocument.Parse(Assert.Single(rcs.Requests).Body)) Assert.Equal(["reqCode", "reqTime"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());   // a token is saved, but the RCS has no authentication for this API: none is sent
        rcs.Respond = () => (500, $"error mentioning tokenCode={Token}", 0);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await Refresh(h);
        Assert.DoesNotContain(log.Snapshot(), l => l.Contains(Token));                                  // not in any log line, success or failure
        Assert.Equal("tokenCode=***", FleetService.Sanitize("tokenCode=abc123"));
        Assert.DoesNotContain("pw-secret", FleetService.Sanitize("password: pw-secret-abc and more") ?? "");

        await using var rcs2 = await FakeRcs.StartAsync();
        var (h2, log2) = await SetupAsync(rcs2, auth: ConnectionOptions.AuthUserPassword); await using var _h2 = h2;
        await Refresh(h2);
        using (var body = JsonDocument.Parse(Assert.Single(rcs2.Requests).Body)) Assert.Equal(["reqCode", "reqTime"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());   // no username, password or token
        Assert.DoesNotContain(log2.Snapshot(), l => l.Contains("pw-secret"));
    }

    [Fact]
    public async Task Polling_never_depends_on_a_token_username_or_password()
    {
        await using var rcs = await FakeRcs.StartAsync();
        rcs.Respond = () => (200, Fleet(Amr("2048")), 0);
        var (h, _) = await SetupAsync(rcs, auth: ConnectionOptions.AuthNone); await using var _h = h;          // no credential saved at all
        Assert.True(await Refresh(h));
        var v = await View(h);
        Assert.Equal(FleetStates.Connected, v.Connection); Assert.Null(v.LastErrorCode); Assert.Single(v.Amrs);
        await h.Db(async db => { foreach (var x in await db.ConnectionSettings.ToListAsync()) x.Update(true, x.SettingsJson, null, "test", h.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });   // and no stored secret either
        h.Clock.Advance(TimeSpan.FromSeconds(5)); Assert.True(await Refresh(h, force: false));
        Assert.Equal(FleetStates.Connected, (await View(h)).Connection);
    }

    // ================================================================================================ proof of active polling: the log
    [Fact]
    public async Task Every_poll_writes_started_then_success_or_failed_and_a_snapshot_update_with_the_robot_codes()
    {
        await using var rcs = await FakeRcs.StartAsync();
        rcs.Respond = () => (200, Fleet(Amr("2048"), Amr("1357")), 0);
        var (h, log) = await SetupAsync(rcs); await using var _h = h;
        await Refresh(h);
        var lines = log.Snapshot();
        var started = lines.FindIndex(l => l.Contains("RCS_FLEET_POLL_STARTED")); var success = lines.FindIndex(l => l.Contains("RCS_FLEET_POLL_SUCCESS")); var snap = lines.FindIndex(l => l.Contains("RCS_FLEET_SNAPSHOT_UPDATED"));
        Assert.True(started >= 0 && success > started && snap > success);
        Assert.Contains("HTTP 200", lines[success]); Assert.Contains("2 AMR(s) [2048,1357]", lines[success]); Assert.Contains("last successful poll", lines[success]); Assert.Contains("ms", lines[success]);
        Assert.Contains("2 AMR(s) in the response", lines[snap]);
        Assert.Contains("source Real", lines[started]);                                                  // the log says which source answered

        rcs.Respond = () => (500, "boom", 0);
        h.Clock.Advance(TimeSpan.FromSeconds(5)); await Refresh(h);
        var failed = log.Snapshot().Last(l => l.Contains("RCS_FLEET_POLL_FAILED"));
        Assert.Contains("RCS_HTTP_ERROR:500", failed); Assert.Contains("HTTP 500", failed); Assert.Contains("consecutive failures 1", failed);
    }

    [Fact]
    public async Task The_interval_is_5_seconds_a_slightly_early_tick_still_polls_and_two_polls_never_overlap()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, _) = await SetupAsync(rcs); await using var _h = h;
        Assert.True(await Refresh(h, force: false));                                                    // first poll
        h.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.False(await Refresh(h, force: false));         // too early: no query
        h.Clock.Advance(TimeSpan.FromSeconds(2.8)); Assert.True(await Refresh(h, force: false));        // 4.8 s since the last poll: a timer tick that came a few ms early still polls
        Assert.Equal(2, rcs.Requests.Count);

        rcs.Respond = () => (200, Fleet(Amr("1")), 800);
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        var first = Refresh(h, force: false); await Task.Delay(150);
        var second = await Refresh(h, force: true);                                                     // another poll while one is running: refused at once
        Assert.False(second); Assert.True(await first);
        Assert.Equal(3, rcs.Requests.Count);
    }

    // ================================================================================================ no simulated AGV status, production guard
    [Fact]
    public async Task While_the_fleet_is_the_real_RCS_the_business_AGV_status_is_never_taken_from_the_simulator()
    {
        await using var rcs = await FakeRcs.StartAsync();
        var (h, _) = await SetupAsync(rcs); await using var _h = h;
        await h.SeedStandardAsync();
        await h.Db(async db => { foreach (var a in await db.Agvs.ToListAsync()) a.SetRobotCode(a.Code == "AGV1" ? "2048" : "1357"); await db.SaveChangesAsync(); return 0; });
        await h.Scoped(async sp => { await sp.GetRequiredService<AgvStatusService>().RefreshAsync(); return 0; });
        var agvs = await h.Db(db => db.Agvs.AsNoTracking().ToListAsync());
        Assert.All(agvs, a => { Assert.Equal(AgvConnectionStatus.Unknown, a.ConnectionStatus); Assert.Equal("RCS_FLEET_REAL", a.StatusDetail); });   // not "Online / simulated"
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name; public string ApplicationName { get; set; } = "t"; public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static IConfiguration C(params (string k, string v)[] kv) => new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(x => x.k, x => (string?)x.v)).Build();

    [Fact]
    public void Production_refuses_a_simulated_fleet_unless_explicitly_allowed()
    {
        var prod = new Env("Production");
        Assert.Throws<InvalidOperationException>(() => ProductionSafety.ValidateFleet(C(), prod));                                    // unset = Simulator
        Assert.Throws<InvalidOperationException>(() => ProductionSafety.ValidateFleet(C(("Rcs:Fleet:Source", "Simulator")), prod));
        Assert.False(ProductionSafety.ValidateFleet(C(("Rcs:Fleet:Source", "Real")), prod));
        Assert.True(ProductionSafety.ValidateFleet(C(("Rcs:Fleet:AllowSimulatorInProduction", "true")), prod));                       // explicit, supervised exception
        Assert.False(ProductionSafety.ValidateFleet(C(), new Env("Development")));                                                     // development keeps the simulator
    }
}
