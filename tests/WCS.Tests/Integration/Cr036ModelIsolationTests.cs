using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Routing;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-036 Batch 1: Product Model isolation and Manual QC protection (P2-A design: INV-M1…M6, MI-xx, MQ-xx, RI-xx, MG-xx). All data is created through the real services and the real PostgreSQL schema;
/// the model world ("MA-*" = model A, "MB-*" = model B) is test data, not production configuration. The mode is switched at run time through a mutable options holder.
/// </summary>
[Collection("Database")]
public class Cr036ModelIsolationTests(DatabaseFixture fixture)
{
    private sealed class ModeHolder : IOptions<ModelIsolationOptions> { public ModelIsolationOptions Value { get; } = new(); }
    private sealed record World(RoutingKit Kit, ModeHolder Mode, Guid ModelA, Guid ModelB) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Kit.DisposeAsync();
    }

    private static readonly string[] CncA = ["MA-A"];

    private async Task<World> WorldAsync(ModelIsolationMode mode = ModelIsolationMode.Enforce)
    {
        var holder = new ModeHolder(); holder.Value.Mode = mode;
        var kit = await RoutingKit.CreateAsync(fixture, more: s => s.AddSingleton<IOptions<ModelIsolationOptions>>(holder));
        var (a, b) = await kit.H.Db(async db =>
        {
            var now = kit.H.Clock.UtcNow;
            var ma = new ProductModel("T-MA", "Model A", now); var mb = new ProductModel("T-MB", "Model B", now);
            db.ProductModels.AddRange(ma, mb);
            void L(string code, LocationType t, int cap, Guid m, StationRole r)
            {
                var l = new Location(code, code, t, cap, AllowedRackKind.Any, false, now); l.SetModelAndRole(m, r, now); db.Locations.Add(l);
            }
            L("MA-A", LocationType.Station, 3, ma.Id, StationRole.CncStation);
            L("MA-B", LocationType.Station, 1, ma.Id, StationRole.AutoQcStation);
            L("MA-P1", LocationType.Buffer, 1, ma.Id, StationRole.BufferPosition);
            L("MA-P2", LocationType.Buffer, 1, ma.Id, StationRole.BufferPosition);
            L("MA-M5", LocationType.ManualBuffer, 5, ma.Id, StationRole.ManualQcStation);
            L("MB-A", LocationType.Station, 3, mb.Id, StationRole.CncStation);
            L("MB-B", LocationType.Station, 1, mb.Id, StationRole.AutoQcStation);
            L("MB-P1", LocationType.Buffer, 1, mb.Id, StationRole.BufferPosition);
            await db.SaveChangesAsync();
            return (ma.Id, mb.Id);
        });
        return new World(kit, holder, a, b);
    }

    // ------------------------------------------------------------------ builders
    private static VersionDefinition FullOut(params string[] dests) =>
        new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", dests.Select((d, i) => Dest(i + 1, d)).ToArray()), Done()]);

    private static VersionDefinition EmptyIn(params string[] sources) =>
        new("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", sources.Select((s, i) => Src(i + 1, s)).ToArray()), Done()]);

    private async Task<Guid> ProcessAsync(World w, string code, RequestType type, RackKind kind, string[] triggers, VersionDefinition def, Guid? model, bool publish = true)
    {
        var v = await w.Kit.With(async sp =>
        {
            var admin = sp.GetRequiredService<ProcessAdminService>();
            var p = await admin.CreateAsync(new ProcessHeader(code, code, null, type.ToString(), kind.ToString(), triggers, model), "test");
            await admin.SaveDefinitionAsync(p.Versions[0].Id, def);
            return p.Versions[0].Id;
        });
        if (publish) await w.Kit.PublishAsync(v);
        return v;
    }

    /// <summary>Puts a rack at a location directly in the database (test data, including inconsistent data a real stock-in would refuse).</summary>
    private Task<Guid> PutRackAsync(World w, string location, RackKind kind, Guid? model, bool manual = false) => w.Kit.H.Db(async db =>
    {
        var loc = await db.Locations.SingleAsync(l => l.Code == location);
        var rack = new Rack(kind == RackKind.Full ? RackStatus.Full : RackStatus.Empty, loc.Id, null, null, w.Kit.H.Clock.UtcNow);
        if (model is { } m) rack.AssignModel(m);
        if (manual) rack.MarkManualHandling();
        db.Racks.Add(rack); await db.SaveChangesAsync(); return rack.Id;
    });

    private Task<Rack> RackAsync(World w, Guid id) => w.Kit.H.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == id));
    private Task<Location> LocAsync(World w, string code) => w.Kit.H.Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Code == code));
    private Task<List<RoutingEvent>> EventsAsync(World w, Guid orderId) => w.Kit.H.Db(db => db.RoutingEvents.AsNoTracking().Where(e => e.OrderId == orderId).ToListAsync());
    private static bool HasGuardEvent(IEnumerable<RoutingEvent> events, string code) => events.Any(e => e.Kind == "MODEL_GUARD" && (e.Detail ?? "").Contains(code));

    private static async Task<AppException> Refused(Func<Task> act) => await Assert.ThrowsAsync<AppException>(act);

    // ================================================================== Model isolation (INV-M1 / M2 / M6)

    [Fact]
    public async Task MI01_a_rack_of_another_model_is_never_taken_and_the_order_waits()
    {
        await using var w = await WorldAsync();
        // process of model B: empty rack for MB-A comes from MB-P1; the rack standing at MB-P1 belongs to model A (inconsistent data)
        await ProcessAsync(w, "T-MB-EMPTY", RequestType.DeliveryRequest, RackKind.Empty, ["MB-A"], EmptyIn("MB-P1"), w.ModelB);
        var wrong = await PutRackAsync(w, "MB-P1", RackKind.Empty, w.ModelA);
        var res = await w.Kit.StartAsync("mi01", "MB-A", RequestType.DeliveryRequest, RackKind.Empty);
        Assert.Equal("Waiting", res.Order.State); Assert.Empty(res.Order.Legs);
        Assert.True(HasGuardEvent(await EventsAsync(w, res.Order.Id), ModelCodes.ModelMismatch));
        var r = await RackAsync(w, wrong);
        Assert.Equal(RackStatus.Empty, r.Status); Assert.NotNull(r.CurrentLocationId);   // untouched
    }

    [Fact]
    public async Task MI02_publication_is_refused_when_a_candidate_belongs_to_another_model()
    {
        await using var w = await WorldAsync();
        var v = await ProcessAsync(w, "T-MI02", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B", "MB-P1"), w.ModelA, publish: false);
        var dto = await w.Kit.ValidateAsync(v);
        Assert.Contains(dto.Issues, i => i.Code == ModelCodes.ProcessModelMismatch && i.Severity == IssueSeverity.Error);
        Assert.NotEqual("Validated", dto.State);
        await Assert.ThrowsAnyAsync<Exception>(() => w.Kit.With(sp => sp.GetRequiredService<ProcessAdminService>().PublishAsync(v, "test")));
    }

    [Fact]
    public async Task MI03_a_rack_without_a_model_is_not_selected_for_a_modelled_process()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MI03", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B"), w.ModelA);
        await PutRackAsync(w, "MA-A", RackKind.Full, model: null);
        var res = await w.Kit.StartAsync("mi03", "MA-A", RequestType.PickupRequest, RackKind.Full);
        Assert.Equal("Waiting", res.Order.State); Assert.Empty(res.Order.Legs);
        Assert.True(HasGuardEvent(await EventsAsync(w, res.Order.Id), ModelCodes.RackModelUnknown));
    }

    [Fact]
    public async Task MI04_a_destination_of_another_model_is_never_used_even_when_it_is_first_in_the_list()
    {
        await using var w = await WorldAsync();
        // an Off-mode style process that lists a model B buffer first cannot be published in Enforce; build it while Warn is on, then enforce
        w.Mode.Value.Mode = ModelIsolationMode.Warn;
        await ProcessAsync(w, "T-MI04", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MB-P1", "MA-P1"), w.ModelA);
        w.Mode.Value.Mode = ModelIsolationMode.Enforce;
        await w.Kit.H.Scoped(async sp => await sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-A", RackKind.Full, 1, "test", default, w.ModelA));
        var res = await w.Kit.StartAsync("mi04", "MA-A", RequestType.PickupRequest, RackKind.Full);
        Assert.Equal("MA-P1", res.Order.Legs.Single().DestinationLocationCode);
        Assert.True(HasGuardEvent(await EventsAsync(w, res.Order.Id), ModelCodes.ModelMismatch));
    }

    [Fact]
    public async Task MI06_MI07_a_second_model_works_by_data_only_and_the_rack_keeps_its_model_to_the_end()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MB-FULL", RequestType.PickupRequest, RackKind.Full, ["MB-A"], FullOut("MB-B", "MB-P1"), w.ModelB);
        var rackId = (await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MB-A", RackKind.Full, 1, "test", default, w.ModelB))).Single();
        var res = await w.Kit.StartAsync("mi06", "MB-A", RequestType.PickupRequest, RackKind.Full);
        var leg = res.Order.Legs.Single();
        Assert.Equal("MB-B", leg.DestinationLocationCode);
        await w.Kit.PickAsync(leg.Id); await w.Kit.DropAsync(leg.Id);
        var done = await w.Kit.OrderAsync(res.Order.Id);
        Assert.Equal(OrderState.Completed, done.State);
        var rack = await RackAsync(w, rackId);
        Assert.Equal(w.ModelB, rack.ProductModelId);
        Assert.Equal((await LocAsync(w, "MB-B")).Id, rack.CurrentLocationId);
    }

    [Fact]
    public async Task MI08_a_rack_stored_at_a_buffer_keeps_its_model_and_the_buffer_holds_it()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MI08", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B", "MA-P1"), w.ModelA);
        var inv = new Func<string, Task<IReadOnlyList<Guid>>>(loc => w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync(loc, RackKind.Full, 1, "test", default, w.ModelA)));
        await inv("MA-B");                                   // the QC is occupied: its only slot is taken
        var rackId = (await inv("MA-A")).Single();
        var res = await w.Kit.StartAsync("mi08", "MA-A", RequestType.PickupRequest, RackKind.Full);
        var leg = res.Order.Legs.Single();
        Assert.Equal("MA-P1", leg.DestinationLocationCode);
        await w.Kit.PickAsync(leg.Id); await w.Kit.DropAsync(leg.Id);
        var rack = await RackAsync(w, rackId);
        Assert.Equal(w.ModelA, rack.ProductModelId); Assert.Equal(RackDisposition.Auto, rack.Disposition);
        Assert.Equal((await LocAsync(w, "MA-P1")).Id, rack.CurrentLocationId);
    }

    [Fact]
    public async Task MI10_a_process_without_a_model_cannot_use_a_modelled_location()
    {
        await using var w = await WorldAsync();
        var v = await ProcessAsync(w, "T-MI10", RequestType.PickupRequest, RackKind.Full, ["A1"], FullOut("MA-P1"), model: null, publish: false);
        var dto = await w.Kit.ValidateAsync(v);
        Assert.Contains(dto.Issues, i => i.Code == ModelCodes.LegacyProcessModelLocation && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task MI10b_legacy_processes_and_locations_are_untouched_in_every_mode()
    {
        foreach (var mode in new[] { ModelIsolationMode.Off, ModelIsolationMode.Warn, ModelIsolationMode.Enforce })
        {
            await using var w = await WorldAsync(mode);
            await ProcessAsync(w, "T-LEG", RequestType.PickupRequest, RackKind.Full, ["A1"], FullOut("B1", "F1"), model: null);
            await w.Kit.H.StockAsync("A1", RackKind.Full, 1);
            var res = await w.Kit.StartAsync("leg-" + mode, "A1", RequestType.PickupRequest, RackKind.Full);
            Assert.Equal("B1", res.Order.Legs.Single().DestinationLocationCode);   // exactly the historical behaviour
        }
    }

    [Fact]
    public async Task MI11_stock_in_declares_the_model_and_never_infers_it()
    {
        await using var w = await WorldAsync();
        Task<IReadOnlyList<Guid>> Stock(string loc, Guid? model) => w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync(loc, RackKind.Full, 1, "test", default, model));

        Assert.Equal("RACK_MODEL_REQUIRED", (await Refused(() => Stock("MA-A", null))).Code);                 // modelled location, no model given
        Assert.Equal("RACK_MODEL_LOCATION_MISMATCH", (await Refused(() => Stock("MA-A", w.ModelB))).Code);    // wrong model
        Assert.Equal("MODEL_NOT_FOUND", (await Refused(() => Stock("MA-A", Guid.NewGuid()))).Code);
        var ok = (await Stock("MA-A", w.ModelA)).Single();
        Assert.Equal(w.ModelA, (await RackAsync(w, ok)).ProductModelId);
        var legacy = (await Stock("A1", null)).Single();                                                       // a legacy location needs no model
        Assert.Null((await RackAsync(w, legacy)).ProductModelId);                                              // and nothing is inferred
        var txs = await w.Kit.H.Db(db => db.InventoryTransactions.AsNoTracking().Where(t => t.RackId == ok).ToListAsync());
        Assert.Contains(txs, t => t.EventType == InventoryEventType.ModelAssigned);
    }

    [Fact]
    public async Task MI12_the_model_of_a_location_that_holds_racks_cannot_change_and_a_missing_model_in_a_save_never_clears_it()
    {
        await using var w = await WorldAsync();
        await PutRackAsync(w, "MA-A", RackKind.Full, w.ModelA);
        var ex = await Refused(() => w.Kit.With(sp => sp.GetRequiredService<OperationalConfigService>().SaveLocationAsync(
            new SaveLocationCommand("MA-A", "MA-A", LocationType.Station, 3, AllowedRackKind.Any, true, false, w.ModelB, StationRole.CncStation), false)));
        Assert.Equal("LOCATION_HAS_RACKS", ex.Code);

        // an old client / an old configuration package does not know the fields: they are NOT cleared
        await w.Kit.With(sp => sp.GetRequiredService<OperationalConfigService>().SaveLocationAsync(
            new SaveLocationCommand("MA-A", "renamed", LocationType.Station, 3, AllowedRackKind.Any, true, false), false));
        var loc = await LocAsync(w, "MA-A");
        Assert.Equal("renamed", loc.Name); Assert.Equal(w.ModelA, loc.ProductModelId); Assert.Equal(StationRole.CncStation, loc.StationRole);
    }

    [Fact]
    public async Task MI13_assigning_a_model_to_an_existing_rack_is_audited_and_never_changes_an_existing_model()
    {
        await using var w = await WorldAsync();
        var rackId = await PutRackAsync(w, "A1", RackKind.Full, model: null);      // a legacy rack: model unknown
        Task<RackModelAssignmentDto> Assign(Guid model, string? reason = "checked on site") => w.Kit.With(sp => sp.GetRequiredService<ProductModelService>().AssignRackModelAsync(rackId, model, "admin", reason));

        Assert.Equal("REASON_REQUIRED", (await Refused(() => Assign(w.ModelA, " "))).Code);
        var first = await Assign(w.ModelA);
        Assert.True(first.Changed);
        Assert.False((await Assign(w.ModelA)).Changed);                            // idempotent
        Assert.Equal("RACK_MODEL_ALREADY_SET", (await Refused(() => Assign(w.ModelB))).Code);
        var txs = await w.Kit.H.Db(db => db.InventoryTransactions.AsNoTracking().Where(t => t.RackId == rackId && t.EventType == InventoryEventType.ModelAssigned).ToListAsync());
        Assert.Single(txs); Assert.Contains("checked on site", txs[0].Note);

        var inLocationOfOther = await PutRackAsync(w, "MB-A", RackKind.Full, model: null);
        var ex = await Refused(() => w.Kit.With(sp => sp.GetRequiredService<ProductModelService>().AssignRackModelAsync(inLocationOfOther, w.ModelA, "admin", "x")));
        Assert.Equal("RACK_MODEL_LOCATION_MISMATCH", ex.Code);
    }

    [Fact]
    public async Task MI14_a_rack_that_is_reserved_cannot_get_a_model_assigned_under_a_running_job()
    {
        await using var w = await WorldAsync(ModelIsolationMode.Off);
        await ProcessAsync(w, "T-LEG2", RequestType.PickupRequest, RackKind.Full, ["A1"], FullOut("B1"), model: null);
        await w.Kit.H.StockAsync("A1", RackKind.Full, 1);
        var res = await w.Kit.StartAsync("mi14", "A1", RequestType.PickupRequest, RackKind.Full);
        var rackId = (await w.Kit.OrderAsync(res.Order.Id)).Legs.Single().RackId!.Value;
        var ex = await Refused(() => w.Kit.With(sp => sp.GetRequiredService<ProductModelService>().AssignRackModelAsync(rackId, w.ModelA, "admin", "x")));
        Assert.Equal("RACK_RESERVED", ex.Code);
    }

    [Fact]
    public async Task MI15_a_physical_drop_with_a_model_mismatch_is_recorded_and_flagged_never_refused()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MI15", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B"), w.ModelA);
        var rackId = (await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-A", RackKind.Full, 1, "test", default, w.ModelA))).Single();
        var res = await w.Kit.StartAsync("mi15", "MA-A", RequestType.PickupRequest, RackKind.Full);
        var leg = res.Order.Legs.Single();
        await w.Kit.PickAsync(leg.Id);
        // the configuration of the destination changed while the rack was on the AMR (data drift)
        await w.Kit.H.Db(async db => { var l = await db.Locations.SingleAsync(x => x.Code == "MA-B"); l.SetModelAndRole(w.ModelB, StationRole.AutoQcStation, w.Kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });

        var drop = await w.Kit.DropAsync(leg.Id);
        Assert.Equal(EventOutcome.Applied, drop.Outcome);                                          // the physical event is NOT refused
        var rack = await RackAsync(w, rackId);
        Assert.Equal((await LocAsync(w, "MA-B")).Id, rack.CurrentLocationId);                       // inventory shows reality
        Assert.True((await LocAsync(w, "MA-B")).ReconciliationRequired);                            // and the location needs reconciliation
        Assert.Contains(await EventsAsync(w, res.Order.Id), e => e.Kind == ModelCodes.MismatchAtHandover);
    }

    [Fact]
    public async Task MI16_off_changes_nothing_and_warn_records_without_blocking()
    {
        // Off: a rack of another model is used, no event
        await using (var off = await WorldAsync(ModelIsolationMode.Off))
        {
            await ProcessAsync(off, "T-OFF", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B"), off.ModelA);
            await PutRackAsync(off, "MA-A", RackKind.Full, off.ModelB);
            var r = await off.Kit.StartAsync("off", "MA-A", RequestType.PickupRequest, RackKind.Full);
            Assert.Single(r.Order.Legs);
            Assert.DoesNotContain(await EventsAsync(off, r.Order.Id), e => e.Kind == "MODEL_GUARD");
        }
        // Warn: the same data is used, and the violation is recorded
        await using (var warn = await WorldAsync(ModelIsolationMode.Warn))
        {
            await ProcessAsync(warn, "T-WARN", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B"), warn.ModelA);
            await PutRackAsync(warn, "MA-A", RackKind.Full, warn.ModelB);
            var r = await warn.Kit.StartAsync("warn", "MA-A", RequestType.PickupRequest, RackKind.Full);
            Assert.Single(r.Order.Legs);
            Assert.True(HasGuardEvent(await EventsAsync(warn, r.Order.Id), ModelCodes.ModelMismatch));
        }
    }

    // ================================================================== Manual QC protection (INV-M3 / M4 / M5)

    [Fact]
    public async Task MQ01_MQ03_a_rack_in_manual_handling_is_never_a_source_wherever_it_stands()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MQ03", RequestType.DeliveryRequest, RackKind.Empty, ["MA-B"], EmptyIn("MA-P1"), w.ModelA);   // source candidate: a normal buffer
        await PutRackAsync(w, "MA-P1", RackKind.Empty, w.ModelA, manual: true);
        var res = await w.Kit.StartAsync("mq03", "MA-B", RequestType.DeliveryRequest, RackKind.Empty);
        Assert.Equal("Waiting", res.Order.State); Assert.Empty(res.Order.Legs);
        Assert.True(HasGuardEvent(await EventsAsync(w, res.Order.Id), ModelCodes.RackManualHandling));
    }

    [Fact]
    public async Task MQ02_a_manual_qc_station_is_never_a_source_at_run_time_even_if_a_process_was_published_before()
    {
        await using var w = await WorldAsync();
        w.Mode.Value.Mode = ModelIsolationMode.Warn;   // publication only warns
        await ProcessAsync(w, "T-MQ02", RequestType.DeliveryRequest, RackKind.Empty, ["MA-A"], EmptyIn("MA-M5"), w.ModelA);
        w.Mode.Value.Mode = ModelIsolationMode.Enforce;
        var rack = await PutRackAsync(w, "MA-M5", RackKind.Empty, w.ModelA, manual: true);
        var res = await w.Kit.StartAsync("mq02", "MA-A", RequestType.DeliveryRequest, RackKind.Empty);
        Assert.Equal("Waiting", res.Order.State); Assert.Empty(res.Order.Legs);
        Assert.True(HasGuardEvent(await EventsAsync(w, res.Order.Id), ModelCodes.ManualQcNotSource));
        Assert.Equal(RackDisposition.ManualHandling, (await RackAsync(w, rack)).Disposition);   // untouched
    }

    [Fact]
    public async Task MQ04_the_previous_destination_path_does_not_bypass_the_disposition_guard()
    {
        await using var w = await WorldAsync();
        // A -> QC when it has room, else store at the buffer, wait, then buffer -> QC (the shape of the multi-leg process; all data)
        var def = new VersionDefinition("s1",
        [
            Move("s1", 1, "Delivery", "Request", "Candidates", "done", [Dest(1, "MA-B")], wait: "Alternative", alt: "s2"),
            Move("s2", 2, "BufferStorage", "Request", "Candidates", "w1", [Dest(1, "MA-P1")]),
            new StepDefinition("w1", 3, "Waiting", null, null, null, "s3", null, null, null, [Cond("InventoryState", "HasFreeSlot", "MA-B")], null, null, null),
            Move("s3", 4, "BufferRetrieval", "PreviousDestination", "Candidates", "done", [Dest(1, "MA-B")]),
            Done(),
        ]);
        await ProcessAsync(w, "T-MQ04", RequestType.PickupRequest, RackKind.Full, CncA, def, w.ModelA);
        var qcRack = await PutRackAsync(w, "MA-B", RackKind.Full, w.ModelA);                  // QC is occupied
        var rackId = (await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-A", RackKind.Full, 1, "test", default, w.ModelA))).Single();
        var res = await w.Kit.StartAsync("mq04", "MA-A", RequestType.PickupRequest, RackKind.Full);
        var leg1 = res.Order.Legs.Single();
        Assert.Equal("MA-P1", leg1.DestinationLocationCode);
        await w.Kit.PickAsync(leg1.Id); await w.Kit.DropAsync(leg1.Id);                     // the rack is stored at the buffer
        await w.Kit.H.Db(async db =>                                                         // the rack is handed to manual handling meanwhile; the QC is freed
        {
            (await db.Racks.SingleAsync(r => r.Id == rackId)).MarkManualHandling();
            db.Racks.Remove(await db.Racks.SingleAsync(r => r.Id == qcRack));
            await db.SaveChangesAsync(); return 0;
        });
        await w.Kit.TickAsync();
        var order = await w.Kit.OrderAsync(res.Order.Id);
        Assert.Equal(OrderState.ReconciliationRequired, order.State);                       // never dispatched automatically
        Assert.Equal(ModelCodes.RackManualHandling, order.StateReason);
        Assert.Single(order.Legs);                                                          // no second leg
    }

    [Fact]
    public async Task MQ05_a_rack_in_manual_handling_is_not_the_rack_a_plc_pickup_refers_to()
    {
        await using var w = await WorldAsync();
        var rack = (await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("A1", RackKind.Full, 1, "test"))).Single();
        await w.Kit.H.Db(async db => { (await db.Racks.SingleAsync(r => r.Id == rack)).MarkManualHandling(); await db.SaveChangesAsync(); return 0; });
        var enforced = await w.Kit.PlcRequestAsync("PLC-T1", "A1", "REQ-MQ05", pickup: true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.ReconciliationRequired, enforced.Outcome);           // not served automatically

        // the same situation at A2 with the feature OFF: the historical behaviour (the rack counts as the reported rack; no reconciliation because of manual handling)
        var rack2 = (await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("A2", RackKind.Full, 1, "test"))).Single();
        await w.Kit.H.Db(async db => { (await db.Racks.SingleAsync(r => r.Id == rack2)).MarkManualHandling(); await db.SaveChangesAsync(); return 0; });
        w.Mode.Value.Mode = ModelIsolationMode.Off;
        var off = await w.Kit.PlcRequestAsync("PLC-T1", "A2", "REQ-MQ05B", pickup: true, rackFull: 2);
        Assert.NotEqual(PlcRequestOutcome.ReconciliationRequired, off.Outcome);
    }

    [Fact]
    public async Task MQ06_MQ07_publication_refuses_a_manual_qc_station_as_buffer_or_as_source()
    {
        await using var w = await WorldAsync();
        var asBuffer = await ProcessAsync(w, "T-MQ06", RequestType.PickupRequest, RackKind.Full, CncA,
            new VersionDefinition("s1", [Move("s1", 1, "BufferStorage", "Request", "Candidates", "done", [Dest(1, "MA-M5")]), Done()]), w.ModelA, publish: false);
        Assert.Contains((await w.Kit.ValidateAsync(asBuffer)).Issues, i => i.Code == ModelCodes.ManualQcNotBuffer && i.Severity == IssueSeverity.Error);

        var asSource = await ProcessAsync(w, "T-MQ07", RequestType.DeliveryRequest, RackKind.Empty, ["MA-A"], EmptyIn("MA-M5"), w.ModelA, publish: false);
        Assert.Contains((await w.Kit.ValidateAsync(asSource)).Issues, i => i.Code == ModelCodes.ManualQcNotSource && i.Severity == IssueSeverity.Error);

        // PH2-05 / PH2-06 shape: the request location (= the source) is the manual QC station
        var requestAtM5 = await ProcessAsync(w, "T-MQ07B", RequestType.PickupRequest, RackKind.Empty, ["MA-M5"], FullOut("MA-A"), w.ModelA, publish: false);
        Assert.Contains((await w.Kit.ValidateAsync(requestAtM5)).Issues, i => i.Code == ModelCodes.ManualQcNotSource && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task MQ08_a_rack_created_at_a_manual_qc_station_is_in_manual_handling_from_the_start()
    {
        await using var w = await WorldAsync();
        var ids = await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-M5", RackKind.Full, 2, "test", default, w.ModelA));
        foreach (var id in ids) Assert.Equal(RackDisposition.ManualHandling, (await RackAsync(w, id)).Disposition);
        var txs = await w.Kit.H.Db(db => db.InventoryTransactions.AsNoTracking().Where(t => t.EventType == InventoryEventType.DispositionChanged).ToListAsync());
        Assert.Equal(2, txs.Count);
    }

    [Fact]
    public async Task MQ09_MQ10_RI03_RI04_RI05_only_a_confirmed_drop_changes_inventory_and_the_disposition()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MQ10", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B", "MA-P1", "MA-P2", "MA-M5"), w.ModelA);
        foreach (var loc in new[] { "MA-B", "MA-P1", "MA-P2" })                               // QC and both buffer positions are occupied
            await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync(loc, RackKind.Full, 1, "test", default, w.ModelA));
        var rackId = (await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-A", RackKind.Full, 1, "test", default, w.ModelA))).Single();
        var aId = (await LocAsync(w, "MA-A")).Id;

        var res = await w.Kit.StartAsync("mq10", "MA-A", RequestType.PickupRequest, RackKind.Full);
        var leg = res.Order.Legs.Single();
        Assert.Equal("MA-M5", leg.DestinationLocationCode);
        await w.Kit.TickAsync();                                                              // dispatched to the AGV system
        var dispatched = await RackAsync(w, rackId);                                          // RI-03 / MQ-09: dispatch changes nothing
        Assert.Equal(aId, dispatched.CurrentLocationId); Assert.Equal(RackStatus.Full, dispatched.Status); Assert.Equal(RackDisposition.Auto, dispatched.Disposition);

        await w.Kit.PickAsync(leg.Id);                                                        // RI-04: the confirmed pick moves the rack on to the AMR
        var picked = await RackAsync(w, rackId);
        Assert.Equal(RackStatus.InTransit, picked.Status); Assert.Null(picked.CurrentLocationId); Assert.Equal(RackDisposition.Auto, picked.Disposition);

        await w.Kit.DropAsync(leg.Id);                                                        // RI-05 / MQ-10: the confirmed drop
        var dropped = await RackAsync(w, rackId);
        Assert.Equal((await LocAsync(w, "MA-M5")).Id, dropped.CurrentLocationId);            // still in the inventory, at M5
        Assert.Equal(RackStatus.Full, dropped.Status);                                        // Full / Empty is kept
        Assert.Equal(RackDisposition.ManualHandling, dropped.Disposition);
        Assert.Equal(w.ModelA, dropped.ProductModelId);
        var txs = await w.Kit.H.Db(db => db.InventoryTransactions.AsNoTracking().Where(t => t.RackId == rackId).OrderBy(t => t.OccurredAt).ToListAsync());
        Assert.Contains(txs, t => t.EventType == InventoryEventType.PickConfirmed);
        Assert.Contains(txs, t => t.EventType == InventoryEventType.DropConfirmed);
        Assert.Contains(txs, t => t.EventType == InventoryEventType.DispositionChanged);
        Assert.Equal(OrderState.Completed, (await w.Kit.OrderAsync(res.Order.Id)).State);
    }

    [Fact]
    public async Task MQ11_nothing_in_wcs_returns_a_rack_from_manual_handling_to_auto()
    {
        // structural: no public member of the rack or the repositories takes a RackDisposition to set
        var setters = typeof(Rack).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(RackDisposition))).ToList();
        Assert.Empty(setters);
        Assert.False(typeof(Rack).GetProperty(nameof(Rack.Disposition))!.SetMethod?.IsPublic ?? false);

        // behavioural: scheduler ticks and a stock adjustment never change it
        await using var w = await WorldAsync();
        var ids = await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-M5", RackKind.Full, 2, "test", default, w.ModelA));
        for (var i = 0; i < 3; i++) await w.Kit.TickAsync();
        foreach (var id in ids) Assert.Equal(RackDisposition.ManualHandling, (await RackAsync(w, id)).Disposition);
    }

    [Fact]
    public async Task MQ14_the_phase_1_manual_buffer_F5_behaves_exactly_as_before()
    {
        await using var w = await WorldAsync();
        var f5 = await LocAsync(w, "F5");
        Assert.Equal(LocationType.ManualBuffer, f5.Type); Assert.Null(f5.StationRole); Assert.Null(f5.ProductModelId);
        await ProcessAsync(w, "T-F5", RequestType.DeliveryRequest, RackKind.Full, ["B1"],
            new VersionDefinition("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "F5")]), Done()]), model: null);
        await w.Kit.H.StockAsync("F5", RackKind.Full, 1);
        var res = await w.Kit.StartAsync("mq14", "B1", RequestType.DeliveryRequest, RackKind.Full);
        Assert.Equal("F5", res.Order.Legs.Single().SourceLocationCode);                      // F5 is still a valid source for a legacy process
        var rack = await w.Kit.RackAtAsync("F5");
        Assert.Equal(RackDisposition.Auto, rack!.Disposition);
    }

    [Fact]
    public async Task MQ15_without_any_free_destination_and_no_manual_qc_the_order_waits_and_nothing_is_assigned()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-MQ15", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B", "MA-P1"), w.ModelA);
        foreach (var loc in new[] { "MA-B", "MA-P1" })
            await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync(loc, RackKind.Full, 1, "test", default, w.ModelA));
        await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-A", RackKind.Full, 1, "test", default, w.ModelA));
        var res = await w.Kit.StartAsync("mq15", "MA-A", RequestType.PickupRequest, RackKind.Full);
        Assert.Equal("Waiting", res.Order.State); Assert.Empty(res.Order.Legs);
        Assert.Equal(0, await w.Kit.H.Db(db => db.Racks.CountAsync(r => r.Disposition == RackDisposition.ManualHandling)));   // no Manual QC (of any group) was used
    }

    // ================================================================== Reservation / Inventory (RI)

    [Fact]
    public async Task RI01_RI02_a_reserved_or_in_transit_rack_is_not_taken_by_another_job()
    {
        await using var w = await WorldAsync();
        await ProcessAsync(w, "T-RI01", RequestType.PickupRequest, RackKind.Full, CncA, FullOut("MA-B", "MA-P1", "MA-P2"), w.ModelA);
        await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-A", RackKind.Full, 1, "test", default, w.ModelA));   // ONE rack
        var first = await w.Kit.StartAsync("ri01a", "MA-A", RequestType.PickupRequest, RackKind.Full);
        Assert.Single(first.Order.Legs);
        var second = await w.Kit.StartAsync("ri01b", "MA-A", RequestType.PickupRequest, RackKind.Full);
        Assert.Equal("Waiting", second.Order.State); Assert.Empty(second.Order.Legs);          // reserved: not taken (RI-01)

        await w.Kit.PickAsync(first.Order.Legs.Single().Id);                                   // in transit
        await w.Kit.TickAsync();
        var again = await w.Kit.OrderAsync(second.Order.Id);
        Assert.Empty(again.Legs);                                                              // still not taken (RI-02)
    }

    [Fact]
    public async Task RI07_a_stock_adjustment_never_removes_a_rack_that_is_in_manual_handling()
    {
        await using var w = await WorldAsync();
        var ids = await w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync("MA-M5", RackKind.Full, 2, "test", default, w.ModelA));
        var ex = await Refused(() => w.Kit.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().AdjustStockAsync("MA-M5", RackKind.Full, 0, "test", "remove")));
        Assert.Equal("RACK_MANUAL_HANDLING_REQUIRES_APPROVED_ADJUSTMENT", ex.Code);   // CR-036 A.1: the cause is named (nothing is reserved)
        Assert.Equal(2, await w.Kit.H.Db(db => db.Racks.CountAsync(r => ids.Contains(r.Id))));   // the inventory keeps them
    }

    // ================================================================== Migration (MG)

    private const string BeforeMigration = "20261124090000_AddProcessVersionReadiness";
    private static WcsDbContext Context(string cs) => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
    private static async Task<string?> ScalarAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return (await cmd.ExecuteScalarAsync())?.ToString();
    }
    private static async Task ExecAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task MG01_MG02_MG03_the_migration_is_additive_keeps_legacy_data_untouched_and_refuses_an_unsafe_rollback()
    {
        var scratch = new DatabaseFixture();   // its own database: the shared one is never rolled back
        await scratch.InitializeAsync();
        try
        {
            Guid rackId;
            await using (var kit = await RoutingKit.CreateAsync(scratch))
            {
                await kit.H.StockAsync("A1", RackKind.Full, 1);
                var (_, v) = await kit.CreateProcessAsync("T-MG", RequestType.PickupRequest, RackKind.Full, ["A1"], FullOut("B1"));
                await kit.PublishAsync(v);
                rackId = (await kit.H.Db(db => db.Racks.AsNoTracking().SingleAsync())).Id;
            }
            var cs = scratch.ConnectionString;

            // MG-03: with no model data the migration can be rolled back and applied again; legacy rows survive
            await using (var c1 = Context(cs)) await c1.GetService<IMigrator>().MigrateAsync(BeforeMigration);
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='racks' AND column_name IN ('Disposition','ProductModelId')"));
            Assert.Equal("1", await ScalarAsync(cs, "SELECT COUNT(*) FROM racks"));
            await using (var c2 = Context(cs)) await c2.Database.MigrateAsync();

            // MG-01: legacy rows are untouched by the migration: no model, no role, disposition Auto
            Assert.Equal("Auto", await ScalarAsync(cs, $"SELECT \"Disposition\" FROM racks WHERE \"Id\" = '{rackId}'"));
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM racks WHERE \"ProductModelId\" IS NOT NULL OR \"Disposition\" <> 'Auto'"));
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM locations WHERE \"ProductModelId\" IS NOT NULL OR \"StationRole\" IS NOT NULL"));
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM business_processes WHERE \"ProductModelId\" IS NOT NULL"));
            Assert.Equal("0", await ScalarAsync(cs, "SELECT COUNT(*) FROM product_models"));

            // MG-02: a rack in manual handling makes the rollback refuse (it would become dispatchable again)
            await ExecAsync(cs, $"UPDATE racks SET \"Disposition\" = 'ManualHandling' WHERE \"Id\" = '{rackId}'");
            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => { await using var c = Context(cs); await c.GetService<IMigrator>().MigrateAsync(BeforeMigration); });
            Assert.Contains("ROLLBACK_REFUSED", ex.ToString());
            Assert.Equal("ManualHandling", await ScalarAsync(cs, $"SELECT \"Disposition\" FROM racks WHERE \"Id\" = '{rackId}'"));   // nothing was lost
            await ExecAsync(cs, $"UPDATE racks SET \"Disposition\" = 'Auto' WHERE \"Id\" = '{rackId}'");

            // model data also makes it refuse
            await ExecAsync(cs, $"INSERT INTO product_models (\"Id\", \"Code\", \"Name\", \"Enabled\", \"CreatedAt\", \"UpdatedAt\") VALUES ('{Guid.NewGuid()}', 'X', 'x', true, now(), now())");
            var ex2 = await Assert.ThrowsAnyAsync<Exception>(async () => { await using var c = Context(cs); await c.GetService<IMigrator>().MigrateAsync(BeforeMigration); });
            Assert.Contains("ROLLBACK_REFUSED", ex2.ToString());
            Assert.Equal("1", await ScalarAsync(cs, "SELECT COUNT(*) FROM product_models"));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
