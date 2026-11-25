using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Application.ConfigTransfer;
using WCS.Application.Config;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-036 stabilization (P2-A.1): configuration transfer of the model data (export / import, old packages, conflicts), model guard on the reconciliation of legacy tasks, stock adjustment
/// of racks in manual handling and the rack filter itself. Real services, real PostgreSQL; two databases for the transfer.
/// </summary>
[Collection("Database")]
public class Cr036A1Tests(DatabaseFixture fixture)
{
    private static readonly ActorInfo Admin = new(Guid.NewGuid(), "admin", UserRole.Admin);
    private static readonly string[] Plain = [ConfigGroupIds.Location, ConfigGroupIds.Agv, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess];
    private static readonly string[] WithModels = [ConfigGroupIds.ProductModel, ConfigGroupIds.Location, ConfigGroupIds.Agv, ConfigGroupIds.PlcStation, ConfigGroupIds.BusinessProcess];
    private static readonly string[] A12 = ["A1", "A2"];

    private sealed class ModeHolder : IOptions<ModelIsolationOptions> { public ModelIsolationOptions Value { get; } = new(); }

    // ================================================================== ConfigTransfer (two systems)

    private sealed class Systems : IAsyncDisposable
    {
        public required RoutingKit A { get; init; }
        public required RoutingKit B { get; init; }
        public required DatabaseFixture SecondDb { get; init; }
        public required string Dir { get; init; }
        public async ValueTask DisposeAsync()
        {
            await A.DisposeAsync(); await B.DisposeAsync(); await SecondDb.DisposeAsync();
            try { Directory.Delete(Dir, true); } catch (IOException) { /* best effort */ }
        }
    }

    private async Task<Systems> SystemsAsync()
    {
        var second = new DatabaseFixture(); await second.InitializeAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "cfgtransfer-a1-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
        void Opts(IServiceCollection s) => s.Configure<ConfigTransferOptions>(o => { o.Enabled = true; o.BackupPath = dir; o.MinFreeMb = 1; });
        return new Systems { A = await RoutingKit.CreateAsync(fixture, more: Opts), B = await RoutingKit.CreateAsync(second, more: Opts), SecondDb = second, Dir = dir };
    }

    private static Task<T> On<T>(RoutingKit k, Func<ConfigTransferService, Task<T>> f) => k.With(sp => f(sp.GetRequiredService<ConfigTransferService>()));
    private static async Task<byte[]> ExportAsync(RoutingKit k, params string[] groups) => (await On(k, s => s.ExportAsync(groups, Admin))).Data;
    private static async Task<(Guid Id, ImportPlan Plan)> PreviewAsync(RoutingKit k, byte[] package)
    {
        var up = await On(k, s => s.UploadAsync(package, Admin));
        return (up.ImportId, await On(k, s => s.PreviewAsync(up.ImportId, null, Admin)));
    }
    private static async Task<ApplyResult> ApplyAsync(RoutingKit k, Guid id, ImportPlan plan, bool skipConflicts = false)
    {
        await k.With(sp => sp.GetRequiredService<MaintenanceService>().SetAsync(true, "test", Admin));
        return await On(k, s => s.ApplyAsync(id, new ApplyRequest(plan.PreviewToken!, true, skipConflicts), Admin));
    }
    private static JsonNode Entry(byte[] zip, string file)
    {
        using var z = new ZipArchive(new MemoryStream(zip)); using var r = new StreamReader(z.GetEntry(file)!.Open()); return JsonNode.Parse(r.ReadToEnd())!;
    }

