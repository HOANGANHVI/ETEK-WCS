using System.Text.Json;
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

/// <summary>CR-014: write approval per IO point (independent of reading, pollability and the handshake), strict typed values, and the guarded write path.</summary>
public class WriteValueValidatorTests
{
    private static WriteValueValidator.Verdict Check(PlcDataType t, PointValue v, int? words = null, string? enc = null) => WriteValueValidator.Check(t, words, enc, v);

    [Fact]
    public void BOOL_accepts_only_a_boolean_and_nothing_is_coerced()
    {
        Assert.True(Check(PlcDataType.Bool, PointValue.OfBool(true)).Ok);
        Assert.Equal("TYPE_MISMATCH", Check(PlcDataType.Bool, PointValue.OfInt(1)).Code);
        Assert.Equal("TYPE_MISMATCH", Check(PlcDataType.Bool, PointValue.OfString("true")).Code);
    }

    [Theory]
    [InlineData(-32768, true)] [InlineData(32767, true)] [InlineData(0, true)]
    [InlineData(32768, false)] [InlineData(-32769, false)] [InlineData(70000, false)]
    public void INT_is_a_signed_16_bit_word(int value, bool ok)
    {
        var v = Check(PlcDataType.Int, PointValue.OfInt(value));
        Assert.Equal(ok, v.Ok);
        if (!ok) Assert.Equal("VALUE_OUT_OF_RANGE", v.Code);
        Assert.Equal("TYPE_MISMATCH", Check(PlcDataType.Int, PointValue.OfBool(true)).Code);
    }

    [Fact]
    public void STRING_needs_ASCII_a_known_length_and_must_fit_the_registers()
    {
        Assert.True(Check(PlcDataType.String, PointValue.OfString("ABCDEFGH"), 4, "ASCII").Ok);                      // 4 words = 8 characters exactly
        Assert.Equal("STRING_TOO_LONG", Check(PlcDataType.String, PointValue.OfString("ABCDEFGHI"), 4, "ASCII").Code);
        Assert.Equal("STRING_NOT_ASCII", Check(PlcDataType.String, PointValue.OfString("Rack Đầy"), 8, "ASCII").Code);
        Assert.Equal("STRING_LENGTH_UNKNOWN", Check(PlcDataType.String, PointValue.OfString("A"), null, "ASCII").Code);
        Assert.Equal("WRITE_TYPE_UNSUPPORTED", Check(PlcDataType.String, PointValue.OfString("A"), 4, "UTF-16").Code);   // an encoding the driver cannot write
        Assert.Equal("WRITE_TYPE_UNSUPPORTED", Check(PlcDataType.String, PointValue.OfString("A"), 4, null).Code);
        Assert.False(WriteValueValidator.IsWriteSupported(PlcDataType.String, "UTF-8"));
        Assert.True(WriteValueValidator.IsWriteSupported(PlcDataType.Bool, null) && WriteValueValidator.IsWriteSupported(PlcDataType.Int, null));
    }

    [Fact]
    public void The_domain_keeps_read_and_write_approval_apart_and_an_address_change_withdraws_the_write_decision()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new StationIoPoint("PLC-01", "A1", 15, "Command", "Request_Ack", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, "M101", "", now);
        Assert.Equal((WriteApprovalStatus.NotApproved, false), (p.WriteApproval, p.WriteAllowed));
        Assert.Equal("IO_NO_ADDRESS", Assert.Throws<WCS.Domain.Common.DomainException>(() => p.ApproveWrite("admin", now)).Code);       // no address yet
        p.ConfigureAddress("M200", null, null, now);
        Assert.True(p.IsPollable); Assert.False(p.WriteAllowed);                                                                        // pollable: no write
        p.Approve("admin", now);                                                                                                        // read approval
        Assert.Equal((WriteApprovalStatus.NotApproved, false), (p.WriteApproval, p.WriteAllowed));                                       // ...is not a write approval
        p.ApproveWrite("engineer", now);
        Assert.Equal((WriteApprovalStatus.Approved, true, "engineer"), (p.WriteApproval, p.WriteAllowed, p.WriteDecidedBy));
        Assert.Equal(IoConfigStatus.Approved, p.Status);                                                                                // the read approval is untouched
        p.RejectWrite("admin", "wrong register", now);
        Assert.Equal((WriteApprovalStatus.Rejected, false, "wrong register"), (p.WriteApproval, p.WriteAllowed, p.WriteDecisionNote));
        p.ApproveWrite("admin", now); p.ConfigureAddress("M201", null, null, now);                                                      // another address: another decision
        Assert.Equal((WriteApprovalStatus.NotApproved, false, null), (p.WriteApproval, p.WriteAllowed, p.WriteDecidedBy));
        var inbound = new StationIoPoint("PLC-01", "A1", 2, "Request", "Pickup_Request", PlcSignalDirection.PlcToSoftware, PlcDataType.Bool, "M100", "", now);
        inbound.ConfigureAddress("M300", null, null, now);
        Assert.Equal("IO_NOT_WRITABLE", Assert.Throws<WCS.Domain.Common.DomainException>(() => inbound.ApproveWrite("admin", now)).Code);   // a PLC -> Software signal is never written
    }

    [Fact]
    public void WritePolicy_still_needs_the_write_approval_in_the_real_read_write_mode()
    {
        var all = new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true, Write = new PlcWriteOptions { Enabled = true } };
        var point = new IoPointRef(Guid.NewGuid(), "PLC-01", "A1", "Request_Ack", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, "M101", null, null, WriteAllowed: false, "Command", Pollable: true);
        Assert.Equal("WRITE_NOT_ALLOWED", WritePolicy.Evaluate(all, point).Code);                                         // pollable is not enough
        Assert.Equal("WRITE_NOT_DECLARED", WritePolicy.Evaluate(all, point with { WriteAllowed = true }).Code);              // CR-044: nor is the Admin approval
        all.Declare(point);
        Assert.True(WritePolicy.Evaluate(all, point with { WriteAllowed = true }).Allowed);
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadOnly, AllowLiveConnection = true }, point with { WriteAllowed = true }).Code);   // the mode still wins
    }
}

