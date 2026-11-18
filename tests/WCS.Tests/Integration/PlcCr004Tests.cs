using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-004: connection config vs IO mapping split. Backend rules: Admin-only changes, device scope D/W/M, Pollable never implied by entering an address, per-PLC isolation.</summary>
[Collection("Database")]
public class PlcCr004Tests(DatabaseFixture fixture)
{
    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static Task<Guid> Id(Harness h, string plc, string station, string signal) =>
        h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.PlcId == plc && p.StationId == station && p.SignalName == signal).Select(p => p.Id).SingleAsync());

    [Fact]
    public async Task Operator_cannot_change_connections_or_IO_mapping_or_read_the_configuration()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var a1 = await Id(h, "PLC-01", "A1", "Station_Online");
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new { ipAddress = "192.168.10.101" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a1}/address", new { address = "M100" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Delete, $"/api/plc/points/{a1}/address")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{a1}/approve")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a1}/write-allowed", new { allowed = true })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/plc/points")).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/plc/connections")).Status);
        Assert.Null(await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Id == a1).Select(p => p.ActualAddress).SingleAsync()));
    }

    [Fact]
    public async Task IO_mapping_lists_368_points_with_pollable_false_and_a_valid_address_makes_a_point_pollable_without_approval()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var all = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/points")).Root.GetProperty("data");
        Assert.Equal(368, all.GetArrayLength());
        Assert.All(all.EnumerateArray(), p => { Assert.False(p.GetProperty("pollable").GetBoolean()); Assert.Equal(JsonValueKind.Null, p.GetProperty("actualAddress").ValueKind); });
        Assert.Equal(23, (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/points?plcId=PLC-02&stationId=B1")).Root.GetProperty("data").GetArrayLength());

        var id = await Id(h, "PLC-01", "A1", "Station_Online");
        var set = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "M100" });
        Assert.Equal(200, set.Status);
        Assert.True(set.Root.GetProperty("data").GetProperty("pollable").GetBoolean());             // CR-012: a valid address is pollable at once, NOT approved
        Assert.Equal("CONFIGURED", set.Root.GetProperty("data").GetProperty("configStatus").GetString());
        var approved = await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{id}/approve");
        Assert.True(approved.Root.GetProperty("data").GetProperty("pollable").GetBoolean());         // approval (write permission / process mapping) does not change reading
        var changed = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "M101" });
        Assert.True(changed.Root.GetProperty("data").GetProperty("pollable").GetBoolean());         // a new valid address stays pollable
        Assert.Equal("CONFIGURED", changed.Root.GetProperty("data").GetProperty("configStatus").GetString());   // ... and the approval is withdrawn
    }

    [Theory]
    [InlineData("X10")]
    [InlineData("x7")]
    [InlineData("Y20")]
    [InlineData(" y010 ")]
    public async Task X_and_Y_are_rejected_in_IO_mapping_and_nothing_is_stored_or_converted(string address)
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var id = await Id(h, "PLC-01", "A1", "Station_Online");
        var r = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address });
        Assert.Equal(400, r.Status); Assert.Contains("OUT_OF_SCOPE", r.Raw);
        Assert.Null(await h.Db(db => db.StationIoPoints.AsNoTracking().Where(p => p.Id == id).Select(p => p.ActualAddress).SingleAsync()));
    }

    [Fact]
    public async Task D_W_M_rules_and_same_address_on_different_PLCs_but_not_within_one_PLC()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var a1 = await Id(h, "PLC-01", "A1", "Station_Online"); var a2 = await Id(h, "PLC-01", "A2", "Station_Online"); var b1 = await Id(h, "PLC-02", "B1", "Station_Online");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a1}/address", new { address = "M100" })).Status);
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a2}/address", new { address = "M100" })).Status);      // same PLC
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{b1}/address", new { address = "M100" })).Status);      // other PLC
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a2}/address", new { address = "D100" })).Status);      // BOOL on a word device
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a2}/address", new { address = "Z5" })).Status);
        var str = await Id(h, "PLC-01", "A1", "Request_ID");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{str}/address", new { address = "D2000", wordLength = 8, encoding = "ASCII" })).Status);
    }

    [Fact]
    public async Task Engineering_tools_work_in_Development_and_are_refused_in_Production()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var dev = await As(f, "admin1");
        var body = new { plcId = "PLC-01", online = false };
        Assert.Equal(200, (await dev.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/plc-online", body)).Status);

        await using var prod = new DbApiFactory(fixture).WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Plc:AllowSimulatorInProduction"] = "true" })); });   // CR-006 explicit exception
        var admin = prod.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }).WithClientHeader();   // Production cookies are Secure
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        var r = await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/plc-online", body);
        Assert.Equal(409, r.Status); Assert.Contains("NOT_AVAILABLE_IN_PRODUCTION", r.Raw);
        Assert.Equal(409, (await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/simulator/value", new { plcId = "PLC-01", stationId = "A1", signal = "Station_Online", value = true })).Status);
        // CR-015: the PLC write is NOT an environment-blocked engineering tool any more: in Production it is judged by the write guards (here: the STRING has no configured length)
        var write = await admin.SendJsonAsync(HttpMethod.Post, "/api/plc/stations/A1/signals/Request_ID/test-write", new { value = "X" });
        Assert.NotEqual("NOT_AVAILABLE_IN_PRODUCTION", write.Root.ErrorCode()); Assert.Equal(400, write.Status);   // judged by the value/write guards (a STRING with no configured length), not by the environment
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/connections")).Status);   // configuration itself stays available to Admin
    }

    [Fact]
    public async Task Editing_PLC01_does_not_change_the_other_PLCs_and_the_four_PLCs_are_listed()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var before = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/connections")).Root.GetProperty("data");
        Assert.Equal(new[] { "PLC-01", "PLC-02", "PLC-03", "PLC-04" }, before.EnumerateArray().Select(c => c.GetProperty("plcId").GetString()).ToArray());
        var rest = before.EnumerateArray().Where(c => c.GetProperty("plcId").GetString() != "PLC-01").Select(c => c.GetRawText()).ToArray();

        var put = await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new { ipAddress = "192.168.10.101", port = 5002, timeoutMs = 500, enabled = false });
        Assert.Equal(200, put.Status);
        var after = (await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/connections")).Root.GetProperty("data");
        Assert.Equal(5002, after.EnumerateArray().First().GetProperty("port").GetInt32());
        Assert.Equal(rest, after.EnumerateArray().Where(c => c.GetProperty("plcId").GetString() != "PLC-01").Select(c => c.GetRawText()).ToArray());
        Assert.Equal(368, await h.Db(db => db.StationIoPoints.CountAsync()));
    }
}