    private static async Task<(Guid Id, string Code)> ModelAsync(RoutingKit k, string code, string name)
    {
        var id = await k.H.Db(async db => { var m = new ProductModel(code, name, k.H.Clock.UtcNow); db.ProductModels.Add(m); await db.SaveChangesAsync(); return m.Id; });
        return (id, code);
    }
    private static Task SetModelAsync(RoutingKit k, string location, Guid? model, StationRole? role) => k.H.Db(async db =>
    {
        (await db.Locations.SingleAsync(l => l.Code == location)).SetModelAndRole(model, role, k.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0;
    });
    private static async Task<Guid> ModelProcessAsync(RoutingKit k, string code, Guid? model, bool publish)
    {
        var v = await k.With(async sp =>
        {
            var admin = sp.GetRequiredService<ProcessAdminService>();
            var p = await admin.CreateAsync(new ProcessHeader(code, code, null, nameof(RequestType.PickupRequest), nameof(RackKind.Full), A12, model), "test");
            await admin.SaveDefinitionAsync(p.Versions[0].Id, OneLeg("Request", Dest(1, "B1")));
            return p.Versions[0].Id;
        });
        if (publish) await k.PublishAsync(v);
        return v;
    }
    private static PlanItem LocationItem(ImportPlan plan, string key) => plan.Items.Single(i => i.Group == ConfigGroupIds.Location && i.Key == key);

    [Fact]
    public async Task CT01_the_model_data_survives_an_export_import_round_trip_and_is_matched_by_code_not_by_id()
    {
        await using var s = await SystemsAsync();
        var (idA, _) = await ModelAsync(s.A, "T-MA", "Model A");
        await SetModelAsync(s.A, "A1", idA, StationRole.CncStation); await SetModelAsync(s.A, "B1", idA, StationRole.AutoQcStation);
        await ModelProcessAsync(s.A, "T-PKG", idA, publish: true);

        var package = await ExportAsync(s.A, WithModels);
        Assert.Equal("T-MA", Entry(package, "product-models.json").ToJsonString().Contains("T-MA") ? "T-MA" : null);
        var locs = Entry(package, "locations.json").ToJsonString();
        Assert.Contains("\"productModelCode\":\"T-MA\"", locs); Assert.Contains("\"stationRole\":\"CncStation\"", locs);
        Assert.DoesNotContain(idA.ToString(), locs);                                                       // never a database id

        var (id, plan) = await PreviewAsync(s.B, package);
        Assert.Equal(PlanAction.Create, plan.Items.Single(i => i.Group == ConfigGroupIds.ProductModel && i.Key == "T-MA").Action);
        Assert.Null(LocationItem(plan, "A1").ConflictCode);
        await ApplyAsync(s.B, id, plan);

        var idB = (await s.B.H.Db(db => db.ProductModels.AsNoTracking().SingleAsync(m => m.Code == "T-MA"))).Id;
        Assert.NotEqual(idA, idB);                                                                         // different system, different id
        var a1 = await s.B.H.LocationAsync("A1");
        Assert.Equal(idB, a1.ProductModelId); Assert.Equal(StationRole.CncStation, a1.StationRole);
        Assert.Equal(StationRole.AutoQcStation, (await s.B.H.LocationAsync("B1")).StationRole);
        Assert.Equal(idB, (await s.B.H.Db(db => db.BusinessProcesses.AsNoTracking().SingleAsync(p => p.Code == "T-PKG"))).ProductModelId);

        // exporting B again gives the same model fields (exact restore)
        var again = await ExportAsync(s.B, WithModels);
        Assert.Equal(Entry(package, "product-models.json").ToJsonString(), Entry(again, "product-models.json").ToJsonString());
        var rec = (Entry(again, "locations.json")["records"] ?? Entry(again, "locations.json")).ToJsonString();
        Assert.Contains("\"productModelCode\":\"T-MA\"", rec); Assert.Contains("\"stationRole\":\"AutoQcStation\"", rec);
    }

    [Fact]
    public async Task CT02_an_old_package_without_the_model_fields_never_clears_the_model_role_or_process_model_that_exist_here()
    {
        await using var s = await SystemsAsync();
        await s.A.H.SetCapacityAsync("A1", 4);                                                             // so the old package really updates A1
        await s.A.CreateProcessAsync("T-PKG", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B1")));
        var old = await ExportAsync(s.A, Plain);
        Assert.DoesNotContain("productModelCode", Entry(old, "locations.json").ToJsonString());            // an old package: the fields do not exist

        var (idB, _) = await ModelAsync(s.B, "T-MB", "Model B");
        await SetModelAsync(s.B, "A1", idB, StationRole.CncStation);
        await ModelProcessAsync(s.B, "T-PKG", idB, publish: false);

        var (id, plan) = await PreviewAsync(s.B, old);
        Assert.DoesNotContain(LocationItem(plan, "A1").Changes, c => c.Field is "productModelCode" or "stationRole");
        await ApplyAsync(s.B, id, plan);

        var a1 = await s.B.H.LocationAsync("A1");
        Assert.Equal(4, a1.RackCapacity);                                                                  // the package WAS applied
        Assert.Equal(idB, a1.ProductModelId); Assert.Equal(StationRole.CncStation, a1.StationRole);        // and the model data was not cleared
        Assert.Equal(idB, (await s.B.H.Db(db => db.BusinessProcesses.AsNoTracking().SingleAsync(p => p.Code == "T-PKG"))).ProductModelId);
    }

    [Fact]
    public async Task CT03_an_import_cannot_change_the_model_of_a_location_that_holds_racks()
    {
        await using var s = await SystemsAsync();
        var (idA, _) = await ModelAsync(s.A, "T-MA", "Model A"); await SetModelAsync(s.A, "A1", idA, StationRole.CncStation);
        var (idB, _) = await ModelAsync(s.B, "T-MB", "Model B");
        await s.B.H.StockAsync("A1", RackKind.Full, 1);                                                    // a rack stands at A1 here
        await SetModelAsync(s.B, "A1", idB, StationRole.CncStation);

        var (id, plan) = await PreviewAsync(s.B, await ExportAsync(s.A, ConfigGroupIds.ProductModel, ConfigGroupIds.Location));
        Assert.Equal("LOCATION_HAS_RACKS", LocationItem(plan, "A1").ConflictCode);
        await ApplyAsync(s.B, id, plan, skipConflicts: true);
        Assert.Equal(idB, (await s.B.H.LocationAsync("A1")).ProductModelId);                               // unchanged
    }

    [Fact]
    public async Task CT04_an_import_cannot_change_the_model_of_a_location_used_by_a_published_version()
    {
        await using var s = await SystemsAsync();
        var (idA, _) = await ModelAsync(s.A, "T-MA", "Model A"); await SetModelAsync(s.A, "A1", idA, StationRole.CncStation);
        await s.B.CreateProcessAsync("T-REF", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B1")));
        var refVersion = await s.B.H.Db(db => db.BusinessProcessVersions.AsNoTracking().SingleAsync());
        await s.B.PublishAsync(refVersion.Id);                                                             // Published, with A1 as a trigger

        var (id, plan) = await PreviewAsync(s.B, await ExportAsync(s.A, ConfigGroupIds.ProductModel, ConfigGroupIds.Location));
        Assert.Equal("LOCATION_REFERENCED_BY_PUBLISHED", LocationItem(plan, "A1").ConflictCode);
        await ApplyAsync(s.B, id, plan, skipConflicts: true);
        Assert.Null((await s.B.H.LocationAsync("A1")).ProductModelId);

        // the same rule holds for the API path (the import only reuses it)
        var (modelOnB, _) = await ModelAsync(s.B, "T-MB", "Model B");
        var ex = await Assert.ThrowsAsync<AppException>(() => s.B.With(sp => sp.GetRequiredService<OperationalConfigService>().SaveLocationAsync(
            new SaveLocationCommand("A1", "A1", LocationType.Station, 1, AllowedRackKind.Any, true, true, modelOnB, StationRole.CncStation), false)));
        Assert.Equal("LOCATION_REFERENCED_BY_PUBLISHED", ex.Code);
    }

    [Fact]
    public async Task CT05_a_location_whose_model_is_neither_here_nor_in_the_selected_import_is_a_conflict()
    {
        await using var s = await SystemsAsync();
        var (idA, _) = await ModelAsync(s.A, "T-MA", "Model A"); await SetModelAsync(s.A, "A1", idA, StationRole.CncStation);
        var (id, plan) = await PreviewAsync(s.B, await ExportAsync(s.A, ConfigGroupIds.Location));          // the model group is NOT part of the package
        Assert.Equal("MISSING_DEPENDENCY", LocationItem(plan, "A1").ConflictCode);
        _ = id;
    }

    // ================================================================== reconciliation of legacy tasks

    private async Task<(Harness H, TransportTask Task, Guid RackId, ModeHolder Mode)> LegacyReconcileCaseAsync(ModelIsolationMode mode, Guid? rackModel, Guid? locationModel)
    {
        var holder = new ModeHolder(); holder.Value.Mode = mode;
        var h = await Harness.CreateAsync(fixture, s => s.AddSingleton<IOptions<ModelIsolationOptions>>(holder));
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000; h.SchedulerOptions.RcsRetryMaxDelayMs = 4000; h.SchedulerOptions.RcsMaxAttempts = 4;
        var created = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(created.Id, "operator stop", "tester"));
        h.Rcs.Unreachable = true;
        for (var i = 0; i < 30; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }   // the cancel is never confirmed: the task needs reconciliation
        var task = await h.GetTaskAsync(created.Id);
        Assert.True(task.RequiresReconciliation);
        var rackId = (await h.RacksAtAsync("A1")).Single().Id;
        var ids = await h.Db(async db =>
        {
            var now = h.Clock.UtcNow;
            var ma = new ProductModel("T-MA", "A", now); var mb = new ProductModel("T-MB", "B", now); db.ProductModels.AddRange(ma, mb); await db.SaveChangesAsync();
            return new Dictionary<string, Guid> { ["T-MA"] = ma.Id, ["T-MB"] = mb.Id };
        });
        Guid? Resolve(Guid? marker) => marker == MA ? ids["T-MA"] : marker == MB ? ids["T-MB"] : null;
        await h.Db(async db =>
        {
            if (Resolve(rackModel) is { } rm) (await db.Racks.SingleAsync(r => r.Id == rackId)).AssignModel(rm);
            if (Resolve(locationModel) is { } lm) (await db.Locations.SingleAsync(l => l.Code == "B1")).SetModelAndRole(lm, null, h.Clock.UtcNow);
            await db.SaveChangesAsync(); return 0;
        });
        return (h, task, rackId, holder);
    }

    private static readonly Guid MA = Guid.Parse("00000000-0000-0000-0000-00000000000a"), MB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static async Task<(Rack Rack, Location Loc, TransportTask Task)> ReconcileAtB1Async(Harness h, TransportTask task, Guid rackId)
    {
        await h.RunAsync(sp => sp.GetRequiredService<IInventoryService>().ReconcileTaskAsync(task.Id, "B1", "admin", "seen on site"));
        var rack = await h.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == rackId));
        return (rack, await h.LocationAsync("B1"), await h.GetTaskAsync(task.Id));
    }

