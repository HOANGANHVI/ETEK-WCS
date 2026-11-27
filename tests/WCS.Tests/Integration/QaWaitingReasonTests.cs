using Microsoft.EntityFrameworkCore;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Routing;
using WCS.Tests.Support;
using static WCS.Tests.Support.RoutingKit;

namespace WCS.Tests.Integration;

/// <summary>QA-001: an order that waits because the station port is still held says so, instead of "no usable rack"; the cause is updated when it changes. Nothing else changes.</summary>
[Collection("Database")]
public class QaWaitingReasonTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_waiting_order_names_the_held_port_and_starts_after_the_AMR_has_left()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-PORT", RequestType.PickupRequest, RackKind.Full, ["A1"], OneLeg("Request", Dest(1, "B1"), Dest(1, "B2", order: 2)));
        await kit.PublishAsync(v);
        var first = await kit.StartAsync("QA-1", "A1", RequestType.PickupRequest, RackKind.Full);
        Assert.Equal("No usable rack at the request location", (await kit.OrderAsync(first.Order.Id)).StateReason);   // really no rack

        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.TickAsync();
        var leg = (await kit.OrderAsync(first.Order.Id)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id);
        var held = await kit.H.Db(d => d.StationTransactions.SingleAsync(t => t.PortId == "A1" && t.State != StationTransactionState.Completed));
        Assert.Equal(StationTransactionState.HandoverCompleted, held.State);

        await kit.H.StockAsync("A1", RackKind.Full, 1);
        var second = await kit.StartAsync("QA-2", "A1", RequestType.PickupRequest, RackKind.Full);
        await kit.TickAsync();
        var waiting = await kit.OrderAsync(second.Order.Id);
        Assert.Equal((OrderState.Waiting, WaitKind.SourceUnavailable), (waiting.State, waiting.WaitKind));
        Assert.StartsWith("PORT_HELD: port A1 is held by a Station Transaction in HandoverCompleted", waiting.StateReason);

        Assert.Equal(1, await kit.ExitAsync(leg.Id, "A1"));
        await kit.TickAsync();
        Assert.Single((await kit.OrderAsync(second.Order.Id)).Legs);
    }

    /// <summary>CR-052 (LVC-D843F4F4): the rack is to be brought to the request location, whose port is still held by the order that took a rack away from it.</summary>
    [Fact]
    public async Task A_destination_that_is_the_request_location_names_the_held_port_and_the_order_starts_after_the_AMR_has_left()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, away) = await kit.CreateProcessAsync("T-AWAY", RequestType.PickupRequest, RackKind.Full, ["A1"], OneLeg("Request", Dest(1, "F1")));
        await kit.PublishAsync(away);
        var (_, back) = await kit.CreateProcessAsync("T-BACK", RequestType.DeliveryRequest, RackKind.Empty, ["A1"],
            new("s1", [Move("s1", 1, "Delivery", "Candidates", "Request", "done", [Src(1, "F3")]), Done()]));
        await kit.PublishAsync(back);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.StockAsync("F3", RackKind.Empty, 1);
        var first = await kit.StartAsync("D-1", "A1", RequestType.PickupRequest, RackKind.Full, "T-AWAY");
        await kit.TickAsync();
        var leg = (await kit.OrderAsync(first.Order.Id)).Legs.Single();
        await kit.PickAsync(leg.Id); await kit.DropAsync(leg.Id);                       // A1 is empty again, its port is still held

        var second = await kit.StartAsync("D-2", "A1", RequestType.DeliveryRequest, RackKind.Empty, "T-BACK");
        await kit.TickAsync(); await kit.TickAsync();
        var waiting = await kit.OrderAsync(second.Order.Id);
        Assert.Equal((OrderState.Waiting, WaitKind.DestinationUnavailable), (waiting.State, waiting.WaitKind));
        Assert.StartsWith("PORT_HELD: No usable destination - A1: port A1 is held by a Station Transaction in HandoverCompleted", waiting.StateReason);
        Assert.Contains(first.Order.Id.ToString("N")[..8], waiting.StateReason);          // which order holds it
        Assert.Empty(waiting.Legs);
        Assert.Equal(0, await kit.H.Db(d => d.Reservations.CountAsync(r => r.Status == ReservationStatus.Active)));
        Assert.Equal(1, await kit.H.Db(d => d.RoutingEvents.CountAsync(e => e.OrderId == second.Order.Id && e.Kind == "ORDER_WAITING")));   // an unchanged cause is recorded once

        Assert.Equal(1, await kit.ExitAsync(leg.Id, "A1"));
        await kit.TickAsync();
        var started = (await kit.OrderAsync(second.Order.Id)).Legs.Single();
        Assert.Equal(("F3", "A1"), (started.SourceLocationCode, started.DestinationLocationCode));
    }

    /// <summary>CR-052: every candidate says why it cannot be used; the port wording is used only when held ports are the only cause.</summary>
    [Theory]
    [InlineData("BeforePick", WaitKind.DestinationUnavailable)]
    [InlineData("AfterPick", WaitKind.DestinationUnavailable_AgvHoldingRack)]
    public async Task Each_unusable_destination_candidate_names_its_own_cause(string resolution, WaitKind wait)
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, v) = await kit.CreateProcessAsync("T-CAUSE", RequestType.PickupRequest, RackKind.Full, ["A1"],
            new("s1", [Move("s1", 1, "Pickup", "Request", "Candidates", "done", [Dest(1, "B1"), Dest(1, "B2", order: 2)], resolution), Done()]));
        await kit.PublishAsync(v);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        await kit.H.StockAsync("B1", RackKind.Full, 1);
        await kit.H.Db(async d => { var b2 = await d.Locations.SingleAsync(l => l.Code == "B2"); b2.SetReconciliationRequired(true, kit.H.Clock.UtcNow); await d.SaveChangesAsync(); return 0; });
        var order = await kit.StartAsync("C-1", "A1", RequestType.PickupRequest, RackKind.Full);
        await kit.TickAsync();
        if (resolution == "AfterPick") { await kit.PickAsync((await kit.OrderAsync(order.Order.Id)).Legs.Single().Id); await kit.TickAsync(); }
        var o = await kit.OrderAsync(order.Order.Id);
        Assert.Equal((OrderState.Waiting, wait), (o.State, o.WaitKind));
        Assert.DoesNotContain("PORT_HELD:", o.StateReason);
        Assert.Contains("B1: no free slot (1 rack(s) and 0 reservation(s), capacity 1)", o.StateReason);
        Assert.Contains("B2: needs inventory reconciliation", o.StateReason);
    }
}
