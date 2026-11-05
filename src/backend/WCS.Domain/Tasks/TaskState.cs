namespace WCS.Domain.Tasks;

/// <summary>Exactly the 13 business states of task-state-machine.md 3.2.</summary>
public enum TaskState
{
    Pending = 1,
    Validating = 2,
    Queued = 3,
    Dispatching = 4,
    Accepted = 5,
    Executing = 6,
    WaitingForPlc = 7,
    WaitingForDestination = 8,
    Continuing = 9,
    Completed = 10,
    Failed = 11,
    CancelPending = 12,
    Cancelled = 13,
}
