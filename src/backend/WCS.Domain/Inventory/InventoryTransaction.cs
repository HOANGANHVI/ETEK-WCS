using WCS.Domain.Common;

namespace WCS.Domain.Inventory;

/// <summary>Immutable audit record of every inventory change (inventory-management.md 4.2 "Inventory Transaction").</summary>
public class InventoryTransaction
{
    private InventoryTransaction() { }

    public InventoryTransaction(InventoryEventType type, Guid? rackId, Guid? fromLocationId, Guid? toLocationId, Guid? taskId,
        RackStatus? statusBefore, RackStatus? statusAfter, ConfirmationSource source, string? userName, BindOutcome bind,
        string? note, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        EventType = type; RackId = rackId; FromLocationId = fromLocationId; ToLocationId = toLocationId; TaskId = taskId;
        StatusBefore = statusBefore; StatusAfter = statusAfter; Source = source; UserName = userName; Bind = bind;
        Note = note; OccurredAt = now;
    }

    public Guid Id { get; private set; }
    public InventoryEventType EventType { get; private set; }
    public Guid? RackId { get; private set; }
    public Guid? FromLocationId { get; private set; }
    public Guid? ToLocationId { get; private set; }
    public Guid? TaskId { get; private set; }
    public RackStatus? StatusBefore { get; private set; }
    public RackStatus? StatusAfter { get; private set; }
    public ConfirmationSource Source { get; private set; }
    public string? UserName { get; private set; }
    public BindOutcome Bind { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
