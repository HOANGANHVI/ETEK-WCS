using WCS.Domain.Common;

namespace WCS.Domain.Tasks;

/// <summary>
/// WCS business task (independent from the RCS task; mapping is kept in <see cref="RcsTaskCode"/>).
/// All state changes MUST go through <see cref="TransitionTo"/> so invalid transitions are impossible.
/// </summary>
public class TransportTask
{
    private readonly List<TaskHistoryEntry> _history = [];

    private TransportTask() { }

    /// <param name="agvId">The business AGV chosen by WCS (Legacy dispatch). Null when the RCS chooses the AMR (<paramref name="rcsManaged"/>): the task then has no AGV until the RCS reports one.</param>
    public static TransportTask Create(string requestKey, ProcessFlow flow, RackKind rackKind, Guid? agvId,
        Guid requestLocationId, string? requestedBy, DateTimeOffset now, bool rcsManaged = false)
    {
        if (string.IsNullOrWhiteSpace(requestKey)) throw new DomainException("REQUEST_KEY_REQUIRED", "RequestKey is required for idempotency.");
        var id = Guid.NewGuid();
        var t = new TransportTask
        {
            Id = id,
            RequestKey = requestKey.Trim(),
            Flow = flow,
            RackKind = rackKind,
            AgvId = agvId,
            RcsDispatchState = rcsManaged ? Tasks.RcsDispatchState.NotSent : null,
            RequestLocationId = requestLocationId,
            RequestedBy = requestedBy,
            State = TaskState.Pending,
            RcsRequestCode = $"WCS-{id:N}",
            CurrentLeg = TaskLeg.Pick,
            CreatedAt = now,
            UpdatedAt = now,
            StateEnteredAt = now,
        };
        t._history.Add(new TaskHistoryEntry(id, null, TaskState.Pending, "TASK_CREATED", $"flow={flow}", "WCS", now, HistoryDetail.Of("TASK_CREATED", ("flow", flow))));
        return t;
    }

    public Guid Id { get; private set; }
    /// <summary>Idempotency key of the originating request (PLC request identity/sequence or API key).</summary>
    public string RequestKey { get; private set; } = default!;
    public ProcessFlow Flow { get; private set; }
    public RackKind RackKind { get; private set; }
    /// <summary>
    /// The business AGV (a row of <c>agvs</c>). Legacy dispatch: set at creation by WCS. RcsManaged dispatch (CR-028): NULL until the RCS reports an AMR AND that robot code
    /// belongs to an <c>agvs</c> row; the identity of the real AMR is <see cref="RobotCode"/>. A null value means "no AGV known", never "AGV1".
    /// </summary>
    public Guid? AgvId { get; private set; }
    /// <summary>CR-028: the AMR (RCS robotCode / agvCode) that the RCS reported for this task, exactly as reported. Null until the RCS assigned one.</summary>
    public string? RobotCode { get; private set; }
    /// <summary>CR-038: Simulator or Real, fixed when the task is created (null only for rows created before CR-038). It never moves to the other dispatcher.</summary>
    public WCS.Domain.Rcs.ExecutionMode? ExecutionMode { get; private set; }
    public void PinExecutionMode(WCS.Domain.Rcs.ExecutionMode mode)
    {
        if (ExecutionMode is { } current && current != mode) throw new DomainException("EXECUTION_MODE_FIXED", "The execution mode cannot be changed after creation.");
        ExecutionMode = mode;
    }
    /// <summary>CR-028: when WCS first learned the robot of the task from the RCS.</summary>
    public DateTimeOffset? AssignedAt { get; private set; }
    /// <summary>CR-028: hand-over tracking for RcsManaged dispatch; null for Legacy dispatch and for tasks created before CR-028.</summary>
    public RcsDispatchState? RcsDispatchState { get; private set; }
    /// <summary>CR-028: the last time the assignment was asked from the RCS (drives the check interval).</summary>
    public DateTimeOffset? AssignmentCheckedAt { get; private set; }
    /// <summary>CR-028: consecutive failed assignment queries.</summary>
    public int AssignmentSyncFailures { get; private set; }
    public bool IsRcsManaged => RcsDispatchState is not null;
    /// <summary>Station/location that raised the request.</summary>
    public Guid RequestLocationId { get; private set; }
    public Guid? SourceLocationId { get; private set; }
    public Guid? DestinationLocationId { get; private set; }
    public Guid? RackId { get; private set; }
    public TaskState State { get; private set; }
    public TaskLeg CurrentLeg { get; private set; }
    public bool PlcPermissionGranted { get; private set; }
    public string? StateReason { get; private set; }
    /// <summary>Stable code of the state reason and its parameters (JSON); null when the reason is a raw external text. <see cref="StateReason"/> keeps the original English text.</summary>
    public string? StateReasonCode { get; private set; }
    public string? StateReasonArgsJson { get; private set; }
    public string? RequestedBy { get; private set; }
    /// <summary>CR-021: the id that ties every log event of this task's life together (set once at creation; null for tasks created before CR-021).</summary>
    public string? CorrelationId { get; private set; }
    public void SetCorrelation(string? correlationId) { if (CorrelationId is null && !string.IsNullOrWhiteSpace(correlationId)) CorrelationId = correlationId.Trim().Length > 100 ? correlationId.Trim()[..100] : correlationId.Trim(); }

