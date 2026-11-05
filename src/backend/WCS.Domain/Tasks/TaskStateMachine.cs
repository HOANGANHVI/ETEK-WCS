namespace WCS.Domain.Tasks;

/// <summary>
/// Allowed task transitions. The set of STATES and the AGV-occupying subset come from task-state-machine.md (3.2, 3.3).
/// The transition TABLE is a technical derivation (see ADR-0002) because the specification lists states but not edges.
/// </summary>
public static class TaskStateMachine
{
    /// <summary>States that occupy an AGV (task-state-machine.md 3.3). PENDING and QUEUED do not.</summary>
    public static readonly IReadOnlySet<TaskState> AgvOccupyingStates = new HashSet<TaskState>
    {
        TaskState.Dispatching, TaskState.Accepted, TaskState.Executing, TaskState.WaitingForPlc,
        TaskState.WaitingForDestination, TaskState.Continuing, TaskState.CancelPending,
    };

    public static readonly IReadOnlySet<TaskState> TerminalStates = new HashSet<TaskState>
    {
        TaskState.Completed, TaskState.Failed, TaskState.Cancelled,
    };

    private static readonly Dictionary<TaskState, TaskState[]> Edges = new()
    {
        [TaskState.Pending] = [TaskState.Validating, TaskState.Failed, TaskState.Cancelled],
        [TaskState.Validating] = [TaskState.Queued, TaskState.Dispatching, TaskState.Failed, TaskState.Cancelled],
        [TaskState.Queued] = [TaskState.Dispatching, TaskState.Failed, TaskState.Cancelled],
        [TaskState.Dispatching] = [TaskState.Accepted, TaskState.Failed, TaskState.CancelPending],
        [TaskState.Accepted] = [TaskState.Executing, TaskState.Failed, TaskState.CancelPending],
        [TaskState.Executing] = [TaskState.WaitingForPlc, TaskState.WaitingForDestination, TaskState.Continuing,
                                  TaskState.Completed, TaskState.Failed, TaskState.CancelPending],
        [TaskState.WaitingForPlc] = [TaskState.Executing, TaskState.Failed, TaskState.CancelPending],
        [TaskState.WaitingForDestination] = [TaskState.Continuing, TaskState.Failed, TaskState.CancelPending],
        [TaskState.Continuing] = [TaskState.Executing, TaskState.Failed, TaskState.CancelPending],
        [TaskState.CancelPending] = [TaskState.Cancelled, TaskState.Failed, TaskState.Completed],
        [TaskState.Completed] = [],
        [TaskState.Failed] = [],
        [TaskState.Cancelled] = [],
    };

    public static bool CanTransition(TaskState from, TaskState to) => Edges[from].Contains(to);
    public static IReadOnlyCollection<TaskState> NextStates(TaskState from) => Edges[from];
    public static bool OccupiesAgv(TaskState s) => AgvOccupyingStates.Contains(s);
    public static bool IsTerminal(TaskState s) => TerminalStates.Contains(s);
}
