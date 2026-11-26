using WCS.Domain.Common;

namespace WCS.Domain.Routing;

/// <summary>
/// Transport Task of CR-010 (the whole transport job of one Business Process). It is named <c>TransportOrder</c> in code because
/// <see cref="WCS.Domain.Tasks.TransportTask"/> already exists and keeps its meaning: the AGV execution task of the approved flows (its state machine is protected).
/// An order owns its Legs; finishing a Station Transaction never completes an order.
/// </summary>
public class TransportOrder
{
    private TransportOrder() { }

    /// <summary>CR-038: Simulator or Real, fixed when the order is created (null only for rows created before CR-038). It never moves to the other dispatcher.</summary>
    public WCS.Domain.Rcs.ExecutionMode? ExecutionMode { get; private set; }
    public void PinExecutionMode(WCS.Domain.Rcs.ExecutionMode mode)
    {
        if (ExecutionMode is { } current && current != mode) throw new DomainException("EXECUTION_MODE_FIXED", "The execution mode cannot be changed after creation.");
        ExecutionMode = mode;
    }

    public static TransportOrder Create(string requestKey, ProcessSnapshot snapshot, string requestLocationCode, string? requestedBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(requestKey)) throw new DomainException("REQUEST_KEY_REQUIRED", "RequestKey is required for idempotency.");
        return new TransportOrder
        {
            Id = Guid.NewGuid(), RequestKey = requestKey.Trim(), ProcessId = snapshot.ProcessId, ProcessCode = snapshot.ProcessCode,
            ProcessVersionId = snapshot.VersionId, ProcessVersionNumber = snapshot.VersionNumber, SnapshotJson = snapshot.ToJson(),
            RackKind = snapshot.RackKind, RequestLocationCode = requestLocationCode, CurrentStepKey = snapshot.FirstStepKey,
            RequestedBy = requestedBy, State = OrderState.Created, CreatedAt = now, UpdatedAt = now, StateEnteredAt = now,
        };
    }

    private static readonly Dictionary<OrderState, OrderState[]> Edges = new()
    {
        [OrderState.Created] = [OrderState.Accepted, OrderState.InProgress, OrderState.Waiting, OrderState.Failed, OrderState.Cancelled],
        [OrderState.Accepted] = [OrderState.InProgress, OrderState.Waiting, OrderState.Failed, OrderState.Cancelled, OrderState.ReconciliationRequired],
        [OrderState.InProgress] = [OrderState.Waiting, OrderState.Completed, OrderState.Failed, OrderState.Cancelled, OrderState.ReconciliationRequired],
        [OrderState.Waiting] = [OrderState.InProgress, OrderState.Completed, OrderState.Failed, OrderState.Cancelled, OrderState.ReconciliationRequired],
        [OrderState.ReconciliationRequired] = [OrderState.InProgress, OrderState.Waiting, OrderState.Failed, OrderState.Cancelled],
        [OrderState.Completed] = [], [OrderState.Failed] = [], [OrderState.Cancelled] = [],
    };

    public Guid Id { get; private set; }
    public string RequestKey { get; private set; } = default!;
    public Guid ProcessId { get; private set; }
    public string ProcessCode { get; private set; } = default!;
    public Guid ProcessVersionId { get; private set; }
    public int ProcessVersionNumber { get; private set; }
    /// <summary>Copy of the process version used by this order (see <see cref="ProcessSnapshot"/>).</summary>
    public string SnapshotJson { get; private set; } = default!;
    public RackKind RackKind { get; private set; }
    public string RequestLocationCode { get; private set; } = default!;
    public string? RequestedBy { get; private set; }
    /// <summary>CR-021: the id that ties every log event of this order's life (legs, station transactions) together; set once at creation, null for orders created before CR-021.</summary>
    public string? CorrelationId { get; private set; }
    public void SetCorrelation(string? correlationId) { if (CorrelationId is null && !string.IsNullOrWhiteSpace(correlationId)) CorrelationId = correlationId.Trim().Length > 100 ? correlationId.Trim()[..100] : correlationId.Trim(); }
    public Guid? RackId { get; private set; }
    /// <summary>CR-040: position in the official queue, given ONCE at admission and never changed. NULL = not a queued request (a process without the readiness requirement).</summary>
    public long? QueueSequence { get; private set; }
    /// <summary>CR-040 (D-13): the Queue Ordering Scope, pinned the first time it is known. NULL = not determined yet (OQ-54).</summary>
    public string? QueueScope { get; private set; }
    /// <summary>CR-040 (D-12): true while the request is Held. Only a request that has not been dispatched can be Held.</summary>
    public bool QueueHeld { get; private set; }

    public void Enqueue(long sequence, string? scope, DateTimeOffset now)
    {
        if (QueueSequence is not null) throw new DomainException("QUEUE_SEQUENCE_FIXED", "The queue position of a request never changes.");
        QueueSequence = sequence; QueueScope = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim(); UpdatedAt = now;
    }

    public void PinQueueScope(string scope, DateTimeOffset now) { if (QueueScope is null) { QueueScope = scope.Trim(); UpdatedAt = now; } }

    public void SetQueueHeld(bool held, DateTimeOffset now)
    {
        if (held && LegCount > 0) throw new DomainException("QUEUE_HOLD_NOT_ALLOWED", "A dispatched request is never put on hold.");
        if (QueueHeld != held) { QueueHeld = held; UpdatedAt = now; }
    }

    public OrderState State { get; private set; }
    public WaitKind WaitKind { get; private set; }
    public string? StateReason { get; private set; }
    public string? CurrentStepKey { get; private set; }
    /// <summary>Number of legs created so far (also the next leg sequence - 1).</summary>
    public int LegCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset StateEnteredAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public uint Version { get; private set; }

    /// <summary>
    /// CR-042 (OQ-42-08): true when this order FAILED after it had moved a rack. A failure is not a confirmation of where the rack is or that it may be used, so the order keeps
    /// owning it until an Admin releases it. (An unfinished order owns its racks without this flag; Completed and Cancelled release them.)
    /// </summary>
    public bool RacksHeld { get; private set; }

    public void ReleaseHeldRacks(DateTimeOffset now)
    {
        if (!RacksHeld) throw new DomainException("ORDER_HOLDS_NO_RACK", "This order holds no rack that needs a release.");
        RacksHeld = false; UpdatedAt = now;
    }

    public ICollection<TransportLeg> Legs { get; private set; } = new List<TransportLeg>();

    public bool IsTerminal => State is OrderState.Completed or OrderState.Failed or OrderState.Cancelled;
    public ProcessSnapshot Snapshot => ProcessSnapshot.FromJson(SnapshotJson);

    public void TransitionTo(OrderState next, string? reason, WaitKind wait, DateTimeOffset now)
    {
        if (next == State) { WaitKind = wait; StateReason = reason ?? StateReason; UpdatedAt = now; return; }
        if (!Edges[State].Contains(next)) throw new DomainException("INVALID_ORDER_TRANSITION", $"Transport Task transition {State} -> {next} is not allowed.");
        State = next; WaitKind = next == OrderState.Waiting ? wait : WaitKind.None; StateReason = reason; StateEnteredAt = now; UpdatedAt = now;
        if (IsTerminal) FinishedAt = now;
        if (next == OrderState.Failed && Legs.Any(l => l.PickConfirmedAt is not null)) RacksHeld = true;   // CR-042 (OQ-42-08): the caller loads the legs
    }

    public void MoveToStep(string? stepKey, DateTimeOffset now) { CurrentStepKey = stepKey; UpdatedAt = now; }
    public void BindRack(Guid rackId, DateTimeOffset now) { RackId = rackId; UpdatedAt = now; }
    public int NextLegSequence() => ++LegCount;
}

