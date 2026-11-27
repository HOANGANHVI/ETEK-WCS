using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-048: the default address of every point is the one written in the workbook; what an Admin enters or removes survives every later start; the default can be restored.</summary>
[Collection("Database")]
public class Cr048DefaultAddressTests(DatabaseFixture fixture)
{
    private static readonly PlcCatalog Product = PlcCatalog.LoadEmbedded();

    private async Task<(Harness H, WebApplicationFactory<Program> F, HttpClient Admin)> BootAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<PlcCatalog>(); s.AddSingleton(Product); }));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin));
            await db.SaveChangesAsync();
        }
        await SeedAsync(f);
        var admin = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        return (h, f, admin);
    }

    /// <summary>What every start of the application does.</summary>
    private static async Task<SeedResult> SeedAsync(WebApplicationFactory<Program> f, PlcCatalog? catalog = null)
    {
        using var scope = f.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>();
        return catalog is null ? await seeder.SeedAsync() : await seeder.SeedAsync(catalog);
    }

    private static async Task<Dictionary<string, JsonElement>> PointsAsync(HttpClient c)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, "/api/plc/points");
        Assert.Equal(200, r.Status);
        return r.Root.GetProperty("data").Clone().EnumerateArray().ToDictionary(p => $"{p.GetProperty("stationId").GetString()}/{p.GetProperty("signalName").GetString()}");
    }

    private static string? Str(JsonElement p, string name) => p.GetProperty(name).GetString();
    private static string Id(JsonElement p) => Str(p, "id")!;

    [Fact]
    public async Task Every_point_gets_exactly_the_address_of_the_workbook_for_its_station_and_signal()
    {
        var (h, f, admin) = await BootAsync();
        await using var _ = h; await using var __ = f;
        // read from the workbook itself, not from the catalog
        var (cells, merged) = Cr047WorkbookSheetTests.Read(0);
        string At(char col, int row)
        {
            var m = merged.FirstOrDefault(x => x.Col == col.ToString() && x.From <= row && row <= x.To);
            return cells.GetValueOrDefault(m.Col is null ? $"{col}{row}" : $"{col}{m.From}") ?? "";
        }
        var expected = Enumerable.Range(3, 200).Where(r => At('C', r) == "Command").Select(r => $"{At('B', r)}/{At('D', r)}={At('H', r)}").Order().ToList();
        Assert.Equal(96, expected.Count);
        var points = await PointsAsync(admin);
        Assert.Equal(expected, points.Select(p => $"{p.Key}={Str(p.Value, "actualAddress")}").Order());
        Assert.All(points.Values, p => Assert.True(p.GetProperty("pollable").GetBoolean()));

        // a second start changes nothing
        var again = await SeedAsync(f);
        Assert.Equal((0, 0, 0, 0), (again.PointsAdded, again.DefaultsAssigned, again.DefaultsBlocked, again.PointsRetired));
    }

    [Fact]
    public async Task What_an_Admin_enters_or_removes_survives_the_next_starts_and_the_default_can_be_restored()
    {
        var (h, f, admin) = await BootAsync();
        await using var _ = h; await using var __ = f;
        var points = await PointsAsync(admin);
        var edited = Id(points["A1/PLC_MissionRequest"]);
        var removed = Id(points["A1/PLC_ReaddyTranfer"]);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{edited}/address", new { address = "D1700" })).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/plc/points/{removed}/address")).Status);

        await SeedAsync(f);
        points = await PointsAsync(admin);
        Assert.Equal(("D1700", "Custom", "D1500"), (Str(points["A1/PLC_MissionRequest"], "actualAddress"), Str(points["A1/PLC_MissionRequest"], "addressOrigin"), Str(points["A1/PLC_MissionRequest"], "defaultAddress")));
        Assert.Equal((null, "Custom"), (Str(points["A1/PLC_ReaddyTranfer"], "actualAddress"), Str(points["A1/PLC_ReaddyTranfer"], "addressOrigin")));

        foreach (var id in new[] { edited, removed })
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{id}/address/restore-default")).Status);
        points = await PointsAsync(admin);
        Assert.Equal(("D1500", "WorkbookDefault"), (Str(points["A1/PLC_MissionRequest"], "actualAddress"), Str(points["A1/PLC_MissionRequest"], "addressOrigin")));
        Assert.Equal(("D1501", "WorkbookDefault"), (Str(points["A1/PLC_ReaddyTranfer"], "actualAddress"), Str(points["A1/PLC_ReaddyTranfer"], "addressOrigin")));
    }

    [Fact]
    public async Task A_default_held_by_an_Admin_address_is_not_taken_and_restoring_says_why()
    {
        var (h, f, admin) = await BootAsync();
        await using var _ = h; await using var __ = f;
        var points = await PointsAsync(admin);
        var owner = Id(points["A1/PLC_MissionRequest"]);       // default D1500
        var squatter = Id(points["A2/PLC_MissionRequest"]);    // default D1510
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/plc/points/{owner}/address")).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{squatter}/address", new { address = "D1500" })).Status);
        var refused = await admin.SendJsonAsync(HttpMethod.Post, $"/api/plc/points/{owner}/address/restore-default");
        Assert.Contains("ADDRESS_CONFLICT", refused.Raw);

        // a point that was never configured (a database that already used its default elsewhere) stays empty and is reported
        await h.Db(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE station_io_points SET \"AddressOrigin\" = 'None' WHERE \"Id\" = {0}", Guid.Parse(owner));
            return 0;
        });
        Assert.Equal(1, (await SeedAsync(f)).DefaultsBlocked);
        Assert.Null(Str((await PointsAsync(admin))["A1/PLC_MissionRequest"], "actualAddress"));
        Assert.Equal("D1500", Str((await PointsAsync(admin))["A2/PLC_MissionRequest"], "actualAddress"));
    }

    [Fact]
    public async Task A_default_follows_the_catalog_and_an_Admin_address_does_not()
    {
        var (h, f, admin) = await BootAsync();
        await using var _ = h; await using var __ = f;
        var points = await PointsAsync(admin);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{Id(points["A1/PLC_ReaddyTranfer"])}/address", new { address = "D1801" })).Status);
        var changed = PlcCatalog.LoadEmbedded();
        var a1 = changed.Stations.Single(s => s.StationId == "A1");
        a1.Addresses["PLC_MissionRequest"] = "D1900";
        a1.Addresses["PLC_ReaddyTranfer"] = "D1901";
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/plc/points/{Id(points["A1/PLC_MissionCompleted"])}/address")).Status);
        a1.Addresses["PLC_MissionCompleted"] = "D1902";
        await SeedAsync(f, changed);
        points = await PointsAsync(admin);
        Assert.Equal("D1900", Str(points["A1/PLC_MissionRequest"], "actualAddress"));
        Assert.Equal("D1801", Str(points["A1/PLC_ReaddyTranfer"], "actualAddress"));
        Assert.Null(Str(points["A1/PLC_MissionCompleted"], "actualAddress"));   // removed stays removed after a catalog change
    }

    [Fact]
    public async Task A_default_address_permits_no_write()
    {
        var (h, f, admin) = await BootAsync();
        await using var _ = h; await using var __ = f;
        var points = await PointsAsync(admin);
        var p = points["A1/Mission_Feedback"];
        Assert.Equal("NOT_APPROVED", Str(p, "writeApproval"));
        Assert.Contains("WRITE_SIGNAL_NOT_CONFIRMED", (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{Id(p)}/write-allowed", new { allowed = true })).Raw);
        var o = new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true };
        o.Write.Enabled = true;
        o.Write.AllowListText = "PLC-01|A1|Mission_Feedback|D1601";
        var point = new IoPointRef(Guid.Parse(Id(p)), "PLC-01", "A1", "Mission_Feedback", PlcSignalDirection.SoftwareToPlc, PlcDataType.Int, "D1601", null, null, true, "Command", true);
        Assert.Equal("WRITE_SIGNAL_NOT_CONFIRMED", WritePolicy.Evaluate(o, point).Code);
    }
}
