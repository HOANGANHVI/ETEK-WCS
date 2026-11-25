using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Common;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Locations;
using WCS.Domain.Routing;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>CR-010: configurable Business Process (draft, validate, publish, versions), reference import, protected rules, PLC provisioning and recovery.</summary>
[Collection("Database")]
public class Cr010ProcessConfigTests(DatabaseFixture fixture)
{
    private static readonly string[] A = ["A1", "A2"];

    private static VersionDefinition To(string destination) =>
        new("s1", [Move("s1", 1, "Delivery", "Request", "Candidates", "done", [Dest(1, destination)]), Done()]);

    // ------------------------------------------------------------------ 21 / 22: the route is data
    [Fact]
    public async Task Test21_and_22_changing_A1_to_B1_into_A1_to_C1_needs_no_code_and_a_running_task_keeps_its_old_version()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (processId, v1) = await kit.CreateProcessAsync("T-AB", RequestType.PickupRequest, RackKind.Full, A, To("B1"));
        await kit.PublishAsync(v1);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);

        var order1 = (await kit.StartAsync("old", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        Assert.Equal("B1", order1.Legs.Single().DestinationLocationCode);
        Assert.Equal(1, order1.ProcessVersion);

        // Admin edits the route: a NEW version (copy of v1), destination C1 instead of B1, validate, publish. No build, no code.
        var v2 = await kit.With(async sp =>
        {
            var admin = sp.GetRequiredService<ProcessAdminService>();
            var nv = await admin.NewVersionAsync(processId, v1, "admin");
            await admin.SaveDefinitionAsync(nv.Id, To("C1"));
            return nv.Id;
        });
        var published = await kit.PublishAsync(v2);
        Assert.Equal(2, published.Number);

        // the old version is Disabled, exactly one version is Published
        var versions = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().GetAsync(processId));
        Assert.Equal(2, versions.PublishedVersion);
        Assert.Equal("Disabled", versions.Versions.Single(v => v.Number == 1).State);

        var order2 = (await kit.StartAsync("new", "A2", RequestType.PickupRequest, RackKind.Full)).Order;
        Assert.Equal("C1", order2.Legs.Single().DestinationLocationCode);
        Assert.Equal(2, order2.ProcessVersion);

        // the task started under version 1 still goes to B1 and finishes there
        var leg = (await kit.OrderAsync(order1.Id)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id);
        var done = await kit.OrderAsync(order1.Id);
        Assert.Equal(OrderState.Completed, done.State);
        Assert.Equal(1, done.ProcessVersionNumber);
        Assert.NotNull(await kit.RackAtAsync("B1"));
        Assert.Contains("\"B1\"", done.SnapshotJson);
        Assert.DoesNotContain("\"C1\"", done.SnapshotJson);
    }

    [Fact]
    public async Task A_published_version_cannot_be_edited_and_a_draft_cannot_be_published_without_validation()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v1) = await kit.CreateProcessAsync("T-E", RequestType.PickupRequest, RackKind.Full, A, To("B1"));
        await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().PublishAsync(v1, "x")));   // still Draft
        await kit.PublishAsync(v1);
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SaveDefinitionAsync(v1, To("C1"))));
        Assert.Equal("VERSION_NOT_EDITABLE", ex.Code);
    }

    // ------------------------------------------------------------------ 23 / 24: validation before publish
    private static async Task<IReadOnlyList<ProcessIssue>> IssuesAsync(RoutingKit kit, VersionDefinition def, string code = "T-X", RackKind kind = RackKind.Full)
    {
        var (_, v) = await kit.CreateProcessAsync(code, RequestType.PickupRequest, kind, A, def);
        var res = await kit.ValidateAsync(v);
        Assert.Equal("Draft", res.State);                       // an invalid version never becomes Validated
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().PublishAsync(v, "x")));
        Assert.Equal("VERSION_NOT_VALIDATED", ex.Code);          // and therefore can never be published
        return res.Issues;
    }

    private static void Has(IReadOnlyList<ProcessIssue> issues, string code) => Assert.Contains(issues, i => i.Code == code && i.Severity == IssueSeverity.Error);

    [Fact]
    public async Task Test23_invalid_branches_are_reported_and_block_publishing()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var cond = new StepDefinition("c1", 1, "Condition", null, null, null, "m1", null, null, null, [Cond("LocationEnabled", null, "B1")], null, null, null);
        Has(await IssuesAsync(kit, new("c1", [cond, Move("m1", 2, "Delivery", "Request", "Candidates", "done", [Dest(1, "B1")]), Done()]), "T-1"), "CONDITION_NO_ALTERNATIVE");

        Has(await IssuesAsync(kit, new("s1", [Move("s1", 1, "Delivery", "Request", "Candidates", "done", [Dest(1, "B1")]), Done(), Done("orphan", 5)]), "T-2"), "STEP_ORPHAN");

        Has(await IssuesAsync(kit, new("s1", [Move("s1", 1, "Delivery", "Request", "Candidates", "done", [Dest(1, "ZZ")]), Done()]), "T-3"), "LOCATION_NOT_FOUND");

        await kit.H.Db(async db => { var l = await db.Locations.SingleAsync(x => x.Code == "B2"); l.Configure("B2", 1, AllowedRackKind.Any, false, true, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        Has(await IssuesAsync(kit, To("B2"), "T-4"), "LOCATION_DISABLED");

        Has(await IssuesAsync(kit, new("s1", [Move("s1", 1, "BufferStorage", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]), "T-5"), "BUFFER_NOT_ALLOWED");

        Has(await IssuesAsync(kit, To("F3"), "T-6"), "RACK_TYPE_MISMATCH");     // F3 holds empty racks only; the process moves full racks
    }

    [Fact]
    public async Task Test23_loops_and_destinations_that_cannot_store_a_rack_are_refused()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var c1 = new StepDefinition("c1", 1, "Condition", null, null, null, "c2", "done", null, null, [Cond("LocationEnabled", null, "B1")], null, null, null);
        var c2 = new StepDefinition("c2", 2, "Condition", null, null, null, "c1", "done", null, null, [Cond("LocationEnabled", null, "B1")], null, null, null);
        Has(await IssuesAsync(kit, new("c1", [c1, c2, Done()]), "T-LOOP"), "ROUTE_CYCLE");

        await kit.H.Db(async db => { db.Locations.Add(new Location("W1", "W1", LocationType.WaitingPosition, 0, AllowedRackKind.Any, false, kit.H.Clock.UtcNow)); await db.SaveChangesAsync(); return 0; });
        Has(await IssuesAsync(kit, To("W1"), "T-WAIT"), "DESTINATION_NOT_STORAGE");        // a waiting position is not a storage location
    }

    [Fact]
    public async Task Test24_a_branch_that_never_reaches_a_completion_is_reported()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var issues = await IssuesAsync(kit, new("s1", [Move("s1", 1, "Delivery", "Request", "Candidates", null, [Dest(1, "B1")]), Done()]), "T-DEAD");
        Has(issues, "STEP_NO_NEXT");
        Has(issues, "NO_PATH_TO_COMPLETION");
        Has(issues, "STEP_ORPHAN");     // the completion step is unreachable too
    }

    [Fact]
    public async Task Unsupported_condition_types_cannot_even_be_saved_and_script_text_is_never_executed()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var bad = new StepDefinition("c1", 1, "Condition", null, null, null, "done", "done", null, null, [Cond("Javascript", "1==1", "B1")], null, null, null);
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.CreateProcessAsync("T-JS", RequestType.PickupRequest, RackKind.Full, A, new("c1", [bad, Done()])));
        Assert.Equal("DEFINITION_INVALID", ex.Code);
    }

    [Fact]
    public async Task Condition_steps_choose_the_branch_from_inventory_state_not_from_names()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var c = new StepDefinition("c1", 1, "Condition", null, null, null, "toB1", "toC1", null, null, [Cond("InventoryState", "HasFreeSlot", "B1")], null, null, null);
        var def = new VersionDefinition("c1", [c,
            Move("toB1", 2, "Delivery", "Request", "Candidates", "done", [Dest(1, "B1")]),
            Move("toC1", 3, "Delivery", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]);
        var (_, v) = await kit.CreateProcessAsync("T-COND", RequestType.PickupRequest, RackKind.Full, A, def);
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);
        await kit.H.StockAsync("B1", RackKind.Full, 1);                                           // B1 full: the condition is false
        var o = (await kit.StartAsync("c-1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        Assert.Equal("C1", o.Legs.Single().DestinationLocationCode);
    }

    [Fact]
    public async Task An_offline_PLC_makes_its_port_unusable_so_nothing_is_dispatched_to_it()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-OFF", RequestType.PickupRequest, RackKind.Full, A, To("B1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        kit.Link.Offline.Add("PLC-T2");                                                           // B1 is served by PLC-T2
        var o = (await kit.StartAsync("off", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        Assert.Equal(nameof(OrderState.Waiting), o.State);
        Assert.Empty(o.Legs);
        kit.Link.Offline.Clear();
        await kit.TickAsync();
        Assert.Equal(LegState.InProgress, (await kit.OrderAsync(o.Id)).Legs.Single().State);       // back online: the order continues by itself
    }

    // ------------------------------------------------------------------ reference processes and protected rules
    [Fact]
    public async Task Reference_import_creates_drafts_only_idempotently_and_marks_BP04_as_review()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var first = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().ImportReferenceAsync(ReferenceProcessCatalog.Load(), "admin"));
        var second = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().ImportReferenceAsync(ReferenceProcessCatalog.Load(), "admin"));
        Assert.Equal(18, first.Imported);   // CR-039: PH2-07..12 added to the reference data
        Assert.Equal((0, 18), (second.Imported, second.Skipped));
        var list = await kit.With(sp => sp.GetRequiredService<ProcessAdminService>().ListAsync());
        Assert.All(list, p => Assert.Null(p.PublishedVersion));                                    // nothing is published by an import
        Assert.True(list.Single(p => p.Code == "BP-04").ReviewBlocked);
        Assert.False(list.Single(p => p.Code == "PH2-04").ReviewBlocked);                           // D-09 approved PH2-04 (applied by CR-039); BP-04 stays REVIEW
    }

    [Fact]
    public async Task Test20_BP04_stays_REVIEW_it_cannot_be_validated_published_or_started()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var all = await kit.ImportReferenceAsync();
        var v = await kit.ValidateAsync(all["BP-04"].VersionId);
        Assert.Equal("Draft", v.State);
        Has(v.Issues, "PROCESS_REVIEW_BLOCKED");
        await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().PublishAsync(all["BP-04"].VersionId, "x")));
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("bp4", "B1", RequestType.DeliveryRequest, RackKind.Full, "BP-04"));
        Assert.Equal("PROCESS_REVIEW_BLOCKED", ex.Code);
    }

    [Fact]
    public async Task The_approved_phase_1_flows_validate_and_publish_from_the_reference_configuration_but_phase_2_needs_its_own_locations()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var all = await kit.ImportReferenceAsync();
        foreach (var c in new[] { "BP-01", "BP-02", "BP-03", "BP-05", "BP-06" }) await kit.PublishAsync(all[c].VersionId);

        var ph2 = await kit.ValidateAsync(all["PH2-01"].VersionId);
        Assert.Equal("Draft", ph2.State);
        Has(ph2.Issues, "LOCATION_NOT_FOUND");                                                     // P1/P2/M5 are not Phase 1 locations; F-names are not mapped to them
    }

    [Fact]
    public async Task Test18_A3_A4_D1_D2_are_protected_a_route_may_not_use_them()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await kit.H.Db(async db => { db.Locations.Add(new Location("A3", "A3", LocationType.Station, 1, AllowedRackKind.Any, true, kit.H.Clock.UtcNow)); await db.SaveChangesAsync(); return 0; });
        Has(await IssuesAsync(kit, To("A3"), "T-A3"), "LOCATION_PROTECTED");
        // a PLC request that points at A3 (code 3) never starts anything: no process and no port are configured for it
        var r = await kit.PlcRequestAsync("PLC-T1", null, "A3-REQ", true, rackFull: 3);
        Assert.Equal(PlcRequestOutcome.Rejected, r.Outcome);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));
    }

    [Fact]
    public async Task PLC_position_codes_outside_the_confirmed_1_to_4_are_refused_without_choosing_a_substitute()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var r = await kit.PlcRequestAsync("PLC-T1", null, "CODE-9", true, rackFull: 9);
        Assert.Equal("UNSUPPORTED_LOCATION_CODE", r.Code);
        Assert.Empty(await kit.TransactionsAsync());
    }

    // ------------------------------------------------------------------ 17: unapproved mapping
    [Fact]
    public async Task Test17_a_route_through_a_port_with_an_unapproved_IO_mapping_can_be_published_but_accepts_no_request()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture, approvePorts: false);
        var (_, v) = await kit.CreateProcessAsync("T-NA", RequestType.PickupRequest, RackKind.Full, A, To("B1"));
        var res = await kit.ValidateAsync(v);
        Assert.Equal("Validated", res.State);                                                      // CR-039 (D-11): a device condition belongs to Readiness, not to Publication
        Assert.Contains(res.Issues, i => i.Code == "PORT_MAPPING_NOT_APPROVED" && i.Severity == IssueSeverity.Warning);
        await kit.PublishAsync(v);
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.StartAsync("na", "A1", RequestType.PickupRequest, RackKind.Full, "T-NA"));
        Assert.Equal("PROCESS_NOT_READY", ex.Code);                                               // so no physical operation can start on it, even without RouteEngine:EnforceReadiness
        var ready = await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<ProcessAdminService>().SetReadinessAsync(v, "Ready", "try", "admin")));
        Assert.Equal("READINESS_CONDITIONS_UNMET", ready.Code);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));
    }

    // ------------------------------------------------------------------ 15 / 16: PLC provisioning and independent mapping
    [Fact]
    public async Task Test15_and_16_a_new_PLC_gets_an_independent_copy_of_the_template_and_editing_one_PLC_changes_no_other()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var templateBefore = kit.H.Services.GetRequiredService<IPlcTemplateProvider>().Signals.Select(s => (s.SignalName, s.TemplateAddressReference)).ToList();
        var seeded = await kit.H.Db(d => d.PlcConnections.CountAsync());

        // more than four PLCs exist in total: the number of PLCs is data
        foreach (var n in new[] { "N1", "N2", "N3", "N4", "N5" })
            await kit.With(sp => sp.GetRequiredService<PlcProvisioningService>().AddPlcAsync(new AddPlcCommand($"PLC-{n}", $"PLC {n}", "10.9.9.9", [new AddPlcStationCommand($"{n}-a", null), new AddPlcStationCommand($"{n}-b", null)]), "admin"));
        Assert.Equal(seeded + 5, await kit.H.Db(d => d.PlcConnections.CountAsync()));

        var perPort = templateBefore.Count;
        Assert.Equal(perPort, await kit.H.Db(d => d.StationIoPoints.CountAsync(p => p.StationId == "N1-a")));
        Assert.Equal(perPort, await kit.H.Db(d => d.StationIoPoints.CountAsync(p => p.StationId == "N5-b")));

        // a new PLC is disabled, has no network parameter, and nothing is approved
        var plc = await kit.H.Db(d => d.PlcConnections.AsNoTracking().SingleAsync(c => c.Id == "PLC-N1"));
        Assert.False(plc.Enabled);
        Assert.NotEmpty(plc.MissingParameters());
        Assert.Equal(0, await kit.H.Db(d => d.StationIoPoints.CountAsync(p => p.PlcId == "PLC-N1" && p.Status != WCS.Domain.Plc.IoConfigStatus.NeedsConfiguration)));

        // edit one point of PLC-N1: no other PLC, no other port and not the template changes
        var point = await kit.H.Db(d => d.StationIoPoints.AsNoTracking().FirstAsync(p => p.PlcId == "PLC-N1" && p.StationId == "N1-a" && p.DataType == WCS.Domain.Plc.PlcDataType.Bool));
        await kit.With(sp => sp.GetRequiredService<PlcConfigService>().SetAddressAsync(point.Id, new SetPointAddressCommand("M5000", null, null)));
        Assert.Equal(1, await kit.H.Db(d => d.StationIoPoints.CountAsync(p => p.ActualAddress == "M5000")));
        Assert.Equal(0, await kit.H.Db(d => d.StationIoPoints.CountAsync(p => p.PlcId != "PLC-N1" && p.Status != WCS.Domain.Plc.IoConfigStatus.NeedsConfiguration && p.PlcId.StartsWith("PLC-N"))));
        Assert.Equal(0, await kit.H.Db(d => d.StationIoPoints.CountAsync(p => p.PlcId == "PLC-N1" && p.StationId == "N1-b" && p.ActualAddress != null)));
        Assert.Equal(templateBefore, kit.H.Services.GetRequiredService<IPlcTemplateProvider>().Signals.Select(s => (s.SignalName, s.TemplateAddressReference)).ToList());

        // a route through the new (unapproved, disabled) PLC's port is refused at publish time
        await kit.H.Db(async db => { db.Locations.Add(new Location("X9", "X9", LocationType.Station, 1, AllowedRackKind.Any, true, kit.H.Clock.UtcNow)); await db.SaveChangesAsync(); return 0; });
        await kit.With(sp => sp.GetRequiredService<PlcProvisioningService>().SetPortLinkAsync("N1-a", "X9", true));
        Assert.Equal("X9", (await kit.H.Db(d => d.PlcStations.AsNoTracking().SingleAsync(s => s.Id == "N1-a"))).LocationCode);
    }

    [Fact]
    public async Task A_location_can_be_linked_to_only_one_port()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var ex = await Assert.ThrowsAsync<AppException>(() => kit.With(sp => sp.GetRequiredService<PlcProvisioningService>().SetPortLinkAsync("A2", "A1", true)));
        Assert.Equal("LOCATION_ALREADY_LINKED", ex.Code);
    }

    // ------------------------------------------------------------------ 10: PLC disconnect and recovery
    [Fact]
    public async Task Test10_PLC_disconnect_keeps_the_transaction_and_reconnect_reconciles_with_the_real_state_before_continuing()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-REC", RequestType.PickupRequest, RackKind.Full, A, To("B1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var req = await kit.PlcRequestAsync("PLC-T1", "A1", "R-REC", true, rackFull: 1);
        var orderId = req.Order!.Id;
        var txId = req.Transaction!.Id;

        // link lost: state is kept, nothing is cancelled, no signal is touched
        await kit.With(sp => sp.GetRequiredService<StationTransactionService>().SuspendPlcAsync("PLC-T1", "PLC_LINK_LOST"));
        var tx = (await kit.TransactionsAsync()).Single(t => t.Id == txId);
        Assert.True(tx.Suspended);
        Assert.True(tx.IsActive);
        Assert.NotEqual(OrderState.Cancelled, (await kit.OrderAsync(orderId)).State);
        Assert.NotEqual(OrderState.Failed, (await kit.OrderAsync(orderId)).State);

        // reconnect but the PLC cannot be read: nothing is decided, the transaction stays suspended
        var none = await kit.With(sp => sp.GetRequiredService<StationTransactionService>().RecoverPlcAsync("PLC-T1", kit.Observations));
        Assert.All(none, r => Assert.Equal("NoData", r.Decision));
        Assert.True((await kit.TransactionsAsync()).Single(t => t.Id == txId).Suspended);

        // the PLC shows the same request further along: resume at that step (no step is repeated, nothing is re-dispatched)
        var legsBefore = (await kit.OrderAsync(orderId)).Legs.Count;
        kit.Observations.ByPort["A1"] = new PlcHandshakeObservation("R-REC", Enumerable.Range(1, 9).ToDictionary(i => i, i => i <= 4));
        var resumed = await kit.With(sp => sp.GetRequiredService<StationTransactionService>().RecoverPlcAsync("PLC-T1", kit.Observations));
        Assert.Equal("Resume", resumed.Single(r => r.PortId == "A1").Decision);
        tx = (await kit.TransactionsAsync()).Single(t => t.Id == txId);
        Assert.False(tx.Suspended);
        Assert.Equal(4, tx.HandshakeStep);
        Assert.Equal(legsBefore, (await kit.OrderAsync(orderId)).Legs.Count);

        // a different Request_ID on the PLC: the outcome cannot be inferred -> reconciliation for the transaction AND its task
        kit.Observations.ByPort["A1"] = new PlcHandshakeObservation("OTHER", Enumerable.Range(1, 9).ToDictionary(i => i, i => i <= 4));
        await kit.With(sp => sp.GetRequiredService<StationTransactionService>().RecoverPlcAsync("PLC-T1", kit.Observations));
        Assert.Equal(StationTransactionState.ReconciliationRequired, (await kit.TransactionsAsync()).Single(t => t.Id == txId).State);
        Assert.Equal(OrderState.ReconciliationRequired, (await kit.OrderAsync(orderId)).State);
        await kit.TickAsync();
        Assert.Equal(OrderState.ReconciliationRequired, (await kit.OrderAsync(orderId)).State);   // the engine does not move it on its own
    }

    [Fact]
    public async Task A_request_whose_task_could_not_start_is_closed_and_the_port_is_free_again()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);      // no process is published
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var r = await kit.PlcRequestAsync("PLC-T1", "A1", "NOPROC", true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.Rejected, r.Outcome);
        Assert.Equal("PROCESS_NOT_FOUND", r.Code);
        Assert.DoesNotContain(await kit.TransactionsAsync(), t => t.IsActive);                    // the port was released
    }
}