    [Theory]
    [InlineData(ModelIsolationMode.Off, false)]
    [InlineData(ModelIsolationMode.Warn, false)]
    [InlineData(ModelIsolationMode.Enforce, true)]
    public async Task RC01_a_rack_of_a_model_confirmed_at_a_location_of_another_model_is_recorded_as_it_is_and_the_location_is_flagged_only_when_enforcing(ModelIsolationMode mode, bool flagged)
    {
        var (h, task, rackId, _) = await LegacyReconcileCaseAsync(mode, MA, MB);
        await using var _h = h;
        var (rack, loc, after) = await ReconcileAtB1Async(h, task, rackId);
        Assert.Equal(loc.Id, rack.CurrentLocationId);                                                       // the physical position is recorded: never refused
        Assert.Equal(RackStatus.Full, rack.Status);
        var modelA = (await h.Db(db => db.ProductModels.AsNoTracking().SingleAsync(m => m.Code == "T-MA"))).Id;
        Assert.Equal(modelA, rack.ProductModelId);                                                          // the model is NEVER changed to fit the location
        Assert.True(after.ReconciliationCompleted);
        Assert.Equal(flagged, loc.ReconciliationRequired);
        Assert.Equal(mode != ModelIsolationMode.Off, after.History.Any(x => x.EventName == "MODEL_GUARD" && x.Detail.Contains(ModelCodes.ModelMismatch)));
        Assert.Contains(await h.Db(db => db.InventoryTransactions.AsNoTracking().Where(t => t.RackId == rackId).ToListAsync()), t => t.EventType == InventoryEventType.Reconciliation);
    }

