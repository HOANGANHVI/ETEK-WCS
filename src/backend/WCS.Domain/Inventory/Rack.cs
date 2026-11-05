using WCS.Domain.Common;

namespace WCS.Domain.Inventory;

/// <summary>
/// Rack record owned by WCS (BR-03).
///
/// NEEDS_CONFIRMATION: the final rack model (individual Rack ID vs. quantity per type) is NOT approved.
/// This entity is a neutral proposal: every rack has a WCS-internal surrogate <see cref="Id"/>; the field-level
/// <see cref="ExternalRackId"/> is OPTIONAL. Quantity-only operation is represented by racks without an external id.
/// No business logic may depend on <see cref="ExternalRackId"/> being present.
/// </summary>
public class Rack
{
    private Rack() { }

    public Rack(RackStatus status, Guid? locationId, string? rackType, string? externalRackId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Status = status;
        CurrentLocationId = locationId;
        RackType = rackType;
        ExternalRackId = string.IsNullOrWhiteSpace(externalRackId) ? null : externalRackId.Trim();
        LastConfirmedAt = now;
    }

    public Guid Id { get; private set; }
    public string? ExternalRackId { get; private set; }
    public string? RackType { get; private set; }
    public RackStatus Status { get; private set; }
    /// <summary>A rack has at most one current location (inventory-management.md 4.4). Null while in transit.</summary>
    public Guid? CurrentLocationId { get; private set; }
    public Guid? CurrentTaskId { get; private set; }
    public DateTimeOffset LastConfirmedAt { get; private set; }
    public uint Version { get; private set; }

    public void BeginTransit(Guid taskId, DateTimeOffset now)
    {
        if (Status is not (RackStatus.Full or RackStatus.Empty))
            throw new DomainException("RACK_NOT_PICKABLE", $"Rack in status {Status} cannot be picked.");
        if (CurrentLocationId is null) throw new DomainException("RACK_NOT_AT_LOCATION", "Rack has no current location.");
        Status = RackStatus.InTransit;
        CurrentLocationId = null;
        CurrentTaskId = taskId;
        LastConfirmedAt = now;
    }

    public void PlaceAt(Guid locationId, RackStatus status, DateTimeOffset now)
    {
        if (status is RackStatus.InTransit) throw new DomainException("INVALID_RACK_STATUS", "Cannot place a rack as IN_TRANSIT.");
        Status = status;
        CurrentLocationId = locationId;
        CurrentTaskId = null;
        LastConfirmedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now) { Status = RackStatus.Unknown; LastConfirmedAt = now; }

    public void Bind(string externalRackId)
    {
        if (string.IsNullOrWhiteSpace(externalRackId)) throw new DomainException("RACK_ID_REQUIRED", "External rack id is required to bind.");
        ExternalRackId = externalRackId.Trim();
    }
}
