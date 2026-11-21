using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-027 baseline: ONE deterministic transport scenario and the statistics it produces (counts of tasks per state and per AGV, completed trips / legs, orders,
/// station transactions, inventory movements, racks, reservations, AGV configuration rows). The golden text below was captured on the code of <c>main</c> BEFORE CR-027.
/// After CR-027 the same scenario - also while the RCS fleet is being polled with 0, 1, 2, 3 or many AMRs - must produce exactly the same text:
/// the number of AMRs seen by RCS never changes the number of tasks, trips or any historical figure.
/// </summary>
public static class StatsScenario
{
    public static readonly string[] Rows = ["tasks", "completedPerAgv", "history", "orders", "legs", "tx", "inventoryTx", "racks", "reservations", "agvs"];

    /// <summary>Runs the scenario (legacy tasks on both AGVs + multi-leg routing orders) on the given kit and returns the statistics as stable text.</summary>
    public static async Task<string> RunAsync(RoutingKit kit, Func<Task>? between = null)
    {
        var h = kit.H;
        await h.StockAsync("F1", RackKind.Full, 3); await h.StockAsync("F3", RackKind.Empty, 3);
        // legacy flows: AGV1 (full racks) and AGV2 (empty racks)
        var full1 = (await h.CreateTaskAsync("BP05", "F5", "stat-full-1")).Task;
        var full2 = (await h.CreateTaskAsync("BP05", "F5", "stat-full-2")).Task;       // queued behind full1 (PER_AGV)
        var empty1 = (await h.CreateTaskAsync("BP02", "A1", "stat-empty-1")).Task;
        if (between is not null) await between();
        await h.AdvanceUntilAsync(full1.Id, TaskState.Completed);
        await h.AdvanceUntilAsync(empty1.Id, TaskState.Completed);
        await h.Scoped(async sp => { await sp.GetRequiredService<WCS.Application.Abstractions.ITaskOrchestrator>().CancelTaskAsync(full2.Id, "baseline cancel", "test"); return 0; });
        if (between is not null) await between();
        var cancelled = await h.GetTaskAsync(full2.Id); h.Rcs.ConfirmCancel(cancelled.RcsRequestCode);   // the RCS confirms the cancellation (as its callback / status poll would)
        await h.TickAsync(); await h.TickAsync();

        // routing orders: two complete (one leg each), one cancelled
        var (_, v) = await kit.CreateProcessAsync("T-STAT", RequestType.PickupRequest, RackKind.Full, ["C1", "A2"], OneLeg("Request", Dest(1, "B1")));
        await kit.PublishAsync(v);
        await h.StockAsync("C1", RackKind.Full, 1); await h.StockAsync("A2", RackKind.Full, 1);
        var o1 = (await kit.StartAsync("stat-o1", "C1", RequestType.PickupRequest, RackKind.Full)).Order;
        await kit.PickAsync(o1.Legs.Single().Id); await kit.DropAsync(o1.Legs.Single().Id);
        var o2 = (await kit.StartAsync("stat-o2", "A2", RequestType.PickupRequest, RackKind.Full)).Order;
        if (between is not null) await between();
        await kit.With(sp => sp.GetRequiredService<WCS.Application.Routing.IRouteEngine>().CancelAsync(o2.Id, "baseline cancel", "test"));
        if (between is not null) await between();
        return await CaptureAsync(h);
    }

    public static async Task<string> CaptureAsync(Harness h)
    {
        var o = new Dictionary<string, object>();
        o["tasks"] = await h.Db(async db => (await db.Tasks.AsNoTracking().GroupBy(t => t.State).Select(g => new { s = g.Key.ToString(), n = g.Count() }).ToListAsync()).OrderBy(x => x.s).ToDictionary(x => x.s, x => x.n));
        o["completedPerAgv"] = await h.Db(async db => (await (from t in db.Tasks.AsNoTracking().Where(t => t.State == TaskState.Completed) join a in db.Agvs on t.AgvId equals a.Id group t by a.Code into g select new { c = g.Key, n = g.Count() }).ToListAsync()).OrderBy(x => x.c).ToDictionary(x => x.c, x => x.n));
        o["history"] = await h.Db(async db => (await db.TaskHistory.AsNoTracking().GroupBy(x => x.EventName).Select(g => new { e = g.Key, n = g.Count() }).ToListAsync()).OrderBy(x => x.e).ToDictionary(x => x.e, x => x.n));
        o["orders"] = await h.Db(async db => (await db.TransportOrders.AsNoTracking().GroupBy(x => x.State).Select(g => new { s = g.Key.ToString(), n = g.Count() }).ToListAsync()).OrderBy(x => x.s).ToDictionary(x => x.s, x => x.n));
        o["legs"] = await h.Db(async db => (await db.TransportLegs.AsNoTracking().GroupBy(x => x.State).Select(g => new { s = g.Key.ToString(), n = g.Count() }).ToListAsync()).OrderBy(x => x.s).ToDictionary(x => x.s, x => x.n));
        o["tx"] = await h.Db(async db => (await db.StationTransactions.AsNoTracking().GroupBy(x => x.State).Select(g => new { s = g.Key.ToString(), n = g.Count() }).ToListAsync()).OrderBy(x => x.s).ToDictionary(x => x.s, x => x.n));
        o["inventoryTx"] = await h.Db(db => db.InventoryTransactions.CountAsync());
        o["racks"] = await h.Db(async db => (await db.Racks.AsNoTracking().GroupBy(x => x.Status).Select(g => new { s = g.Key.ToString(), n = g.Count() }).ToListAsync()).OrderBy(x => x.s).ToDictionary(x => x.s, x => x.n));
        o["reservations"] = await h.Db(async db => (await db.Reservations.AsNoTracking().GroupBy(x => x.Status).Select(g => new { s = g.Key.ToString(), n = g.Count() }).ToListAsync()).OrderBy(x => x.s).ToDictionary(x => x.s, x => x.n));
        o["agvs"] = await h.Db(async db => (await db.Agvs.AsNoTracking().OrderBy(a => a.Code).Select(a => new { a.Code, role = a.Role.ToString(), a.RcsRobotCode, a.Enabled, a.DispatchHold }).ToListAsync()).Select(a => $"{a.Code}/{a.role}/{a.RcsRobotCode}/{a.Enabled}/{a.DispatchHold}").ToList());
        return JsonSerializer.Serialize(o);
    }
}

[Collection("Database")]
public class Cr027StatisticsBaselineTests(DatabaseFixture fixture)
{
    /// <summary>Captured on main (a0c4c70) before CR-027; do NOT edit to make a later change pass.</summary>
    public const string Golden = "{\"tasks\":{\"Cancelled\":1,\"Completed\":2},\"completedPerAgv\":{\"AGV1\":1,\"AGV2\":1},\"history\":{\"DESTINATION_RESERVED\":2,\"PICK_CONFIRMED\":2,\"RCS_CANCEL_REQUESTED\":1,\"RECONCILIATION_REQUIRED\":1,\"SOURCE_RESERVED\":3,\"STATE_CHANGED\":22,\"TASK_CREATED\":3},\"orders\":{\"Cancelled\":1,\"Completed\":1},\"legs\":{\"Completed\":1},\"tx\":{\"HandoverCompleted\":2},\"inventoryTx\":14,\"racks\":{\"Empty\":3,\"Full\":5},\"reservations\":{\"Active\":1,\"Consumed\":6},\"agvs\":[\"AGV1/FullRack/R-FULL/True/False\",\"AGV2/EmptyRack/R-EMPTY/True/False\"]}";

    [Fact]
    public async Task The_baseline_scenario_produces_the_statistics_recorded_before_CR027()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var stats = await StatsScenario.RunAsync(kit);
        Assert.Equal(Golden, stats);
    }
}
