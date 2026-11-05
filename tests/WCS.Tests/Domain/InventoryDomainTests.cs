using Microsoft.Extensions.Options;
using WCS.Application.Flows;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;

namespace WCS.Tests.Domain;

public class InventoryDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Rack_pick_makes_it_in_transit_without_a_location()
    {
        var loc = Guid.NewGuid();
        var rack = new Rack(RackStatus.Full, loc, null, null, Now);
        rack.BeginTransit(Guid.NewGuid(), Now);
        Assert.Equal(RackStatus.InTransit, rack.Status);
        Assert.Null(rack.CurrentLocationId);
    }

    [Theory]
    [InlineData(RackStatus.InTransit)]
    [InlineData(RackStatus.Unknown)]
    public void Rack_that_is_not_full_or_empty_cannot_be_picked(RackStatus status)
    {
        var rack = new Rack(status, status == RackStatus.InTransit ? null : Guid.NewGuid(), null, null, Now);
        Assert.Throws<DomainException>(() => rack.BeginTransit(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Rack_cannot_be_placed_as_in_transit()
    {
        var rack = new Rack(RackStatus.Full, Guid.NewGuid(), null, null, Now);
        Assert.Throws<DomainException>(() => rack.PlaceAt(Guid.NewGuid(), RackStatus.InTransit, Now));
    }

    [Fact]
    public void Rack_can_exist_without_an_external_id()
    {
        // NEEDS_CONFIRMATION: the rack model is not decided; quantity-only operation must remain possible.
        var rack = new Rack(RackStatus.Empty, Guid.NewGuid(), null, null, Now);
        Assert.Null(rack.ExternalRackId);
    }

    [Fact]
    public void Location_capacity_counts_occupied_and_reserved()
    {
        var loc = new Location("F1", "F1", LocationType.Buffer, 2, AllowedRackKind.Full, false, Now);
        Assert.True(loc.HasFreeSlot(occupied: 1, reserved: 0));
        Assert.False(loc.HasFreeSlot(occupied: 1, reserved: 1));
        Assert.False(loc.HasFreeSlot(occupied: 2, reserved: 0));
    }

    [Fact]
    public void Location_rack_kind_and_reconciliation_rules()
    {
        var loc = new Location("F3", "F3", LocationType.Buffer, 5, AllowedRackKind.Empty, false, Now);
        Assert.True(loc.Accepts(RackKind.Empty));
        Assert.False(loc.Accepts(RackKind.Full));

        loc.SetReconciliationRequired(true, Now);
        Assert.False(loc.HasFreeSlot(0, 0));
        Assert.Equal(OccupancyStatus.Unknown, loc.GetOccupancy(0, 0));
    }

    [Fact]
    public void Occupancy_is_derived_and_a_reservation_alone_is_not_occupancy()
    {
        var loc = new Location("B1", "B1", LocationType.Station, 1, AllowedRackKind.Any, true, Now);
        Assert.Equal(OccupancyStatus.Empty, loc.GetOccupancy(0, 0));
        Assert.Equal(OccupancyStatus.Reserved, loc.GetOccupancy(0, 1));
        Assert.Equal(OccupancyStatus.Occupied, loc.GetOccupancy(1, 0));
    }

    [Fact]
    public void Reservation_close_is_idempotent()
    {
        var r = Reservation.ForSourceRack(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        r.Release(Now);
        r.Consume(Now.AddMinutes(1));
        Assert.Equal(ReservationStatus.Released, r.Status);
    }

    [Fact]
    public void Flow_catalog_rejects_bp04_configuration()
    {
        var options = Options.Create(new ProcessFlowOptions());
        options.Value.Flows["Bp04"] = new FlowRuleOptions { RackKind = "Full" };
        var ex = Assert.Throws<InvalidOperationException>(() => new FlowCatalog(options));
        Assert.Contains("BP-04", ex.Message);
    }

    [Theory]
    [InlineData("BP-04")]
    [InlineData("A3")]
    [InlineData("D1")]
    [InlineData("")]
    public void Flow_catalog_does_not_parse_review_or_legacy_flows(string text)
    {
        var catalog = new FlowCatalog(Options.Create(new ProcessFlowOptions()));
        Assert.False(catalog.TryParseFlow(text, out _));
    }

    [Theory]
    [InlineData("BP-01", ProcessFlow.Bp01)]
    [InlineData("bp03", ProcessFlow.Bp03)]
    [InlineData("BP_06", ProcessFlow.Bp06)]
    public void Flow_catalog_parses_approved_flows(string text, ProcessFlow expected)
    {
        var catalog = new FlowCatalog(Options.Create(new ProcessFlowOptions()));
        Assert.True(catalog.TryParseFlow(text, out var flow));
        Assert.Equal(expected, flow);
    }
}