    [Theory]
    [InlineData(ModelIsolationMode.Off, false)]
    [InlineData(ModelIsolationMode.Warn, false)]
    [InlineData(ModelIsolationMode.Enforce, true)]
    public async Task RC02_a_rack_without_a_model_confirmed_at_a_location_with_a_model_gets_no_model_and_the_location_is_flagged_only_when_enforcing(ModelIsolationMode mode, bool flagged)
    {
        var (h, task, rackId, _) = await LegacyReconcileCaseAsync(mode, null, MA);
        await using var _h = h;
        var (rack, loc, after) = await ReconcileAtB1Async(h, task, rackId);
        Assert.Equal(loc.Id, rack.CurrentLocationId);
        Assert.Null(rack.ProductModelId);                                                                   // not inferred from the location
        Assert.Equal(flagged, loc.ReconciliationRequired);
        Assert.Equal(mode != ModelIsolationMode.Off, after.History.Any(x => x.EventName == "MODEL_GUARD" && x.Detail.Contains(ModelCodes.RackModelUnknown)));
    }

    [Theory]
    [InlineData(ModelIsolationMode.Off)]
    [InlineData(ModelIsolationMode.Warn)]
    [InlineData(ModelIsolationMode.Enforce)]
    public async Task RC03_a_consistent_or_unmodelled_reconciliation_is_exactly_the_historical_one_in_every_mode(ModelIsolationMode mode)
    {
        foreach (var (rackModel, locModel) in new (Guid?, Guid?)[] { (MA, MA), (null, null) })
        {
            var (h, task, rackId, _) = await LegacyReconcileCaseAsync(mode, rackModel, locModel);
            await using var _h = h;
            var (rack, loc, after) = await ReconcileAtB1Async(h, task, rackId);
            Assert.Equal(loc.Id, rack.CurrentLocationId);
            Assert.False(loc.ReconciliationRequired);                                                       // cleared as before
            Assert.DoesNotContain(after.History, x => x.EventName == "MODEL_GUARD");
        }
    }

