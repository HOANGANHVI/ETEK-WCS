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

/// <summary>CR-003 through the real API + PostgreSQL: new connection parameters, address bases, approved-only polling, Admin-only diagnostics.</summary>
[Collection("Database")]
public class PlcMcIntegrationTests(DatabaseFixture fixture)
{
    private static readonly McRoute Route = new(0, 255, 1023, 0);   // TEST values entered through the API below

    private async Task<(Harness H, WebApplicationFactory<Program> F)> ArrangeAsync(Dictionary<string, string?> plc)
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
        foreach (var kv in plc) cfg[kv.Key] = kv.Value;
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(cfg)));
        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(WebApplicationFactory<Program> f, string user)
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

    private static object Conn(int port, object? extra = null) => new
    {
        ipAddress = "127.0.0.1", port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0,
        monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true,
        connectTimeoutMs = (int?)null, readTimeoutMs = (int?)null, writeTimeoutMs = (int?)null, xyAddressBase = (int?)null, wAddressBase = (int?)null,
    };

    private static Task<(int Status, JsonElement Root, string Raw)> PutConn(HttpClient admin, object body) => admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", body);

    private static async Task<JsonElement?> Point(HttpClient c, string station, string signal)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, $"/api/plc/stations/{station}/io");
        return r.Status == 200 ? r.Root.GetProperty("data").GetProperty("points").EnumerateArray().First(p => p.GetProperty("signalName").GetString() == signal) : null;
    }

    private static async Task<Guid> Id(Harness h, string station, string signal) =>
        await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.StationId == station && p.SignalName == signal).Select(p => p.Id).SingleAsync());

    [Fact]
    public async Task New_connection_parameters_are_validated_stored_and_the_X_Y_W_base_gates_the_addresses()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        var (h, f) = await ArrangeAsync(new() { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "true" });
        await using var _h = h; using var _f = f;
        var admin = await As(f, "admin1");

        // range checks on the new fields
        foreach (var bad in new object[]
        {
            new { ipAddress = "127.0.0.1", connectTimeoutMs = 5 }, new { ipAddress = "127.0.0.1", readTimeoutMs = 999999 }, new { ipAddress = "127.0.0.1", writeTimeoutMs = 0 },
            new { ipAddress = "127.0.0.1", xyAddressBase = 10 }, new { ipAddress = "127.0.0.1", wAddressBase = 8 },
        })
            Assert.Equal(400, (await PutConn(admin, bad)).Status);

        // an X address cannot be accepted while the X/Y base is unconfirmed
        var ok = await PutConn(admin, new { ipAddress = "127.0.0.1", port = plc.Port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0, monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true });
        Assert.Equal(200, ok.Status);
        Assert.Equal(JsonValueKind.Null, ok.Root.GetProperty("data").GetProperty("xyAddressBase").ValueKind);        // never defaulted
        var online = await Id(h, "A1", "Station_Online");
        var refused = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{online}/address", new { address = "X10" });
        Assert.Equal(400, refused.Status); Assert.Contains("NEEDS_CONFIRMATION", refused.Raw);
        var refusedW = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{await Id(h, "A1", "Rack_Full")}/address", new { address = "W10" });
        Assert.Equal(400, refusedW.Status);

        // confirm the base (octal) and the timeouts: the address is accepted and stored in the engineer's notation (NOT converted)
        var full = await PutConn(admin, new { ipAddress = "127.0.0.1", port = plc.Port, protocol = "McProtocol3EBinaryTcp", networkNumber = 0, pcNumber = 255, moduleIoNumber = 1023, plcStationNumber = 0, monitoringTimer = 16, timeoutMs = 300, pollingIntervalMs = 50, enabled = true, connectTimeoutMs = 500, readTimeoutMs = 400, writeTimeoutMs = 600, xyAddressBase = 8, wAddressBase = 16 });
        Assert.Equal(200, full.Status);
        var d = full.Root.GetProperty("data");
        Assert.Equal(500, d.GetProperty("connectTimeoutMs").GetInt32()); Assert.Equal(400, d.GetProperty("readTimeoutMs").GetInt32()); Assert.Equal(600, d.GetProperty("writeTimeoutMs").GetInt32());
        Assert.Equal(8, d.GetProperty("xyAddressBase").GetInt32()); Assert.Equal(16, d.GetProperty("wAddressBase").GetInt32());
        // CR-004: X/Y are OUT_OF_SCOPE for IO mapping even with a confirmed base - rejected by the API, nothing stored
        var xOnline = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{online}/address", new { address = " x010 " });
        Assert.Equal(400, xOnline.Status); Assert.Contains("X/Y", xOnline.Raw);
        Assert.Null(await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Id == online).Select(p => p.ActualAddress).SingleAsync()));
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{online}/address", new { address = "Y10" })).Status);
        // W (word) with a confirmed base is accepted and stored in the engineer's notation (NOT converted)
        var reqId = await Id(h, "A1", "Request_ID");
        var accepted = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{reqId}/address", new { address = " w01a ", wordLength = 8, encoding = "ASCII" });
        Assert.Equal(200, accepted.Status);
        var stored = accepted.Root.GetProperty("data").GetProperty("actualAddress").GetString();
        Assert.Equal("W1A", stored);
        Assert.Equal("W1A", await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Id == reqId).Select(p => p.ActualAddress).SingleAsync()));
        // an overlap is judged on the resolved device number: A2 may not use the same W1A on the SAME PLC
        var dup = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{await Id(h, "A2", "Request_ID")}/address", new { address = "W01A", wordLength = 8, encoding = "ASCII" });
        Assert.Equal(400, dup.Status); Assert.Contains("trùng", dup.Raw);
        // persisted in the table
        var row = await h.Db(db => db.PlcConnections.AsNoTracking().Where(c => c.Id == "PLC-01").Select(c => new { c.ConnectTimeoutMs, c.XyAddressBase, c.WAddressBase }).SingleAsync());
        Assert.Equal(500, row.ConnectTimeoutMs); Assert.Equal(8, row.XyAddressBase); Assert.Equal(16, row.WAddressBase);
    }

    [Fact]
    public async Task Entered_addresses_are_polled_without_approval_diagnostics_are_Admin_only_and_the_exchange_hex_matches_the_wire()
    {
        await using var plc = new MelsecMcMockServer { ExpectedRoute = Route }; plc.Start();
        plc.SetBit(100, true); plc.SetWord(1500, 42);
        var (h, f) = await ArrangeAsync(new() { ["Plc:Mode"] = "RealReadOnly", ["Plc:AllowLiveConnection"] = "true" });
        await using var _h = h; using var _f = f;
        var admin = await As(f, "admin1");
        Assert.Equal(200, (await PutConn(admin, Conn(plc.Port))).Status);

        var pickup = await Id(h, "A1", "Pickup_Request"); var rack = await Id(h, "A1", "Rack_Full");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{pickup}/address", new { address = "M100" })).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{rack}/address", new { address = "D1500" })).Status);      // CR-012: entered = polled, nobody approved anything

        Assert.True(await WaitFor(async () => (await Point(admin, "A1", "Pickup_Request"))?.GetProperty("quality").GetString() == "Good"));
        await Task.Delay(300);                                                                                                                         // several more poll cycles
        Assert.Equal("NotConfigured", (await Point(admin, "A1", "Station_Online"))!.Value.GetProperty("quality").GetString());                          // no address: still not read
        var heads = plc.ReceivedFrames.Select(x => McFrame.ParseRequest(x).Head).ToList();
        Assert.Contains(new DeviceAddress(PlcDevice.M, 100), heads);
        Assert.Equal(0, await h.Db(db => db.StationIoPoints.CountAsync(p => p.Status == IoConfigStatus.Approved)));                                    // nothing was approved
        Assert.True(await WaitFor(async () => (await Point(admin, "A1", "Rack_Full"))?.GetProperty("quality").GetString() == "Good"));
        Assert.Equal(42, (await Point(admin, "A1", "Rack_Full"))!.Value.GetProperty("value").GetInt32());

        // diagnostics: Admin sees the exchange (request hex of a real frame), Operator sees neither the record nor the network details
        var adminOverview = await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview");
        var plc1 = adminOverview.Root.GetProperty("data").GetProperty("plcs").EnumerateArray().First(p => p.GetProperty("plcId").GetString() == "PLC-01");
        var ex = plc1.GetProperty("diagnostics").GetProperty("lastExchange");
        Assert.Equal("READ", ex.GetProperty("operation").GetString()); Assert.Equal("OK", ex.GetProperty("outcome").GetString());
        Assert.StartsWith("50 00 00 FF FF 03 00 0C 00 10 00 01 04", ex.GetProperty("requestHex").GetString());
        Assert.StartsWith("D0 00 00 FF FF 03 00", ex.GetProperty("responseHex").GetString());

        var op = await As(f, "op1");
        var opRaw = (await op.SendJsonAsync(HttpMethod.Get, "/api/plc/overview")).Raw;
        Assert.DoesNotContain("requestHex", opRaw); Assert.DoesNotContain("lastExchange", opRaw); Assert.DoesNotContain("127.0.0.1", opRaw);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", Conn(plc.Port))).Status);
    }
}