    /// <summary>Unique request code sent to RCS (idempotent task creation).</summary>
    public string RcsRequestCode { get; private set; } = default!;
    /// <summary>RCS task code mapping (task-state-machine.md 3.1). Null until RCS accepted.</summary>
    public string? RcsTaskCode { get; private set; }
    public int DispatchAttempts { get; private set; }
    /// <summary>CR-008A: RCS calls made since the task entered its CURRENT state (dispatch / continue / cancel). Reset by every state change; drives the retry backoff.</summary>
    public int RcsRetryCount { get; private set; }
    /// <summary>CR-008A: time of the latest RCS call attempt in the current state (null before the first one).</summary>
    public DateTimeOffset? RcsLastAttemptAt { get; private set; }

    public DateTimeOffset? PickConfirmedAt { get; private set; }
    public DateTimeOffset? DropConfirmedAt { get; private set; }
    /// <summary>Set when physical outcome is uncertain; resources stay held until reconciled (inventory-management.md 4.8).</summary>
    public bool RequiresReconciliation { get; private set; }
    public bool ReconciliationCompleted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset StateEnteredAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<TaskHistoryEntry> History => _history;

    public bool IsTerminal => TaskStateMachine.IsTerminal(State);
    public bool OccupiesAgv => TaskStateMachine.OccupiesAgv(State);

    public void TransitionTo(TaskState next, string reason, string source, DateTimeOffset now, HistoryDetail? code = null)
    {
        code ??= HistoryDetail.FromCodedReason(reason);
        if (!TaskStateMachine.CanTransition(State, next)) throw new InvalidTaskTransitionException(State, next);
        var prev = State;
        State = next;
        // a terminal task keeps its final retry count / last attempt time (shown to the Admin who reconciles it); every other state starts a new series
        if (!TaskStateMachine.IsTerminal(next)) { RcsRetryCount = 0; RcsLastAttemptAt = null; }
        StateReason = reason;
        StateReasonCode = code?.Code; StateReasonArgsJson = code?.ArgsJson;
        StateEnteredAt = now;
        UpdatedAt = now;
        if (TaskStateMachine.IsTerminal(next)) FinishedAt = now;
        _history.Add(new TaskHistoryEntry(Id, prev, next, "STATE_CHANGED", reason, source, now, code));
    }

    public void Note(string eventName, string? detail, string source, DateTimeOffset now, HistoryDetail? code = null)
    {
        UpdatedAt = now;
        _history.Add(new TaskHistoryEntry(Id, State, State, eventName, detail, source, now, code ?? HistoryDetail.FromCodedReason(detail)));
    }

    /// <summary>Most recent history entry (insertion order breaks ties between identical timestamps).</summary>
    public TaskHistoryEntry? LastHistory => _history.Select((h, i) => (h, i)).OrderByDescending(x => x.h.OccurredAt).ThenByDescending(x => x.i).Select(x => x.h).FirstOrDefault();

