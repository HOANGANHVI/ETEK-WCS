using WCS.Domain.Common;

namespace WCS.Domain.Inventory;

/// <summary>
/// Resource reservation (inventory-management.md 4.5). A source reservation locks one rack (unique while ACTIVE);
/// a destination reservation consumes one slot of a location's capacity.
/// </summary>
public class Reservation
{
    private Reservation() { }

    public static Reservation ForSourceRack(Guid taskId, Guid rackId, Guid locationId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, Kind = ReservationKind.SourceRack, RackId = rackId, LocationId = locationId,
        Status = ReservationStatus.Active, CreatedAt = now,
    };

    public static Reservation ForDestination(Guid taskId, Guid locationId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, Kind = ReservationKind.DestinationLocation, LocationId = locationId,
        Status = ReservationStatus.Active, CreatedAt = now,
    };

    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public ReservationKind Kind { get; private set; }
    public Guid? RackId { get; private set; }
    public Guid? LocationId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public void Consume(DateTimeOffset now) => Close(ReservationStatus.Consumed, now);
    public void Release(DateTimeOffset now) => Close(ReservationStatus.Released, now);

    private void Close(ReservationStatus to, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active) return; // idempotent
        Status = to; ClosedAt = now;
    }
}
