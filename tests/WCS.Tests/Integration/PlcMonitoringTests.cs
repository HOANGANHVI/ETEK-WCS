using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-020: PLC Monitoring - the merged monitoring list, the immediate read of one point and the read-back after a write. Everything runs against the MC MOCK server (never a real PLC); what these
/// tests prove is the WCS side of the protocol, not the behaviour of a physical PLC.
/// </summary>
[Collection("Database")]
public class PlcMonitoringTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);
    private static Dictionary<string, string?> AllKeys => new() { ["Plc:Mode"] = "RealReadWrite", ["Plc:AllowLiveConnection"] = "true", ["Plc:Write:Enabled"] = "true" };

    private async Task<(Harness H, WebApplicationFactory<Program> F, HttpClient Admin, HttpClient Op)> BootAsync(MelsecMcMockServer plc, Dictionary<string, string?> cfg)
    {
        var h = await Harness.CreateAsync(fixture);
        var f0 = new DbApiFactory(fixture);
        using (var scope = f0.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        }
        await f0.DisposeAsync();
        var all = new Dictionary<string, string?> { ["Plc:ReconnectInitialDelayMs"] = "50", ["Plc:ReconnectMaxDelayMs"] = "200" };
        foreach (var kv in cfg) all[kv.Key] = kv.Value;
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(all)); });
        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        HttpClient Client() => f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }).WithClientHeader();
        var admin = Client(); Assert.Equal(200, await admin.LoginAsync("admin1"));
        var op = Client(); Assert.Equal(200, await op.LoginAsync("op1"));
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new
        {
            ipAddress = "127.0.0.1", port = plc.Port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0,
            monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true,
        })).Status);
        return (h, f, admin, op);
    }

    private static Task<Guid> Id(Harness h, string signal) =>
        h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == "PLC-01" && p.StationId == "A1" && p.SignalName == signal).Select(p => p.Id).SingleAsync());

    private static async Task Enter(HttpClient admin, Guid id, string address) =>
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address })).Status);

    private static async Task<bool> WaitFor(Func<Task<bool>> cond, int ms = 6000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (await cond()) return true; await Task.Delay(30); }
        return await cond();
    }

    private static async Task<string?> State(HttpClient c) =>
        (await c.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("plcs").EnumerateArray().First(p => p.GetProperty("plcId").GetString() == "PLC-01").GetProperty("state").GetString();

    private static async Task<JsonElement> Row(HttpClient c, string signal, string plc = "PLC-01", string station = "A1")
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, $"/api/plc/monitor/points?plcId={plc}&stationId={station}");
        Assert.Equal(200, r.Status);
        return r.Root.GetProperty("data").GetProperty("points").EnumerateArray().Single(p => p.GetProperty("signalName").GetString() == signal).Clone();
    }

    private static Task<(int Status, JsonElement Root, string Raw)> ReadNow(HttpClient c, string signal) =>
        c.SendJsonAsync(HttpMethod.Post, $"/api/plc/stations/A1/signals/{signal}/read", new { });

    private static int ReadFrames(MelsecMcMockServer plc) => plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Count(r => r.Command == McFrame.CommandBatchRead);
    private static int WriteFrames(MelsecMcMockServer plc) => plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Count(r => r.Command == McFrame.CommandBatchWrite);

    // ---------------------------------------------------------------- status rules (no PLC needed)
    [Fact]
    public void Status_is_Good_only_for_a_fresh_successful_read_and_every_other_state_is_told_apart()
    {
        var now = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero); var stale = TimeSpan.FromSeconds(15);
        Assert.Equal("Good", PlcMonitor.StatusOf("Good", now.AddSeconds(-2), now, stale));
        Assert.Equal("Stale", PlcMonitor.StatusOf("Good", now.AddSeconds(-16), now, stale));            // a value that stopped refreshing is never Good
        Assert.Equal("Unknown", PlcMonitor.StatusOf("Good", null, now, stale));
        Assert.Equal("Bad", PlcMonitor.StatusOf("Bad", now, now, stale));
        Assert.Equal("Offline", PlcMonitor.StatusOf("Disconnected", now.AddSeconds(-1), now, stale));
        Assert.Equal("Unknown", PlcMonitor.StatusOf("Unknown", null, now, stale));
        Assert.Equal("NotConfigured", PlcMonitor.StatusOf("NotConfigured", null, now, stale));
        Assert.Equal("Simulated", PlcMonitor.StatusOf("Simulated", now, now, stale));                    // never presented as a real device value
    }

    [Fact]
    public void Read_back_verifies_only_an_exact_match_and_never_coerces()
    {
        Assert.Equal("VERIFIED", PlcMonitor.Verify(PointValue.OfInt(321), new ReadResult(true, null, null, PointValue.OfInt(321))).Status);
        Assert.Equal("MISMATCH", PlcMonitor.Verify(PointValue.OfInt(321), new ReadResult(true, null, null, PointValue.OfInt(7))).Status);
        Assert.Equal("MISMATCH", PlcMonitor.Verify(PointValue.OfBool(true), new ReadResult(true, null, null, PointValue.OfBool(false))).Status);
        Assert.Equal("VERIFIED", PlcMonitor.Verify(PointValue.OfString("AB"), new ReadResult(true, null, null, PointValue.OfString("AB\0\0"))).Status);   // only padding is ignored
        Assert.Equal("MISMATCH", PlcMonitor.Verify(PointValue.OfString("AB"), new ReadResult(true, null, null, PointValue.OfString("ABC"))).Status);
        var failed = PlcMonitor.Verify(PointValue.OfInt(1), new ReadResult(false, "READ_FAILED", "end code"));
        Assert.Equal(("UNAVAILABLE", "READ_FAILED"), (failed.Status, failed.Code));                      // a failed read-back is not a confirmation
    }

    // ---------------------------------------------------------------- the monitoring list and the immediate read
    [Fact]
    public async Task The_monitor_list_shows_the_live_value_status_and_time_per_PLC_and_an_Operator_never_sees_addresses_or_approvals()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, op) = await BootAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;
        var agv = await Id(h, "AGV_ID"); plc.SetWord(1600, 42); await Enter(admin, agv, "D1600");
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        // (the first poll may happen before the mock word is read at all: wait for the value itself, not only for the status)
        Assert.True(await WaitFor(async () => { var r = await Row(admin, "AGV_ID"); return r.GetProperty("status").GetString() == "Good" && r.GetProperty("value").ValueKind == JsonValueKind.Number && r.GetProperty("value").GetInt32() == 42; }));

        var a = await Row(admin, "AGV_ID");
        Assert.Equal(42, a.GetProperty("value").GetInt32());
        Assert.Equal("PLC-01", a.GetProperty("plcId").GetString()); Assert.Equal("D1600", a.GetProperty("address").GetString());
        Assert.Equal("NOT_APPROVED", a.GetProperty("writeApproval").GetString());
        Assert.NotEqual(JsonValueKind.Null, a.GetProperty("lastUpdated").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(a.GetProperty("description").GetString()));

        var o = await Row(op, "AGV_ID");                                                                   // the Operator monitors: same value, no address, no approval data
        Assert.Equal(42, o.GetProperty("value").GetInt32()); Assert.Equal("Good", o.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, o.GetProperty("address").ValueKind); Assert.Equal(JsonValueKind.Null, o.GetProperty("writeApproval").ValueKind);

        // another PLC (nothing configured, never connected) is told apart: its points are not shown as Good
        var other = await Row(admin, "AGV_ID", "PLC-02", "B1");
        Assert.Equal("PLC-02", other.GetProperty("plcId").GetString()); Assert.NotEqual("Good", other.GetProperty("status").GetString());
        var all = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/monitor/points")).Root.GetProperty("data").GetProperty("points");
        Assert.Equal(368, all.GetArrayLength());
        Assert.Equal(4, all.EnumerateArray().Select(p => p.GetProperty("plcId").GetString()).Distinct().Count());
        Assert.Equal(0, WriteFrames(plc));                                                                 // monitoring never writes
    }

    [Fact]
    public async Task Read_now_returns_the_value_the_PLC_holds_at_that_moment_and_both_roles_may_read_but_it_never_writes()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, op) = await BootAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;
        var agv = await Id(h, "AGV_ID"); plc.SetWord(1600, 11); await Enter(admin, agv, "D1600");
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));

        var r1 = await ReadNow(admin, "AGV_ID");
        Assert.Equal(200, r1.Status); Assert.True(r1.Root.GetProperty("data").GetProperty("success").GetBoolean());
        Assert.Equal(11, r1.Root.GetProperty("data").GetProperty("value").GetInt32());
        plc.SetWord(1600, 12);                                                                             // the PLC changes; a read after that must show it
        var r2 = await ReadNow(op, "AGV_ID");
        Assert.Equal(200, r2.Status); Assert.Equal(12, r2.Root.GetProperty("data").GetProperty("value").GetInt32());
        Assert.Equal(12, (await Row(admin, "AGV_ID")).GetProperty("value").GetInt32());                    // the shared station handler is updated, not a private copy
        Assert.Equal(0, WriteFrames(plc));

        var none = await ReadNow(admin, "Request_Ack");                                                    // no address: nothing is sent, and it says why
        Assert.True(none.Status == 200 && !none.Root.GetProperty("data").GetProperty("success").GetBoolean());
        Assert.Equal("NOT_CONFIGURED", none.Root.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal(404, (await ReadNow(admin, "No_Such_Signal")).Status);
    }

    [Fact]
    public async Task Parallel_reads_of_one_point_share_one_PLC_read()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;
        var agv = await Id(h, "AGV_ID"); plc.SetWord(1600, 5); await Enter(admin, agv, "D1600");
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        plc.ResponseDelay = TimeSpan.FromMilliseconds(200);                                                // each PLC read now takes 200 ms: 12 un-shared reads would need > 2.4 s
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => ReadNow(admin, "AGV_ID")));
        sw.Stop();
        Assert.All(results, r => { Assert.Equal(200, r.Status); Assert.Equal(5, r.Root.GetProperty("data").GetProperty("value").GetInt32()); });
        Assert.True(sw.ElapsedMilliseconds < 1500, $"12 parallel reads took {sw.ElapsedMilliseconds} ms: they were not shared");
    }

    [Fact]
    public async Task A_failed_read_is_Bad_with_the_cause_and_a_lost_PLC_is_Offline_never_Good()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;
        var agv = await Id(h, "AGV_ID"); plc.SetWord(1600, 5); await Enter(admin, agv, "D1600");
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        Assert.True(await WaitFor(async () => (await Row(admin, "AGV_ID")).GetProperty("status").GetString() == "Good"));

        plc.ForceEndCode(0x4031, 100000);                                                                  // the PLC answers every request with an error end code
        var bad = await ReadNow(admin, "AGV_ID");
        Assert.True(bad.Status == 200 && !bad.Root.GetProperty("data").GetProperty("success").GetBoolean());
        Assert.Equal("READ_FAILED", bad.Root.GetProperty("data").GetProperty("code").GetString());
        Assert.Contains("4031", bad.Raw);
        Assert.True(await WaitFor(async () => (await Row(admin, "AGV_ID")).GetProperty("status").GetString() == "Bad"));
        plc.ForceEndCode(0, 0);
        Assert.True(await WaitFor(async () => (await Row(admin, "AGV_ID")).GetProperty("status").GetString() == "Good"));   // recovers by polling

        await plc.DisposeAsync();                                                                          // the PLC goes away
        Assert.True(await WaitFor(async () => (await Row(admin, "AGV_ID")).GetProperty("status").GetString() == "Offline", 10000));
        var offline = await ReadNow(admin, "AGV_ID");
        Assert.True(offline.Status == 200 && !offline.Root.GetProperty("data").GetProperty("success").GetBoolean());
        Assert.Contains(offline.Root.GetProperty("data").GetProperty("code").GetString(), new[] { "NOT_CONNECTED", "READ_FAILED" });
    }

    // ---------------------------------------------------------------- write + read-back, and the CR-019 guards reached from the monitoring screen
    [Fact]
    public async Task A_write_is_followed_by_a_read_back_from_the_PLC_and_the_CR019_guards_still_apply()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, op) = await BootAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;
        var agv = await Id(h, "AGV_ID"); await Enter(admin, agv, "D1600");
        var ack = await Id(h, "Request_Ack"); await Enter(admin, ack, "M101");
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        Task<(int Status, JsonElement Root, string Raw)> Write(HttpClient c, string s, object v) => c.SendJsonAsync(HttpMethod.Post, $"/api/plc/stations/A1/signals/{s}/test-write", new { value = v });

        Assert.Equal(403, (await Write(op, "AGV_ID", 5)).Status);                                          // Operator: no write
        Assert.Equal("WRITE_NOT_ALLOWED", (await Write(admin, "AGV_ID", 5)).Root.ErrorCode());             // not approved
        Assert.Equal(0, WriteFrames(plc));
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{agv}/write-approval", new { decision = "Approve" })).Status);
        Assert.Equal("VALUE_OUT_OF_RANGE", (await Write(admin, "AGV_ID", 40000)).Root.ErrorCode());        // validator still applies
        Assert.Equal(0, WriteFrames(plc));

        var ok = await Write(admin, "AGV_ID", 321);
        Assert.Equal(200, ok.Status);
        var rb = ok.Root.GetProperty("data").GetProperty("readBack");
        Assert.Equal("VERIFIED", rb.GetProperty("status").GetString()); Assert.Equal(321, rb.GetProperty("value").GetInt32());
        Assert.Equal((ushort)321, plc.GetWord(1600));
        Assert.Equal(1, WriteFrames(plc));                                                                 // the read-back is a read: the write was sent exactly once
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_TEST_WRITE" && a.Result == AuditResult.Success).ToListAsync());
        Assert.Contains(audit, a => (a.AfterJson ?? "").Contains("VERIFIED"));                              // the verification outcome is audited

        // a failed write: no read-back is claimed and nothing is resent
        plc.ForceEndCode(0x4031, 100000);
        var failed = await Write(admin, "AGV_ID", 7);
        plc.ForceEndCode(0, 0);
        Assert.Equal("WRITE_FAILED", failed.Root.ErrorCode());
        Assert.Equal(2, WriteFrames(plc));                                                                 // the failed write was sent once (answered with an end code) and never resent
        Assert.NotEqual((ushort)7, plc.GetWord(1600));
        Assert.Equal("WRITE_BLOCKED_SAFETY", (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{await Id(h, "Transfer_Permit")}/write-approval", new { decision = "Approve" })).Root.ErrorCode());
    }
}
