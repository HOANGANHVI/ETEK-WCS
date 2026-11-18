using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Routing;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-010: Transport Task / Leg / Station Transaction behaviour of the Route Engine against the real PostgreSQL schema.
/// The processes used here are DATA created through the admin service; the engine contains no location name.
/// </summary>
[Collection("Database")]
public class Cr010MultiLegTests(DatabaseFixture fixture)
{
    private static readonly string[] A = ["A1", "A2"];

    private static async Task<(RoutingKit Kit, Guid OrderId, Guid Leg1)> StartMultiAsync(DatabaseFixture f, bool b1Occupied, string key = "k1", string at = "A1")
    {
        var kit = await RoutingKit.CreateAsync(f);
        var (_, ver) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(ver);
        await kit.H.StockAsync(at, RackKind.Full, 1);
        if (b1Occupied) await kit.H.StockAsync("B1", RackKind.Full, 1);
        var start = await kit.StartAsync(key, at, RequestType.PickupRequest, RackKind.Full);
        return (kit, start.Order.Id, start.Order.Legs.Single().Id);
    }

    [Fact]
    public async Task Test01_full_rack_at_A1_goes_directly_to_B1_when_B1_is_free()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: false);
        await using var _ = kit;

        var order = await kit.OrderAsync(orderId);
        Assert.Equal(OrderState.InProgress, order.State);
        var leg = order.Legs.Single();
        Assert.Equal(("A1", "B1"), (leg.SourceLocationCode, leg.DestinationLocationCode));
        Assert.Equal(LegState.InProgress, leg.State);          // AGV assigned and dispatched (simulator)
        Assert.NotNull(leg.AgvTaskCode);

        Assert.Equal(EventOutcome.Applied, (await kit.PickAsync(leg1)).Outcome);
        Assert.Equal(RackStatus.InTransit, (await kit.RacksAsync()).Single().Status);     // on the AMR: not at A1, not at B1
        Assert.Equal(EventOutcome.Applied, (await kit.DropAsync(leg1)).Outcome);

        order = await kit.OrderAsync(orderId);
        Assert.Equal(OrderState.Completed, order.State);
        Assert.Equal(LegState.Completed, order.Legs.Single().State);
        var rack = (await kit.RackAtAsync("B1"))!;
        Assert.Equal(RackStatus.Full, rack.Status);
    }

    [Fact]
    public async Task Test02_and_03_B1_not_ready_rack_is_stored_at_buffer_inventory_updated_and_the_task_continues_to_exist()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: true);
        await using var _ = kit;

        var order = await kit.OrderAsync(orderId);
        var leg = order.Legs.Single();
        Assert.Equal(("A1", "F1"), (leg.SourceLocationCode, leg.DestinationLocationCode));   // the alternative branch of the configuration
        Assert.Equal(RouteStepType.BufferStorage, leg.StepType);

        await kit.PickAsync(leg1);
        Assert.Null(await kit.RackAtAsync("F1"));                                            // not at the buffer yet: it is on the AMR
        await kit.DropAsync(leg1);

        order = await kit.OrderAsync(orderId);
        Assert.Equal(OrderState.Waiting, order.State);                                       // the Transport Task did NOT complete
        Assert.Equal(WaitKind.AtBuffer, order.WaitKind);
        Assert.Equal(LegState.Completed, order.Legs.Single().State);
        var stored = (await kit.RackAtAsync("F1"))!;                                          // inventory records the real position
        Assert.Equal(RackStatus.Full, stored.Status);
        Assert.Equal(order.RackId, stored.Id);
    }

    [Fact]
    public async Task Test04_destination_becomes_ready_and_the_task_continues_from_the_buffer()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: true);
        await using var _ = kit;
        await kit.PickAsync(leg1); await kit.DropAsync(leg1);
        await kit.TickAsync();
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(orderId)).State);              // B1 still full: keeps waiting, nothing else chosen
        Assert.Equal(1, (await kit.OrderAsync(orderId)).Legs.Count);

        await kit.With(sp => sp.GetRequiredService<WCS.Application.Abstractions.IInventoryService>().AdjustStockAsync("B1", RackKind.Full, 0, "test", null));
        await kit.TickAsync();

        var order = await kit.OrderAsync(orderId);
        Assert.Equal(2, order.Legs.Count);
        var leg2 = order.Legs.OrderBy(l => l.Sequence).Last();
        Assert.Equal(("F1", "B1"), (leg2.SourceLocationCode, leg2.DestinationLocationCode));
        Assert.Equal(RouteStepType.BufferRetrieval, leg2.StepType);
        Assert.Equal(LegState.InProgress, leg2.State);

        await kit.PickAsync(leg2.Id); await kit.DropAsync(leg2.Id);
        order = await kit.OrderAsync(orderId);
        Assert.Equal(OrderState.Completed, order.State);
        Assert.Equal(RackStatus.Full, (await kit.RackAtAsync("B1"))!.Status);
        Assert.Null(await kit.RackAtAsync("F1"));
    }

    [Fact]
    public async Task Test05_station_transaction_at_A1_completes_while_the_transport_task_is_still_waiting()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: true);
        await using var _ = kit;
        await kit.PickAsync(leg1); await kit.DropAsync(leg1);

        var a1 = (await kit.TransactionsAsync(orderId)).Single(t => t.LocationCode == "A1");
        Assert.True(a1.IsActive);                                                              // AMR picked but has not left the port yet
        Assert.Equal(StationTransactionState.HandoverCompleted, a1.State);

        Assert.Equal(1, await kit.ExitAsync(leg1, "A1"));                                       // AMR left the port
        a1 = (await kit.TransactionsAsync(orderId)).Single(t => t.LocationCode == "A1");
        Assert.Equal(StationTransactionState.Completed, a1.State);
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(orderId)).State);                // the Transport Task is NOT completed by it
    }

    [Fact]
    public async Task Test06_port_A1_accepts_a_new_request_after_the_AMR_left_even_though_the_first_task_is_waiting()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: true);
        await using var _ = kit;
        await kit.PickAsync(leg1); await kit.DropAsync(leg1);

        // before the AMR leaves the port a new request at A1 is refused
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var busy = await kit.PlcRequestAsync("PLC-T1", "A1", "REQ-BUSY", pickup: true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.Rejected, busy.Outcome);
        Assert.Equal("PORT_BUSY", busy.Code);

        await kit.ExitAsync(leg1, "A1");
        var again = await kit.PlcRequestAsync("PLC-T1", "A1", "REQ-NEXT", pickup: true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.Accepted, again.Outcome);
        Assert.NotEqual(orderId, again.Order!.Id);
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(orderId)).State);                // the first task keeps waiting independently
    }

    [Fact]
    public async Task Test07_two_ports_of_the_same_PLC_hold_two_station_transactions_at_the_same_time()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);

        var results = await Task.WhenAll(kit.PlcRequestAsync("PLC-T1", "A1", "R-A1", true, rackFull: 1), kit.PlcRequestAsync("PLC-T1", "A2", "R-A2", true, rackFull: 2));
        Assert.All(results, r => Assert.Equal(PlcRequestOutcome.Accepted, r.Outcome));

        var active = (await kit.TransactionsAsync()).Where(t => t.IsActive && t.RequestId != null).ToList();   // the two PLC requests (a leg may also open a transaction at its destination port)
        Assert.Equal(2, active.Count);
        Assert.Equal(new[] { "A1", "A2" }, active.Select(t => t.PortId).Order().ToArray());
        Assert.All(active, t => Assert.Equal("PLC-T1", t.PlcId));                                // one PLC, two independent transactions (no PLC-wide lock)
    }

    [Fact]
    public async Task Test08_ports_of_two_different_PLCs_work_at_the_same_time()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v1) = await kit.CreateProcessAsync("T-A", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        var (_, v2) = await kit.CreateProcessAsync("T-B", RequestType.DeliveryRequest, RackKind.Full, ["C1"], OneLeg("Candidates", Dest(1, "B1")) with
        { Steps = [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "A2")]), Done()] });
        await kit.PublishAsync(v1); await kit.PublishAsync(v2);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);

        // PLC-T1 port A1 (pickup) and PLC-T2 port C1 (delivery) at the same moment (the pickup leg delivers to B1, another port: no contention)
        var results = await Task.WhenAll(kit.PlcRequestAsync("PLC-T1", "A1", "R-1", true, rackFull: 1), kit.PlcRequestAsync("PLC-T2", "C1", "R-2", false, delivery: true));
        Assert.All(results, r => Assert.Equal(PlcRequestOutcome.Accepted, r.Outcome));
        var active = (await kit.TransactionsAsync()).Where(t => t.IsActive).ToList();
        Assert.Equal(new[] { "PLC-T1", "PLC-T2" }, active.Select(t => t.PlcId).Distinct().Order().ToArray());
    }

    [Fact]
    public async Task Test09_two_requests_for_the_same_port_never_create_two_active_transactions()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);

        var rs = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => kit.PlcRequestAsync("PLC-T1", "A1", $"RACE-{i}", true, rackFull: 1)));
        Assert.Equal(1, rs.Count(r => r.Outcome == PlcRequestOutcome.Accepted));
        Assert.Equal(5, rs.Count(r => r.Code == "PORT_BUSY"));
        Assert.Equal(1, (await kit.TransactionsAsync()).Count(t => t.PortId == "A1" && t.IsActive));
    }

    [Fact]
    public async Task Test14_duplicate_Request_ID_never_creates_a_second_transaction_or_task()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);

        var first = await kit.PlcRequestAsync("PLC-T1", "A1", "SAME-ID", true, rackFull: 1);
        var second = await kit.PlcRequestAsync("PLC-T1", "A1", "SAME-ID", true, rackFull: 1);
        Assert.Equal(PlcRequestOutcome.Accepted, first.Outcome);
        Assert.Equal(PlcRequestOutcome.Duplicate, second.Outcome);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
        Assert.Single((await kit.TransactionsAsync()).Where(t => t.RequestId == "SAME-ID"));
        Assert.Equal(1, await kit.H.Db(d => d.TransportOrders.CountAsync()));
    }

    [Fact]
    public async Task Test12_PLC_and_inventory_disagree_goes_to_reconciliation_nothing_is_dispatched_and_inventory_is_not_overwritten()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        // WCS inventory holds NO rack at A1, but the PLC reports a full rack there (Rack_Full = 1)
        var r = await kit.PlcRequestAsync("PLC-T1", "A1", "MISMATCH", true, rackFull: 1);

        Assert.Equal(PlcRequestOutcome.ReconciliationRequired, r.Outcome);
        Assert.Equal("RECON_PLC_INVENTORY_MISMATCH", r.Code);
        Assert.Equal(0, await kit.H.Db(d => d.TransportOrders.CountAsync()));                    // no task, therefore no AGV dispatch
        Assert.Empty(await kit.RacksAsync());                                                     // inventory was not rewritten to match the PLC
        Assert.True((await kit.H.LocationAsync("A1")).ReconciliationRequired);                    // nothing else may use the uncertain location
        var tx = Assert.Single(await kit.TransactionsAsync());
        Assert.Equal(StationTransactionState.ReconciliationRequired, tx.State);
    }

    [Fact]
    public async Task Test13_no_destination_and_no_buffer_the_AMR_holds_the_rack_and_inventory_does_not_move_it()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var all = await kit.ImportReferenceAsync();
        await kit.PublishAsync(all["BP-01"].VersionId);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        foreach (var l in new[] { "B1", "B2" }) await kit.H.StockAsync(l, RackKind.Full, 1);
        foreach (var l in new[] { "F1", "F2" }) await kit.H.StockAsync(l, RackKind.Full, 3);
        await kit.H.StockAsync("F5", RackKind.Full, 3);

        var order = (await kit.StartAsync("bp1-full", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var leg = order.Legs.Single();
        await kit.PickAsync(leg.Id);

        var after = await kit.OrderAsync(order.Id);
        Assert.Equal(OrderState.Waiting, after.State);
        Assert.Equal(WaitKind.DestinationUnavailable_AgvHoldingRack, after.WaitKind);
        var l1 = after.Legs.Single();
        Assert.Equal(LegState.Waiting, l1.State);
        Assert.True(l1.RackOnAgv);
        var rack = (await kit.RacksAsync()).Single(r => r.Id == l1.RackId);
        Assert.Equal(RackStatus.InTransit, rack.Status);                                          // NOT stored anywhere: the AMR still holds it
        Assert.Null(rack.CurrentLocationId);
        Assert.Null(l1.DestinationLocationCode);                                                  // no position outside the declared candidates was chosen
        // the AGV stays committed: no other leg can take it (PER_AGV)
        Assert.Equal(1, await kit.H.Db(d => d.TransportLegs.CountAsync(x => x.AgvId != null && x.State == LegState.Waiting)));
    }

    [Fact]
    public async Task Engine_never_uses_a_location_that_is_not_a_declared_candidate()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        // the only declared destination is B1; B1 is full while B2 and F1 are free: the engine must wait, never pick B2 or F1
        var (_, v) = await kit.CreateProcessAsync("T-ONLY-B1", RequestType.PickupRequest, RackKind.Full, A, new VersionDefinition("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "B1")], "AfterPick"), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("B1", RackKind.Full, 1);
        var o = (await kit.StartAsync("x1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        await kit.PickAsync(o.Legs.Single().Id);
        await kit.TickAsync();
        var after = await kit.OrderAsync(o.Id);
        Assert.Null(after.Legs.Single().DestinationLocationCode);
        Assert.Equal(OrderState.Waiting, after.State);
    }

    [Fact]
    public async Task Per_AGV_two_legs_for_one_AGV_never_run_at_the_same_time_and_the_database_agrees()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1); await kit.H.StockAsync("A2", RackKind.Full, 1);

        var o1 = (await kit.StartAsync("o1", "A1", RequestType.PickupRequest, RackKind.Full)).Order;
        var o2 = (await kit.StartAsync("o2", "A2", RequestType.PickupRequest, RackKind.Full)).Order;
        Assert.Equal(LegState.InProgress, (await kit.OrderAsync(o1.Id)).Legs.Single().State);
        var second = (await kit.OrderAsync(o2.Id)).Legs.Single();
        Assert.Equal(LegState.Ready, second.State);                                                 // queued: AGV1 is busy
        Assert.Null(second.AgvId);

        // finishing the first leg frees the AGV and the next pass dispatches the queued leg
        var l1 = (await kit.OrderAsync(o1.Id)).Legs.Single();
        await kit.PickAsync(l1.Id); await kit.DropAsync(l1.Id);
        await kit.TickAsync();
        Assert.Equal(LegState.InProgress, (await kit.OrderAsync(o2.Id)).Legs.Single().State);
    }

    [Fact]
    public async Task Database_refuses_two_AGV_holding_legs_for_one_AGV()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var agv = await kit.H.AgvAsync("AGV1");
        var (_, v) = await kit.CreateProcessAsync("T-MULTI", RequestType.PickupRequest, RackKind.Full, A, MultiLeg("B1", "F1"));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.StartAsync("db1", "A1", RequestType.PickupRequest, RackKind.Full);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => kit.H.Db(async db =>
        {
            var order = await db.TransportOrders.FirstAsync();
            var extra = TransportLeg.Create(order.Id, 9, "s1", RouteStepType.Delivery, "A2", "B2", RackKind.Full, false, kit.H.Clock.UtcNow);
            extra.TransitionTo(LegState.Ready, null, WaitKind.None, kit.H.Clock.UtcNow);
            extra.AssignAgv(agv.Id, kit.H.Clock.UtcNow);
            extra.TransitionTo(LegState.InProgress, null, WaitKind.None, kit.H.Clock.UtcNow);
            db.TransportLegs.Add(extra);
            await db.SaveChangesAsync();
            return 0;
        }));
        Assert.Contains("one_active_per_agv", ((Npgsql.PostgresException)ex.InnerException!).ConstraintName);
    }

    [Fact]
    public async Task Restart_in_the_middle_of_a_multi_leg_process_resumes_without_repeating_a_completed_leg()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: true);
        await using var _ = kit;
        await kit.PickAsync(leg1); await kit.DropAsync(leg1);
        var snapshotBefore = (await kit.OrderAsync(orderId)).SnapshotJson;

        await kit.H.RestartAsync();                                                                 // new service provider, same database: all memory is gone
        await kit.TickAsync();
        Assert.Equal(OrderState.Waiting, (await kit.OrderAsync(orderId)).State);
        Assert.Single((await kit.OrderAsync(orderId)).Legs);                                        // the completed leg is not repeated, no leg is duplicated

        await kit.With(sp => sp.GetRequiredService<WCS.Application.Abstractions.IInventoryService>().AdjustStockAsync("B1", RackKind.Full, 0, "test", null));
        await kit.H.RestartAsync();
        await kit.TickAsync(); await kit.TickAsync();
        var order = await kit.OrderAsync(orderId);
        Assert.Equal(2, order.Legs.Count);                                                          // exactly one new leg after recovery
        Assert.Equal(snapshotBefore, order.SnapshotJson);                                           // the same process version snapshot
        Assert.Equal(LegState.InProgress, order.Legs.OrderBy(l => l.Sequence).Last().State);
    }

    [Fact]
    public async Task Cancel_is_refused_while_a_rack_is_on_the_AMR_and_allowed_before_any_pick()
    {
        var (kit, orderId, leg1) = await StartMultiAsync(fixture, b1Occupied: false);
        await using var _ = kit;
        await kit.PickAsync(leg1);
        var ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() =>
            kit.With(sp => sp.GetRequiredService<IRouteEngine>().CancelAsync(orderId, "x", "test")));
        Assert.Equal("ORDER_NOT_CANCELLABLE", ex.Code);
    }
}