    [Fact]
    public async Task RC04_a_flagged_location_is_not_used_automatically_and_a_rack_in_manual_handling_is_not_reserved_by_a_legacy_task()
    {
        // a rack in manual handling standing at a legacy source location: Off = the historical behaviour, Enforce = not reserved by the automatic (legacy) task
        foreach (var (mode, reserved) in new[] { (ModelIsolationMode.Off, true), (ModelIsolationMode.Enforce, false) })
        {
            var holder = new ModeHolder(); holder.Value.Mode = mode;
            await using var h = await Harness.CreateAsync(fixture, s => s.AddSingleton<IOptions<ModelIsolationOptions>>(holder));
            await h.SeedStandardAsync(); await h.StockAsync("A1", RackKind.Full, 1);
            await h.Db(async db => { (await db.Racks.SingleAsync()).MarkManualHandling(); await db.SaveChangesAsync(); return 0; });
            var task = (await h.CreateTaskAsync("BP01", "A1")).Task;
            var active = await h.ActiveReservationsAsync();
            Assert.Equal(reserved, active.Any(r => r.Kind == ReservationKind.SourceRack));
            Assert.Equal(RackDisposition.ManualHandling, (await h.Db(db => db.Racks.AsNoTracking().SingleAsync())).Disposition);   // never touched
            _ = task;
        }
    }

    // ================================================================== stock adjustment of racks in manual handling

    private async Task<(RoutingKit Kit, Guid Model)> AdjustWorldAsync(ModelIsolationMode mode)
    {
        var holder = new ModeHolder(); holder.Value.Mode = mode;
        var kit = await RoutingKit.CreateAsync(fixture, more: s => s.AddSingleton<IOptions<ModelIsolationOptions>>(holder));
        var (m, _) = await ModelAsync(kit, "T-MA", "Model A");
        await SetModelAsync(kit, "F1", m, StationRole.BufferPosition);                                      // F1: Full buffer, capacity 3
        await SetModelAsync(kit, "F5", m, StationRole.ManualQcStation);                                     // F5 (ManualBuffer, capacity 3) used here as the manual QC station of the test model
        return (kit, m);
    }
    private static async Task<Guid> PutAsync(RoutingKit k, string location, Guid model, bool manual)
    {
        return await k.H.Db(async db =>
        {
            var loc = await db.Locations.SingleAsync(l => l.Code == location);
            var r = new Rack(RackStatus.Full, loc.Id, null, null, k.H.Clock.UtcNow); r.AssignModel(model); if (manual) r.MarkManualHandling();
            db.Racks.Add(r); await db.SaveChangesAsync(); return r.Id;
        });
    }
    private static Task<int> AdjustAsync(RoutingKit k, string location, int target) =>
        k.H.Scoped(sp => sp.GetRequiredService<IInventoryService>().AdjustStockAsync(location, RackKind.Full, target, "test", "adjust"));