    /// <summary>CR-008A: records the note only when it differs from the latest history entry, so a repeated identical outcome never floods the history.</summary>
    public bool NoteIfChanged(string eventName, string? detail, string source, DateTimeOffset now, HistoryDetail? code = null)
    {
        // compared with every entry of the most recent timestamp, so the result does not depend on the order the history was loaded in
        if (_history.Count > 0)
        {
            var latest = _history.Max(h => h.OccurredAt);
            if (_history.Any(h => h.OccurredAt == latest && h.EventName == eventName && h.Detail == detail)) return false;
        }
        Note(eventName, detail, source, now, code);
        return true;
    }

    /// <summary>True when the history already holds an entry with this event name (used to write a one-off marker exactly once).</summary>
    public bool HasHistory(string eventName) => _history.Any(h => h.EventName == eventName);

    /// <summary>CR-008A: one more RCS call attempt in the current state (used by the backoff gate).</summary>
    public void RegisterRcsAttempt(DateTimeOffset now) { RcsRetryCount++; RcsLastAttemptAt = now; UpdatedAt = now; }

    public void AssignSource(Guid locationId, Guid rackId, DateTimeOffset now)
    {
        SourceLocationId = locationId; RackId = rackId; UpdatedAt = now;
    }

    public void AssignDestination(Guid locationId, DateTimeOffset now)
    {
        DestinationLocationId = locationId; UpdatedAt = now;
    }

    /// <summary>States in which a user may still edit/delete a task: no AGV has been claimed and nothing was sent to RCS.</summary>
    public static bool IsUserEditableState(TaskState s) => s is TaskState.Pending or TaskState.Validating or TaskState.Queued;

    public bool IsUserEditable => IsUserEditableState(State);

    /// <summary>
    /// True when nothing has been handed to an AGV or to RCS yet: waiting state, no RCS task, no dispatch attempt, no physical event.
    /// Only then may a user edit or hard-delete the task (CR-001, approved 2026-11-06).
    /// </summary>
    public bool IsUntouchedByAgvAndRcs => IsUserEditable && RcsTaskCode is null && DispatchAttempts == 0 && PickConfirmedAt is null && DropConfirmedAt is null;

    /// <summary>
    /// Replaces ALL user-supplied request data (process flow and requesting station; the rack kind and AGV follow from the flow).
    /// Identity (id, request key, requester, RCS codes) is never changed. Source/destination selection is cleared so it is re-evaluated;
    /// the caller releases reservations and re-reserves.
    /// </summary>
    public void ReconfigureRequest(ProcessFlow flow, RackKind rackKind, Guid? agvId, Guid requestLocationId, string detail, string source, DateTimeOffset now, HistoryDetail? code = null)
    {
        if (!IsUntouchedByAgvAndRcs) throw new DomainException("TASK_NOT_EDITABLE", $"Task in state {State} can no longer be edited.");
        Flow = flow; RackKind = rackKind; AgvId = agvId; RequestLocationId = requestLocationId;
        SourceLocationId = null; RackId = null; DestinationLocationId = null;
        UpdatedAt = now;
        _history.Add(new TaskHistoryEntry(Id, State, State, "TASK_EDITED", detail, source, now, code));
    }

    public void RegisterDispatchAttempt(DateTimeOffset now)
    {
        DispatchAttempts++; RegisterRcsAttempt(now);
        if (RcsDispatchState is Tasks.RcsDispatchState.NotSent or Tasks.RcsDispatchState.Sending) RcsDispatchState = Tasks.RcsDispatchState.Sending;   // CR-028: a call is under way; a timeout leaves it here
    }

    /// <summary>CR-028: the RCS answered "successful" to the create request. Never moves an already assigned task backwards.</summary>
    public void MarkRcsAccepted(DateTimeOffset now)
    {
        if (RcsDispatchState is null or Tasks.RcsDispatchState.Assigned) return;
        RcsDispatchState = Tasks.RcsDispatchState.Accepted; UpdatedAt = now;
    }

