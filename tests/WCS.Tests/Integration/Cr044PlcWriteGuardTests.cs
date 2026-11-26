using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

/// <summary>CR-044 (OW-2): deny-by-default of real PLC writes - declaration, reserved registers, worker lock and audit. Policy only: nothing is connected.</summary>
public class PlcWriteGuardPolicyTests
{
    private static IConfigurationRoot Shipped => new ConfigurationBuilder().AddJsonFile("api.appsettings.json").Build();

    /// <summary>Real read/write with every key and the reserved registers exactly as the application ships them.</summary>
    private static PlcRuntimeOptions Real()
    {
        var o = Shipped.GetSection("Plc").Get<PlcRuntimeOptions>()!;
        o.Mode = PlcRuntimeMode.RealReadWrite; o.AllowLiveConnection = true; o.Write.Enabled = true;
        return o;
    }

    private static IoPointRef Point(string address, string signal = "Request_Ack", PlcDataType type = PlcDataType.Int, int? words = null, bool approved = true) =>
        new(Guid.NewGuid(), "PLC-01", "A1", signal, PlcSignalDirection.SoftwareToPlc, type, address, words, type == PlcDataType.String ? "ASCII" : null, approved, "Command");

    [Fact]
    public void The_shipped_configuration_declares_nothing_so_a_real_write_is_refused()
    {
        var o = Real();
        Assert.Empty(o.Write.AllowList);
        Assert.False(new PlcRuntimeOptions().Write.Enabled); Assert.Empty(new PlcRuntimeOptions().Write.AllowList);
        Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, Point("D100")).Code);   // real mode + keys + Admin approval is not a permission
    }

    [Fact]
    public void A_declaration_must_match_the_PLC_the_station_the_signal_and_the_address()
    {
        var o = Real(); var p = Point("D100");
        void Only(string plc, string station, string signal, string address) => o.Write.AllowList = [new() { PlcId = plc, StationId = station, Signal = signal, Address = address }];
        foreach (var (plc, station, signal, address) in new[] { ("PLC-02", "A1", "Request_Ack", "D100"), ("PLC-01", "A2", "Request_Ack", "D100"), ("PLC-01", "A1", "AGV_ID", "D100"), ("PLC-01", "A1", "Request_Ack", "D101"),
            ("", "A1", "Request_Ack", "D100"), ("PLC-01", "A1", "Request_Ack", ""), ("*", "*", "*", "*") })
        { Only(plc, station, signal, address); Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, p).Code); }
        Only("plc-01", "a1", "request_ack", "d0100"); Assert.True(WritePolicy.Evaluate(o, p).Allowed);
        Assert.Equal("WRITE_NOT_ALLOWED", WritePolicy.Evaluate(o, p with { WriteAllowed = false }).Code);   // a declaration does not replace the Admin approval
        Assert.Equal("ADDRESS_NOT_APPROVED", WritePolicy.Evaluate(o, p with { Pollable = false }).Code);
    }

    [Fact]
    public void A_STRING_is_declared_with_its_length()
    {
        var o = Real(); var p = Point("D100", type: PlcDataType.String, words: 4);
        o.Declare(p); Assert.True(WritePolicy.Evaluate(o, p).Allowed);
        Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, p with { WordLength = 8 }).Code);
        o.Write.AllowList[0].Words = null; Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, p).Code);
    }

    public static TheoryData<string> ReservedRegisters()
    {
        var data = new TheoryData<string>();
        foreach (var station in new[] { 0, 10, 20, 30 }) foreach (var a in new[] { 1500, 1501, 1502, 1600, 1601 }) data.Add($"D{a + station}");
        for (var m = 9200; m <= 9205; m++) data.Add($"M{m}");
        return data;
    }

    [Theory]
    [MemberData(nameof(ReservedRegisters))]
    public void A_register_of_the_unconfirmed_interface_is_refused_even_when_declared(string address)
    {
        var o = Real(); var p = Point(address, type: address[0] == 'M' ? PlcDataType.Bool : PlcDataType.Int);
        o.Declare(p);
        Assert.Equal(PlcWriteGuard.Reserved, WritePolicy.Evaluate(o, p).Code);
        Assert.Equal(PlcWriteGuard.Reserved, WritePolicy.Evaluate(o, p, forHandshake: true).Code);
    }

    [Theory]
    [InlineData("AGV_ID", "D1600")] [InlineData("Error_Code", "D1610")] [InlineData("Request_Ack", "D1500")]
    public void A_legacy_signal_entered_at_its_template_address_cannot_reach_the_new_registers(string signal, string address)
    {
        var o = Real(); var p = Point(address, signal);
        o.Declare(p);
        Assert.Equal(PlcWriteGuard.Reserved, WritePolicy.Evaluate(o, p).Code);
    }

    [Fact]
    public void A_write_that_would_reach_into_a_reserved_register_is_refused_and_its_neighbours_are_not()
    {
        var o = Real();
        var spanning = Point("D1598", type: PlcDataType.String, words: 4); o.Declare(spanning);
        Assert.Equal(PlcWriteGuard.Reserved, WritePolicy.Evaluate(o, spanning).Code);
        foreach (var p in new[] { Point("D1503"), Point("D1599"), Point("D1602"), Point("M9199", type: PlcDataType.Bool), Point("M9206", type: PlcDataType.Bool) })
        { o.Declare(p); Assert.True(WritePolicy.Evaluate(o, p).Allowed, p.Address); }
    }

    [Theory]
    [InlineData("D1600-M1601")] [InlineData("D1601-D1600")] [InlineData("W10")] [InlineData("all")] [InlineData("")]
    public void An_unreadable_reserved_list_stops_every_real_write(string entry)
    {
        var o = Real(); var p = Point("D100"); o.Declare(p);
        o.Write.ReservedAddresses.Add(entry);
        Assert.Equal(PlcWriteGuard.ReservedConfigInvalid, WritePolicy.Evaluate(o, p).Code);
    }

    [Fact]
    public void The_simulator_is_not_affected_and_read_only_stays_read_only()
    {
        var p = Point("D1600", "AGV_ID", approved: false);
        var sim = Shipped.GetSection("Plc").Get<PlcRuntimeOptions>()!;
        Assert.Equal(PlcRuntimeMode.Simulator, sim.Mode);
        Assert.True(WritePolicy.Evaluate(sim, p).Allowed);
        var ro = Real(); ro.Mode = PlcRuntimeMode.RealReadOnly; ro.Declare(Point("D100"));
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(ro, Point("D100")).Code);
    }
}

