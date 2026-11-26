using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

[Collection("Database")]
public class PlcRuntimeIntegrationTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);   // TEST values entered through the API below

    private async Task<(Harness H, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> F)> ArrangeAsync(Dictionary<string, string?>? plc = null, bool start = true)
    {
        var h = await Harness.CreateAsync(fixture);
        var baseF = new DbApiFactory(fixture);
        using (var scope = baseF.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        }
        await baseF.DisposeAsync();
        var cfg = new Dictionary<string, string?> { ["Plc:SimulatorPollingIntervalMs"] = "40", ["Plc:ReconnectInitialDelayMs"] = "50", ["Plc:ReconnectMaxDelayMs"] = "200" };
        if (plc is not null) foreach (var kv in plc) cfg[kv.Key] = kv.Value;
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)));
        if (start) await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static async Task<bool> WaitFor(Func<Task<bool>> cond, int ms = 6000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (await cond()) return true; await Task.Delay(30); }
        return await cond();
    }

    private static async Task<JsonElement> Io(HttpClient c, string station)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, $"/api/plc/stations/{station}/io");
        return r.Status == 200 ? r.Root.GetProperty("data") : default;
    }

    private static async Task<JsonElement?> Point(HttpClient c, string station, string signal)
    {
        var io = await Io(c, station);
        if (io.ValueKind == JsonValueKind.Undefined) return null;
        return io.GetProperty("points").EnumerateArray().First(p => p.GetProperty("signalName").GetString() == signal);
    }

    private static async Task<string> State(HttpClient c, string plc)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, "/api/plc/overview");
        return r.Root.GetProperty("data").GetProperty("plcs").EnumerateArray().First(p => p.GetProperty("plcId").GetString() == plc).GetProperty("state").GetString()!;
    }

    // ---------------------------------------------------------------- simulator mode
    [Fact]
    public async Task Simulator_mode_runs_4_workers_over_16_stations_and_368_points_with_no_physical_connection()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        Assert.True(await WaitFor(async () =>
        {
            var o = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data");
            return o.GetProperty("plcs").EnumerateArray().All(p => p.GetProperty("state").GetString() == "Connected" && p.GetProperty("goodPoints").GetInt32() == 92);
        }));
        var data = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data");
        Assert.Equal("Simulator", data.GetProperty("runtime").GetProperty("effectiveMode").GetString());
        Assert.Equal(4, data.GetProperty("plcs").GetArrayLength());
        Assert.Equal(16, data.GetProperty("stations").GetArrayLength());
        Assert.All(data.GetProperty("stations").EnumerateArray(), s => Assert.Equal(23, s.GetProperty("total").GetInt32()));
        var rt = f.Services.GetRequiredService<IPlcRuntime>();
        Assert.Equal(368, rt.Stations(true).Sum(s => s.Points.Count));
        Assert.All(rt.Stations(true).SelectMany(s => s.Points), p => Assert.Equal("Simulated", p.Quality));   // never reported as a real device value
        // health reports the runtime without claiming a real PLC
        var health = (await admin.SendJsonAsync(HttpMethod.Get, "/api/system/health")).Root.GetProperty("data").GetProperty("integration").GetProperty("plcRuntime");
        Assert.Equal("Simulator", health.GetProperty("mode").GetString()); Assert.Equal(4, health.GetProperty("connected").GetInt32());
    }

    [Fact]
    public async Task Operators_may_monitor_but_see_no_addresses_network_details_or_errors_and_cannot_use_configuration_or_writes()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1"); var admin = await As(f, "admin1");
        Assert.True(await WaitFor(async () => await State(op, "PLC-01") == "Connected"));

        var overview = await op.SendJsonAsync(HttpMethod.Get, "/api/plc/overview");
        Assert.Equal(200, overview.Status);
        Assert.DoesNotContain("192.168.10", overview.Raw);                                      // no IP for operators
        Assert.DoesNotContain("diagnostics\":{", overview.Raw);
        var io = await op.SendJsonAsync(HttpMethod.Get, "/api/plc/stations/A1/io");
        Assert.Equal(200, io.Status);
        Assert.Equal(23, io.Root.GetProperty("data").GetProperty("points").GetArrayLength());
        Assert.DoesNotContain("M9000", io.Raw);                                                 // not even the template reference
        Assert.All(io.Root.GetProperty("data").GetProperty("points").EnumerateArray(), p => Assert.Equal(JsonValueKind.Null, p.GetProperty("address").ValueKind));
        Assert.Equal(404, (await op.SendJsonAsync(HttpMethod.Get, "/api/plc/stations/ZZ/io")).Status);

        foreach (var (m, url) in new[] { ("POST", "/api/plc/stations/A1/signals/Request_Ack/test-write"), ("POST", "/api/plc/simulator/value"), ("POST", "/api/plc/simulator/plc-online"),
                     ("POST", "/api/plc/simulator/station-fault"), ("POST", "/api/plc/connections/PLC-01/test") })
            Assert.Equal(403, (await op.SendJsonAsync(new HttpMethod(m), url, new { })).Status);
        var adm = await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/stations/A1/io");
        Assert.Contains("\"address\"", adm.Raw);
        Assert.Equal(401, (await f.CreateClient().WithClientHeader().SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Status);
    }

    [Fact]
    public async Task Simulated_PLC_values_reach_the_station_view_and_a_PLC_going_offline_does_not_affect_the_others()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1"); var op = await As(f, "op1");
        Assert.True(await WaitFor(async () => await State(admin, "PLC-04") == "Connected"));

        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/value", new { plcId = "PLC-01", stationId = "A1", signal = "Pickup_Request", value = true })).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/value", new { plcId = "PLC-02", stationId = "B1", signal = "Rack_Full", value = 3 })).Status);
        Assert.True(await WaitFor(async () => (await Point(op, "A1", "Pickup_Request"))?.GetProperty("value").ValueKind == JsonValueKind.True));
        Assert.True(await WaitFor(async () => (await Point(op, "B1", "Rack_Full"))?.GetProperty("value").GetInt32() == 3));
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/value", new { plcId = "PLC-01", stationId = "A1", signal = "Rack_Full", value = "abc" })).Status);   // INT needs a number

        var off = await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/plc-online", new { plcId = "PLC-01", online = false });
        Assert.True(off.Status == 200, off.Raw);
        var seen = new List<string>();
        Assert.True(await WaitFor(async () => { var st = await State(op, "PLC-01"); seen.Add(st); return st == "Faulted"; }), string.Join(",", seen.Distinct()));
        Assert.True(await WaitFor(async () => (await Point(op, "A1", "Pickup_Request"))?.GetProperty("quality").GetString() == "Disconnected"));
        Assert.Equal(JsonValueKind.True, (await Point(op, "A1", "Pickup_Request"))!.Value.GetProperty("value").ValueKind);     // last value remains
        foreach (var other in new[] { "PLC-02", "PLC-03", "PLC-04" }) Assert.Equal("Connected", await State(op, other));
        await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/value", new { plcId = "PLC-02", stationId = "B1", signal = "Rack_Full", value = 9 });
        Assert.True(await WaitFor(async () => (await Point(op, "B1", "Rack_Full"))?.GetProperty("value").GetInt32() == 9));        // still live while PLC-01 is down

        await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/plc-online", new { plcId = "PLC-01", online = true });
        Assert.True(await WaitFor(async () => await State(op, "PLC-01") == "Connected"));
        Assert.True(await WaitFor(async () => (await Point(op, "A1", "Pickup_Request"))?.GetProperty("quality").GetString() == "Simulated"));

        // one failing station: its points are Bad, the other stations of the same PLC are not
        await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/station-fault", new { plcId = "PLC-01", stationId = "A3", failing = true });
        Assert.True(await WaitFor(async () => (await Point(op, "A3", "Rack_Full"))?.GetProperty("quality").GetString() == "Bad"));
        Assert.Equal("Simulated", (await Point(op, "A4", "Rack_Full"))!.Value.GetProperty("quality").GetString());
        Assert.Equal("Connected", await State(op, "PLC-01"));

        // diagnostics / logs are persisted and Admin-only
        Assert.True(await WaitFor(async () => (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/logs?plcId=PLC-01")).Root.GetProperty("data").EnumerateArray().Any(l => l.GetProperty("event").GetString() == "STATE")));
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/plc/logs")).Status);
        Assert.True(await h.Db(db => db.PlcCommLog.AsNoTracking().AnyAsync(l => l.PlcId == "PLC-01")));
    }

    [Fact]
    public async Task Test_writes_follow_the_safety_rules_even_in_the_simulator()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        Assert.True(await WaitFor(async () => await State(admin, "PLC-01") == "Connected"));
        string Url(string s) => $"/api/plc/stations/A1/signals/{s}/test-write";

        var ok = await admin.SendJsonAsync(HttpMethod.Post, Url("Request_Ack"), new { value = true });
        Assert.Equal(200, ok.Status);
        Assert.True(await WaitFor(async () => (await Point(admin, "A1", "Request_Ack"))?.GetProperty("value").ValueKind == JsonValueKind.True));

        foreach (var (signal, code) in new[] { ("Transfer_Permit", "WRITE_BLOCKED_SAFETY"), ("AGV_Departure_Permit", "WRITE_BLOCKED_SAFETY"), ("Safety_Ready", "WRITE_DIRECTION"), ("Pickup_Request", "WRITE_DIRECTION") })
        {
            var r = await admin.SendJsonAsync(HttpMethod.Post, Url(signal), new { value = true });
            Assert.Equal(422, r.Status); Assert.Equal(code, r.Root.ErrorCode());
        }
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Post, Url("AGV_ID"), new { value = true })).Status);   // wrong JSON type for an INT
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Post, Url("No_Such_Signal"), new { value = true })).Status);
        var permit = await Point(admin, "A1", "Transfer_Permit");
        Assert.NotEqual(JsonValueKind.True, permit!.Value.GetProperty("value").ValueKind);                              // the permit was never set
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_TEST_WRITE").ToListAsync());
        Assert.Contains(audit, a => a.Result == AuditResult.Success); Assert.Contains(audit, a => a.Result == AuditResult.Failed);
    }

    // ---------------------------------------------------------------- real modes against the MC mock server
    private static async Task ConfigureAndApproveAsync(HttpClient admin, Harness h, int port, params (string Station, string Signal, string Address)[] points)
    {
        var put = await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new
        {
            ipAddress = "127.0.0.1", port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0,
            monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true,
        });
        Assert.Equal(200, put.Status);
        foreach (var (station, signal, address) in points)
        {
            var id = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.StationId == station && p.SignalName == signal).Select(p => p.Id).SingleAsync());
            var setAddr = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address });
            Assert.True(setAddr.Status == 200, setAddr.Raw);
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{id}/approve")).Status);
        }
    }

    [Fact]
    public async Task REAL_READ_ONLY_connects_only_when_both_keys_and_the_Admin_switch_allow_it_and_never_writes()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        plc.SetBit(100, true); plc.SetWord(1500, 42);

        // (1) deployment key missing: nothing connects even though the catalog exists
        var (h, f1) = await ArrangeAsync(new() { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "false" });
        await using var _h = h;
        var admin1 = await As(f1, "admin1");
        Assert.Equal("Disabled", await State(admin1, "PLC-01"));
        var attempt = await admin1.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new { ipAddress = "127.0.0.1", port = plc.Port, enabled = true });
        Assert.Equal(400, attempt.Status); Assert.Contains("NEEDS_CONFIGURATION", attempt.Raw);                    // incomplete parameters cannot be switched on
        Assert.Equal(0, plc.ConnectionCount);
        await f1.DisposeAsync();

        // (2) all keys + complete confirmed parameters + approved addresses: connects read-only
        using var f2 = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "true", ["Plc:ReconnectInitialDelayMs"] = "50" })));
        await f2.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        var admin = await As(f2, "admin1");
        await ConfigureAndApproveAsync(admin, h, plc.Port, ("A1", "Pickup_Request", "M100"), ("A1", "Rack_Full", "D1500"), ("A1", "Request_Ack", "M101"));
        Assert.True(await WaitFor(async () => await State(admin, "PLC-01") == "Connected"));
        Assert.True(await WaitFor(async () => (await Point(admin, "A1", "Pickup_Request"))?.GetProperty("quality").GetString() == "Good"));
        Assert.Equal(JsonValueKind.True, (await Point(admin, "A1", "Pickup_Request"))!.Value.GetProperty("value").ValueKind);
        Assert.Equal(42, (await Point(admin, "A1", "Rack_Full"))!.Value.GetProperty("value").GetInt32());
        Assert.Equal("NotConfigured", (await Point(admin, "A1", "Safety_Ready"))!.Value.GetProperty("quality").GetString());   // unconfirmed points stay untouched
        Assert.Equal("Disabled", await State(admin, "PLC-02"));                                                                // other PLCs were never switched on
        Assert.True(plc.ConnectionCount >= 1);                                                                                  // (each configuration change reloads the worker)
        Assert.All(plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)), r => Assert.Equal(McFrame.CommandBatchRead, r.Command));      // reads only

        // a write is refused in READ_ONLY, even for an approved, allow-listed point, and nothing reaches the PLC
        var ack = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.StationId == "A1" && p.SignalName == "Request_Ack").Select(p => p.Id).SingleAsync());
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/write-allowed", new { allowed = true })).Status);
        var denied = await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/stations/A1/signals/Request_Ack/test-write", new { value = true });
        Assert.Equal(422, denied.Status); Assert.Equal("READ_ONLY", denied.Root.ErrorCode());
        Assert.False(plc.GetBit(101));
        Assert.DoesNotContain(plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)), r => r.Command == McFrame.CommandBatchWrite);
        var info = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime");
        Assert.Equal("RealReadOnly", info.GetProperty("effectiveMode").GetString());
    }

    [Fact]
    public async Task REAL_READ_WRITE_degrades_to_read_only_without_every_write_key_and_still_blocks_safety_signals_with_all_of_them()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f0) = await ArrangeAsync(start: false);
        await using var _h = h; await f0.DisposeAsync();

        async Task<(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> F, HttpClient Admin)> Boot(Dictionary<string, string?> cfg)
        {
            var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)));
            await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
            return (f, await As(f, "admin1"));
        }
        var common = new Dictionary<string, string?> { ["Plc:Mode"] = "RealReadWrite", ["Plc:AllowLiveConnection"] = "true", ["Plc:ReconnectInitialDelayMs"] = "50" };

        // (a) write key missing -> effective mode READ_ONLY, the write is refused
        var (fa, adminA) = await Boot(common);
        await ConfigureAndApproveAsync(adminA, h, plc.Port, ("A1", "Request_Ack", "M101"), ("A1", "Transfer_Permit", "M102"), ("A1", "Pickup_Request", "M100"));
        Assert.Equal("RealReadOnly", (await adminA.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime").GetProperty("effectiveMode").GetString());
        Assert.True(await WaitFor(async () => await State(adminA, "PLC-01") == "Connected"));
        var ack = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.StationId == "A1" && p.SignalName == "Request_Ack").Select(p => p.Id).SingleAsync());
        await adminA.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/write-allowed", new { allowed = true });
        Assert.Equal("READ_ONLY", (await adminA.SendJsonAsync(HttpMethod.Post, "/api/plc/stations/A1/signals/Request_Ack/test-write", new { value = true })).Root.ErrorCode());
        Assert.False(plc.GetBit(101));
        await fa.DisposeAsync();

        // (b) every key present: the allow-listed point is written, the control signals are STILL refused
        var keys = new Dictionary<string, string?>(common) { ["Plc:Write:Enabled"] = "true" }
            .Declare(("PLC-01", "A1", "Request_Ack", "M101"), ("PLC-01", "A1", "Transfer_Permit", "M102"));   // CR-044: the permit stays blocked although declared
        var (fb, adminB) = await Boot(keys);
        await using var _fb = fb;
        Assert.Equal("RealReadWrite", (await adminB.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime").GetProperty("effectiveMode").GetString());
        Assert.True(await WaitFor(async () => await State(adminB, "PLC-01") == "Connected"));
        var ok = await adminB.SendJsonAsync(HttpMethod.Post, "/api/plc/stations/A1/signals/Request_Ack/test-write", new { value = true });
        Assert.Equal(200, ok.Status);
        Assert.True(plc.GetBit(101));                                                                                      // really reached the (mock) PLC
        var written = plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Single(r => r.Command == McFrame.CommandBatchWrite);
        Assert.Equal((new DeviceAddress(PlcDevice.M, 101), 1), (written.Head, written.Points));                            // exactly one bit, nothing else

        var permit = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.StationId == "A1" && p.SignalName == "Transfer_Permit").Select(p => p.Id).SingleAsync());
        Assert.Equal(422, (await adminB.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{permit}/write-allowed", new { allowed = true })).Status);
        Assert.Equal("WRITE_BLOCKED_SAFETY", (await adminB.SendJsonAsync(HttpMethod.Post, "/api/plc/stations/A1/signals/Transfer_Permit/test-write", new { value = true })).Root.ErrorCode());
        Assert.False(plc.GetBit(102));
        Assert.Equal("WRITE_DIRECTION", (await adminB.SendJsonAsync(HttpMethod.Post, "/api/plc/stations/A1/signals/Pickup_Request/test-write", new { value = true })).Root.ErrorCode());
        Assert.Equal(1, plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Count(r => r.Command == McFrame.CommandBatchWrite));      // still exactly one write in total
    }

    // ---------------------------------------------------------------- SignalR authorization
    private static async Task<HubConnection> HubAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f, string user, List<string> received, string[] events)
    {
        var client = f.CreateClient().WithClientHeader();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password = TestUsers.Password });
        var cookie = res.Headers.GetValues("Set-Cookie").First().Split(';')[0];
        var hub = new HubConnectionBuilder().WithUrl(new Uri(f.Server.BaseAddress, "/hubs/wcs"), o =>
        {
            o.HttpMessageHandlerFactory = _ => f.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling;
            o.Headers["Cookie"] = cookie;
        }).Build();
        foreach (var e in events) hub.On<JsonElement>(e, _ => { lock (received) received.Add(e); });
        await hub.StartAsync();
        return hub;
    }

    [Fact]
    public async Task The_hub_rejects_anonymous_clients_and_admin_only_events_reach_Admins_but_never_Operators()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var anon = await f.CreateClient().PostAsync("/hubs/wcs/negotiate?negotiateVersion=1", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anon.StatusCode);

        var events = new[] { "PlcStatusChanged", "StationIoChanged", "PlcLog", "PlcSecretProbe" };
        var adminGot = new List<string>(); var opGot = new List<string>();
        await using var adminHub = await HubAsync(f, "admin1", adminGot, events);
        await using var opHub = await HubAsync(f, "op1", opGot, events);

        var notifier = f.Services.GetRequiredService<IWcsNotifier>();
        await notifier.PublishAsync([new WcsEvent("PlcStatusChanged", new { plcId = "PLC-01" }), new WcsEvent("PlcLog", new { message = "diag" }, AdminOnly: true),
            new WcsEvent("PlcSecretProbe", new { x = 1 }, AdminOnly: true)]);
        Assert.True(await WaitFor(() => Task.FromResult(adminGot.Contains("PlcSecretProbe"))));
        Assert.True(await WaitFor(() => Task.FromResult(opGot.Contains("PlcStatusChanged"))));
        await Task.Delay(300);
        Assert.Contains("PlcLog", adminGot); Assert.Contains("PlcSecretProbe", adminGot);
        Assert.DoesNotContain("PlcLog", opGot); Assert.DoesNotContain("PlcSecretProbe", opGot);       // Operators never receive admin-only events

        // runtime events: a real status change is delivered to both, the matching diagnostic log only to the Admin
        adminGot.Clear(); opGot.Clear();
        f.Services.GetRequiredService<ISimulatorControl>().SetPlcOnline("PLC-03", false);
        Assert.True(await WaitFor(() => Task.FromResult(adminGot.Contains("PlcStatusChanged") && adminGot.Contains("PlcLog"))));
        Assert.True(await WaitFor(() => Task.FromResult(opGot.Contains("PlcStatusChanged"))));
        Assert.DoesNotContain("PlcLog", opGot);
    }
}