    /// <summary>
    /// CR-028: records what the RCS said about the AMR of this task (queryTaskStatus agvCode or the robotCode of a callback). An empty code means "not assigned yet".
    /// <paramref name="agvId"/> is the <c>agvs</c> row of that robot code when one exists (never invented).
    /// </summary>
    public AssignmentOutcome RecordAssignment(string? robotCode, Guid? agvId, DateTimeOffset now)
    {
        AssignmentCheckedAt = now; AssignmentSyncFailures = 0; UpdatedAt = now;
        var code = string.IsNullOrWhiteSpace(robotCode) ? null : robotCode.Trim();
        if (code is null)
        {
            if (RcsDispatchState is Tasks.RcsDispatchState.Assigned) return AssignmentOutcome.NoChange;   // the RCS no longer names a robot (for example the task finished): what was learned stays
            RcsDispatchState = Tasks.RcsDispatchState.AwaitingAssignment;
            return AssignmentOutcome.Waiting;
        }
        if (RobotCode is null)
        {
            RobotCode = code; AgvId = agvId ?? AgvId; AssignedAt = now; RcsDispatchState = Tasks.RcsDispatchState.Assigned;
            return AssignmentOutcome.Assigned;
        }
        if (string.Equals(RobotCode, code, StringComparison.Ordinal))
        {
            RcsDispatchState = Tasks.RcsDispatchState.Assigned; AgvId ??= agvId;
            return AssignmentOutcome.NoChange;
        }
        RobotCode = code; AgvId = agvId; AssignedAt = now; RcsDispatchState = Tasks.RcsDispatchState.Assigned;   // the RCS named another robot: WCS follows the RCS
        return AssignmentOutcome.RobotChanged;
    }

    /// <summary>CR-028: the assignment query failed. A task that already has a robot keeps it; any other task shows the synchronisation error until a query succeeds.</summary>
    public void RegisterAssignmentSyncFailure(DateTimeOffset now)
    {
        AssignmentCheckedAt = now; AssignmentSyncFailures++; UpdatedAt = now;
        if (RcsDispatchState is not null and not Tasks.RcsDispatchState.Assigned) RcsDispatchState = Tasks.RcsDispatchState.SyncError;
    }

    public void AttachRcsTask(string rcsTaskCode, DateTimeOffset now)
    {
        if (RcsTaskCode is not null && RcsTaskCode != rcsTaskCode)
            throw new DomainException("RCS_TASK_CODE_MISMATCH", "Task is already mapped to a different RCS task code.");
        RcsTaskCode = rcsTaskCode; UpdatedAt = now;
    }

    public void SetPlcPermission(bool granted, DateTimeOffset now) { PlcPermissionGranted = granted; UpdatedAt = now; }

    public void ConfirmPick(DateTimeOffset now)
    {
        if (PickConfirmedAt is not null) throw new DomainException("PICK_ALREADY_CONFIRMED", "Pick already confirmed.");
        PickConfirmedAt = now; CurrentLeg = TaskLeg.Drop; PlcPermissionGranted = false; UpdatedAt = now;
    }

    public void ConfirmDrop(DateTimeOffset now)
    {
        if (PickConfirmedAt is null) throw new DomainException("DROP_BEFORE_PICK", "Drop cannot be confirmed before pick.");
        if (DropConfirmedAt is not null) throw new DomainException("DROP_ALREADY_CONFIRMED", "Drop already confirmed.");
        DropConfirmedAt = now; PlcPermissionGranted = false; UpdatedAt = now;
    }

    public void RequireReconciliation(string detail, DateTimeOffset now, HistoryDetail? code = null)
    {
        RequiresReconciliation = true; UpdatedAt = now;
        _history.Add(new TaskHistoryEntry(Id, State, State, "RECONCILIATION_REQUIRED", detail, "WCS", now, code ?? HistoryDetail.FromCodedReason(detail)));
    }

    public void MarkReconciled(DateTimeOffset now)
    {
        ReconciliationCompleted = true; RequiresReconciliation = false; UpdatedAt = now;
    }
}