/// <summary>CR-044 against the MC mock server and a real database: nothing reaches the PLC without a declaration, and every decision is audited.</summary>
[Collection("Database")]
public class Cr044PlcWriteGuardTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);

    private static Dictionary<string, string?> Keys(string mode = "RealReadWrite") => new()
    {
        ["Plc:Mode"] = mode, ["Plc:AllowLiveConnection"] = "true", ["Plc:Write:Enabled"] = "true", ["Plc:ReconnectInitialDelayMs"] = "50", ["Plc:ReconnectMaxDelayMs"] = "200", ["Plc:SimulatorPollingIntervalMs"] = "40",
    };

    private sealed class BrokenAudit : IPlcWriteAudit
    {
        public Task RecordAsync(PlcWriteAuditRecord record, CancellationToken ct = default) => throw new InvalidOperationException("audit store unavailable");
    }

    private async Task<(Harness H, WebApplicationFactory<Program> F, HttpClient Admin, HttpClient Op)> BootAsync(MelsecMcMockServer plc, Dictionary<string, string?> cfg, bool brokenAudit = false)
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
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg));
            if (brokenAudit) b.ConfigureTestServices(s => s.AddSingleton<IPlcWriteAudit, BrokenAudit>());
        });
        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        HttpClient Client() => f.CreateClient().WithClientHeader();
        var admin = Client(); Assert.Equal(200, await admin.LoginAsync("admin1"));
        var op = Client(); Assert.Equal(200, await op.LoginAsync("op1"));
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new
        {
            ipAddress = "127.0.0.1", port = plc.Port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0,
            monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true,
        })).Status);
        return (h, f, admin, op);
    }

    private static async Task ConfigureAsync(Harness h, HttpClient admin, params (string Signal, string Address)[] points)
    {
        foreach (var (signal, address) in points)
        {
            var id = await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == "PLC-01" && p.StationId == "A1" && p.SignalName == signal).Select(p => p.Id).SingleAsync());
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address })).Status);
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{id}/write-approval", new { decision = "Approve" })).Status);
        }
    }

    private static Task<(int Status, JsonElement Root, string Raw)> Write(HttpClient c, string signal, object value) =>
        c.SendJsonAsync(HttpMethod.Post, $"/api/plc/stations/A1/signals/{signal}/test-write", new { value });

    private static int Writes(MelsecMcMockServer plc) => plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x)).Count(r => r.Command == McFrame.CommandBatchWrite);

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 8000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (condition()) return true; await Task.Delay(30); }
        return condition();
    }

    private static Task<bool> ConnectedAsync(WebApplicationFactory<Program> f) => WaitFor(() => f.Services.GetRequiredService<IPlcRuntime>().Worker("PLC-01")?.Status(false).State == "Connected");

    private static Task<List<AuditEntry>> AuditAsync(Harness h) => h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action.StartsWith("PLC_WRITE")).OrderBy(a => a.OccurredAt).ToListAsync());

    [Fact]
    public async Task Real_mode_with_every_key_and_an_approval_still_writes_nothing_that_is_not_declared_and_reading_goes_on()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, Keys().Declare(("PLC-01", "A1", "AGV_ID", "D1600")));   // declared at a reserved register
        await using var _h = h; using var _f = f;
        plc.SetWord(1600, 42);
        await ConfigureAsync(h, admin, ("Request_Ack", "M101"), ("AGV_ID", "D1600"));
        Assert.True(await ConnectedAsync(f));
        Assert.Equal((PlcWriteGuard.NotDeclared, 422), (await Write(admin, "Request_Ack", true) is var a ? (a.Root.ErrorCode(), a.Status) : default));
        Assert.Equal(PlcWriteGuard.Reserved, (await Write(admin, "AGV_ID", 1)).Root.ErrorCode());
        Assert.Equal(0, Writes(plc)); Assert.False(plc.GetBit(101)); Assert.Equal((ushort)42, plc.GetWord(1600));

        // the reserved register is still READ
        var runtime = f.Services.GetRequiredService<IPlcRuntime>();
        Assert.True(await WaitFor(() => runtime.Handler("A1")!.Read("AGV_ID") is { Value.Value: 42 }), "the point is no longer read");

        var audit = await AuditAsync(h);
        Assert.Equal(2, audit.Count);
        Assert.All(audit, x => Assert.Equal(("PLC_WRITE_DENIED", AuditResult.Denied, "admin1"), (x.Action, x.Result, x.Actor)));
        Assert.Contains(audit, x => x.Detail!.Contains("Request_Ack") && x.Detail.Contains("M101") && x.Detail.Contains(PlcWriteGuard.NotDeclared));
        Assert.Contains(audit, x => x.Detail!.Contains("D1600") && x.Detail.Contains(PlcWriteGuard.Reserved));
        Assert.True(await h.Db(db => db.AuditLog.CountAsync(x => x.Action == "PLC_TEST_WRITE" && x.Result != AuditResult.Success)) >= 2);
    }

    [Fact]
    public async Task A_declared_point_is_written_and_the_authorization_and_the_result_are_audited()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, Keys().Declare(("PLC-01", "A1", "AGV_ID", "D1700")));
        await using var _h = h; using var _f = f;
        await ConfigureAsync(h, admin, ("AGV_ID", "D1700"));
        Assert.True(await ConnectedAsync(f));
        Assert.Equal(200, (await Write(admin, "AGV_ID", 7)).Status);
        Assert.Equal((ushort)7, plc.GetWord(1700)); Assert.Equal(1, Writes(plc));
        var audit = await AuditAsync(h);
        Assert.Equal(["PLC_WRITE_AUTHORIZED", "PLC_WRITE_RESULT"], audit.Select(x => x.Action).ToArray());
        Assert.All(audit, x => { Assert.Equal((AuditResult.Success, "admin1"), (x.Result, x.Actor)); Assert.Contains("D1700", x.Detail); Assert.Contains("= 7", x.Detail); });
    }

    [Fact]
    public async Task A_write_that_cannot_be_audited_is_not_sent()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, Keys().Declare(("PLC-01", "A1", "AGV_ID", "D1700")), brokenAudit: true);
        await using var _h = h; using var _f = f;
        await ConfigureAsync(h, admin, ("AGV_ID", "D1700"));
        Assert.True(await ConnectedAsync(f));
        Assert.Equal(PlcWriteGuard.AuditUnavailable, (await Write(admin, "AGV_ID", 7)).Root.ErrorCode());
        Assert.Equal(0, Writes(plc));
    }

    [Fact]
    public async Task The_connection_worker_refuses_an_address_a_caller_makes_up_and_an_undeclared_point()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, Keys().Declare(("PLC-01", "A1", "AGV_ID", "D1700"), ("PLC-01", "A1", "AGV_ID", "D1800")));
        await using var _h = h; using var _f = f;
        await ConfigureAsync(h, admin, ("AGV_ID", "D1700"), ("Request_Ack", "M101"));
        Assert.True(await ConnectedAsync(f));
        var runtime = f.Services.GetRequiredService<IPlcRuntime>();
        var worker = runtime.Worker("PLC-01")!;
        var configured = runtime.Handler("A1")!.Find("AGV_ID")!;
        var invented = await worker.EnqueueWriteAsync(configured with { Address = "D1800" }, PointValue.OfInt(1));   // even a declared one: the worker writes only its configured points
        Assert.Equal((false, PlcWriteGuard.UnknownPoint), (invented.Success, invented.Code));
        var undeclared = await worker.EnqueueWriteAsync(runtime.Handler("A1")!.Find("Request_Ack")!, PointValue.OfBool(true));
        Assert.Equal((false, PlcWriteGuard.NotDeclared), (undeclared.Success, undeclared.Code));
        Assert.Equal(0, Writes(plc));
    }

    [Theory]
    [InlineData("RealReadOnly", "READ_ONLY")]
    public async Task A_read_only_deployment_refuses_and_audits(string mode, string code)
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, Keys(mode).Declare(("PLC-01", "A1", "AGV_ID", "D1700")));
        await using var _h = h; using var _f = f;
        await ConfigureAsync(h, admin, ("AGV_ID", "D1700"));
        Assert.True(await ConnectedAsync(f));
        Assert.Equal(code, (await Write(admin, "AGV_ID", 7)).Root.ErrorCode());
        Assert.Equal(0, Writes(plc));
        Assert.Equal("PLC_WRITE_DENIED", Assert.Single(await AuditAsync(h)).Action);
    }

    [Fact]
    public async Task A_write_request_refused_by_the_role_or_by_the_API_is_audited_too()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, op) = await BootAsync(plc, Keys());
        await using var _h = h; using var _f = f;
        Assert.Equal(403, (await Write(op, "AGV_ID", 7)).Status);
        Assert.Equal(401, (await Write(f.CreateClient().WithClientHeader(), "AGV_ID", 7)).Status);
        var denied = await AuditAsync(h);
        Assert.Equal(["op1", null], denied.Select(x => x.Actor).ToArray());
        Assert.All(denied, x => Assert.Equal(AuditResult.Denied, x.Result));
        Assert.Equal("IO_POINT_NOT_FOUND", (await Write(admin, "No_Such_Signal", 1)).Root.ErrorCode());
        Assert.Equal("VALUE_INVALID", (await Write(admin, "AGV_ID", "text")).Root.ErrorCode());
        Assert.Equal(2, await h.Db(db => db.AuditLog.CountAsync(x => x.Action == "PLC_TEST_WRITE" && x.Result != AuditResult.Success)));
        Assert.Equal(0, Writes(plc));
    }

    [Fact]
    public async Task The_simulator_writes_as_before_without_a_declaration()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f, admin, _) = await BootAsync(plc, Keys("Simulator"));
        await using var _h = h; using var _f = f;
        await ConfigureAsync(h, admin, ("AGV_ID", "D1600"));
        Assert.True(await ConnectedAsync(f));
        Assert.Equal(200, (await Write(admin, "AGV_ID", 7)).Status);
        Assert.Equal(0, plc.ReceivedFrames.Count);   // the mock PLC is never contacted in the simulator
        Assert.Empty(await AuditAsync(h));
    }
}
