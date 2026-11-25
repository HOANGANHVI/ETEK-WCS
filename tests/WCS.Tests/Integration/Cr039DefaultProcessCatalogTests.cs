using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Plc;
using WCS.Domain.Routing;
using WCS.Infrastructure.Seeding;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-039: the default transport processes. A new database gets them Published (D-11) but accepting no request until an Admin sets Ready, which needs the device conditions.
/// Real PostgreSQL, real seeders, the real route engine and the SIMULATOR dispatcher; PLC ports and approvals are TEST data, nothing talks to a device or to an RCS.
/// </summary>
[Collection("Database")]
public class Cr039DefaultProcessCatalogTests(DatabaseFixture fixture)
{
    private static readonly string[] Published = ["PH2-01", "PH2-02", "PH2-03", "PH2-04", "PH2-06", "PH2-07", "PH2-08", "PH2-09", "PH2-10", "PH2-11", "PH2-12"];

    private async Task<RoutingKit> NewDeploymentAsync(bool locations = true)
    {
        var kit = await RoutingKit.CreateAsync(fixture, seedPorts: false);
        // an EMPTY location table, then exactly what start-up does
        await kit.H.Db(async db => { db.Locations.RemoveRange(await db.Locations.ToListAsync()); await db.SaveChangesAsync(); return 0; });
        if (locations) await kit.With(sp => sp.GetRequiredService<WcsDataSeeder>().SeedDefaultLocationsAsync());
        return kit;
    }

    private static Task<ProcessSeedResult> SeedAsync(RoutingKit kit, IReadOnlyList<ReferenceProcess>? catalog = null) =>
        kit.With(sp => sp.GetRequiredService<DefaultProcessSeeder>().SeedAsync(default, catalog));

