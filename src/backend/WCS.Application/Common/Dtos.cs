using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Application;

public sealed record AgvDto(Guid Id, string Code, string Role, string? RcsRobotCode, bool Enabled, string ConnectionStatus,
    string? StatusDetail, DateTimeOffset? LastStatusAt, Guid? ActiveTaskId, bool Busy,
    bool DispatchHold = false, string? DispatchHoldReason = null, DateTimeOffset? DispatchHoldAt = null, Guid? DispatchHoldTaskId = null);

public sealed record LocationDto(Guid Id, string Code, string Name, string Type, int RackCapacity, string AllowedRackKind,
    bool Enabled, bool RequiresPlcPermission, int Occupied, int Reserved, string Occupancy, Guid? ProductModelId = null, string? StationRole = null);

public sealed record RackDto(Guid Id, string? ExternalRackId, string? RackType, string Status, Guid? CurrentLocationId,
    Guid? CurrentTaskId, DateTimeOffset LastConfirmedAt, Guid? ProductModelId = null, string Disposition = "Auto");

public sealed record InventoryTransactionDto(Guid Id, string EventType, Guid? RackId, Guid? FromLocationId, Guid? ToLocationId,
    Guid? TaskId, string? StatusBefore, string? StatusAfter, string Source, string? UserName, string Bind, string? Note,
    DateTimeOffset OccurredAt);

/// <summary><c>Detail</c> is the original text; <c>DetailCode</c> + <c>DetailArgs</c> let the UI phrase it in the selected language (null: show <c>Detail</c> as it is).</summary>
public sealed record TaskHistoryDto(Guid Id, string? FromState, string? ToState, string Event, string? Detail, string Source,
    DateTimeOffset OccurredAt, string? DetailCode = null, IReadOnlyDictionary<string, string>? DetailArgs = null);

public sealed record TaskDto(Guid Id, string RequestKey, string Flow, string RackKind, Guid? AgvId, Guid RequestLocationId,
    Guid? SourceLocationId, Guid? DestinationLocationId, Guid? RackId, string State, string Leg, bool PlcPermissionGranted,
    string? StateReason, string RcsRequestCode, string? RcsTaskCode, bool RequiresReconciliation,
    DateTimeOffset? PickConfirmedAt, DateTimeOffset? DropConfirmedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt, string? StateReasonCode = null, IReadOnlyDictionary<string, string>? StateReasonArgs = null,
    string? RobotCode = null, string? RcsDispatchState = null, DateTimeOffset? AssignedAt = null,
    string? ExecutionMode = null);   // CR-028 / CR-038 (additive): ExecutionMode = Simulator | Real, null for a task created before CR-038

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public static class DtoMapper
{
    public static AgvDto ToDto(this Agv a, Guid? activeTaskId) => new(a.Id, a.Code, a.Role.ToString(), a.RcsRobotCode, a.Enabled,
        a.ConnectionStatus.ToString(), a.StatusDetail, a.LastStatusAt, activeTaskId, activeTaskId is not null,
        a.DispatchHold, a.DispatchHoldReason, a.DispatchHoldAt, a.DispatchHoldTaskId);

    public static LocationDto ToDto(this Location l, int occupied, int reserved) => new(l.Id, l.Code, l.Name, l.Type.ToString(),
        l.RackCapacity, l.AllowedRackKind.ToString(), l.Enabled, l.RequiresPlcPermission, occupied, reserved,
        l.GetOccupancy(occupied, reserved).ToString(), l.ProductModelId, l.StationRole?.ToString());

    public static RackDto ToDto(this Rack r) => new(r.Id, r.ExternalRackId, r.RackType, r.Status.ToString(), r.CurrentLocationId,
        r.CurrentTaskId, r.LastConfirmedAt, r.ProductModelId, r.Disposition.ToString());

    public static InventoryTransactionDto ToDto(this InventoryTransaction t) => new(t.Id, t.EventType.ToString(), t.RackId,
        t.FromLocationId, t.ToLocationId, t.TaskId, t.StatusBefore?.ToString(), t.StatusAfter?.ToString(), t.Source.ToString(),
        t.UserName, t.Bind.ToString(), t.Note, t.OccurredAt);

    public static TaskHistoryDto ToDto(this TaskHistoryEntry h) => new(h.Id, h.FromState?.ToString(), h.ToState?.ToString(),
        h.EventName, h.Detail, h.Source, h.OccurredAt, ResolvedCode(h)?.Code, ResolvedCode(h)?.Args);

    /// <summary>The stored code, or (entries written before codes existed) the code of one of the exact formats WCS itself wrote; null when the text cannot be identified reliably.</summary>
    private static HistoryDetail? ResolvedCode(TaskHistoryEntry h) =>
        h.DetailCode is { } c ? new HistoryDetail(c, HistoryDetail.ParseArgs(h.DetailArgsJson)) : HistoryDetail.Legacy(h.EventName, h.Detail);

    public static TaskDto ToDto(this TransportTask t) => new(t.Id, t.RequestKey, t.Flow.ToString(), t.RackKind.ToString(), t.AgvId,
        t.RequestLocationId, t.SourceLocationId, t.DestinationLocationId, t.RackId, t.State.ToString(), t.CurrentLeg.ToString(),
        t.PlcPermissionGranted, t.StateReason, t.RcsRequestCode, t.RcsTaskCode, t.RequiresReconciliation, t.PickConfirmedAt,
        t.DropConfirmedAt, t.CreatedAt, t.UpdatedAt, t.FinishedAt, ReasonCode(t)?.Code, ReasonCode(t)?.Args, t.RobotCode, t.RcsDispatchState?.ToString(), t.AssignedAt, t.ExecutionMode?.ToString());

    private static HistoryDetail? ReasonCode(TransportTask t) =>
        t.StateReasonCode is { } c ? new HistoryDetail(c, HistoryDetail.ParseArgs(t.StateReasonArgsJson)) : HistoryDetail.Legacy("STATE_CHANGED", t.StateReason);
}
