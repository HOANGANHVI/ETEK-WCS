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
/// CR-015: the PLC write is not blocked by the environment any more (Production included). It is guarded by Admin role, per-point write approval, value validation, WritePolicy
/// (REAL_READ_WRITE + every write key) and the MC driver - and nothing is ever written on its own. Everything runs against the MC mock server, never a real PLC.
/// </summary>
[Collection("Database")]
public class PlcWriteEnablementTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);

    private static Dictionary<string, string?> AllKeys => new()
    {
        ["Plc:Mode"] = "RealReadWrite", ["Plc:AllowLiveConnection"] = "true", ["Plc:Write:Enabled"] = "true",
        ["Plc:Write:EngineerApprovalReference"] = "TEST-APPROVAL-001", ["Plc:Write:HandshakeApproved"] = "true",
    };

    /// <summary>A PRODUCTION host (secure cookies, no development shortcuts) on the given configuration, with the PLC runtime started and PLC-01 pointed at the mock.</summary>
    private async Task<(Harness H, WebApplicationFactory<Program> F, HttpClient Admin, HttpClient Op)> BootProductionAsync(MelsecMcMockServer plc, Dictionary<string, string?> cfg)
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

    private static Task<(int Status, JsonElement Root, string Raw)> Approve(HttpClient c, Guid id) =>
        c.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{id}/write-approval", new { decision = "Approve" });

    private static Task<(int Status, JsonElement Root, string Raw)> Write(HttpClient c, string signal, object value) =>
        c.SendJsonAsync(HttpMethod.Post, $"/api/plc/stations/A1/signals/{signal}/test-write", new { value });

    private static async Task<bool> WaitFor(Func<Task<bool>> cond, int ms = 6000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (await cond()) return true; await Task.Delay(30); }
        return await cond();
    }

    private static async Task<string?> State(HttpClient c) =>
        (await c.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("plcs").EnumerateArray().First(p => p.GetProperty("plcId").GetString() == "PLC-01").GetProperty("state").GetString();

    private static int Writes(MelsecMcMockServer plc) => plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Count(r => r.Command == McFrame.CommandBatchWrite);

    // ---------------------------------------------------------------- Production + RealReadWrite
    [Fact]
    public async Task In_Production_with_RealReadWrite_and_every_key_an_approved_point_is_written_by_an_Admin_only_and_every_step_is_recorded()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, op) = await BootProductionAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;   // (a recorder of an earlier test may still flush late rows into the shared database: only rows of THIS run count)
        var ack = await Id(h, "Request_Ack"); var agv = await Id(h, "AGV_ID"); var beat = await Id(h, "Heartbeat_SW");
        await Enter(admin, ack, "M101"); await Enter(admin, agv, "D1600"); await Enter(admin, beat, "M103");
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        Assert.Equal("RealReadWrite", (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime").GetProperty("effectiveMode").GetString());

        // nothing is written on its own: not when the connection comes up, not while the screens poll
        await Task.Delay(500);
        Assert.Equal(0, Writes(plc));

        // the environment is no longer a reason to refuse; the guards are: role, approval, value, policy
        Assert.Equal(403, (await Write(op, "Request_Ack", true)).Status);                                                   // an Operator cannot write...
        Assert.Equal(403, (await Approve(op, ack)).Status);                                                                 // ...nor approve
        var notApproved = await Write(admin, "Request_Ack", true);
        Assert.Equal(("WRITE_NOT_ALLOWED", 422), (notApproved.Root.ErrorCode(), notApproved.Status));                      // pollable, connected, but no write approval
        Assert.Equal(0, Writes(plc));

        Assert.Equal(200, (await Approve(admin, ack)).Status); Assert.Equal(200, (await Approve(admin, agv)).Status);
        Assert.Equal("VALUE_INVALID", (await Write(admin, "Request_Ack", "on")).Root.ErrorCode());                          // wrong data type
        Assert.Equal("VALUE_OUT_OF_RANGE", (await Write(admin, "AGV_ID", 40000)).Root.ErrorCode());
        Assert.Equal(0, Writes(plc));

        var ok = await Write(admin, "Request_Ack", true);
        Assert.Equal(200, ok.Status);
        Assert.True(plc.GetBit(101));                                                                                       // the (mock) PLC really holds it
        Assert.Equal(200, (await Write(admin, "AGV_ID", 321)).Status);
        Assert.Equal((ushort)321, plc.GetWord(1600));
        Assert.Equal("WRITE_NOT_ALLOWED", (await Write(admin, "Heartbeat_SW", true)).Root.ErrorCode());                      // the unapproved sibling stays out
        Assert.False(plc.GetBit(103));
        Assert.True(Writes(plc) == 2, "write frames: " + string.Join(" | ", plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Where(r => r.Command == McFrame.CommandBatchWrite).Select(r => $"{r.Head} x{r.Points}")));

        // audit: who, what, result
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_TEST_WRITE").OrderBy(a => a.OccurredAt).ToListAsync());
        Assert.Contains(audit, a => a is { Actor: "admin1", Result: AuditResult.Success } && (a.AfterJson ?? "").Contains("Request_Ack"));
        Assert.Contains(audit, a => a is { Actor: "admin1", Result: AuditResult.Success } && (a.AfterJson ?? "").Contains("321"));
        Assert.Contains(audit, a => a.Actor == "admin1" && a.Result != AuditResult.Success && (a.Detail ?? "").Contains("WRITE_NOT_ALLOWED"));

        // diagnostics: the MC request/response of the writes is kept
        var rec = f.Services.GetRequiredService<PlcExchangeRecorder>(); await rec.FlushAsync(8000);
        var rows = await h.Db(db => db.PlcExchangeLog.AsNoTracking().Where(x => x.PlcId == "PLC-01" && x.Operation == "WRITE").ToListAsync());
        Assert.Equal("M101;D1600", string.Join(";", rows.OrderBy(r => r.OccurredAt).Select(r => $"{r.Device}{r.StartAddress}")));   // exactly the two approved writes
        Assert.All(rows, r => { Assert.Equal("OK", r.Outcome); Assert.False(string.IsNullOrWhiteSpace(r.RequestHex)); Assert.False(string.IsNullOrWhiteSpace(r.ResponseHex)); });
        Assert.Contains(await h.Db(db => db.PlcCommLog.AsNoTracking().Where(l => l.Event == "WRITE").Select(l => l.Message).ToListAsync()), m => m.Contains("Request_Ack"));
    }

    [Fact]
    public async Task In_Production_a_runtime_without_every_write_key_stays_read_only_and_the_refusal_is_the_policys_not_the_environments()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var noKeys = new Dictionary<string, string?> { ["Plc:Mode"] = "RealReadWrite", ["Plc:AllowLiveConnection"] = "true" };           // keys left at their defaults
        var (h, f, admin, _) = await BootProductionAsync(plc, noKeys);
        await using var _h = h; using var _f = f;
        var ack = await Id(h, "Request_Ack");
        await Enter(admin, ack, "M101"); Assert.Equal(200, (await Approve(admin, ack)).Status);
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        Assert.Equal("RealReadOnly", (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("runtime").GetProperty("effectiveMode").GetString());

        var r = await Write(admin, "Request_Ack", true);
        Assert.Equal(("READ_ONLY", 422), (r.Root.ErrorCode(), r.Status));
        Assert.NotEqual("NOT_AVAILABLE_IN_PRODUCTION", r.Root.ErrorCode());
        Assert.False(plc.GetBit(101)); Assert.Equal(0, Writes(plc));
    }

    // ---------------------------------------------------------------- what the PLC answers
    [Fact]
    public async Task A_write_is_a_success_only_when_the_PLC_confirms_it_an_end_code_a_silent_PLC_a_dropped_link_and_an_offline_PLC_are_reported_as_what_they_are()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootProductionAsync(plc, AllKeys);
        await using var _h = h; using var _f = f;
        var agv = await Id(h, "AGV_ID");
        await Enter(admin, agv, "D1600"); Assert.Equal(200, (await Approve(admin, agv)).Status);
        Assert.True(await WaitFor(async () => await State(admin) == "Connected"));
        async Task Reconnected() => Assert.True(await WaitFor(async () => await State(admin) == "Connected", 10000));

        // the PLC answers with an error end code: a failure carrying that end code, and the value did not change
        plc.ForceEndCode(0x4031, 100000);                                                                                   // (every request until reset, so the write is certainly one of them)
        var endCode = await Write(admin, "AGV_ID", 7);
        plc.ForceEndCode(0, 0);
        Assert.Equal(("WRITE_FAILED", 422), (endCode.Root.ErrorCode(), endCode.Status));
        Assert.Contains("4031", endCode.Raw);
        Assert.NotEqual((ushort)7, plc.GetWord(1600));

        // a PLC that does not answer in time: the write may have been executed -> ambiguous (never "success", never retried)
        var before = Writes(plc);
        plc.ResponseDelay = TimeSpan.FromSeconds(2);
        var silent = await Write(admin, "AGV_ID", 8);
        Assert.Equal("WRITE_AMBIGUOUS", silent.Root.ErrorCode()); Assert.NotEqual(200, silent.Status);
        plc.ResponseDelay = TimeSpan.Zero;
        await Reconnected();
        Assert.Equal(before + 1, Writes(plc));                                                                              // sent once, not resent

        // the link is cut while the write is on the wire: ambiguous as well
        // (the mock drops the NEXT request, which may be a poll: try until the write itself is the one that is cut; any non-200 must be the ambiguous kind)
        string? droppedCode = null;
        for (var i = 0; i < 12 && droppedCode is null; i++)
        {
            await Reconnected();
            plc.DropNextRequests(1);
            var dropped = await Write(admin, "AGV_ID", 9);
            if (dropped.Status != 200) droppedCode = dropped.Root.ErrorCode(); else plc.DropNextRequests(0);
        }
        Assert.Equal("WRITE_AMBIGUOUS", droppedCode);
        await Reconnected();

        // after recovery a normal write is confirmed again
        Assert.Equal(200, (await Write(admin, "AGV_ID", 10)).Status);
        Assert.Equal((ushort)10, plc.GetWord(1600));

        // the PLC goes away: never reported as written or connected
        await plc.DisposeAsync();
        Assert.True(await WaitFor(async () => await State(admin) != "Connected", 10000));
        var offline = await Write(admin, "AGV_ID", 11);
        Assert.NotEqual(200, offline.Status);
        Assert.Contains(offline.Root.ErrorCode(), new[] { "NOT_CONNECTED", "WRITE_AMBIGUOUS", "WRITE_FAILED" });

        // every outcome is in the audit log and in the PLC log; the MC exchanges (request, response or the error) are kept for diagnosis
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_TEST_WRITE").ToListAsync());
        Assert.True(audit.Count(a => a.Result == AuditResult.Success) >= 1);
        Assert.True(audit.Count(a => a.Result != AuditResult.Success) >= 4);
        Assert.Contains(audit, a => (a.Detail ?? "").Contains("WRITE_AMBIGUOUS"));
        Assert.Contains(audit, a => (a.Detail ?? "").Contains("WRITE_FAILED") && (a.Detail ?? "").Contains("4031"));
        var rec = f.Services.GetRequiredService<PlcExchangeRecorder>(); await rec.FlushAsync(8000);
        var rows = await h.Db(db => db.PlcExchangeLog.AsNoTracking().Where(x => x.Operation == "WRITE").ToListAsync());
        Assert.Contains(rows, r => r.Outcome == "END_CODE" && r.EndCode == 0x4031);
        Assert.Contains(rows, r => r.Outcome == "AMBIGUOUS");   // a write that may have been executed has its own outcome, distinct from OK and from a refusal
        Assert.Contains(await h.Db(db => db.PlcCommLog.AsNoTracking().Select(l => l.Event).ToListAsync()), e => e == "WRITE_AMBIGUOUS");
    }

    // ---------------------------------------------------------------- deployment defaults
    [Fact]
    public void The_deployment_makes_RealReadWrite_the_supported_mode_and_leaves_every_attestation_to_the_operator()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deployment", "docker-compose.yml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var compose = File.ReadAllText(Path.Combine(dir!.FullName, "deployment", "docker-compose.yml"));
        Assert.Contains("Plc__Mode: ${PLC_MODE:-RealReadWrite}", compose);                                                  // read-only is no longer forced
        Assert.Contains("Plc__AllowLiveConnection: ${PLC_ALLOW_LIVE_CONNECTION:-false}", compose);                          // no socket to a real PLC until the operator says so
        Assert.Contains("Plc__Write__Enabled: ${PLC_WRITE_ENABLED:-false}", compose);
        Assert.Contains("Plc__Write__EngineerApprovalReference: ${PLC_WRITE_APPROVAL_REFERENCE:-}", compose);               // never pre-filled
        Assert.Contains("Plc__Write__HandshakeApproved: ${PLC_WRITE_HANDSHAKE_APPROVED:-false}", compose);
        Assert.DoesNotContain("PLC_MODE:-RealReadOnly", compose);
    }
}