/// <summary>
/// One leg of an order: one rack moved from a source to a destination (a Pickup/Delivery/BufferStorage/BufferRetrieval step). Source and destination
/// are location CODES chosen by the engine from the order's process snapshot; the leg also remembers whether the rack is on the AMR.
/// </summary>
public class TransportLeg
{
    private TransportLeg() { }

    public static TransportLeg Create(Guid orderId, int sequence, string stepKey, RouteStepType stepType, string? sourceCode, string? destinationCode,
        RackKind rackKind, bool destinationNoBind, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), OrderId = orderId, Sequence = sequence, StepKey = stepKey, StepType = stepType, SourceLocationCode = sourceCode,
        DestinationLocationCode = destinationCode, RackKind = rackKind, DestinationNoBind = destinationNoBind, State = LegState.Pending,
        AgvRequestCode = $"LEG-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now, StateEnteredAt = now,
    };

    private static readonly Dictionary<LegState, LegState[]> Edges = new()
    {
        [LegState.Pending] = [LegState.Ready, LegState.Waiting, LegState.Failed, LegState.Cancelled],
        [LegState.Ready] = [LegState.InProgress, LegState.Waiting, LegState.Failed, LegState.Cancelled],
        [LegState.InProgress] = [LegState.Waiting, LegState.Completed, LegState.Failed, LegState.Cancelled, LegState.Ready],
        [LegState.Waiting] = [LegState.Ready, LegState.InProgress, LegState.Failed, LegState.Cancelled],
        [LegState.Completed] = [], [LegState.Failed] = [], [LegState.Cancelled] = [],
    };

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public int Sequence { get; private set; }
    public string StepKey { get; private set; } = default!;
    public RouteStepType StepType { get; private set; }
    public string? SourceLocationCode { get; private set; }
    /// <summary>Null while the destination is still to be chosen (DestinationResolution.AfterPick).</summary>
    public string? DestinationLocationCode { get; private set; }
    public bool DestinationNoBind { get; private set; }
    public RackKind RackKind { get; private set; }
    public Guid? RackId { get; private set; }
    public Guid? AgvId { get; private set; }
    /// <summary>CR-028 (RcsManaged): the AMR the RCS chose for THIS leg, as the RCS reported it (queryTaskStatus agvCode). Null = none reported yet. Never copied from the Transport Task / Order and never a default;
    /// <c>AgvId</c> stays empty for these legs (PER_AGV does not apply). Legacy legs keep RobotCode null.</summary>
    public string? RobotCode { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    /// <summary>Idempotent request code of the AGV dispatch of this leg.</summary>
    public string AgvRequestCode { get; private set; } = default!;
    public string? AgvTaskCode { get; private set; }
    public LegState State { get; private set; }
    public WaitKind WaitKind { get; private set; }
    public string? StateReason { get; private set; }
    public DateTimeOffset? PickConfirmedAt { get; private set; }
    public DateTimeOffset? DropConfirmedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset StateEnteredAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsTerminal => State is LegState.Completed or LegState.Failed or LegState.Cancelled;
    /// <summary>True while the AGV is committed to this leg (PER_AGV): InProgress or Waiting WITH an AGV (a waiting AMR still holds its rack).</summary>
    public bool OccupiesAgv => AgvId is not null && State is LegState.InProgress or LegState.Waiting;
    public bool RackOnAgv => PickConfirmedAt is not null && DropConfirmedAt is null;

    public void TransitionTo(LegState next, string? reason, WaitKind wait, DateTimeOffset now)
    {
        if (next == State) { WaitKind = wait; StateReason = reason ?? StateReason; UpdatedAt = now; return; }
        if (next == LegState.Ready && State == LegState.InProgress && PickConfirmedAt is not null)
            throw new DomainException("LEG_ALREADY_PICKED", "A leg whose rack was picked cannot go back to Ready.");
        if (!Edges[State].Contains(next)) throw new DomainException("INVALID_LEG_TRANSITION", $"Leg transition {State} -> {next} is not allowed.");
        State = next; WaitKind = next == LegState.Waiting ? wait : WaitKind.None; StateReason = reason; StateEnteredAt = now; UpdatedAt = now;
        if (IsTerminal) FinishedAt = now;
    }

    public void AssignAgv(Guid agvId, DateTimeOffset now) { AgvId = agvId; UpdatedAt = now; }
    public void ReleaseAgv(DateTimeOffset now) { AgvId = null; UpdatedAt = now; }

    /// <summary>
    /// Records the AMR the RCS reported for this leg (RcsManaged). An empty code changes nothing. The first code is stored. A different code BEFORE the rack was picked is followed (the RCS may re-plan).
    /// A different code while the rack is on the AMR is REFUSED: the continuity of the transport must not be changed silently, and no hand-over procedure is approved; the leg keeps the robot it had.
    /// </summary>
    public LegRobotOutcome RecordRobot(string? robotCode, DateTimeOffset now)
    {
        var code = robotCode?.Trim();
        if (string.IsNullOrEmpty(code) || code == RobotCode) return LegRobotOutcome.NoChange;
        if (RobotCode is null) { RobotCode = code; AssignedAt = now; UpdatedAt = now; return LegRobotOutcome.Assigned; }
        if (RackOnAgv) return LegRobotOutcome.RefusedRackOnAmr;
        RobotCode = code; AssignedAt = now; UpdatedAt = now; return LegRobotOutcome.Changed;
    }
    public void AttachAgvTask(string code, DateTimeOffset now)
    {
        if (AgvTaskCode is not null && AgvTaskCode != code) throw new DomainException("AGV_TASK_CODE_MISMATCH", "Leg is already mapped to a different AGV task code.");
        AgvTaskCode = code; UpdatedAt = now;
    }
    public void SetSource(string code, DateTimeOffset now) { SourceLocationCode = code; UpdatedAt = now; }
    public void SetDestination(string code, bool noBind, DateTimeOffset now) { DestinationLocationCode = code; DestinationNoBind = noBind; UpdatedAt = now; }
    public void BindRack(Guid rackId, DateTimeOffset now) { RackId = rackId; UpdatedAt = now; }

    public void ConfirmPick(DateTimeOffset now)
    {
        if (PickConfirmedAt is not null) throw new DomainException("PICK_ALREADY_CONFIRMED", "Pick already confirmed.");
        PickConfirmedAt = now; UpdatedAt = now;
    }

    public void ConfirmDrop(DateTimeOffset now)
    {
        if (PickConfirmedAt is null) throw new DomainException("DROP_BEFORE_PICK", "Drop cannot be confirmed before pick.");
        if (DropConfirmedAt is not null) throw new DomainException("DROP_ALREADY_CONFIRMED", "Drop already confirmed.");
        DropConfirmedAt = now; UpdatedAt = now;
    }
}

/// <summary>Immutable trace of what the engine decided (monitoring and audit of one order).</summary>
public class RoutingEvent
{
    private RoutingEvent() { }

    public RoutingEvent(Guid orderId, Guid? legId, Guid? transactionId, string kind, string? detail, string source, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); OrderId = orderId; LegId = legId; TransactionId = transactionId; Kind = kind; Detail = detail; Source = source; OccurredAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid? LegId { get; private set; }
    public Guid? TransactionId { get; private set; }
    public string Kind { get; private set; } = default!;
    public string? Detail { get; private set; }
    public string Source { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
}