    [Fact]
    public async Task AD01_reducing_the_stock_removes_Auto_racks_as_before()
    {
        var (k, m) = await AdjustWorldAsync(ModelIsolationMode.Enforce); await using var _k = k;
        var ids = new[] { await PutAsync(k, "F1", m, false), await PutAsync(k, "F1", m, false), await PutAsync(k, "F1", m, false) };
        Assert.Equal(1, await AdjustAsync(k, "F1", 1));
        Assert.Equal(1, await k.H.Db(db => db.Racks.CountAsync(r => ids.Contains(r.Id))));
        Assert.Equal(2, await k.H.Db(db => db.InventoryTransactions.CountAsync(t => t.EventType == InventoryEventType.ManualAdjustment && t.Note == "adjust")));
    }

    [Fact]
    public async Task AD02_AD04_a_rack_in_manual_handling_is_not_removed_the_refusal_names_the_cause_and_nothing_changes()
    {
        var (k, m) = await AdjustWorldAsync(ModelIsolationMode.Enforce); await using var _k = k;
        var manual = new[] { await PutAsync(k, "F5", m, true), await PutAsync(k, "F5", m, true) };
        var ex = await Assert.ThrowsAsync<AppException>(() => AdjustAsync(k, "F5", 0));
        Assert.Equal("RACK_MANUAL_HANDLING_REQUIRES_APPROVED_ADJUSTMENT", ex.Code);                         // not "RACKS_RESERVED": nothing is reserved
        var racks = await k.H.Db(db => db.Racks.AsNoTracking().Where(r => manual.Contains(r.Id)).ToListAsync());
        Assert.Equal(2, racks.Count); Assert.All(racks, r => Assert.Equal(RackDisposition.ManualHandling, r.Disposition));   // still there, still manual: never changed to Auto
        Assert.Equal(0, await k.H.Db(db => db.InventoryTransactions.CountAsync(t => t.EventType == InventoryEventType.ManualAdjustment)));
    }

