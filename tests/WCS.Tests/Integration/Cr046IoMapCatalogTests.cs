using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-046: the catalog the PRODUCT ships (IO map of 22/11/2026), from the workbook to the API. The other PLC tests run on the legacy fixture; these load the embedded catalog explicitly.</summary>
[Collection("Database")]
public class Cr046IoMapCatalogTests(DatabaseFixture fixture)
{
    private static readonly PlcCatalog Product = PlcCatalog.LoadEmbedded();
    private static readonly string[] Signals = ["PLC_MissionRequest", "PLC_ReaddyTranfer", "PLC_MissionCompleted", "AGV_RequestTranfer", "AGV_TranferCompleted", "Mission_Feedback"];

    private static string RepoFile(string relative)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent;
        return Path.Combine(d!.FullName, relative);
    }

    [Fact]
    public void The_product_catalog_is_the_standard_IO_map()
    {
        Assert.Equal("405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3", Product.Source.Sha256);
        Assert.Equal(Product.Source.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(RepoFile(Product.Source.File)))));
        Assert.Equal(["PLC-01=192.168.10.101:A1,A2,A3,A4", "PLC-02=192.168.10.111:B1,B2,D1,D2", "PLC-03=192.168.10.121:X1,X2,X3,X4", "PLC-04=192.168.10.131:E1,E2,G1,G2"],
            Product.Plcs.Select(p => $"{p.PlcId}={p.IpAddress}:{string.Join(',', p.Stations)}"));
        Assert.Equal(Signals, Product.Template.Select(t => t.SignalName));
        Assert.All(Product.Template, t => Assert.Equal("Int", t.DataType));
        Assert.Equal(["PlcToSoftware", "PlcToSoftware", "PlcToSoftware", "SoftwareToPlc", "SoftwareToPlc", "SoftwareToPlc"], Product.Template.Select(t => t.Direction));
        foreach (var plc in Product.Plcs)
            for (var n = 0; n < 4; n++)
            {
                var a = Product.Stations.Single(s => s.StationId == plc.Stations[n]).Addresses;
                Assert.Equal([$"D15{n}0", $"D15{n}1", $"D15{n}2", $"D16{n}0", $"D16{n}0", $"D16{n}1"], Signals.Select(s => a[s]));
            }
        Assert.Equal(["AGV_TranferCompleted"], Product.Template.Single(t => t.SignalName == "AGV_RequestTranfer").SharedRegisterWith);
        Assert.Equal("3: Hoàn thànhPick, 6: Hoàn thành Drop", Product.Template.Single(t => t.SignalName == "AGV_TranferCompleted").Values);
        Assert.Equal(4, Product.PlcSignals.Count(s => s is { SignalName: "HeartBit", Address: "M9200" }));
        Assert.All(Signals.Append("HeartBit"), s => Assert.True(UnconfirmedSignals.Contains(s)));
    }

    [Fact]
    public void No_address_approval_or_declaration_lets_a_signal_of_the_IO_map_reach_a_real_PLC()
    {
        var o = new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true };
        o.Write.Enabled = true;
        foreach (var name in new[] { "AGV_RequestTranfer", "AGV_TranferCompleted", "Mission_Feedback" })
        {
            o.Write.AllowListText = $"PLC-01|A1|{name}|D1700";   // declared, approved, outside the reserved registers
            var p = new IoPointRef(Guid.NewGuid(), "PLC-01", "A1", name, PlcSignalDirection.SoftwareToPlc, PlcDataType.Int, "D1700", null, null, true, "Command", true);
            var d = WritePolicy.Evaluate(o, p);
            Assert.False(d.Allowed);
            Assert.Equal("WRITE_SIGNAL_NOT_CONFIRMED", d.Code);
        }
    }

    /// <summary>A Production-like host on the PRODUCT catalog.</summary>
    private async Task<(Harness H, WebApplicationFactory<Program> F, HttpClient Admin)> BootAsync(bool legacyFirst)
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<PlcCatalog>(); s.AddSingleton(Product); }));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin));
            await db.SaveChangesAsync();
            var seeder = scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>();
            if (legacyFirst)
            {
                await seeder.SeedAsync(LegacyPlcCatalog.Instance);
                var old = await db.StationIoPoints.SingleAsync(p => p.StationId == "A1" && p.SignalName == "Request_Ack");
                old.ConfigureAddress("M101", null, null, DateTimeOffset.UtcNow);                 // data entered on the earlier catalog
                var oldD = await db.StationIoPoints.SingleAsync(p => p.StationId == "A1" && p.SignalName == "AGV_ID");
                oldD.ConfigureAddress("D1700", null, null, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
            await seeder.SeedAsync();
        }
        var admin = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        return (h, f, admin);
    }

    private static async Task<JsonElement> Data(HttpClient c, string path)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, path);
        Assert.Equal(200, r.Status);
        return r.Root.GetProperty("data").Clone();
    }

    private static string S(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    [Fact]
    public async Task A_fresh_database_shows_exactly_the_IO_map_with_reference_addresses_and_nothing_writable()
    {
        var (h, f, admin) = await BootAsync(legacyFirst: false);
        await using var _ = h; await using var __ = f;
        var points = (await Data(admin, "/api/plc/points")).EnumerateArray().ToList();
        Assert.Equal(96, points.Count);
        Assert.Equal(Signals.Order(), points.Select(p => S(p, "signalName")).Distinct().Order());
        Assert.All(points, p =>
        {
            Assert.Equal(JsonValueKind.Null, p.GetProperty("actualAddress").ValueKind);   // a document address is never a configured address
            Assert.False(p.GetProperty("confirmed").GetBoolean());
            Assert.True(p.GetProperty("inIoMap").GetBoolean());
            Assert.NotEqual(true, p.GetProperty("writeAllowed").GetBoolean());
        });
        var x4 = points.Single(p => S(p, "stationId") == "X4" && S(p, "signalName") == "AGV_TranferCompleted");
        Assert.Equal("D1630", S(x4, "templateAddressReference"));

        var map = await Data(admin, "/api/plc/io-map");
        Assert.Equal("AGV_PLC Interface_List_22_11_2026.xlsx", S(map, "sourceFile"));
        Assert.False(map.GetProperty("confirmed").GetBoolean());
        Assert.Equal(120, map.GetProperty("sheet").GetProperty("rows").GetArrayLength());
        Assert.Equal(0, map.GetProperty("leftoverAddresses").GetInt32());

        // an address may be entered; approving a write stays refused
        var id = S(x4, "id");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/address", new { address = "D1630" })).Status);
        var approve = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{id}/write-allowed", new { allowed = true });
        Assert.Contains("WRITE_SIGNAL_NOT_CONFIRMED", approve.Raw);
        // the shared register: the second signal cannot take the same configured address (not implemented), and says why
        var request = S(points.Single(p => S(p, "stationId") == "X4" && S(p, "signalName") == "AGV_RequestTranfer"), "id");
        var second = await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{request}/address", new { address = "D1630" });
        Assert.Contains("ADDRESS_CONFLICT", second.Raw);

        var handshake = (await Data(admin, "/api/plc/overview")).GetProperty("stations").EnumerateArray().ToList();
        Assert.Equal(16, handshake.Count);
        Assert.All(handshake, s => Assert.Equal(6, s.GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task An_existing_database_keeps_its_rows_but_only_the_IO_map_is_shown_read_and_written()
    {
        var (h, f, admin) = await BootAsync(legacyFirst: true);
        await using var _ = h; await using var __ = f;
        Assert.Equal(16 * 23 + 16 * 6, await h.Db(db => db.StationIoPoints.CountAsync()));                       // nothing deleted
        Assert.Equal(2, await h.Db(db => db.StationIoPoints.CountAsync(p => p.ActualAddress != null)));          // entered addresses kept
        Assert.Equal(19, await h.Db(db => db.PlcStations.CountAsync()));                                           // X4, G1, G2 added; X6, G3, G7 kept

        Assert.Equal(96, (await Data(admin, "/api/plc/points")).GetArrayLength());
        Assert.Equal(16 * 23 + 96, (await Data(admin, "/api/plc/points?includeRetired=true")).GetArrayLength());
        var stations = (await Data(admin, "/api/plc/stations")).EnumerateArray().Select(s => S(s, "stationId")).ToList();
        Assert.Equal(16, stations.Count);
        Assert.DoesNotContain("X6", stations);
        Assert.Contains("X4", stations);
        // nothing of the earlier map is reported, except how many addresses are still entered on it
        var map = await Data(admin, "/api/plc/io-map");
        Assert.Equal(2, map.GetProperty("leftoverAddresses").GetInt32());
        var raw = map.GetRawText();
        foreach (var gone in new[] { "Pickup_Request", "Request_Ack", "X6\"", "G3\"", "G7\"" }) Assert.DoesNotContain(gone, raw);

        // CR-047: a location still linked to a station of the earlier map: that station is no port any more, and the location can be given to the station of the map
        await h.SeedStandardAsync();
        await h.Db(async db => { (await db.PlcStations.SingleAsync(s => s.Id == "X6")).Update("A2", true, DateTimeOffset.UtcNow); await db.SaveChangesAsync(); return 0; });
        using (var scope = f.Services.CreateScope())
        {
            var routing = scope.ServiceProvider.GetRequiredService<WCS.Application.Routing.IRoutingRepository>();
            Assert.Null(await routing.FindPortByLocationAsync("A2"));
            Assert.DoesNotContain(await routing.ListLinkedPortsAsync(), s => s.Id == "X6");
            await scope.ServiceProvider.GetRequiredService<PlcProvisioningService>().SetPortLinkAsync("X4", "A2", true);
        }
        Assert.Equal(["X4:A2"], await h.Db(db => db.PlcStations.Where(s => s.LocationCode == "A2").Select(s => s.Id + ":" + s.LocationCode).ToListAsync()));

        await f.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        var rt = f.Services.GetRequiredService<IPlcRuntime>();
        Assert.Null(rt.Handler("X6"));
        Assert.Equal(6, rt.Handler("A1")!.Points.Count());
        Assert.Null(rt.Handler("A1")!.Find("Request_Ack"));
        var write = await rt.WriteAsync("PLC-01", "A1", "Request_Ack", PointValue.OfBool(true), "test");
        Assert.False(write.Success);

        // the point says whether an address exists (the address itself stays Admin only); the screens count and show values by it
        var snap = (await Data(admin, "/api/plc/stations/X1/io"));
        Assert.Equal(6, snap.GetProperty("total").GetInt32());
        Assert.All(snap.GetProperty("points").EnumerateArray(), p => Assert.False(p.GetProperty("hasAddress").GetBoolean()));

        // a retired row: its address can be cleared, nothing else
        var old = await h.Db(db => db.StationIoPoints.Where(p => p.StationId == "A1" && p.SignalName == "Request_Ack").Select(p => p.Id).SingleAsync());
        Assert.Contains("IO_POINT_RETIRED", (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{old}/address", new { address = "M102" })).Raw);
        Assert.Contains("IO_POINT_RETIRED", (await admin.SendJsonAsync(HttpMethod.Put, $"/api/plc/points/{old}/write-allowed", new { allowed = true })).Raw);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Delete, $"/api/plc/points/{old}/address")).Status);
        await rt.StopAsync();
    }
}
