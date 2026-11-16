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

    public static TransportTask Create(string requestKey, ProcessFlow flow, RackKind rackKind, Guid agvId,
        Guid requestLocationId, string? requestedBy, DateTimeOffset now)
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
            RequestLocationId = requestLocationId,
            RequestedBy = requestedBy,
            State = TaskState.Pending,
            RcsRequestCode = $"WCS-{id:N}",
            CurrentLeg = TaskLeg.Pick,
            CreatedAt = now,
            UpdatedAt = now,
            StateEnteredAt = now,
        };
        t._history.Add(new TaskHistoryEntry(id, null, TaskState.Pending, "TASK_CREATED", $"flow={flow}", "WCS", now));
        return t;
    }

    public Guid Id { get; private set; }
    /// <summary>Idempotency key of the originating request (PLC request identity/sequence or API key).</summary>
    public string RequestKey { get; private set; } = default!;
    public ProcessFlow Flow { get; private set; }
    public RackKind RackKind { get; private set; }
    public Guid AgvId { get; private set; }
    /// <summary>Station/location that raised the request.</summary>
    public Guid RequestLocationId { get; private set; }
    public Guid? SourceLocationId { get; private set; }
    public Guid? DestinationLocationId { get; private set; }
    public Guid? RackId { get; private set; }
    public TaskState State { get; private set; }
    public TaskLeg CurrentLeg { get; private set; }
    public bool PlcPermissionGranted { get; private set; }
    public string? StateReason { get; private set; }
    public string? RequestedBy { get; private set; }

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

    public void TransitionTo(TaskState next, string reason, string source, DateTimeOffset now)
    {
        if (!TaskStateMachine.CanTransition(State, next)) throw new InvalidTaskTransitionException(State, next);
        var prev = State;
        State = next;
        RcsRetryCount = 0; RcsLastAttemptAt = null;
        StateReason = reason;
        StateEnteredAt = now;
        UpdatedAt = now;
        if (TaskStateMachine.IsTerminal(next)) FinishedAt = now;
        _history.Add(new TaskHistoryEntry(Id, prev, next, "STATE_CHANGED", reason, source, now));
    }

    public void Note(string eventName, string? detail, string source, DateTimeOffset now)
    {
        UpdatedAt = now;
        _history.Add(new TaskHistoryEntry(Id, State, State, eventName, detail, source, now));
    }

    /// <summary>Most recent history entry (insertion order breaks ties between identical timestamps).</summary>
    public TaskHistoryEntry? LastHistory => _history.Select((h, i) => (h, i)).OrderByDescending(x => x.h.OccurredAt).ThenByDescending(x => x.i).Select(x => x.h).FirstOrDefault();

    /// <summary>CR-008A: records the note only when it differs from the latest history entry, so a repeated identical outcome never floods the history.</summary>
    public bool NoteIfChanged(string eventName, string? detail, string source, DateTimeOffset now)
    {
        // compared with every entry of the most recent timestamp, so the result does not depend on the order the history was loaded in
        if (_history.Count > 0)
        {
            var latest = _history.Max(h => h.OccurredAt);
            if (_history.Any(h => h.OccurredAt == latest && h.EventName == eventName && h.Detail == detail)) return false;
        }
        Note(eventName, detail, source, now);
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
    public void ReconfigureRequest(ProcessFlow flow, RackKind rackKind, Guid agvId, Guid requestLocationId, string detail, string source, DateTimeOffset now)
    {
        if (!IsUntouchedByAgvAndRcs) throw new DomainException("TASK_NOT_EDITABLE", $"Task in state {State} can no longer be edited.");
        Flow = flow; RackKind = rackKind; AgvId = agvId; RequestLocationId = requestLocationId;
        SourceLocationId = null; RackId = null; DestinationLocationId = null;
        UpdatedAt = now;
        _history.Add(new TaskHistoryEntry(Id, State, State, "TASK_EDITED", detail, source, now));
    }

    public void RegisterDispatchAttempt(DateTimeOffset now) { DispatchAttempts++; RegisterRcsAttempt(now); }

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

    public void RequireReconciliation(string detail, DateTimeOffset now)
    {
        RequiresReconciliation = true; UpdatedAt = now;
        _history.Add(new TaskHistoryEntry(Id, State, State, "RECONCILIATION_REQUIRED", detail, "WCS", now));
    }

    public void MarkReconciled(DateTimeOffset now)
    {
        ReconciliationCompleted = true; RequiresReconciliation = false; UpdatedAt = now;
    }
}
