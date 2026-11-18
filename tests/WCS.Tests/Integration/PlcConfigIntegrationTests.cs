using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

[Collection("Database")]
public class PlcConfigIntegrationTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync(bool seed = true)
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
        await db.SaveChangesAsync();
        if (seed) await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    // ---------------------------------------------------------------- seeder
    [Fact]
    public async Task Seeder_loads_4_PLCs_16_stations_and_368_points_idempotently_with_no_address()
    {
        var (h, f) = await ArrangeAsync(seed: false);
        await using var _h = h; await using var _f = f;
        using var scope = f.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>();

        var first = await seeder.SeedAsync();
        Assert.Equal((4, 16, 368), (first.PlcsAdded, first.StationsAdded, first.PointsAdded));
        var again = await seeder.SeedAsync();
        Assert.Equal((0, 0, 0), (again.PlcsAdded, again.StationsAdded, again.PointsAdded));     // idempotent
        Assert.Equal((4, 16, 368), (again.PlcsTotal, again.StationsTotal, again.PointsTotal));

        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        var points = await db.StationIoPoints.AsNoTracking().ToListAsync();
        Assert.All(points, p => { Assert.Null(p.ActualAddress); Assert.Equal(IoConfigStatus.NeedsConfiguration, p.Status); Assert.False(p.WriteAllowed); });
        Assert.Equal(16, points.GroupBy(p => (p.PlcId, p.StationId)).Count());
        Assert.All(points.GroupBy(p => (p.PlcId, p.StationId)), g => Assert.Equal(23, g.Count()));
        Assert.Equal(368, points.Select(p => (p.PlcId, p.StationId, p.SignalName)).Distinct().Count());     // PLC_ID + Station_ID + Signal_Name is unique
        var conns = await db.PlcConnections.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(Topology.Expected.Select(t => t.Ip), conns.Select(c => c.IpAddress));
        Assert.All(conns, c => { Assert.Null(c.Port); Assert.Null(c.Protocol); Assert.False(c.Enabled); Assert.NotEmpty(c.MissingParameters()); });
    }

    [Fact]
    public async Task Reseeding_never_overwrites_configuration_entered_by_an_admin()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        var p = await db.StationIoPoints.FirstAsync(x => x.PlcId == "PLC-01" && x.StationId == "A1" && x.SignalName == "Safety_Ready");
        p.ConfigureAddress("M500", null, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("M500", (await db.StationIoPoints.AsNoTracking().SingleAsync(x => x.Id == p.Id)).ActualAddress);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_duplicate_address_on_one_PLC_but_accepts_it_on_another()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        var a = await db.StationIoPoints.FirstAsync(x => x.StationId == "A1" && x.SignalName == "Safety_Ready");
        var a2 = await db.StationIoPoints.FirstAsync(x => x.StationId == "A2" && x.SignalName == "Safety_Ready");     // same PLC-01
        var b1 = await db.StationIoPoints.FirstAsync(x => x.StationId == "B1" && x.SignalName == "Safety_Ready");      // PLC-02
        a.ConfigureAddress("M100", null, null, DateTimeOffset.UtcNow);
        b1.ConfigureAddress("M100", null, null, DateTimeOffset.UtcNow);      // different physical PLC: allowed
        await db.SaveChangesAsync();

        a2.ConfigureAddress("M100", null, null, DateTimeOffset.UtcNow);       // same PLC: refused by the unique index (last line of defence)
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---------------------------------------------------------------- API + permissions
    [Fact]
    public async Task Operator_cannot_reach_PLC_configuration_and_unauthenticated_callers_get_401()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        foreach (var (m, url) in new[] { ("GET", "/api/plc/connections"), ("PUT", "/api/plc/connections/PLC-01"), ("GET", "/api/plc/points"),
                     ("PUT", $"/api/plc/points/{Guid.NewGuid()}/address"), ("POST", $"/api/plc/points/{Guid.NewGuid()}/approve"),
                     ("PUT", $"/api/plc/points/{Guid.NewGuid()}/write-allowed"), ("GET", "/api/plc/logs"), ("GET", "/api/plc/connections/PLC-01/address-check") })
            Assert.Equal(403, (await op.SendJsonAsync(new HttpMethod(m), url, m == "GET" ? null : new { })).Status);
        Assert.Equal(401, (await f.CreateClient().WithClientHeader().SendJsonAsync(HttpMethod.Get, "/api/plc/connections")).Status);
    }

    [Fact]
    public async Task Admin_lists_and_edits_the_four_connections_and_nothing_is_defaulted()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");

        var list = await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/connections");
        Assert.Equal(200, list.Status);
        Assert.Equal(4, list.Root.GetProperty("data").GetArrayLength());
        var first = list.Root.GetProperty("data")[0];
        Assert.Equal("NEEDS_CONFIGURATION", first.GetProperty("configStatus").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("port").ValueKind);        // nothing pre-filled
        Assert.Equal(4, first.GetProperty("stations").GetArrayLength());

        var bad = await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new { ipAddress = "192.168.10", port = 70000, enabled = false });
        Assert.Equal(400, bad.Status);
        var fields = bad.Root.GetProperty("error").GetProperty("details").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("ipAddress", fields); Assert.Contains("port", fields);

        var ok = await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new { ipAddress = "192.168.10.101", port = 5007, protocol = "McProtocol3EBinaryTcp", enabled = false });
        Assert.Equal(200, ok.Status);
        Assert.Equal("NEEDS_CONFIGURATION", ok.Root.GetProperty("data").GetProperty("configStatus").GetString());   // still missing net/pc/module/station/timers
        Assert.Contains("networkNumber", ok.Root.GetProperty("data").GetProperty("missingParameters").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-09", new { ipAddress = "192.168.10.9", enabled = false })).Status);
        Assert.Contains(await h.Db(db => db.AuditLog.AsNoTracking().ToListAsync()), a => a is { Action: "PLC_CONNECTION_UPDATE", Result: AuditResult.Success, Actor: "admin1" });
    }

    [Fact]
    public async Task Address_entry_is_validated_per_PLC_approved_and_audited()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        async Task<Guid> Id(string station, string signal) => await h.Db(db => db.StationIoPoints.AsNoTracking().Where(x => x.StationId == station && x.SignalName == signal).Select(x => x.Id).SingleAsync());

        var a1 = await Id("A1", "Safety_Ready"); var a2 = await Id("A2", "Safety_Ready"); var b1 = await Id("B1", "Safety_Ready");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a1}/address", new { address = "m100" })).Status);   // stored normalised
        Assert.Equal("M100", await h.Db(db => db.StationIoPoints.AsNoTracking().Where(x => x.Id == a1).Select(x => x.ActualAddress).SingleAsync()));

        var conflict = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a2}/address", new { address = "M100" });     // same PLC-01
        Assert.Equal(400, conflict.Status);
        Assert.Contains("trùng", conflict.Raw);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{b1}/address", new { address = "M100" })).Status);   // other PLC-02: fine

        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{a2}/address", new { address = "D100" })).Status);   // BOOL on a word device
        var reqId = await Id("A1", "Request_ID");
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{reqId}/address", new { address = "D1500" })).Status);   // STRING needs length+encoding
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{reqId}/address", new { address = "D1500", wordLength = 10, encoding = "ASCII" })).Status);

        // approval is a separate, audited step; write permission needs approval and excludes control signals
        var ack = await Id("A1", "Request_Ack");
        Assert.Equal(422, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{ack}/approve")).Status);                 // no address yet
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M200" });
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{ack}/approve")).Status);                  // the READ approval (CR-014: write approval no longer depends on it)
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/write-allowed", new { allowed = true })).Status);
        var permit = await Id("A1", "Transfer_Permit");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{permit}/address", new { address = "M201" });
        await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{permit}/approve");
        var blocked = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{permit}/write-allowed", new { allowed = true });
        Assert.Equal(422, blocked.Status); Assert.Equal("WRITE_BLOCKED_SAFETY", blocked.Root.ErrorCode());
        var inbound = await Id("A1", "Pickup_Request");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{inbound}/address", new { address = "M300" });
        await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{inbound}/approve");
        Assert.Equal(422, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{inbound}/write-allowed", new { allowed = true })).Status);   // PLC -> Software is never written

        // changing the address withdraws approval and write permission
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{ack}/address", new { address = "M202" });
        var afterEdit = await h.Db(db => db.StationIoPoints.AsNoTracking().SingleAsync(x => x.Id == ack));
        Assert.Equal((IoConfigStatus.Configured, false), (afterEdit.Status, afterEdit.WriteAllowed));

        var check = await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/connections/PLC-01/address-check");
        Assert.Equal(0, check.Root.GetProperty("data").GetArrayLength());
        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action.StartsWith("PLC_IO_")).ToListAsync());
        Assert.Contains(audit, a => a is { Action: "PLC_IO_ADDRESS_APPROVE", Actor: "admin1", Result: AuditResult.Success });
        Assert.Contains(audit, a => a is { Action: "PLC_IO_ADDRESS_SET", Result: AuditResult.Failed });
    }

    [Fact]
    public async Task Enabling_a_real_connection_needs_the_complete_confirmed_parameter_set()
    {
        var (h, f) = await ArrangeAsync();
        await using var _h = h; await using var _f = f;
        var realFactory = f.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Plc:Mode"] = "RealReadOnly" })));
        var admin = realFactory.CreateClient().WithClientHeader();
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        var partial = await admin.SendJsonAsync(HttpMethod.Put, "/api/plc/connections/PLC-01", new { ipAddress = "192.168.10.101", port = 5007, enabled = true });
        Assert.Equal(400, partial.Status);
        Assert.Contains("networkNumber", partial.Raw); Assert.Contains("NEEDS_CONFIGURATION", partial.Raw);
        Assert.False(await h.Db(db => db.PlcConnections.AsNoTracking().Where(c => c.Id == "PLC-01").Select(c => c.Enabled).SingleAsync()));   // nothing was switched on
    }
}