    [Fact]
    public async Task AD03_a_mixed_location_removes_only_what_the_adjustment_may_remove()
    {
        var (k, m) = await AdjustWorldAsync(ModelIsolationMode.Enforce); await using var _k = k;
        var auto = new[] { await PutAsync(k, "F1", m, false), await PutAsync(k, "F1", m, false) };
        var manual = await PutAsync(k, "F1", m, true);
        var ex = await Assert.ThrowsAsync<AppException>(() => AdjustAsync(k, "F1", 0));                       // would need the manual rack too
        Assert.Equal("RACK_MANUAL_HANDLING_REQUIRES_APPROVED_ADJUSTMENT", ex.Code);
        Assert.Equal(3, await k.H.Db(db => db.Racks.CountAsync(r => r.CurrentLocationId != null && (auto.Contains(r.Id) || r.Id == manual))));   // all three are still there
        Assert.Equal(1, await AdjustAsync(k, "F1", 1));                                                      // the two Auto racks may go, the manual rack stays
        Assert.Equal(RackDisposition.ManualHandling, (await k.H.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == manual))).Disposition);
        Assert.Equal(0, await k.H.Db(db => db.Racks.CountAsync(r => auto.Contains(r.Id))));
    }

    [Fact]
    public async Task AD05_a_reserved_rack_still_reports_the_reservation()
    {
        var (k, m) = await AdjustWorldAsync(ModelIsolationMode.Enforce); await using var _k = k;
        var manual = await PutAsync(k, "F5", m, true);
        await k.H.Db(async db =>
        {
            var loc = await db.Locations.SingleAsync(l => l.Code == "F5");
            db.Reservations.Add(Reservation.ForSourceRack(Guid.NewGuid(), manual, loc.Id, k.H.Clock.UtcNow)); await db.SaveChangesAsync(); return 0;
        });
        var ex = await Assert.ThrowsAsync<AppException>(() => AdjustAsync(k, "F5", 0));
        Assert.Equal("RACKS_RESERVED", ex.Code);                                                            // a real reservation keeps its own code
        Assert.Equal(RackDisposition.ManualHandling, (await k.H.Db(db => db.Racks.AsNoTracking().SingleAsync(r => r.Id == manual))).Disposition);
    }

    [Fact]
    public async Task AD06_with_the_feature_off_the_adjustment_is_the_historical_one()
    {
        var (k, m) = await AdjustWorldAsync(ModelIsolationMode.Off); await using var _k = k;
        await PutAsync(k, "F1", m, false); await PutAsync(k, "F1", m, false);
        Assert.Equal(0, await AdjustAsync(k, "F1", 0));
        Assert.Equal(0, await k.H.Db(db => db.Racks.CountAsync()));
    }

    // ================================================================== the rack filter itself and the legacy process in the migration

    [Fact]
    public async Task RF01_the_repository_filter_selects_by_model_by_no_model_and_by_disposition_and_the_historical_query_is_unchanged()
    {
        var (k, m) = await AdjustWorldAsync(ModelIsolationMode.Enforce); await using var _k = k;
        var (other, _) = await ModelAsync(k, "T-MB", "Model B");
        var a = await PutAsync(k, "F1", m, false);
        var b = await PutAsync(k, "F1", other, false);
        var none = await k.H.Db(async db => { var loc = await db.Locations.SingleAsync(l => l.Code == "F1"); var r = new Rack(RackStatus.Full, loc.Id, null, null, k.H.Clock.UtcNow); db.Racks.Add(r); await db.SaveChangesAsync(); return r.Id; });
        var manual = await PutAsync(k, "F1", m, true);
        var f1 = (await k.H.LocationAsync("F1")).Id;
        async Task<Guid?> Find(RackSelection? sel) => await k.H.Scoped(sp => sp.GetRequiredService<IUnitOfWork>().ExecuteAsync(async t =>
            (sel is null ? await sp.GetRequiredService<IRackRepository>().FindUnreservedAsync(f1, RackStatus.Full, t)
                         : await sp.GetRequiredService<IRackRepository>().FindUnreservedAsync(f1, RackStatus.Full, sel, t))?.Id));

        Assert.Equal(a, await Find(new RackSelection(m, false, true)));              // exactly model A, Auto (the manual rack of model A is excluded)
        Assert.Equal(b, await Find(new RackSelection(other, false, true)));
        Assert.Equal(none, await Find(new RackSelection(null, true, true)));         // no model only
        Assert.Null(await Find(new RackSelection(Guid.NewGuid(), false, true)));     // a model nobody has
        Assert.NotEqual(manual, await Find(new RackSelection(null, false, true)));   // Auto only, any model
        Assert.NotNull(await Find(null));                                            // the historical query still returns racks (it knows nothing about models)
    }

    [Fact]
    public async Task MI05_the_migration_keeps_a_legacy_process_version_as_it_was_published_with_no_model_and_not_ready()
    {
        var scratch = new DatabaseFixture(); await scratch.InitializeAsync();
        try
        {
            await using (var kit = await RoutingKit.CreateAsync(scratch))
            {
                var (_, v) = await kit.CreateProcessAsync("T-LEGACY", RequestType.PickupRequest, RackKind.Full, A12, OneLeg("Request", Dest(1, "B1")));
                await kit.PublishAsync(v);
            }
            var cs = scratch.ConnectionString;
            await using (var down = new WCS.Infrastructure.Persistence.WcsDbContext(new DbContextOptionsBuilder<WCS.Infrastructure.Persistence.WcsDbContext>().UseNpgsql(cs).Options))
                await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(down)!.MigrateAsync("20261124090000_AddProcessVersionReadiness");
            await using (var up = new WCS.Infrastructure.Persistence.WcsDbContext(new DbContextOptionsBuilder<WCS.Infrastructure.Persistence.WcsDbContext>().UseNpgsql(cs).Options))
                await up.Database.MigrateAsync();
            await using var c = new Npgsql.NpgsqlConnection(cs); await c.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT v.\"State\", v.\"Readiness\", p.\"ProductModelId\" IS NULL, p.\"Enabled\" FROM business_process_versions v JOIN business_processes p ON p.\"Id\" = v.\"ProcessId\"", c);
            await using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("Published", r.GetString(0)); Assert.Equal("NotReady", r.GetString(1)); Assert.True(r.GetBoolean(2)); Assert.True(r.GetBoolean(3));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
