using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

public sealed record LegDto(Guid Id, int Sequence, string StepKey, string StepType, string? SourceLocationCode, string? DestinationLocationCode, string RackKind,
    Guid? RackId, Guid? AgvId, string? AgvTaskCode, string State, string WaitKind, string? StateReason, bool RackOnAgv,
    DateTimeOffset? PickConfirmedAt, DateTimeOffset? DropConfirmedAt, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt,
    string? RobotCode = null, DateTimeOffset? AssignedAt = null);   // CR-028 (additive): the AMR the RCS chose for this leg

public sealed record StationTransactionDto(Guid Id, string PortId, string PlcId, string LocationCode, string Operation, Guid? OrderId, Guid? LegId, string? RequestId,
    string State, int HandshakeStep, Guid? AgvId, bool Suspended, string? SuspendedReason, string? ReconciliationReason, string? FailureReason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? FinishedAt);

public sealed record OrderDto(Guid Id, string RequestKey, string ProcessCode, int ProcessVersion, string RackKind, string RequestLocationCode, string State, string WaitKind,
    string? StateReason, string? CurrentStepKey, Guid? RackId, IReadOnlyList<LegDto> Legs, IReadOnlyList<StationTransactionDto> Transactions,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? FinishedAt);

public sealed record RoutingEventDto(Guid Id, Guid? LegId, Guid? TransactionId, string Kind, string? Detail, string Source, DateTimeOffset OccurredAt);

public static class RoutingMapping
{
    public static LegDto ToDto(this TransportLeg l) => new(l.Id, l.Sequence, l.StepKey, l.StepType.ToString(), l.SourceLocationCode, l.DestinationLocationCode,
        l.RackKind.ToString(), l.RackId, l.AgvId, l.AgvTaskCode, l.State.ToString(), l.WaitKind.ToString(), l.StateReason, l.RackOnAgv,
        l.PickConfirmedAt, l.DropConfirmedAt, l.CreatedAt, l.FinishedAt, l.RobotCode, l.AssignedAt);

    public static StationTransactionDto ToDto(this StationTransaction t) => new(t.Id, t.PortId, t.PlcId, t.LocationCode, t.Operation.ToString(), t.OrderId, t.LegId,
        t.RequestId, t.State.ToString(), t.HandshakeStep, t.AgvId, t.Suspended, t.SuspendedReason, t.ReconciliationReason, t.FailureReason,
        t.CreatedAt, t.UpdatedAt, t.FinishedAt);

    public static OrderDto ToDto(this TransportOrder o, IEnumerable<StationTransaction>? transactions = null) => new(o.Id, o.RequestKey, o.ProcessCode, o.ProcessVersionNumber,
        o.RackKind.ToString(), o.RequestLocationCode, o.State.ToString(), o.WaitKind.ToString(), o.StateReason, o.CurrentStepKey, o.RackId,
        o.Legs.OrderBy(l => l.Sequence).Select(l => l.ToDto()).ToList(), (transactions ?? []).OrderBy(t => t.CreatedAt).Select(t => t.ToDto()).ToList(),
        o.CreatedAt, o.UpdatedAt, o.FinishedAt);

    public static RoutingEventDto ToDto(this RoutingEvent e) => new(e.Id, e.LegId, e.TransactionId, e.Kind, e.Detail, e.Source, e.OccurredAt);
}

public sealed record StartOrderCommand(string RequestKey, string RequestLocationCode, RequestType RequestType, RackKind? RackKind, string? ProcessCode, string? RequestedBy, Guid? TransactionId = null);

public sealed record StartOrderResult(OrderDto Order, bool Created);

public enum EventOutcome { Applied = 1, Duplicate = 2, Rejected = 3 }

public sealed record EventResult(EventOutcome Outcome, string Message, OrderDto? Order);
