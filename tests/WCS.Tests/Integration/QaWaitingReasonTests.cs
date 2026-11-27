using Microsoft.EntityFrameworkCore;
using WCS.Domain.Common;
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
}
