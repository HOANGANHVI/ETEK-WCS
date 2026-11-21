namespace WCS.Domain.Tasks;

/// <summary>
/// CR-028: where a task stands in its hand-over to the RCS when the RCS chooses the AMR (RcsManaged dispatch). It is a tracking attribute of the task, NOT a business state:
/// the 13 task states of task-state-machine.md are unchanged. A task of the Legacy dispatch (and every task created before CR-028) has no value (null).
/// "Sent successfully" is never "assigned to an AMR": only <see cref="Assigned"/> means the RCS reported an AMR for the task.
/// </summary>
public enum RcsDispatchState
{
    /// <summary>The task exists but nothing was sent to the RCS yet.</summary>
    NotSent = 1,
    /// <summary>A create request was sent (or is being retried) and the answer is not known yet. A timeout stays here: it is not a certain failure.</summary>
    Sending = 2,
    /// <summary>The RCS answered "successful" to the create request. Which AMR (if any) is not known yet.</summary>
    Accepted = 3,
    /// <summary>The RCS was asked (queryTaskStatus) and has not assigned an AMR yet.</summary>
    AwaitingAssignment = 4,
    /// <summary>The RCS reported an AMR (agvCode / robotCode) for the task: <c>RobotCode</c> and <c>AssignedAt</c> are set.</summary>
    Assigned = 5,
    /// <summary>The assignment could not be read from the RCS (the status query failed); the next successful query clears it.</summary>
    SyncError = 6,
}

/// <summary>Result of recording what the RCS said about the AMR of a task.</summary>
public enum AssignmentOutcome { NoChange, Waiting, Assigned, RobotChanged }