    private static Task<IReadOnlyList<StartableProcessDto>> StartableAsync(RoutingKit kit) => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().StartableAsync());

    private static Task<Guid> VersionOfAsync(RoutingKit kit, string code) => kit.H.Db(async db =>
    {
        var p = await db.BusinessProcesses.AsNoTracking().SingleAsync(x => x.Code == code);
        return (await db.BusinessProcessVersions.AsNoTracking().SingleAsync(v => v.ProcessId == p.Id && v.State == ProcessConfigState.Published)).Id;
    });

    private static Task<ReadinessDto> ReadyAsync(RoutingKit kit, Guid version) =>
        kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(version, "Ready", "test evidence", "admin"));

    /// <summary>TEST commissioning: one enabled PLC, one approved port for every location that needs PLC permission.</summary>
    private static Task CommissionAsync(RoutingKit kit) => kit.H.Db(async db =>
    {
        var now = kit.H.Clock.UtcNow;
        var plc = new PlcConnection("PLC-SIM", "PLC-SIM", "10.0.0.9", now);
        plc.Update("PLC-SIM", "10.0.0.9", null, null, null, null, null, null, null, null, null, true, "test", now);
        db.PlcConnections.Add(plc); await db.SaveChangesAsync();
        var order = 0;
        foreach (var l in await db.Locations.AsNoTracking().Where(x => x.RequiresPlcPermission).OrderBy(x => x.Code).ToListAsync())
        {
            var st = new PlcStation(l.Code, "PLC-SIM", ++order, now); st.Update(l.Code, true, now); db.PlcStations.Add(st); await db.SaveChangesAsync();
            var point = new StationIoPoint("PLC-SIM", l.Code, 1, "Handshake", "Request_Ack", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, "M9101", "test", now);
            point.ConfigureAddress($"M{1000 + order}", null, null, now); point.Approve("engineer", now); db.StationIoPoints.Add(point);
        }
        await db.SaveChangesAsync();
        return 0;
    });

    private async Task<RoutingKit> CommissionedAsync()
    {
        var kit = await NewDeploymentAsync();
        await SeedAsync(kit);
        await CommissionAsync(kit);
        foreach (var c in Published)
        {   // CR-040: every default is queued; a TEST scope of its own (the real scopes are OQ-54, nothing declares them by default)
            var id = await kit.H.Db(d => d.BusinessProcesses.AsNoTracking().Where(x => x.Code == c).Select(x => x.Id).SingleAsync());
            await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetQueueScopeAsync(id, "TEST-" + c, "test"));
            await ReadyAsync(kit, await VersionOfAsync(kit, c));
        }
        return kit;
    }

    // ================================================================== the catalog on a new database

    [Fact]
    public async Task A_new_database_gets_the_locations_of_the_default_processes_with_the_approved_attributes()
    {
        await using var kit = await NewDeploymentAsync();
        var rows = await kit.H.Db(db => db.Locations.AsNoTracking().ToDictionaryAsync(x => x.Code));
        Assert.Equal(23, rows.Count);
        foreach (var c in new[] { "X1", "X2", "X3", "X4", "Y1", "Y2", "Z1", "Z2" })
        { Assert.Equal(LocationType.Station, rows[c].Type); Assert.Equal(1, rows[c].RackCapacity); Assert.True(rows[c].RequiresPlcPermission); Assert.Null(rows[c].StationRole); }
        foreach (var c in new[] { "P1", "P2", "P3", "P4" })
        { Assert.Equal(LocationType.Buffer, rows[c].Type); Assert.Equal(1, rows[c].RackCapacity); Assert.Equal(AllowedRackKind.Any, rows[c].AllowedRackKind); Assert.False(rows[c].RequiresPlcPermission); }
        foreach (var c in new[] { "M5", "M6" })
        { Assert.Equal(LocationType.ManualBuffer, rows[c].Type); Assert.Equal(2, rows[c].RackCapacity); Assert.Equal(AllowedRackKind.Any, rows[c].AllowedRackKind); Assert.Equal(StationRole.ManualQcStation, rows[c].StationRole); }
        foreach (var c in new[] { "A3", "A4", "D1", "D2" }) Assert.False(rows.ContainsKey(c));
        Assert.All(rows.Values, l => Assert.Null(l.ProductModelId));
    }

    [Fact]
    public async Task The_defaults_are_published_after_deployment_and_PH2_05_stays_a_draft()
    {
        await using var kit = await NewDeploymentAsync();
        var result = await SeedAsync(kit);
        Assert.Equal(Published, result.Published.Order().ToArray());
        Assert.Equal(["PH2-05"], result.LeftDraft);
        var list = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().ListAsync());
        Assert.Equal(12, list.Count);
        Assert.Null(list.Single(p => p.Code == "PH2-05").PublishedVersion);
        Assert.DoesNotContain(list, p => p.Code.StartsWith("BP-", StringComparison.Ordinal));   // Phase 1 keeps running through the flow configuration; BP-04 is never seeded
        var startable = await StartableAsync(kit);
        Assert.Equal(Published, startable.Select(x => x.Code).ToArray());
        Assert.All(startable, s => { Assert.False(s.AcceptsRequests); Assert.Equal("PROCESS_NOT_READY", s.BlockedCode); Assert.Equal("NotReady", s.Readiness); });
    }

    [Fact]
    public async Task The_default_routes_are_the_agreed_ones_and_none_takes_a_rack_out_of_manual_QC()
    {
        static string Ends(ReferenceProcess p) => string.Join(",", p.TriggerLocationCodes) + " | " + string.Join(" > ", p.Definition.Steps[0].Candidates!
            .GroupBy(c => (c.Role, c.Priority)).OrderBy(g => g.Key.Priority).Select(g => g.Key.Role[0] + ":" + string.Join("/", g.OrderBy(c => c.Order).Select(c => c.LocationCode))));
        var all = ReferenceProcessCatalog.Load().Where(p => p.DefaultSinceVersion is not null && p.DefaultPublish).ToDictionary(p => p.Code, p => $"{p.RequestType}/{p.RackKind} {Ends(p)}");
        Assert.Equal("PickupRequest/Full A1,A2 | D:B1/B2 > D:P1/P2 > D:M5", all["PH2-01"]);
        Assert.Equal("DeliveryRequest/Empty A1,A2 | S:B1/B2 > S:P1/P2", all["PH2-02"]);
        Assert.Equal("PickupRequest/Empty B1,B2 | D:A1/A2 > D:P1/P2 > D:M5", all["PH2-03"]);
        Assert.Equal("DeliveryRequest/Full B1,B2 | S:A1/A2 > S:P1/P2", all["PH2-04"]);
        Assert.Equal("ManualRequest/Full M5 | S:P1/P2", all["PH2-06"]);
        Assert.Equal("PickupRequest/Full X1,X2 | D:Y1/Y2 > D:P3/P4 > D:M6", all["PH2-07"]);
        Assert.Equal("DeliveryRequest/Empty X1,X2 | S:P3/P4", all["PH2-08"]);
        Assert.Equal("PickupRequest/Empty Y1,Y2 | D:X1/X2 > D:P3/P4 > D:M6", all["PH2-09"]);
        Assert.Equal("DeliveryRequest/Full Y1,Y2 | S:P3/P4", all["PH2-10"]);
        Assert.Equal("PickupRequest/Full X3,X4 | D:Z1/Z2", all["PH2-11"]);
        Assert.Equal("PickupRequest/Empty Z1,Z2 | D:X3/X4", all["PH2-12"]);
        Assert.Equal(Published, all.Keys.Order().ToArray());
        Assert.DoesNotContain(all.Values, v => v.Contains("S:M5") || v.Contains("S:M6"));
        var published = ReferenceProcessCatalog.Load().Where(p => all.ContainsKey(p.Code));
        Assert.All(published, p => Assert.False(p.TriggerLocationCodes.Any(t => t is "M5" or "M6") && p.Definition.Steps[0].SourceMode == "Request"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_start_up_seed_of_an_empty_database_publishes_the_defaults_once()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var startUp = new WCS.Runtime.DatabaseSeedHostedService(h.Services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<WCS.Runtime.DatabaseSeedHostedService>.Instance);
        await startUp.StartAsync(default);
        await startUp.StartAsync(default);   // a restart
        var list = await h.Scoped(sp => sp.GetRequiredService<ProcessAdminService>().StartableAsync());
        Assert.Equal(Published, list.Select(x => x.Code).ToArray());
        Assert.All(list, s => Assert.False(s.AcceptsRequests));
        Assert.Equal(12, await h.Db(d => d.BusinessProcesses.CountAsync()));
        Assert.Equal(0, await h.Db(d => d.PlcStations.CountAsync(s => s.LocationCode != null)));   // the PLC catalog links no port: Ready needs the Admin to link and approve
    }

    // ================================================================== not ready = nothing runs

    [Fact]
    public async Task A_published_default_accepts_no_request_until_Ready_and_Ready_needs_the_device_conditions()
    {
        await using var kit = await NewDeploymentAsync();
        await SeedAsync(kit);
        await kit.H.StockAsync("A1", RackKind.Full, 1);

        var explicitly = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("n1", "A1", RequestType.PickupRequest, RackKind.Full, "PH2-01"));
        Assert.Equal("PROCESS_NOT_READY", explicitly.Code);
        var matched = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("n2", "A1", RequestType.PickupRequest, RackKind.Full));
        Assert.Equal("PROCESS_NOT_READY", matched.Code);
        var version = await VersionOfAsync(kit, "PH2-01");
        var noPort = await Assert.ThrowsAsync<AppException>(() => ReadyAsync(kit, version));
        Assert.Equal("READINESS_CONDITIONS_UNMET", noPort.Code); Assert.Contains("no PLC port", noPort.Message);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));

        await CommissionAsync(kit);
        await ReadyAsync(kit, version);
        Assert.True((await StartableAsync(kit)).Single(s => s.Code == "PH2-01").AcceptsRequests);
        Assert.False((await StartableAsync(kit)).Single(s => s.Code == "PH2-02").AcceptsRequests);   // Ready is per process version

        // an approval withdrawn after Ready closes the intake again
        await kit.H.Db(async db => { var p = await db.StationIoPoints.SingleAsync(x => x.StationId == "B2"); p.ConfigureAddress("M1999", null, null, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        var after = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("n3", "A1", RequestType.PickupRequest, RackKind.Full, "PH2-01"));
        Assert.Equal("PROCESS_NOT_READY", after.Code);
        Assert.False((await StartableAsync(kit)).Single(s => s.Code == "PH2-01").AcceptsRequests);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));
    }

    // ================================================================== lifecycle in the simulator

    private static async Task<TransportOrder> RunAsync(RoutingKit kit, string code, string at, RequestType type, RackKind kind, string expectedSource, string expectedDestination, string key)
    {
        var order = (await kit.StartAsync(key, at, type, kind, code)).Order;
        var leg = Assert.Single(order.Legs);
        Assert.Equal(expectedSource, leg.SourceLocationCode);
        await kit.PickAsync(leg.Id); await kit.TickAsync();
        var after = (await kit.OrderAsync(order.Id)).Legs.Single();
        Assert.Equal(expectedDestination, after.DestinationLocationCode);
        await kit.DropAsync(leg.Id);
        await kit.ExitAsync(leg.Id, expectedSource); await kit.ExitAsync(leg.Id, expectedDestination);
        await kit.TickAsync();
        var done = await kit.OrderAsync(order.Id);
        Assert.Equal(OrderState.Completed, done.State);
        Assert.NotNull(await kit.RackAtAsync(expectedDestination));
        return done;
    }

    [Theory]
    [InlineData("PH2-01", "A1", RequestType.PickupRequest, RackKind.Full, "A1", "B1")]
    [InlineData("PH2-02", "A2", RequestType.DeliveryRequest, RackKind.Empty, "B1", "A2")]
    [InlineData("PH2-03", "B2", RequestType.PickupRequest, RackKind.Empty, "B2", "A1")]
    [InlineData("PH2-04", "B1", RequestType.DeliveryRequest, RackKind.Full, "A1", "B1")]
    [InlineData("PH2-06", "M5", RequestType.ManualRequest, RackKind.Full, "P1", "M5")]
    [InlineData("PH2-07", "X1", RequestType.PickupRequest, RackKind.Full, "X1", "Y1")]
    [InlineData("PH2-08", "X2", RequestType.DeliveryRequest, RackKind.Empty, "P3", "X2")]
    [InlineData("PH2-09", "Y1", RequestType.PickupRequest, RackKind.Empty, "Y1", "X1")]
    [InlineData("PH2-10", "Y2", RequestType.DeliveryRequest, RackKind.Full, "P3", "Y2")]
    [InlineData("PH2-11", "X4", RequestType.PickupRequest, RackKind.Full, "X4", "Z1")]
    [InlineData("PH2-12", "Z2", RequestType.PickupRequest, RackKind.Empty, "Z2", "X3")]
    public async Task Every_published_default_runs_its_whole_life_in_the_simulator_once_Ready(string code, string at, RequestType type, RackKind kind, string source, string destination)
    {
        await using var kit = await CommissionedAsync();
        await kit.H.StockAsync(source, kind, 1);
        await RunAsync(kit, code, at, type, kind, source, destination, "life-" + code);
    }

    [Theory]
    [InlineData("PH2-01", "A1", RackKind.Full, "B1", "B2", "P1", "P2", "M5")]
    [InlineData("PH2-03", "B1", RackKind.Empty, "A1", "A2", "P1", "P2", "M5")]
    [InlineData("PH2-07", "X1", RackKind.Full, "Y1", "Y2", "P3", "P4", "M6")]
    [InlineData("PH2-09", "Y1", RackKind.Empty, "X1", "X2", "P3", "P4", "M6")]
    public async Task Full_and_empty_racks_go_to_the_buffer_when_the_stations_are_full_and_to_manual_QC_when_the_buffer_is_full(string code, string at, RackKind kind, string s1, string s2, string b1, string b2, string manual)
    {
        await using var kit = await CommissionedAsync();
        await kit.H.StockAsync(s1, kind, 1); await kit.H.StockAsync(s2, kind, 1);
        await kit.H.StockAsync(at, kind, 1);
        await RunAsync(kit, code, at, RequestType.PickupRequest, kind, at, b1, "o1");
        await kit.H.StockAsync(at, kind, 1);
        await RunAsync(kit, code, at, RequestType.PickupRequest, kind, at, b2, "o2");
        await kit.H.StockAsync(at, kind, 1);
        await RunAsync(kit, code, at, RequestType.PickupRequest, kind, at, manual, "o3");
    }

    [Fact]
    public async Task A_buffer_position_holds_full_or_empty_and_the_delivery_takes_only_the_right_kind()
    {
        await using var kit = await CommissionedAsync();
        await kit.H.StockAsync("P3", RackKind.Full, 1); await kit.H.StockAsync("P4", RackKind.Empty, 1);
        await RunAsync(kit, "PH2-08", "X1", RequestType.DeliveryRequest, RackKind.Empty, "P4", "X1", "e");
        await RunAsync(kit, "PH2-10", "Y1", RequestType.DeliveryRequest, RackKind.Full, "P3", "Y1", "f");
    }

    [Theory]
    [InlineData("PH2-11", "X3", RackKind.Full, "Z1", "Z2")]
    [InlineData("PH2-12", "Z1", RackKind.Empty, "X3", "X4")]
    public async Task The_direct_group_waits_when_both_destinations_are_full_and_uses_no_buffer(string code, string at, RackKind kind, string d1, string d2)
    {
        await using var kit = await CommissionedAsync();
        await kit.H.StockAsync(d1, kind, 1); await kit.H.StockAsync(d2, kind, 1); await kit.H.StockAsync(at, kind, 1);
        var order = (await kit.StartAsync("w", at, RequestType.PickupRequest, kind, code)).Order;
        var leg = order.Legs.Single();
        await kit.PickAsync(leg.Id); await kit.TickAsync();
        var waiting = await kit.OrderAsync(order.Id);
        Assert.Equal(OrderState.Waiting, waiting.State);
        Assert.Null(waiting.Legs.Single().DestinationLocationCode);
        foreach (var other in new[] { "P1", "P3", "M5", "M6" }) Assert.Null(await kit.RackAtAsync(other));
    }

    // ================================================================== idempotence, user data, configurability

    [Fact]
    public async Task The_seed_applies_once_never_restores_what_an_Admin_disabled_and_never_touches_an_existing_code()
    {
        await using var kit = await NewDeploymentAsync();
        var (mine, _) = await kit.CreateProcessAsync("PH2-02", RequestType.DeliveryRequest, RackKind.Empty, ["A1"], new("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "P1")]), Done()]));
        var first = await SeedAsync(kit);
        Assert.Equal(["PH2-02"], first.Skipped);
        var untouched = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().GetAsync(mine));
        Assert.Equal("PH2-02", untouched.Name); Assert.Null(untouched.PublishedVersion); Assert.Single(untouched.Versions);

        var version = await VersionOfAsync(kit, "PH2-01");
        await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().DisableAsync(version));
        await kit.H.RestartAsync();
        var second = await SeedAsync(kit);
        Assert.Empty(second.Published); Assert.Empty(second.LeftDraft); Assert.Empty(second.Skipped);
        Assert.DoesNotContain("PH2-01", (await StartableAsync(kit)).Select(s => s.Code));
        Assert.Equal(1, await kit.H.Db(d => d.AuditLog.CountAsync(a => a.Action == DefaultProcessSeeder.CatalogAppliedAction)));
    }

    [Fact]
    public async Task Instances_starting_together_publish_each_default_once()
    {
        await using var kit = await NewDeploymentAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => SeedAsync(kit))));
        Assert.Equal(11, results.Sum(r => r.Published.Count));
        Assert.Equal(12, await kit.H.Db(d => d.BusinessProcesses.CountAsync()));
    }

    [Fact]
    public async Task Without_the_locations_the_defaults_stay_drafts_and_a_taken_trigger_is_not_published_twice()
    {
        await using var kit = await NewDeploymentAsync(locations: false);
        var none = await SeedAsync(kit);
        Assert.Empty(none.Published); Assert.Equal(12, none.LeftDraft.Count);

        await using var taken = await NewDeploymentAsync();
        var (_, v) = await taken.CreateProcessAsync("MINE", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "P1")], "AfterPick"), Done()]));
        await taken.PublishAsync(v);
        var result = await SeedAsync(taken);
        Assert.Contains("PH2-01", result.LeftDraft);
        Assert.DoesNotContain("PH2-01", result.Published);
        Assert.Contains("PH2-02", result.Published);
    }

    [Fact]
    public async Task A_process_the_user_creates_for_a_new_route_appears_in_the_list_without_any_code_change()
    {
        await using var kit = await NewDeploymentAsync();
        await SeedAsync(kit);
        await kit.H.Db(async d => { d.Locations.Add(new Location("NEW-1", "new", LocationType.Buffer, 1, AllowedRackKind.Any, false, kit.H.Clock.UtcNow)); d.Locations.Add(new Location("NEW-2", "new", LocationType.Buffer, 1, AllowedRackKind.Any, false, kit.H.Clock.UtcNow)); await d.SaveChangesAsync(); return 0; });
        var (_, v) = await kit.CreateProcessAsync("MY-ROUTE", RequestType.ManualRequest, RackKind.Full, ["NEW-1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "NEW-2")]), Done()]));
        await kit.PublishAsync(v);
        var item = (await StartableAsync(kit)).Single(s => s.Code == "MY-ROUTE");
        Assert.True(item.AcceptsRequests); Assert.Equal(["NEW-1"], item.TriggerLocationCodes);   // no device condition: behaviour as before
        Assert.Equal(12, (await StartableAsync(kit)).Count);
        await kit.H.StockAsync("NEW-1", RackKind.Full, 1);
        await RunAsync(kit, "MY-ROUTE", "NEW-1", RequestType.ManualRequest, RackKind.Full, "NEW-1", "NEW-2", "mine");
    }
}
