using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Plc;
using WCS.Domain.Rcs;
using WCS.Domain.Routing;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-041: one RequestKey across both engines, the confirmed catalog applied to existing processes, the shared PLC input handler (physical / simulated), the protection of the Real
/// mode against simulated requests, and the extended rack records. Real PostgreSQL, real engine, SIMULATOR dispatcher. Nothing talks to a PLC or an RCS.
/// </summary>
[Collection("Database")]
public class Cr041UnifiedOperationsTests(DatabaseFixture fixture)
{
    private static async Task<string> CodeOf(Func<Task> f) => (await Assert.ThrowsAsync<AppException>(f)).Code;
    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<ProcessAdminService>()));
    private static Task<T> Sim<T>(RoutingKit kit, Func<PlcSimulationService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<PlcSimulationService>()));
    private static Task<T> Racks<T>(RoutingKit kit, Func<RackAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<RackAdminService>()));

    private static Task<int> ModeAsync(RoutingKit kit, ExecutionMode mode) => kit.H.Db(async db =>
    {   // test arrangement of the mode row; the real transition is covered by the CR-038 tests
        var s = await db.ExecutionModeStates.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null) { s = ExecutionModeState.Default(kit.H.Clock.UtcNow); db.ExecutionModeStates.Add(s); }
        s.SwitchTo(mode, "test", kit.H.Clock.UtcNow);
        await db.SaveChangesAsync(); return 0;
    });

    /// <summary>A process A1 → C1 with one full rack at A1 and the PLC → WCS points of port A1 that the intake reads (test data).</summary>
    private static async Task<RoutingKit> PlcKitAsync(DatabaseFixture f)
    {
        var kit = await RoutingKit.CreateAsync(f);
        await kit.H.Db(async db =>
        {
            var i = 10;
            foreach (var (name, type) in new[] { ("Pickup_Request", PlcDataType.Bool), ("Request_ID", PlcDataType.String), ("Rack_Full", PlcDataType.Int) })
            {
                var point = new StationIoPoint("PLC-T1", "A1", i++, "Request", name, PlcSignalDirection.PlcToSoftware, type, "ref", "test", kit.H.Clock.UtcNow);
                point.ConfigureAddress($"D{1500 + i}", type == PlcDataType.String ? 10 : null, null, kit.H.Clock.UtcNow); point.Approve("engineer", kit.H.Clock.UtcNow); db.StationIoPoints.Add(point);
            }
            await db.SaveChangesAsync(); return 0;
        });
        var (_, v) = await kit.CreateProcessAsync("T-PLC", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        kit.Link.Offline.Add("PLC-T1");
        await Sim(kit, s => Task.FromResult(s.Activate("admin", true)));
        return kit;
    }

    private static async Task SetRequestAsync(RoutingKit kit, string requestId)
    {
        await Sim(kit, s => s.SetSignalAsync("A1", "Pickup_Request", "on"));
        await Sim(kit, s => s.SetSignalAsync("A1", "Request_ID", requestId));
        await Sim(kit, s => s.SetSignalAsync("A1", "Rack_Full", "1"));
    }

    private static Task<(int Orders, int Transactions, int Flagged)> StateAsync(RoutingKit kit) => kit.H.Db(async db =>
        (await db.TransportOrders.CountAsync(), await db.StationTransactions.CountAsync(), await db.Locations.CountAsync(l => l.ReconciliationRequired)));

    // ================================================================== one RequestKey, two engines

    [Fact]
    public async Task A_request_key_used_by_a_task_creates_no_order_and_the_other_way_round()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-KEY", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.StockAsync("F1", RackKind.Full, 1);

        await kit.H.CreateTaskAsync("BP05", "F5", "one-key");
        Assert.Equal("REQUEST_KEY_IN_USE", await CodeOf(() => kit.StartAsync("one-key", "A1", RequestType.PickupRequest, RackKind.Full, "T-KEY")));
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));

        await kit.StartAsync("two-key", "A1", RequestType.PickupRequest, RackKind.Full, "T-KEY");
        Assert.Equal("REQUEST_KEY_IN_USE", await CodeOf(() => kit.H.CreateTaskAsync("BP05", "F5", "two-key")));
        Assert.Equal(1, await kit.H.Db(d => d.Tasks.CountAsync()));
    }

    [Fact]
    public async Task Simultaneous_requests_with_one_key_on_both_engines_leave_exactly_one_request()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-RACE", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.StockAsync("F1", RackKind.Full, 1);
        for (var i = 0; i < 5; i++)
        {
            var key = $"race-{i}";
            async Task<bool> Try(Func<Task> f) { try { await f(); return true; } catch (AppException e) when (e.Code == "REQUEST_KEY_IN_USE") { return false; } }
            var won = await Task.WhenAll(Try(() => kit.H.CreateTaskAsync("BP05", "F5", key)), Try(() => kit.StartAsync(key, "A1", RequestType.PickupRequest, RackKind.Full, "T-RACE")));
            Assert.Single(won, x => x);
            Assert.Equal(1, await kit.H.Db(async d => await d.Tasks.CountAsync(t => t.RequestKey == key) + await d.TransportOrders.CountAsync(o => o.RequestKey == key)));
        }
    }

    // ================================================================== shared PLC input handler

    [Fact]
    public async Task A_physical_signal_set_is_normalized_like_the_simulated_one_and_processed_by_nothing_while_the_trigger_rule_is_unapproved()
    {
        await using var kit = await PlcKitAsync(fixture);
        var at = kit.H.Clock.UtcNow;
        IoPointState Point(string name, PlcSignalDirection direction, object? value, PointQuality quality) =>
            new(Guid.NewGuid(), "PLC-T1", "A1", 1, "Request", name, direction.ToString(), "String", "Approved", value, quality.ToString(), at, null, null);
        var station = new StationSnapshot("PLC-T1", "A1", 5, 5, 4,
        [
            Point("Pickup_Request", PlcSignalDirection.PlcToSoftware, true, PointQuality.Good),
            Point("Request_ID", PlcSignalDirection.PlcToSoftware, "P-1", PointQuality.Good),
            Point("Rack_Full", PlcSignalDirection.PlcToSoftware, 1, PointQuality.Good),
            Point("Rack_Empty", PlcSignalDirection.PlcToSoftware, 7, PointQuality.Unknown),   // not read well: absent, never assumed
            Point("Request_Ack", PlcSignalDirection.SoftwareToPlc, true, PointQuality.Good),   // WCS → PLC: not an input
        ]);
        var physical = await kit.With(sp => sp.GetRequiredService<PhysicalPlcInputAdapter>().SubmitAsync(station));
        Assert.Equal(PlcInputHandler.TriggerNotApproved, physical.Result.Code);
        Assert.Equal((0, 0, 0), await StateAsync(kit));

        await SetRequestAsync(kit, "P-1");
        var simulated = await Sim(kit, s => s.EvaluateStationAsync("A1", true));
        Assert.True(simulated.Outcome == "Accepted", simulated.Code + ": " + simulated.Message);
        var expected = PlcRequestAssembler.FromSignals("PLC-T1", "A1", PhysicalPlcInputAdapter.Normalize(station));
        Assert.Equal(new PlcRequestSnapshot("PLC-T1", "A1", "P-1", true, false, 1, null, null, null), expected);
        Assert.Equal(expected with { Simulated = true }, physical.Snapshot with { Simulated = true });
        Assert.False(physical.Snapshot.Simulated);
        Assert.True(simulated.Transaction!.Simulated);
        Assert.Equal("PLC-SIMULATION", simulated.Order!.RequestedBy);
        Assert.Equal("Simulator", simulated.Order.ExecutionMode);
    }

    [Fact]
    public async Task A_repeated_signal_set_creates_nothing_new_OFF_asks_for_nothing_and_reset_returns_to_normal()
    {
        await using var kit = await PlcKitAsync(fixture);
        await SetRequestAsync(kit, "R-1");
        Assert.Equal("Accepted", (await Sim(kit, s => s.EvaluateStationAsync("A1", true))).Outcome);
        await Sim(kit, s => s.SetSignalAsync("A1", "Pickup_Request", "on"));   // the same value again
        Assert.Equal("Duplicate", (await Sim(kit, s => s.EvaluateStationAsync("A1", true))).Outcome);
        await Sim(kit, s => s.SetSignalAsync("A1", "Pickup_Request", "off"));
        await Sim(kit, s => s.SetSignalAsync("A1", "Request_ID", "R-2"));
        Assert.Equal("NO_REQUEST", (await Sim(kit, s => s.EvaluateStationAsync("A1", true))).Code);
        Assert.Equal("SIGNAL_VALUE_INVALID", await CodeOf(() => Sim(kit, s => s.SetSignalAsync("A1", "Pickup_Request", "maybe"))));

        var reset = await Sim(kit, s => s.ResetStationAsync("A1"));
        Assert.All(reset.Signals, x => Assert.Null(x.Value));
        Assert.Equal("D1511", reset.Signals.Single(x => x.Signal == "Pickup_Request").Address);   // the configured address is shown, never invented
        Assert.Equal("REQUEST_ID_MISSING", (await Sim(kit, s => s.EvaluateStationAsync("A1", true))).Code);
        var state = await StateAsync(kit);
        Assert.Equal((1, 1), (state.Orders, state.Transactions));
    }

    // ================================================================== Real mode: nothing simulated reaches the dispatcher

    [Fact]
    public async Task In_Real_mode_signals_can_be_set_but_a_simulated_request_records_and_creates_nothing()
    {
        await using var kit = await PlcKitAsync(fixture);
        await ModeAsync(kit, ExecutionMode.Real);
        await SetRequestAsync(kit, "REAL-1");
        var refused = await Sim(kit, s => s.EvaluateStationAsync("A1", true));
        Assert.Equal(("Rejected", SimulationCodes.RealMode), (refused.Outcome, refused.Code));
        // also with a rack disagreement: the location is not flagged by a simulated request in Real mode
        await Sim(kit, s => s.SetSignalAsync("A1", "Rack_Full", "2"));
        await Sim(kit, s => s.SetSignalAsync("A1", "Request_ID", "REAL-2"));
        Assert.NotEqual("Accepted", (await Sim(kit, s => s.EvaluateStationAsync("A1", true))).Outcome);
        Assert.Equal((0, 0, 0), await StateAsync(kit));
        // the engine refuses too (the mode may change between the first check and the creation of the order)
        Assert.Equal(SimulationCodes.RealMode, await CodeOf(() => kit.With(sp => sp.GetRequiredService<IRouteEngine>()
            .StartAsync(new StartOrderCommand("direct", "A1", RequestType.PickupRequest, RackKind.Full, "T-PLC", "test", SimulationOnly: true)))));
        Assert.Equal(0, await kit.H.Db(d => d.RcsMessages.CountAsync()));
    }

    [Fact]
    public async Task An_interrupted_simulated_start_is_never_resumed_as_real_work()
    {
        await using var kit = await PlcKitAsync(fixture);
        await kit.H.Db(async db =>
        {   // a simulated request that was recorded but not started (e.g. a crash between the two steps)
            var tx = StationTransaction.Create("A1", "PLC-T1", "A1", StationOperation.Pickup, null, null, "ORPHAN", kit.H.Clock.UtcNow, RackKind.Full);
            tx.Advance(StationTransactionState.Requested, kit.H.Clock.UtcNow); tx.Advance(StationTransactionState.Accepted, kit.H.Clock.UtcNow); tx.MarkSimulated();
            db.StationTransactions.Add(tx); await db.SaveChangesAsync(); return 0;
        });
        await ModeAsync(kit, ExecutionMode.Real);
        await kit.With(sp => sp.GetRequiredService<IPlcRequestService>().ResumeOrphansAsync());
        var tx = (await kit.TransactionsAsync()).Single();
        Assert.Equal(StationTransactionState.Failed, tx.State);
        Assert.Contains(SimulationCodes.RealMode, tx.FailureReason);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));
    }

    // ================================================================== the confirmed catalog on existing processes

    [Fact]
    public async Task An_existing_default_process_is_brought_to_the_catalog_as_a_new_draft_and_nothing_else_is_touched()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var all = await kit.ImportReferenceAsync();
        var (id, v1) = all["PH2-04"];
        await kit.H.Db(async db => { var p = await db.BusinessProcesses.SingleAsync(x => x.Id == id); p.MarkReview(true, "imported before D-09", p.SourceReference, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(CatalogStatuses.Differs, (await Admin(kit, s => s.GetAsync(id))).CatalogStatus);
        Assert.Equal("REASON_REQUIRED", await CodeOf(() => Admin(kit, s => s.ApplyCatalogAsync(id, ReferenceProcessCatalog.Load(), " ", "admin"))));

        var draft = await Admin(kit, s => s.ApplyCatalogAsync(id, ReferenceProcessCatalog.Load(), "D-09", "admin"));
        Assert.Equal((2, "Draft"), (draft.Number, draft.State));
        var after = await Admin(kit, s => s.GetAsync(id));
        Assert.False(after.ReviewBlocked); Assert.Null(after.PublishedVersion); Assert.Equal(CatalogStatuses.Matches, after.CatalogStatus);
        Assert.Contains(after.Versions, v => v.Id == v1);
        Assert.Equal("CATALOG_ALREADY_APPLIED", await CodeOf(() => Admin(kit, s => s.ApplyCatalogAsync(id, ReferenceProcessCatalog.Load(), "again", "admin"))));

        // BP-04 stays REVIEW: it is not a default process of the catalog
        Assert.Equal("PROCESS_NOT_IN_CATALOG", await CodeOf(() => Admin(kit, s => s.ApplyCatalogAsync(all["BP-04"].ProcessId, ReferenceProcessCatalog.Load(), "no", "admin"))));
        Assert.True((await Admin(kit, s => s.GetAsync(all["BP-04"].ProcessId))).ReviewBlocked);
    }

    [Fact]
    public async Task A_deactivated_process_leaves_the_request_list_keeps_its_history_and_can_return()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (p, v) = await kit.CreateProcessAsync("T-ARC", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var order = (await kit.StartAsync("a", "A1", RequestType.PickupRequest, RackKind.Full, "T-ARC")).Order;
        ProcessHeader Header() => new("T-ARC", "T-ARC", null, "PickupRequest", "Full", ["A1"]);
        await Admin(kit, s => s.UpdateAsync(p, Header(), false));
        Assert.DoesNotContain(await Admin(kit, s => s.StartableAsync()), x => x.Code == "T-ARC");
        Assert.Equal("PROCESS_DISABLED", await CodeOf(() => kit.StartAsync("b", "A1", RequestType.PickupRequest, RackKind.Full, "T-ARC")));
        Assert.Equal("T-ARC", (await kit.OrderAsync(order.Id)).ProcessCode);
        await Admin(kit, s => s.UpdateAsync(p, Header(), true));
        Assert.Contains(await Admin(kit, s => s.StartableAsync()), x => x.Code == "T-ARC");
    }

    // ================================================================== racks

    [Fact]
    public async Task Rack_type_can_be_set_and_corrected_and_the_reason_a_rack_is_locked_is_available()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var rack = await Racks(kit, s => s.CreateAsync(new("F1", RackKind.Full, "T-1", null, "found", "Tray"), "admin"));
        Assert.Equal("Tray", rack.RackType);
        Assert.Equal("Pallet", (await Racks(kit, s => s.UpdateAsync(rack.Id, new(null, null, null, "wrong type", "Pallet"), "admin"))).RackType);
        Assert.Equal("Pallet", (await Racks(kit, s => s.UpdateAsync(rack.Id, new("F2", null, null, "move only"), "admin"))).RackType);   // null = unchanged
        Assert.Null((await Racks(kit, s => s.UpdateAsync(rack.Id, new(null, null, null, "unclassified", ""), "admin"))).RackType);
        Assert.True((await Racks(kit, s => s.CheckEditableAsync(rack.Id))).Editable);
        var notes = await kit.H.Db(d => d.InventoryTransactions.AsNoTracking().Where(t => t.RackId == rack.Id).Select(t => t.Note).ToListAsync());
        Assert.Contains(notes, n => n!.Contains("type Tray -> Pallet"));

        var (_, v) = await kit.CreateProcessAsync("T-LOCK", RequestType.PickupRequest, RackKind.Full, ["A1"], new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "C1")]), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var busy = (await kit.RackAtAsync("A1"))!.Id;
        await kit.StartAsync("l", "A1", RequestType.PickupRequest, RackKind.Full, "T-LOCK");
        var locked = await Racks(kit, s => s.CheckEditableAsync(busy));
        Assert.False(locked.Editable); Assert.Equal("RACK_RESERVED", locked.Code); Assert.NotEmpty(locked.Message!);
        Assert.Equal("RACK_NOT_FOUND", await CodeOf(() => Racks(kit, s => s.CheckEditableAsync(Guid.NewGuid()))));
    }

    [Fact]
    public async Task Two_people_filling_the_last_slot_at_once_get_one_rack_not_two()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        await kit.H.SetCapacityAsync("F1", 1);
        async Task<bool> Try(string id)
        {
            try { await Racks(kit, s => s.CreateAsync(new("F1", RackKind.Full, id, null, "race"), "admin")); return true; }
            catch (Exception e) when (e is AppException or DbUpdateException) { return false; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Try($"RC-{i}")));
        var count = await kit.H.Db(async d => { var f1 = (await d.Locations.SingleAsync(l => l.Code == "F1")).Id; return await d.Racks.CountAsync(r => r.CurrentLocationId == f1); });
        Assert.Equal(1, count);
        Assert.Equal(1, results.Count(x => x));
    }

    // ================================================================== migration

    [Fact]
    public async Task The_simulation_marker_migration_goes_down_and_up_and_refuses_to_forget_a_waiting_simulated_request()
    {
        var scratch = new DatabaseFixture();
        await scratch.InitializeAsync();
        try
        {
            const string before = "20261125152312_AddTransportQueue";
            var cs = scratch.ConnectionString;
            WcsDbContext Context() => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
            async Task<string?> Scalar(string sql) { await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); return (await cmd.ExecuteScalarAsync())?.ToString(); }
            await using (var kit = await PlcKitAsync(scratch))
                await kit.H.Db(async db =>
                {
                    var tx = StationTransaction.Create("A1", "PLC-T1", "A1", StationOperation.Pickup, null, null, "W", kit.H.Clock.UtcNow, RackKind.Full);
                    tx.Advance(StationTransactionState.Requested, kit.H.Clock.UtcNow); tx.Advance(StationTransactionState.Accepted, kit.H.Clock.UtcNow); tx.MarkSimulated();
                    db.StationTransactions.Add(tx); await db.SaveChangesAsync(); return 0;
                });
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => Context().GetService<IMigrator>().MigrateAsync(before));
            Assert.Contains("ROLLBACK_REFUSED", refused.ToString());
            await Scalar("UPDATE station_transactions SET \"State\" = 'Failed'");
            await Context().GetService<IMigrator>().MigrateAsync(before);
            Assert.Equal("0", await Scalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'station_transactions' AND column_name = 'Simulated'"));
            Assert.Equal("1", await Scalar("SELECT COUNT(*) FROM station_transactions"));
            await Context().Database.MigrateAsync();
            Assert.Equal("1", await Scalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'station_transactions' AND column_name = 'Simulated'"));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
