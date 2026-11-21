using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Fleet;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-028 (owner decision 2026-11-21): every Transport Leg stores the AMR the RCS chose for THAT leg (RobotCode, AssignedAt) - RcsManaged, RCS simulator only. The robot is never defaulted,
/// never copied from the Transport Task, never an AGV1/AGV2 assumption; AgvId stays empty; a robot that the fleet does not know is kept and flagged; a hand-over is not invented.
/// </summary>
[Collection("Database")]
public class Cr028LegRobotTests(DatabaseFixture fixture)
{
    private static readonly string[] A = ["A1", "A2"];
    private static Dictionary<string, string?> Managed => new() { ["Rcs:Dispatch:Mode"] = "RcsManaged" };

    private static async Task<RoutingKit> KitAsync(DatabaseFixture f, bool managed, int amrs, bool twoStep = false)
    {
        var kit = await RoutingKit.CreateAsync(f, extraConfig: managed ? Managed : null);
        kit.H.Rcs.SetFleetSize(amrs);
        var def = twoStep
            ? new VersionDefinition("s1", [Move("s1", 1, "BufferStorage", "Request", "Candidates", "s2", [Dest(1, "F1")]), Move("s2", 2, "BufferRetrieval", "PreviousDestination", "Candidates", "done", [Dest(1, "B1")]), Done()])
            : OneLeg("Request", Dest(1, "B1"), Dest(2, "B2"));
        var (_, v) = await kit.CreateProcessAsync("T-LEG", RequestType.PickupRequest, RackKind.Full, A, def);
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);
        return kit;
    }

    private static async Task SyncAsync(RoutingKit kit, int seconds = 6) { kit.H.Clock.Advance(TimeSpan.FromSeconds(seconds)); await kit.TickAsync(); }
    private static Task<TransportLeg> LegAsync(RoutingKit kit, Guid orderId, int seq = 1) => kit.H.Db(db => db.TransportLegs.AsNoTracking().SingleAsync(l => l.OrderId == orderId && l.Sequence == seq));
    private static Task<List<string>> EventsAsync(RoutingKit kit, Guid orderId) => kit.H.Db(db => db.RoutingEvents.AsNoTracking().Where(e => e.OrderId == orderId).OrderBy(e => e.OccurredAt).Select(e => e.Kind).ToListAsync());

    // ================================================================================================ stored per leg, from the RCS answer
    [Fact]
    public async Task Each_leg_stores_the_AMR_the_RCS_chose_for_it_and_AgvId_stays_empty()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2);
        await kit.H.Scoped(sp => sp.GetRequiredService<FleetService>().RefreshAsync(CancellationToken.None, force: true));   // the fleet read model knows SIM-001 / SIM-002
        var o1 = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var o2 = (await kit.StartAsync("o2", "A2", RequestType.PickupRequest, RackKind.Full)).Order;
        var before = await LegAsync(kit, o1.Id);
        Assert.NotNull(before.AgvTaskCode); Assert.Null(before.RobotCode); Assert.Null(before.AssignedAt);   // sent is NOT assigned
        Assert.True((await kit.TransactionsAsync()).Where(t => t.IsActive).All(t => t.State < StationTransactionState.AgvAssigned));

        await SyncAsync(kit);
        var l1 = await LegAsync(kit, o1.Id); var l2 = await LegAsync(kit, o2.Id);
        Assert.Equal(new[] { "SIM-001", "SIM-002" }, new[] { l1.RobotCode, l2.RobotCode }.Order().ToArray());   // the RCS spread the two legs over the two AMRs
        Assert.All(new[] { l1, l2 }, l => { Assert.NotNull(l.AssignedAt); Assert.Null(l.AgvId); });
        Assert.All((await kit.TransactionsAsync()).Where(t => t.IsActive), t => { Assert.Equal(StationTransactionState.AgvAssigned, t.State); Assert.Null(t.AgvId); });   // now an AMR is known
        Assert.Contains("LEG_ROBOT_ASSIGNED", await EventsAsync(kit, o1.Id));
        Assert.DoesNotContain("LEG_ROBOT_NOT_IN_FLEET", await EventsAsync(kit, o1.Id));
        Assert.Equal(0, await kit.H.Db(db => db.Agvs.CountAsync(a => a.RcsRobotCode == "SIM-001" || a.RcsRobotCode == "SIM-002")));   // no AGV was created from the robot codes
    }

    [Fact]
    public async Task A_leg_the_RCS_has_not_assigned_yet_keeps_no_robot_and_is_assigned_later()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 0);                     // the RCS has no AMR
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        await SyncAsync(kit);
        var waiting = await LegAsync(kit, o.Id);
        Assert.Null(waiting.RobotCode); Assert.Equal(LegState.InProgress, waiting.State);         // not attributed to anybody, not failed
        kit.H.Rcs.SetFleetSize(1); kit.H.Rcs.AssignPending();
        await SyncAsync(kit);
        Assert.Equal("SIM-001", (await LegAsync(kit, o.Id)).RobotCode);
    }

    [Fact]
    public async Task A_failing_status_query_is_a_synchronisation_error_not_a_failed_leg_and_is_recorded_once()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 1);
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        kit.H.Rcs.TaskQueryFailure = true;
        await SyncAsync(kit); await SyncAsync(kit); await SyncAsync(kit);
        var leg = await LegAsync(kit, o.Id);
        Assert.Null(leg.RobotCode); Assert.Equal(LegState.InProgress, leg.State);
        Assert.Equal(1, (await EventsAsync(kit, o.Id)).Count(k => k == "LEG_ROBOT_SYNC_ERROR"));   // not repeated at every tick
        kit.H.Rcs.TaskQueryFailure = false;
        await SyncAsync(kit);
        Assert.Equal("SIM-001", (await LegAsync(kit, o.Id)).RobotCode);                            // recovers
    }

    [Fact]
    public async Task A_robot_that_is_not_in_the_fleet_read_model_is_kept_and_flagged_and_no_AGV_is_created()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 1);                     // FleetService never ran: the read model is empty
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var agvsBefore = await kit.H.Db(db => db.Agvs.CountAsync());
        kit.H.Rcs.ReassignRobot((await LegAsync(kit, o.Id)).AgvRequestCode, "GHOST-9");
        await SyncAsync(kit);
        var leg = await LegAsync(kit, o.Id);
        Assert.Equal("GHOST-9", leg.RobotCode); Assert.Null(leg.AgvId);
        Assert.Contains("LEG_ROBOT_NOT_IN_FLEET", await EventsAsync(kit, o.Id));
        Assert.Equal(agvsBefore, await kit.H.Db(db => db.Agvs.CountAsync()));                      // nothing invented; AGV1 not assumed
    }

    // ================================================================================================ never taken from the task / order
    [Fact]
    public async Task The_robot_of_a_Transport_Task_never_overwrites_or_fills_the_robot_of_a_leg()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2);
        await kit.H.StockAsync("F1", RackKind.Full, 1);
        var task = (await kit.H.CreateTaskAsync("BP05", "F5", "t1")).Task;
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        kit.H.Rcs.ReassignRobot((await LegAsync(kit, o.Id)).AgvRequestCode, "SIM-002");
        kit.H.Rcs.ReassignRobot((await kit.H.GetTaskAsync(task.Id)).RcsRequestCode, "SIM-001");
        await SyncAsync(kit); await kit.H.TickAsync();                                              // routing tick (legs) and scheduler tick (tasks)
        Assert.Equal("SIM-002", (await LegAsync(kit, o.Id)).RobotCode);                            // the leg's own answer
        Assert.Equal("SIM-001", (await kit.H.GetTaskAsync(task.Id)).RobotCode);                    // the task's own answer, untouched by the leg sync
    }

    // ================================================================================================ changes of AMR
    [Fact]
    public async Task A_change_of_AMR_before_the_pick_is_followed_but_while_the_rack_is_on_the_AMR_it_is_refused_and_recorded_once()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2);
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var leg = await LegAsync(kit, o.Id);
        await SyncAsync(kit);
        var first = (await LegAsync(kit, o.Id)).RobotCode!;
        var other = first == "SIM-001" ? "SIM-002" : "SIM-001";
        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, other);
        await SyncAsync(kit, 61);                                                                   // assigned legs are re-checked every AssignedRecheckSeconds
        Assert.Equal(other, (await LegAsync(kit, o.Id)).RobotCode);                                 // before the pick: the RCS re-planned, WCS follows
        Assert.Contains("LEG_ROBOT_CHANGED", await EventsAsync(kit, o.Id));

        await kit.PickAsync(leg.Id);                                                                // the rack is on the AMR now
        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, first);
        await SyncAsync(kit, 61);                                                                   // the FIRST sighting of another AMR is only a warning (a second identical one would confirm it)
        var held = await LegAsync(kit, o.Id);
        Assert.Equal(other, held.RobotCode);                                                        // NOT changed: no approved hand-over
        Assert.Equal(LegState.InProgress, held.State);                                              // and nothing else happens either
        Assert.Equal(1, (await EventsAsync(kit, o.Id)).Count(k => k == "LEG_ROBOT_CHANGE_REFUSED"));
    }

    [Fact]
    public async Task Consecutive_legs_each_keep_their_own_AMR_and_a_difference_is_only_reported()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2, twoStep: true);
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var l1 = await LegAsync(kit, o.Id);
        await SyncAsync(kit);
        await kit.PickAsync(l1.Id); await kit.DropAsync(l1.Id);                                     // leg 1 done (rack stored at the buffer, confirmed) -> leg 2 is sent
        await SyncAsync(kit);
        var a = await LegAsync(kit, o.Id, 1); var b = await LegAsync(kit, o.Id, 2);
        Assert.NotNull(a.RobotCode); Assert.NotNull(b.RobotCode);
        Assert.NotEqual(a.RobotCode, b.RobotCode);                                                  // the least loaded AMR of the simulated RCS: another one for leg 2
        Assert.Equal(LegState.Completed, a.State);                                                  // leg 1 is finished and keeps its history
        Assert.Contains("LEG_ROBOT_DIFFERS_FROM_PREVIOUS_LEG", await EventsAsync(kit, o.Id));       // reported, no action taken, state untouched
        Assert.Equal(LegState.InProgress, b.State);
    }

    // ================================================================================================ LEG_ROBOT_CHANGE_REFUSED: warning first, ReconciliationRequired only when confirmed
    private static async Task<(Guid OrderId, TransportLeg Leg, string First, string Other)> PickedLegAsync(RoutingKit kit)
    {
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var leg = await LegAsync(kit, o.Id);
        await SyncAsync(kit);
        var first = (await LegAsync(kit, o.Id)).RobotCode!;
        await kit.PickAsync(leg.Id);                                                                // the rack is on the AMR
        return (o.Id, leg, first, first == "SIM-001" ? "SIM-002" : "SIM-001");
    }

    [Fact]
    public async Task One_inconsistent_report_is_a_warning_and_is_cleared_when_the_RCS_names_the_original_AMR_again()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2);
        var (oid, leg, first, other) = await PickedLegAsync(kit);
        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, other); await SyncAsync(kit, 61);                 // sighting 1 (an assigned leg is re-checked every 60 s)
        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, first); await SyncAsync(kit);                   // the RCS is consistent again: cleared
        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, other); await SyncAsync(kit, 61);                   // a new sighting 1 - NOT consecutive with the first
        var order = await kit.OrderAsync(oid);
        Assert.Equal(OrderState.InProgress, order.State);                                           // never recoverable-by-hand because of a transient answer
        Assert.Equal(first, (await LegAsync(kit, oid)).RobotCode);
        Assert.DoesNotContain("LEG_ROBOT_CHANGE_CONFIRMED", await EventsAsync(kit, oid));
        Assert.Equal(2, (await EventsAsync(kit, oid)).Count(k => k == "LEG_ROBOT_CHANGE_REFUSED"));
    }

    [Fact]
    public async Task A_failing_query_between_two_reports_neither_confirms_nor_clears()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2);
        var (oid, leg, first, other) = await PickedLegAsync(kit);
        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, other); await SyncAsync(kit, 61);                 // sighting 1
        kit.H.Rcs.TaskQueryFailure = true; await SyncAsync(kit);                                    // inconclusive: nothing changes
        Assert.Equal(OrderState.InProgress, (await kit.OrderAsync(oid)).State);
        kit.H.Rcs.TaskQueryFailure = false; await SyncAsync(kit);                                   // sighting 2 (consecutive): confirmed
        Assert.Equal(OrderState.ReconciliationRequired, (await kit.OrderAsync(oid)).State);
        Assert.Equal(first, (await LegAsync(kit, oid)).RobotCode);
    }

    [Fact]
    public async Task A_confirmed_change_while_the_rack_is_on_the_AMR_stops_the_Order_and_updates_nothing()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2, twoStep: true);
        var (oid, leg, first, other) = await PickedLegAsync(kit);
        var before = await LegAsync(kit, oid);
        var rackBefore = (await kit.RacksAsync()).Single(r => r.Id == before.RackId);
        var invTx = await kit.H.Db(db => db.InventoryTransactions.CountAsync());
        var tasksInRcs = kit.H.Rcs.TaskCount;

        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, other);
        await SyncAsync(kit, 61); await SyncAsync(kit);                                             // the same different AMR in two consecutive queries
        var order = await kit.OrderAsync(oid);
        Assert.Equal(OrderState.ReconciliationRequired, order.State);
        var kinds = await EventsAsync(kit, oid);
        Assert.Contains("LEG_ROBOT_CHANGE_CONFIRMED", kinds); Assert.Contains("RECONCILIATION_REQUIRED", kinds);

        var after = await LegAsync(kit, oid);
        Assert.Equal(first, after.RobotCode); Assert.Equal(before.AssignedAt, after.AssignedAt);   // RobotCode NOT updated
        Assert.Equal(LegState.InProgress, after.State); Assert.Null(after.DropConfirmedAt);        // no hand-over confirmed
        var rack = (await kit.RacksAsync()).Single(r => r.Id == before.RackId);
        Assert.Equal(RackStatus.InTransit, rack.Status); Assert.Null(rack.CurrentLocationId);       // rack position NOT touched
        Assert.Equal(rackBefore.Status, rack.Status);
        Assert.Equal(invTx, await kit.H.Db(db => db.InventoryTransactions.CountAsync()));          // inventory untouched

        kit.H.Rcs.ReassignRobot(leg.AgvRequestCode, first); await SyncAsync(kit); await SyncAsync(kit);   // later answers do not change anything either
        await kit.TickAsync();
        Assert.Single((await kit.OrderAsync(oid)).Legs);                                            // no next leg is created or dispatched
        Assert.Equal(tasksInRcs, kit.H.Rcs.TaskCount);
        Assert.Equal(OrderState.ReconciliationRequired, (await kit.OrderAsync(oid)).State);
    }

    // ================================================================================================ the RCS saying "completed" is not a hand-over
    [Fact]
    public async Task RCS_reports_the_task_completed_but_without_a_confirmed_drop_nothing_advances_until_the_drop_is_confirmed()
    {
        await using var kit = await KitAsync(fixture, managed: true, amrs: 2, twoStep: true);
        var (oid, leg, _, _) = await PickedLegAsync(kit);
        kit.H.Rcs.SetStatus(leg.AgvRequestCode, RcsTaskStatus.Completed);
        var tasksInRcs = kit.H.Rcs.TaskCount;
        await SyncAsync(kit, 61); await SyncAsync(kit, 61); await kit.TickAsync();
        var waiting = await LegAsync(kit, oid);
        Assert.Equal(LegState.InProgress, waiting.State); Assert.Null(waiting.DropConfirmedAt);     // not completed
        Assert.Single((await kit.OrderAsync(oid)).Legs);                                            // the next leg is NOT dispatched
        Assert.Equal(tasksInRcs, kit.H.Rcs.TaskCount);
        Assert.Equal(1, (await EventsAsync(kit, oid)).Count(k => k == "LEG_RCS_COMPLETED_WITHOUT_DROP"));   // reported once, state untouched
        Assert.Equal(OrderState.InProgress, (await kit.OrderAsync(oid)).State);

        await kit.DropAsync(leg.Id);                                                                // the confirmed drop: rack placed, inventory updated, history written
        var order = await kit.OrderAsync(oid);
        Assert.Equal(LegState.Completed, order.Legs.Single(l => l.Sequence == 1).State);
        Assert.Equal(2, order.Legs.Count);                                                          // only now the next leg exists
        Assert.Contains("DROP_CONFIRMED", await EventsAsync(kit, oid));
    }

    // ================================================================================================ Legacy
    [Fact]
    public async Task Legacy_legs_are_unchanged_no_robot_code_no_status_queries()
    {
        await using var kit = await KitAsync(fixture, managed: false, amrs: 2);
        var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        await SyncAsync(kit); await SyncAsync(kit);
        var leg = await LegAsync(kit, o.Id);
        Assert.NotNull(leg.AgvId); Assert.Null(leg.RobotCode); Assert.Null(leg.AssignedAt);        // AGV1 by role, exactly as before
        Assert.Equal(0, kit.H.Rcs.QueryCalls);                                                      // the RCS is not asked anything
        Assert.StartsWith("SIM-LEG-", leg.AgvTaskCode);                                             // the Legacy simulator path is the old one
    }

    // ================================================================================================ rollback of AddLegRobotCode
    private static WcsDbContext Context(string cs) => new(new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(cs).Options);
    private const string PreviousMigration = "20261121014633_AddRcsManagedDispatch";

    private static async Task<long> ScalarAsync(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c); return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Rollback_of_AddLegRobotCode_keeps_every_leg_and_saves_the_robot_codes_and_they_come_back()
    {
        var scratch = new DatabaseFixture();
        await scratch.InitializeAsync();
        try
        {
            await using (var kit = await KitAsync(scratch, managed: true, amrs: 1))
            {
                var o = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
                await SyncAsync(kit);
                Assert.Equal("SIM-001", (await LegAsync(kit, o.Id)).RobotCode);
                var cs = scratch.ConnectionString;

                await Context(cs).GetService<IMigrator>().MigrateAsync(PreviousMigration);          // Down
                Assert.Equal(0, await ScalarAsync(cs, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='transport_legs' AND column_name='RobotCode'"));
                Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM transport_legs"));      // the leg (history) is still there
                Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM transport_leg_robot_backup WHERE \"RobotCode\" = 'SIM-001'"));   // and its AMR was saved, not lost

                await Context(cs).Database.MigrateAsync();                                           // Up again
                Assert.Equal(1, await ScalarAsync(cs, "SELECT COUNT(*) FROM transport_legs WHERE \"RobotCode\" = 'SIM-001' AND \"AssignedAt\" IS NOT NULL"));   // restored
            }
        }
        finally { await scratch.DisposeAsync(); }
    }
}