[Collection("Database")]
public class PlcWriteApprovalTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);

    private async Task<(Harness H, WebApplicationFactory<Program> F, HttpClient Admin)> BootAsync(Dictionary<string, string?>? plc = null)
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
        var cfg = new Dictionary<string, string?> { ["Plc:SimulatorPollingIntervalMs"] = "40", ["Plc:ReconnectInitialDelayMs"] = "50", ["Plc:ReconnectMaxDelayMs"] = "200" };
        foreach (var kv in plc ?? []) cfg[kv.Key] = kv.Value;
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)));
        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        return (h, f, await As(f, "admin1"));
    }

    private static async Task<HttpClient> As(WebApplicationFactory<Program> f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static Task<Guid> Id(Harness h, string station, string signal) =>
        h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == "PLC-01" && p.StationId == station && p.SignalName == signal).Select(p => p.Id).SingleAsync());

    private static Task<StationIoPoint> Row(Harness h, Guid id) => h.Db(db => db.StationIoPoints.AsNoTracking().SingleAsync(p => p.Id == id));

    private static Task<(int Status, JsonElement Root, string Raw)> Decide(HttpClient c, Guid id, string decision, string? note = null) =>
        c.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{id}/write-approval", new { decision, note });

    private static async Task<bool> WaitFor(Func<Task<bool>> cond, int ms = 6000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (await cond()) return true; await Task.Delay(30); }
        return await cond();
    }

    private static async Task<string?> State(HttpClient c, string plcId) =>
        (await c.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Root.GetProperty("data").GetProperty("plcs").EnumerateArray().First(p => p.GetProperty("plcId").GetString() == plcId).GetProperty("state").GetString();

    // ---------------------------------------------------------------- approval
    [Fact]
    public async Task Admin_approves_and_rejects_write_per_point_independently_of_reading_and_every_decision_is_audited()
    {
        var (h, f, admin) = await BootAsync(); await using var _h = h; using var _f = f;
        var ack = await Id(h, "A1", "Request_Ack");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M200" })).Status);

        var before = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/points?plcId=PLC-01&stationId=A1")).Root.GetProperty("data").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == ack);
        Assert.Equal("NOT_APPROVED", before.GetProperty("writeApproval").GetString());                                  // pollable (below) yet awaiting a write decision
        Assert.True(before.GetProperty("pollable").GetBoolean()); Assert.False(before.GetProperty("writeAllowed").GetBoolean());
        Assert.True(before.GetProperty("writeSupported").GetBoolean());

        var approved = await Decide(admin, ack, "Approve");                                                              // 1
        Assert.Equal(200, approved.Status);
        var d = approved.Root.GetProperty("data");
        Assert.Equal(("APPROVED", true, "admin1", "CONFIGURED"), (d.GetProperty("writeApproval").GetString(), d.GetProperty("writeAllowed").GetBoolean(), d.GetProperty("writeDecidedBy").GetString(), d.GetProperty("configStatus").GetString()));
        var row = await Row(h, ack);
        Assert.Equal((WriteApprovalStatus.Approved, true, "admin1", IoConfigStatus.Configured), (row.WriteApproval, row.WriteAllowed, row.WriteDecidedBy, row.Status));   // stored; the read side is untouched
        Assert.NotNull(row.WriteDecidedAt);

        var rejected = await Decide(admin, ack, "reject", "wrong register");                                              // withdrawing
        Assert.Equal(200, rejected.Status);
        row = await Row(h, ack);
        Assert.Equal((WriteApprovalStatus.Rejected, false, "wrong register"), (row.WriteApproval, row.WriteAllowed, row.WriteDecisionNote));

        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action.StartsWith("PLC_IO_WRITE_")).OrderBy(a => a.OccurredAt).ToListAsync());     // 10
        Assert.Equal(["PLC_IO_WRITE_APPROVE", "PLC_IO_WRITE_REJECT"], audit.Select(a => a.Action));
        Assert.All(audit, a => { Assert.Equal(("admin1", AuditResult.Success, ack.ToString()), (a.Actor, a.Result, a.TargetId)); Assert.True(a.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-5)); });
        Assert.Contains("M200", audit[0].AfterJson ?? ""); Assert.Contains("Request_Ack", audit[0].AfterJson ?? "");

        // a new address is a new decision: the approval is withdrawn
        Assert.Equal(200, (await Decide(admin, ack, "Approve")).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M201" })).Status);
        row = await Row(h, ack);
        Assert.Equal((WriteApprovalStatus.NotApproved, false, null), (row.WriteApproval, row.WriteAllowed, row.WriteDecidedBy));
    }

    [Fact]
    public async Task Operators_and_anonymous_callers_cannot_approve_and_the_wrong_points_are_refused()
    {
        var (h, f, admin) = await BootAsync(); await using var _h = h; using var _f = f;
        var ack = await Id(h, "A1", "Request_Ack");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M200" });
        var op = await As(f, "op1");
        Assert.Equal(403, (await Decide(op, ack, "Approve")).Status);                                                    // 2
        Assert.Equal(401, (await Decide(f.CreateClient().WithClientHeader(), ack, "Approve")).Status);
        Assert.Equal(WriteApprovalStatus.NotApproved, (await Row(h, ack)).WriteApproval);

        Assert.Equal(400, (await Decide(admin, ack, "Maybe")).Status);                                                   // not a decision
        var noAddress = await Decide(admin, await Id(h, "A1", "AGV_Assigned"), "Approve");
        Assert.Equal(("IO_NO_ADDRESS", 422), (noAddress.Root.ErrorCode(), noAddress.Status));
        var inbound = await Id(h, "A1", "Pickup_Request");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{inbound}/address", new { address = "M100" });
        Assert.Equal("IO_NOT_WRITABLE", (await Decide(admin, inbound, "Approve")).Root.ErrorCode());                       // PLC -> Software is never written
        var permit = await Id(h, "A1", "Transfer_Permit");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{permit}/address", new { address = "M102" });
        Assert.Equal("WRITE_BLOCKED_SAFETY", (await Decide(admin, permit, "Approve")).Root.ErrorCode());                    // control signals stay out of reach
        Assert.Equal(0, await h.Db(db => db.StationIoPoints.CountAsync(p => p.WriteAllowed)));
        Assert.Contains(await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_IO_WRITE_APPROVE").Select(a => a.Result).ToListAsync()), r => r != AuditResult.Success);   // refusals are audited too
    }

    [Fact]
    public async Task Approving_a_write_changes_neither_the_handshake_nor_the_read_side_nor_the_connection()
    {
        var (h, f, admin) = await BootAsync(); await using var _h = h; using var _f = f;
        var ack = await Id(h, "A1", "Request_Ack");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M200" });
        var connBefore = await h.Db(db => db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Enabled, c.IpAddress, c.Port, c.Protocol }).ToListAsync());
        Assert.Equal(200, (await Decide(admin, ack, "Approve")).Status);
        var hs = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/handshake")).Root.GetProperty("data");              // 11
        Assert.True(hs.GetArrayLength() > 0);
        Assert.All(hs.EnumerateArray(), s => { Assert.Equal("NEEDS_CONFIRMATION", s.GetProperty("approval").GetString()); Assert.Equal(0, s.GetProperty("approvedRules").GetInt32()); });
        Assert.Equal(connBefore, await h.Db(db => db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Enabled, c.IpAddress, c.Port, c.Protocol }).ToListAsync()));
        Assert.Equal(0, await h.Db(db => db.StationIoPoints.CountAsync(p => p.Status == IoConfigStatus.Approved)));       // no read approval was created
        Assert.Equal(0, await h.Db(db => db.PlcCommLog.CountAsync(l => l.Event == "WRITE" || l.Event == "WRITE_FAILED" || l.Event == "WRITE_AMBIGUOUS")));
    }

    // ---------------------------------------------------------------- the write itself (REAL_READ_WRITE against the MC mock)
    [Fact]
    public async Task A_write_needs_the_write_approval_a_value_of_the_right_type_and_reports_what_the_PLC_answered()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin) = await BootAsync(new Dictionary<string, string?>
        {
            ["Plc:Mode"] = "RealReadWrite", ["Plc:AllowLiveConnection"] = "true", ["Plc:Write:Enabled"] = "true",
        }.Declare(("PLC-01", "A1", "Request_Ack", "M101"), ("PLC-01", "A1", "AGV_ID", "D1700"), ("PLC-01", "A1", "Heartbeat_SW", "M103")));
        await using var _h = h; using var _f = f;
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new
        {
            ipAddress = "127.0.0.1", port = plc.Port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0,
            monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true,
        })).Status);
        var ack = await Id(h, "A1", "Request_Ack"); var agv = await Id(h, "A1", "AGV_ID"); var beat = await Id(h, "A1", "Heartbeat_SW");
        foreach (var (id, a) in new[] { (ack, "M101"), (agv, "D1700"), (beat, "M103") })
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = a })).Status);
        Assert.True(await WaitFor(async () => await State(admin, "PLC-01") == "Connected"));
        int Writes() => plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Count(r => r.Command == McFrame.CommandBatchWrite);
        Task<(int Status, JsonElement Root, string Raw)> TestWrite(string signal, object value) =>
            admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/stations/A1/signals/{signal}/test-write", new { value });

        // 3, 9: pollable but not write-approved -> refused before anything reaches the PLC
        var refused = await TestWrite("Request_Ack", true);
        Assert.Equal(("WRITE_NOT_ALLOWED", 422), (refused.Root.ErrorCode(), refused.Status));
        Assert.False(plc.GetBit(101)); Assert.Equal(0, Writes());

        Assert.Equal(200, (await Decide(admin, ack, "Approve")).Status);
        Assert.Equal(200, (await Decide(admin, agv, "Approve")).Status);
        // 5-8: wrong shapes are refused by the API, still nothing is sent
        Assert.Equal("VALUE_INVALID", (await TestWrite("Request_Ack", 1)).Root.ErrorCode());                              // BOOL: true/false only, not 1
        Assert.Equal("VALUE_INVALID", (await TestWrite("Request_Ack", "true")).Root.ErrorCode());
        Assert.Equal("VALUE_INVALID", (await TestWrite("AGV_ID", "7")).Root.ErrorCode());                                 // INT: no text
        Assert.Equal("VALUE_INVALID", (await TestWrite("AGV_ID", 1.5)).Root.ErrorCode());                                 // ...no fraction
        Assert.Equal("VALUE_OUT_OF_RANGE", (await TestWrite("AGV_ID", 70000)).Root.ErrorCode());                           // 6: 16-bit range
        Assert.Equal("VALUE_OUT_OF_RANGE", (await TestWrite("AGV_ID", -32769)).Root.ErrorCode());
        Assert.Equal(0, Writes());

        // 12: approved + valid -> written, and the result is what the PLC really holds
        Assert.Equal(200, (await TestWrite("Request_Ack", true)).Status);
        Assert.True(plc.GetBit(101));
        Assert.Equal(200, (await TestWrite("AGV_ID", 1234)).Status);
        Assert.Equal((ushort)1234, plc.GetWord(1700));
        Assert.Equal(200, (await TestWrite("AGV_ID", -5)).Status);
        Assert.Equal(unchecked((ushort)-5), plc.GetWord(1700));
        Assert.Equal(3, Writes());

        // not approved for writing -> still refused although the approved ones work
        Assert.Equal("WRITE_NOT_ALLOWED", (await TestWrite("Heartbeat_SW", true)).Root.ErrorCode());
        Assert.False(plc.GetBit(103));
        // a rejected point is refused again
        Assert.Equal(200, (await Decide(admin, ack, "Reject")).Status);
        Assert.Equal("WRITE_NOT_ALLOWED", (await TestWrite("Request_Ack", false)).Root.ErrorCode());
        Assert.True(plc.GetBit(101));                                                                                      // unchanged: nothing was sent
        Assert.Equal(3, Writes());

        // the PLC answers with an error end code -> reported as a failure, not as a success
        plc.ForceEndCode(0x4031, 1);
        var failed = await TestWrite("AGV_ID", 7);
        Assert.NotEqual(200, failed.Status); Assert.StartsWith("WRITE_", failed.Root.ErrorCode());
        Assert.NotEqual((ushort)7, plc.GetWord(1700));
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action == "PLC_TEST_WRITE").ToListAsync());
        Assert.Contains(audit, a => a.Result == AuditResult.Success); Assert.Contains(audit, a => a.Result != AuditResult.Success);
    }
}
