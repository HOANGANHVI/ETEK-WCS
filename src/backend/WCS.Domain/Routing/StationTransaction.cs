using WCS.Domain.Common;

namespace WCS.Domain.Routing;

/// <summary>
/// One entry of an AMR into one port (a PLC Station of CR-002) to pick or deliver a rack. A port has at most ONE active transaction (enforced by a
/// partial unique index, not only in memory); different ports - of one PLC or of different PLCs - run independently. The transaction ends only when
/// the physical handover is done AND the AMR has left the port; ending it never completes the Transport Task.
/// </summary>
public class StationTransaction
{
    private StationTransaction() { }

    public static StationTransaction Create(string portId, string plcId, string locationCode, StationOperation operation, Guid? orderId, Guid? legId,
        string? requestId, DateTimeOffset now, WCS.Domain.Common.RackKind? rackKind = null)
    {
        if (string.IsNullOrWhiteSpace(portId)) throw new DomainException("PORT_REQUIRED", "A port is required.");
        if (requestId is { Length: > MaxRequestIdLength }) throw new DomainException("REQUEST_ID_TOO_LONG", $"Request_ID exceeds {MaxRequestIdLength} characters.");
        return new StationTransaction
        {
            Id = Guid.NewGuid(), PortId = portId, PlcId = plcId, LocationCode = locationCode, Operation = operation, OrderId = orderId, LegId = legId,
            RequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId, RackKind = rackKind, State = StationTransactionState.Created, CreatedAt = now, UpdatedAt = now,
        };
    }

    /// <summary>Request_ID is a STRING of length 254 according to the PLC interface owner (CR-010).</summary>
    public const int MaxRequestIdLength = 254;

    /// <summary>The business states a transaction moves through, in order. Failed / ReconciliationRequired may be entered from any non-final state.</summary>
    private static readonly StationTransactionState[] Forward =
    [
        StationTransactionState.Created, StationTransactionState.Requested, StationTransactionState.Accepted, StationTransactionState.AgvAssigned,
        StationTransactionState.AgvApproaching, StationTransactionState.AtStation, StationTransactionState.Handling,
        StationTransactionState.HandoverCompleted, StationTransactionState.AgvExited, StationTransactionState.Completed,
    ];

    public Guid Id { get; private set; }
    /// <summary>PlcStation id (the port).</summary>
    public string PortId { get; private set; } = default!;
    public string PlcId { get; private set; } = default!;
    public string LocationCode { get; private set; } = default!;
    public StationOperation Operation { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? LegId { get; private set; }
    /// <summary>The PLC Request_ID, unique per PLC while the request is known (duplicates never create a second transaction).</summary>
    public string? RequestId { get; private set; }
    /// <summary>Rack kind reported by the PLC request (null when the request does not state one). Kept so an interrupted start can be resumed.</summary>
    public WCS.Domain.Common.RackKind? RackKind { get; private set; }
    public StationTransactionState State { get; private set; }
    /// <summary>Highest handshake step (1..9 of Trang_tính1) observed or completed; only ever increases. 0 = none.</summary>
    public int HandshakeStep { get; private set; }
    public Guid? AgvId { get; private set; }
    /// <summary>The link to the PLC is down: the transaction keeps its state and is never cancelled or reset because of it.</summary>
    public bool Suspended { get; private set; }
    public string? SuspendedReason { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? ReconciliationReason { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public uint Version { get; private set; }

    /// <summary>Holds its port: everything except Completed and Failed (a transaction that needs reconciliation still occupies the port).</summary>
    public bool IsActive => State is not (StationTransactionState.Completed or StationTransactionState.Failed);
    public bool IsFinal => !IsActive;

    public static bool IsActiveState(StationTransactionState s) => s is not (StationTransactionState.Completed or StationTransactionState.Failed);

    /// <summary>Moves forward to <paramref name="next"/>. Backward moves and skipping the exit/handover guard are refused; an equal state is a no-op (idempotent).</summary>
    public void Advance(StationTransactionState next, DateTimeOffset now)
    {
        if (next == State) return;
        if (IsFinal) throw new DomainException("TRANSACTION_FINAL", $"Station Transaction is already {State}.");
        if (next is StationTransactionState.Failed or StationTransactionState.ReconciliationRequired)
            throw new DomainException("USE_DEDICATED_METHOD", "Use Fail / RequireReconciliation.");
        var from = Array.IndexOf(Forward, State);
        var to = Array.IndexOf(Forward, next);
        if (from < 0) // ReconciliationRequired: only an explicit Resume may leave it
            throw new DomainException("TRANSACTION_NEEDS_RECONCILIATION", "A transaction that requires reconciliation must be resumed explicitly.");
        if (to <= from) throw new DomainException("INVALID_TRANSACTION_TRANSITION", $"Station Transaction transition {State} -> {next} is not allowed.");
        // the port is released ONLY after the handover was completed and the AMR has left the port
        if (next == StationTransactionState.Completed && State != StationTransactionState.AgvExited)
            throw new DomainException("EXIT_REQUIRED", "A Station Transaction completes only after the AMR has left the port.");
        if (next == StationTransactionState.AgvExited && State != StationTransactionState.HandoverCompleted)
            throw new DomainException("HANDOVER_REQUIRED", "The AMR can only leave after the handover is completed.");
        State = next; UpdatedAt = now;
        if (next == StationTransactionState.Completed) FinishedAt = now;
    }

    public void AssignAgv(Guid agvId, DateTimeOffset now) { AgvId = agvId; UpdatedAt = now; }
    public void LinkTo(Guid orderId, Guid? legId, DateTimeOffset now) { OrderId = orderId; if (legId is not null) LegId = legId; UpdatedAt = now; }

    /// <summary>Records the highest PLC handshake step seen. A lower step is ignored (steps never go backwards automatically).</summary>
    public bool ObserveHandshakeStep(int step, DateTimeOffset now)
    {
        if (step is < 1 or > 9) throw new DomainException("HANDSHAKE_STEP_INVALID", "Handshake step must be 1..9.");
        if (step <= HandshakeStep) return false;
        HandshakeStep = step; UpdatedAt = now; return true;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (IsFinal || Suspended) return;
        Suspended = true; SuspendedReason = reason; SuspendedAt = now; UpdatedAt = now;
    }

    public void ResumeLink(DateTimeOffset now) { Suspended = false; SuspendedReason = null; SuspendedAt = null; UpdatedAt = now; }

    public void RequireReconciliation(string reason, DateTimeOffset now)
    {
        if (IsFinal) return;
        State = StationTransactionState.ReconciliationRequired; ReconciliationReason = reason; UpdatedAt = now;
    }

    /// <summary>Admin decision after reconciliation: continue from <paramref name="state"/> (never past what was proven).</summary>
    public void ResumeFromReconciliation(StationTransactionState state, DateTimeOffset now)
    {
        if (State != StationTransactionState.ReconciliationRequired) throw new DomainException("NOT_IN_RECONCILIATION", "Transaction does not require reconciliation.");
        if (state is StationTransactionState.ReconciliationRequired or StationTransactionState.Failed or StationTransactionState.Completed || !Forward.Contains(state))
            throw new DomainException("INVALID_RESUME_STATE", "Resume state must be an in-progress state.");
        State = state; ReconciliationReason = null; UpdatedAt = now;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        if (IsFinal) return;
        State = StationTransactionState.Failed; FailureReason = reason; UpdatedAt = now; FinishedAt = now;
    }
}
