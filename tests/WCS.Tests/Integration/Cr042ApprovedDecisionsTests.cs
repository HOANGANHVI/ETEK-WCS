using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Plc;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Plc;
using WCS.Domain.Rcs;
using WCS.Domain.Routing;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-042: the decisions the project owner approved on 2026-11-26 (locked header of a published process, catalog processes not approved for publication, versions disabled by an Admin,
/// stalled queue warning, stock adjustment and racks of unfinished orders, one engine per station) and the repeated dispatch event. Real PostgreSQL, SIMULATOR only.
/// </summary>
[Collection("Database")]
public class Cr042ApprovedDecisionsTests(DatabaseFixture fixture)
{
    private static async Task<string> CodeOf(Func<Task> f) => (await Assert.ThrowsAsync<AppException>(f)).Code;
    private static Task<T> Admin<T>(RoutingKit kit, Func<ProcessAdminService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<ProcessAdminService>()));
    private static Task<T> Inventory<T>(RoutingKit kit, Func<IInventoryService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<IInventoryService>()));
    private static VersionDefinition OneMove(string destination) => new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, destination)]), Done()]);

    private static async Task<(Guid ProcessId, Guid VersionId)> PublishedAsync(RoutingKit kit, string code, string trigger, string destination)
    {
        var (p, v) = await kit.CreateProcessAsync(code, RequestType.PickupRequest, RackKind.Full, [trigger], OneMove(destination));
        await kit.PublishAsync(v);
        return (p, v);
    }

    private static async Task<RoutingKit> QueueKitAsync(DatabaseFixture f, Action<IServiceCollection>? more = null)
    {
        var kit = await RoutingKit.CreateAsync(f, seedPorts: false, more: more, extraConfig: new Dictionary<string, string?> { ["RouteEngine:NewPublicationsRequireReadiness"] = "true" });
        await kit.H.Db(async db => { foreach (var l in await db.Locations.ToListAsync()) l.Configure(l.Name, l.RackCapacity, l.AllowedRackKind, true, false, kit.H.Clock.UtcNow); await db.SaveChangesAsync(); return 0; });
        return kit;
    }

    private static async Task<(Guid ProcessId, Guid VersionId)> QueuedAsync(RoutingKit kit, string code, string trigger, string scope, string destination)
    {
        var (p, v) = await PublishedAsync(kit, code, trigger, destination);
        await Admin(kit, s => s.SetQueueScopeAsync(p, scope, "test"));
        await Admin(kit, s => s.SetReadinessAsync(v, "Ready", "test", "admin"));
        return (p, v);
    }

    private static async Task<OrderDto> RequestAsync(RoutingKit kit, string key, string at, string code) => (await kit.StartAsync(key, at, RequestType.PickupRequest, RackKind.Full, code)).Order;
    private static Task<int> EmptyAsync(RoutingKit kit, string location) => kit.H.Db(async db =>
    {
        var id = (await db.Locations.SingleAsync(l => l.Code == location)).Id;
        db.Racks.RemoveRange(await db.Racks.Where(r => r.CurrentLocationId == id).ToListAsync()); await db.SaveChangesAsync(); return 0;
    });

    // ================================================================== OQ-42-01: header lock

    [Fact]
    public async Task The_header_of_a_published_process_cannot_change_what_it_does()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (p, v) = await PublishedAsync(kit, "T-HDR", "A1", "C1");
        ProcessHeader Header(string name, string type = "PickupRequest", string kind = "Full", params string[] triggers) => new("T-HDR", name, null, type, kind, triggers.Length == 0 ? ["A1"] : triggers);
        Assert.Equal("PROCESS_HEADER_LOCKED", await CodeOf(() => Admin(kit, s => s.UpdateAsync(p, Header("x", triggers: ["A2"]), true))));
        Assert.Equal("PROCESS_HEADER_LOCKED", await CodeOf(() => Admin(kit, s => s.UpdateAsync(p, Header("x", type: "DeliveryRequest"), true))));
        Assert.Equal("PROCESS_HEADER_LOCKED", await CodeOf(() => Admin(kit, s => s.UpdateAsync(p, Header("x", kind: "Empty"), true))));
        Assert.Equal("Renamed", (await Admin(kit, s => s.UpdateAsync(p, Header("Renamed"), true))).Name);   // name, description and the enabled switch stay editable
        Assert.False((await Admin(kit, s => s.UpdateAsync(p, Header("Renamed"), false))).Enabled);
        await Admin(kit, s => s.DisableAsync(v));
        Assert.Equal(["A2"], (await Admin(kit, s => s.UpdateAsync(p, Header("Renamed", triggers: ["A2"]), true))).TriggerLocationCodes);
    }

    [Fact]
    public async Task A_catalog_draft_leaves_a_published_process_untouched_and_is_refused_when_the_header_would_change()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var all = await kit.ImportReferenceAsync();
        var catalog = ReferenceProcessCatalog.Load();
        async Task EditAndPublishAsync(string code, string[]? triggers)
        {
            var (id, version) = all[code];
            var definition = (await Admin(kit, s => s.GetVersionAsync(version))).Definition;
            await Admin(kit, s => s.SaveDefinitionAsync(version, definition with { Steps = definition.Steps.Select(x => x.Candidates is { Count: > 1 } ? x with { Candidates = x.Candidates.Take(1).ToList() } : x).ToList() }));
            var p = await Admin(kit, s => s.GetAsync(id));
            await Admin(kit, s => s.UpdateAsync(id, new ProcessHeader(p.Code, "Local name", p.Description, p.RequestType, p.RackKind, triggers ?? [.. p.TriggerLocationCodes]), true));
            await kit.PublishAsync(version);
        }
        await EditAndPublishAsync("PH2-01", null);
        var draft = await Admin(kit, s => s.ApplyCatalogAsync(all["PH2-01"].ProcessId, catalog, "sync", "admin"));
        Assert.Equal((2, "Draft"), (draft.Number, draft.State));
        var after = await Admin(kit, s => s.GetAsync(all["PH2-01"].ProcessId));
        Assert.Equal("Local name", after.Name); Assert.Equal(1, after.PublishedVersion);

        await EditAndPublishAsync("PH2-03", ["B1"]);
        Assert.Equal("PROCESS_HEADER_LOCKED", await CodeOf(() => Admin(kit, s => s.ApplyCatalogAsync(all["PH2-03"].ProcessId, catalog, "sync", "admin"))));
        var kept = await Admin(kit, s => s.GetAsync(all["PH2-03"].ProcessId));
        Assert.Equal(["B1"], kept.TriggerLocationCodes); Assert.Single(kept.Versions);
    }

    // ================================================================== OQ-42-02

    [Fact]
    public async Task A_catalog_process_that_is_not_approved_for_publication_is_neither_published_nor_set_Ready()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, version) = (await kit.ImportReferenceAsync())["PH2-05"];
        Assert.Equal("PROCESS_NOT_APPROVED_FOR_PUBLICATION", await CodeOf(() => Admin(kit, s => s.PublishAsync(version, "admin"))));
        Assert.Equal("PROCESS_NOT_APPROVED_FOR_PUBLICATION", await CodeOf(() => Admin(kit, s => s.SetReadinessAsync(version, "Ready", "x", "admin"))));
    }

    // ================================================================== OQ-45

    [Fact]
    public async Task A_queued_request_runs_after_a_replacing_publication_but_is_held_when_an_Admin_disables_its_version()
    {
        await using var kit = await QueueKitAsync(fixture);
        var (p, v1) = await QueuedAsync(kit, "Q-W", "A1", "S", "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        var order = await RequestAsync(kit, "w", "A1", "Q-W");
        var v2 = await Admin(kit, s => s.NewVersionAsync(p, v1, "admin"));
        await kit.PublishAsync(v2.Id);
        await kit.TickAsync();
        Assert.NotEqual(WaitKind.QueueHeld, (await kit.OrderAsync(order.Id)).WaitKind);   // replaced by a publication: the pinned version still serves it

        await Admin(kit, s => s.DisableAsync(v1));   // the Admin withdraws the pinned version although another one is published
        await EmptyAsync(kit, "C1");
        await kit.TickAsync(); await kit.TickAsync();
        var held = await kit.OrderAsync(order.Id);
        Assert.Equal(WaitKind.QueueHeld, held.WaitKind); Assert.Empty(held.Legs); Assert.Equal(order.QueueSequence, held.QueueSequence);
    }

    // ================================================================== OQ-42-04

    [Fact]
    public async Task The_head_of_a_scope_waiting_ten_minutes_is_reported_once_and_nothing_is_done_to_the_queue()
    {
        await using var kit = await QueueKitAsync(fixture);
        await QueuedAsync(kit, "Q-S1", "A1", "S", "C1");
        await QueuedAsync(kit, "Q-S2", "A2", "S", "F1");
        foreach (var l in new[] { "A1", "A2", "C1" }) await kit.H.StockAsync(l, RackKind.Full, 1);
        var head = await RequestAsync(kit, "head", "A1", "Q-S1");
        var behind = await RequestAsync(kit, "behind", "A2", "Q-S2");
        Task<IReadOnlyList<QueueAlertDto>> Alerts() => kit.With(sp => sp.GetRequiredService<IRouteEngine>().QueueAlertsAsync());
        Task<int> Events() => kit.H.Db(d => d.RoutingEvents.CountAsync(e => e.Kind == "QUEUE_STALLED"));
        kit.H.Clock.Advance(TimeSpan.FromMinutes(9)); await kit.TickAsync();
        Assert.Empty(await Alerts()); Assert.Equal(0, await Events());

        kit.H.Clock.Advance(TimeSpan.FromMinutes(2)); await kit.TickAsync(); await kit.TickAsync();
        var alert = Assert.Single(await Alerts());
        Assert.Equal((head.Id, "S", 1), (alert.OrderId, alert.Scope, alert.Behind)); Assert.True(alert.Minutes >= 10);
        Assert.Equal(1, await Events());
        Assert.Empty((await kit.OrderAsync(head.Id)).Legs); Assert.Empty((await kit.OrderAsync(behind.Id)).Legs);   // information only
    }

    /// <summary>
    /// Evidence for the open point on OQ-54: two opposite directions in ONE scope block each other (the older waits for a slot that only the newer would free);
    /// in two scopes the newer one runs.
    /// </summary>
    [Theory]
    [InlineData("ONE", "ONE", false)]
    [InlineData("OUT", "BACK", true)]
    public async Task Opposite_directions_block_each_other_in_one_scope(string first, string second, bool secondRuns)
    {
        await using var kit = await QueueKitAsync(fixture);
        await QueuedAsync(kit, "Q-OUT", "A1", first, "C1");    // A1 -> C1
        await QueuedAsync(kit, "Q-BACK", "C1", second, "F1");  // C1 -> elsewhere: the only thing that frees C1
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
        await RequestAsync(kit, "out", "A1", "Q-OUT");
        var back = await RequestAsync(kit, "back", "C1", "Q-BACK");
        await kit.TickAsync(); await kit.TickAsync();
        Assert.Equal(secondRuns ? 1 : 0, (await kit.OrderAsync(back.Id)).Legs.Count);
    }

    // ================================================================== PE-3

    private sealed class BlockedDispatcher : ILegDispatcher
    {
        public string Reason = "FIRST_REASON";
        public Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default) => Task.FromResult(new LegDispatchResult(LegDispatchOutcome.Blocked, null, Reason));
        public Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default) => DispatchAsync(null!, ct);
    }

    [Fact]
    public async Task A_leg_that_stays_blocked_records_one_event_per_reason()
    {
        var dispatcher = new BlockedDispatcher();
        await using var kit = await RoutingKit.CreateAsync(fixture, more: s => s.AddSingleton<ILegDispatcher>(dispatcher));
        await PublishedAsync(kit, "T-BLK", "A1", "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await RequestAsync(kit, "b", "A1", "T-BLK");
        async Task TickManyAsync() { for (var i = 0; i < 4; i++) { kit.H.Clock.Advance(TimeSpan.FromSeconds(1)); await kit.TickAsync(); } }
        Task<List<string?>> Reasons() => kit.H.Db(d => d.RoutingEvents.Where(e => e.Kind == "LEG_DISPATCH_PENDING").OrderBy(e => e.OccurredAt).Select(e => e.Detail).ToListAsync());
        await TickManyAsync();
        Assert.Equal(["FIRST_REASON"], await Reasons());
        dispatcher.Reason = "SECOND_REASON"; await TickManyAsync();
        Assert.Equal(["FIRST_REASON", "SECOND_REASON"], await Reasons());
    }

    // ================================================================== OQ-42-07 and a rack stored at a buffer

    private static async Task<(RoutingKit Kit, Guid Order, Guid Rack)> RackAtBufferAsync(DatabaseFixture f)
    {
        var kit = await RoutingKit.CreateAsync(f);
        var (_, v) = await kit.CreateProcessAsync("T-BUF", RequestType.DeliveryRequest, RackKind.Full, ["A1"], MultiLeg("C1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);   // C1 is full: the rack goes to the buffer
        var order = (await kit.StartAsync("buf", "A1", RequestType.DeliveryRequest, RackKind.Full, "T-BUF")).Order;
        var rack = (await kit.RackAtAsync("A1"))!.Id;
        var leg = (await kit.OrderAsync(order.Id)).Legs.Single(l => !l.IsTerminal);
        Assert.Equal("F1", leg.DestinationLocationCode);
        await kit.PickAsync(leg.Id); await kit.TickAsync(); await kit.DropAsync(leg.Id); await kit.TickAsync();
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(order.Id)).State);
        return (kit, order.Id, rack);
    }

    [Fact]
    public async Task A_stock_adjustment_never_removes_a_rack_that_an_unfinished_order_stored_at_a_buffer()
    {
        var (kit, order, rack) = await RackAtBufferAsync(fixture);
        await using var _ = kit;
        Assert.Equal("RACK_IN_USE_BY_ORDER", await CodeOf(() => Inventory(kit, s => s.AdjustStockAsync("F1", RackKind.Full, 0, "op", "count"))));
        await kit.H.StockAsync("F1", RackKind.Full, 1);
        Assert.Equal(1, await Inventory(kit, s => s.AdjustStockAsync("F1", RackKind.Full, 1, "op", "count")));   // the free rack can go
        Assert.Equal((await kit.H.LocationAsync("F1")).Id, (await kit.RacksAsync()).Single(r => r.Id == rack).CurrentLocationId);
        await kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(order, "done", "admin"));
        Assert.Equal(0, await Inventory(kit, s => s.AdjustStockAsync("F1", RackKind.Full, 0, "op", "count")));
    }

    // ================================================================== OQ-42-03: one engine per station

    private static Dictionary<string, string?> Engines(params (string Station, string Engine)[] assignments)
    {
        var config = new Dictionary<string, string?>
        {
            ["Plc:Integration:StationFlows:0:Station"] = "A1", ["Plc:Integration:StationFlows:0:Signal"] = "Pickup_Request", ["Plc:Integration:StationFlows:0:Flow"] = "Bp01",
        };
        for (var i = 0; i < assignments.Length; i++) { config[$"Plc:Integration:StationEngines:{i}:Station"] = assignments[i].Station; config[$"Plc:Integration:StationEngines:{i}:Engine"] = assignments[i].Engine; }
        return config;
    }

    [Fact]
    public void The_engine_of_a_station_is_one_and_a_contradiction_gives_it_to_nobody()
    {
        StationEngine Of(bool enabled, params (string Station, string Engine)[] a) => new StationEngineRouter(Options.Create(new PlcRuntimeOptions
        {
            Integration = { Enabled = enabled, StationFlows = [new() { Station = "A1", Signal = "Pickup_Request", Flow = "Bp01" }], StationEngines = a.Select(x => new StationEngineAssignment { Station = x.Station, Engine = x.Engine }).ToList() },
        })).EngineFor("A1");
        Assert.Equal(StationEngine.Unassigned, Of(false));
        Assert.Equal(StationEngine.Task, Of(true));   // the behaviour before CR-042
        Assert.Equal(StationEngine.TransportOrder, Of(true, ("A1", "TransportOrder")));   // the cut-over wins over the old mapping
        Assert.Equal(StationEngine.Task, Of(false, ("A1", "task")));
        Assert.Equal(StationEngine.Invalid, Of(true, ("A1", "Task"), ("A1", "TransportOrder")));
        Assert.Equal(StationEngine.Invalid, Of(true, ("A1", "Both")));
    }

    private static async Task<RoutingKit> PlcKitAsync(DatabaseFixture f, Dictionary<string, string?> config)
    {
        var kit = await RoutingKit.CreateAsync(f, extraConfig: config);
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
        await PublishedAsync(kit, "T-ENG", "A1", "C1");
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        kit.Link.Offline.Add("PLC-T1");
        return kit;
    }

    private static async Task<SimulationStartResult> SimulateAsync(RoutingKit kit, string requestId)
    {
        Task<T> Sim<T>(Func<PlcSimulationService, Task<T>> f) => kit.With(sp => f(sp.GetRequiredService<PlcSimulationService>()));
        await Sim(s => Task.FromResult(s.Activate("admin", true)));
        await Sim(s => s.SetSignalAsync("A1", "Pickup_Request", "on"));
        await Sim(s => s.SetSignalAsync("A1", "Request_ID", requestId));
        await Sim(s => s.SetSignalAsync("A1", "Rack_Full", "1"));
        return await Sim(s => s.EvaluateStationAsync("A1", true));
    }

    private static Task<(int Tasks, int Orders)> CountsAsync(RoutingKit kit) => kit.H.Db(async d => (await d.Tasks.CountAsync(), await d.TransportOrders.CountAsync()));

    [Fact]
    public async Task A_station_of_the_task_engine_is_simulated_on_the_task_engine_and_the_order_engine_takes_nothing()
    {
        await using var kit = await PlcKitAsync(fixture, Engines(("A1", "Task")));
        var refused = await kit.PlcRequestAsync("PLC-T1", "A1", "E-1", true, rackFull: 1);
        Assert.Equal(StationEngineCodes.IsTask, refused.Code);
        var first = await SimulateAsync(kit, "E-1");
        Assert.True(first.Outcome == "Accepted", first.Code + ": " + first.Message);
        Assert.NotNull(first.TaskId);
        Assert.Equal("Duplicate", (await SimulateAsync(kit, "E-1")).Outcome);
        Assert.Equal((1, 0), await CountsAsync(kit));
        Assert.Equal("PLC-T1:A1:E-1", (await kit.H.GetTaskAsync(first.TaskId!.Value)).RequestKey);   // the key the physical intake would use
    }

    [Fact]
    public async Task A_simulated_task_engine_request_creates_nothing_in_Real_mode()
    {
        await using var kit = await PlcKitAsync(fixture, Engines(("A1", "Task")));
        await kit.H.Db(async db => { var s = ExecutionModeState.Default(kit.H.Clock.UtcNow); s.SwitchTo(ExecutionMode.Real, "test", kit.H.Clock.UtcNow); db.ExecutionModeStates.Add(s); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(SimulationCodes.RealMode, (await SimulateAsync(kit, "R-1")).Code);
        Assert.Equal((0, 0), await CountsAsync(kit));
    }

    [Fact]
    public async Task A_station_of_the_order_engine_gives_nothing_to_the_task_engine()
    {
        await using var kit = await PlcKitAsync(fixture, Engines(("A1", "TransportOrder")));
        kit.H.Services.GetRequiredService<StationSignalSimulator>().EnqueueRequest(new StationRequest("A1", "Bp01", "PLC-T1:A1:O-1"));
        await kit.H.IntakeAsync();
        Assert.Equal((0, 0), await CountsAsync(kit));
        Assert.Equal(WCS.Domain.Intake.StationRequestStatus.Rejected, await kit.H.Db(d => d.StationRequests.Select(r => r.Status).SingleAsync()));
        var run = await SimulateAsync(kit, "O-1b");
        Assert.True(run.Outcome == "Accepted", run.Code + ": " + run.Message);
        Assert.Equal((0, 1), await CountsAsync(kit));
    }

    [Fact]
    public async Task One_Request_ID_already_taken_by_one_engine_is_refused_by_the_other_after_a_cut_over()
    {
        await using var kit = await PlcKitAsync(fixture, new());   // unassigned: the order engine (simulation / injection) takes it
        var taken = await kit.PlcRequestAsync("PLC-T1", "A1", "X-1", true, rackFull: 1);
        Assert.True(taken.Outcome == PlcRequestOutcome.Accepted, taken.Code + ": " + taken.Message);
        kit.H.Services.GetRequiredService<StationSignalSimulator>().EnqueueRequest(new StationRequest("A1", "Bp01", "PLC-T1:A1:X-1"));
        await kit.H.IntakeAsync();
        Assert.Equal((0, 1), await CountsAsync(kit));

        await kit.H.Db(async db => { db.StationRequests.Add(new WCS.Domain.Intake.StationRequestRecord("PLC-T1:A2:X-2", "A2", "Bp01", kit.H.Clock.UtcNow)); await db.SaveChangesAsync(); return 0; });
        var other = await kit.PlcRequestAsync("PLC-T1", "A2", "X-2", true, rackFull: 2);
        Assert.Equal(StationEngineCodes.TakenByOther, other.Code);
    }

    // ================================================================== migration

    [Fact]
    public async Task The_withdrawal_migration_goes_down_and_up_and_refuses_while_a_held_request_depends_on_it()
    {
        var scratch = new DatabaseFixture();
        await scratch.InitializeAsync();
        try
        {
            const string before = "20261126014651_AddSimulatedStationTransaction";
            var cs = scratch.ConnectionString;
            WcsDbContext Context() => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
            async Task<string?> Scalar(string sql) { await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); return (await cmd.ExecuteScalarAsync())?.ToString(); }
            const string column = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'business_process_versions' AND column_name = 'WithdrawnByAdmin'";
            await using (var kit = await QueueKitAsync(scratch))
            {
                var (_, v) = await QueuedAsync(kit, "Q-MG", "A1", "S", "C1");
                await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("C1", RackKind.Full, 1);
                await RequestAsync(kit, "mg", "A1", "Q-MG");
                await Admin(kit, s => s.DisableAsync(v));
            }
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => Context().GetService<IMigrator>().MigrateAsync(before));
            Assert.Contains("ROLLBACK_REFUSED", refused.ToString());
            await Scalar("UPDATE transport_orders SET \"State\" = 'Cancelled'");
            await Context().GetService<IMigrator>().MigrateAsync(before);
            Assert.Equal("0", await Scalar(column));
            Assert.Equal("1", await Scalar("SELECT COUNT(*) FROM transport_orders"));
            await Context().Database.MigrateAsync();
            Assert.Equal("1", await Scalar(column));
        }
        finally { await scratch.DisposeAsync(); }
    }
}
