using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Application;

public sealed record AgvDto(Guid Id, string Code, string Role, string? RcsRobotCode, bool Enabled, string ConnectionStatus,
    string? StatusDetail, DateTimeOffset? LastStatusAt, Guid? ActiveTaskId, bool Busy);

public sealed record LocationDto(Guid Id, string Code, string Name, string Type, int RackCapacity, string AllowedRackKind,
    bool Enabled, bool RequiresPlcPermission, int Occupied, int Reserved, string Occupancy);

public sealed record RackDto(Guid Id, string? ExternalRackId, string? RackType, string Status, Guid? CurrentLocationId,
    Guid? CurrentTaskId, DateTimeOffset LastConfirmedAt);

public sealed record InventoryTransactionDto(Guid Id, string EventType, Guid? RackId, Guid? FromLocationId, Guid? ToLocationId,
    Guid? TaskId, string? StatusBefore, string? StatusAfter, string Source, string? UserName, string Bind, string? Note,
    DateTimeOffset OccurredAt);

public sealed record TaskHistoryDto(Guid Id, string? FromState, string? ToState, string Event, string? Detail, string Source,
    DateTimeOffset OccurredAt);

public sealed record TaskDto(Guid Id, string RequestKey, string Flow, string RackKind, Guid AgvId, Guid RequestLocationId,
    Guid? SourceLocationId, Guid? DestinationLocationId, Guid? RackId, string State, string Leg, bool PlcPermissionGranted,
    string? StateReason, string RcsRequestCode, string? RcsTaskCode, bool RequiresReconciliation,
    DateTimeOffset? PickConfirmedAt, DateTimeOffset? DropConfirmedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public static class DtoMapper
{
    public static AgvDto ToDto(this Agv a, Guid? activeTaskId) => new(a.Id, a.Code, a.Role.ToString(), a.RcsRobotCode, a.Enabled,
        a.ConnectionStatus.ToString(), a.StatusDetail, a.LastStatusAt, activeTaskId, activeTaskId is not null);

    public static LocationDto ToDto(this Location l, int occupied, int reserved) => new(l.Id, l.Code, l.Name, l.Type.ToString(),
        l.RackCapacity, l.AllowedRackKind.ToString(), l.Enabled, l.RequiresPlcPermission, occupied, reserved,
        l.GetOccupancy(occupied, reserved).ToString());

    public static RackDto ToDto(this Rack r) => new(r.Id, r.ExternalRackId, r.RackType, r.Status.ToString(), r.CurrentLocationId,
        r.CurrentTaskId, r.LastConfirmedAt);

    public static InventoryTransactionDto ToDto(this InventoryTransaction t) => new(t.Id, t.EventType.ToString(), t.RackId,
        t.FromLocationId, t.ToLocationId, t.TaskId, t.StatusBefore?.ToString(), t.StatusAfter?.ToString(), t.Source.ToString(),
        t.UserName, t.Bind.ToString(), t.Note, t.OccurredAt);

    public static TaskHistoryDto ToDto(this TaskHistoryEntry h) => new(h.Id, h.FromState?.ToString(), h.ToState?.ToString(),
        h.EventName, h.Detail, h.Source, h.OccurredAt);

    public static TaskDto ToDto(this TransportTask t) => new(t.Id, t.RequestKey, t.Flow.ToString(), t.RackKind.ToString(), t.AgvId,
        t.RequestLocationId, t.SourceLocationId, t.DestinationLocationId, t.RackId, t.State.ToString(), t.CurrentLeg.ToString(),
        t.PlcPermissionGranted, t.StateReason, t.RcsRequestCode, t.RcsTaskCode, t.RequiresReconciliation, t.PickConfirmedAt,
        t.DropConfirmedAt, t.CreatedAt, t.UpdatedAt, t.FinishedAt);
}
