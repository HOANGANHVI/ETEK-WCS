namespace WCS.Application.Abstractions;

/// <summary>CR-051: where one unit of RCS work (a Phase 1 task, or one leg of a transport order) stands towards the RCS. Derived, never stored.</summary>
public static class RcsWorkStates
{
    /// <summary>Exists in WCS only: nothing has left for the RCS.</summary>
    public const string NotSent = "NotSent";
    /// <summary>Sent; no usable answer yet (waiting, timeout, connection or unknown answer).</summary>
    public const string NoAnswer = "NoAnswer";
    public const string Rejected = "Rejected";
    public const string Accepted = "Accepted";
    public const string Executing = "Executing";
    /// <summary>The AGV system reported that it cannot go on; WCS has not ended the work.</summary>
    public const string Interrupted = "Interrupted";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>One recorded RCS message of a request code (communication log, CR-038).</summary>
public sealed record RcsWorkMessage(long Seq, DateTimeOffset At, string MessageType, bool Outbound, string? Result, string? Error);

public sealed record RcsWorkStatus(string State, DateTimeOffset? SentAt, DateTimeOffset? LastMessageAt, string? Problem);

/// <summary>
/// CR-051: one RCS work unit as the monitoring pages show it. <see cref="Id"/> is the task id (Kind "Task") or the leg id (Kind "OrderLeg", with <see cref="OrderId"/> the transport order).
/// <see cref="RcsRequestCode"/> is the correlation WCS → RCS → AGV: the same code is on the RCS messages and the AMR events.
/// </summary>
public sealed record RcsWorkItem(string Kind, Guid Id, Guid? OrderId, int? LegSequence, string RequestKey, string ProcessCode, string? RequestedBy, string? ExecutionMode,
    Guid? AgvId, string? AgvCode, string? RobotCode, string? Source, string? Destination, string WorkState, string RcsState, string RcsRequestCode, string? RcsTaskCode, string? Detail,
    DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset UpdatedAt, DateTimeOffset? PickConfirmedAt, DateTimeOffset? DropConfirmedAt, DateTimeOffset? FinishedAt);

public interface IRcsWorkQuery
{
    /// <summary>Newest first. <paramref name="sentOnly"/> leaves out what has not left for the RCS; <paramref name="orderId"/> = the legs of one transport order. Read only.</summary>
    Task<IReadOnlyList<RcsWorkItem>> ListAsync(bool sentOnly, Guid? orderId, int take, CancellationToken ct = default);
}

public static class RcsWorkRules
{
    private const string SimulatedEvent = "Simulated AGV Event: ";

    /// <summary>The message types that create the RCS task: a Phase 1 task / a real call ("Create Task") and a leg of the simulator ("Create Leg Task").</summary>
    public static bool IsCreate(string type) => type is "Create Task" or "Create Leg Task";
    public static bool IsCreateResponse(string type) => type is "Create Task Response" or "Create Leg Task Response";

    /// <summary>
    /// The state towards the RCS from the facts WCS holds: the state of the task / leg, the RCS task code, the confirmed pick and the recorded messages.
    /// <paramref name="workState"/> is the name of the TaskState or LegState.
    /// </summary>
    public static RcsWorkStatus Derive(string workState, string? rcsTaskCode, bool picked, IReadOnlyList<RcsWorkMessage> messages)
    {
        var ordered = messages.OrderBy(m => m.Seq).ToList();
        var create = ordered.FirstOrDefault(m => m.Outbound && IsCreate(m.MessageType) && m.Result == "Sent");
        var response = ordered.LastOrDefault(m => !m.Outbound && IsCreateResponse(m.MessageType));
        var last = ordered.Count == 0 ? (DateTimeOffset?)null : ordered[^1].At;
        var accepted = !string.IsNullOrWhiteSpace(rcsTaskCode);
        // "Blocked" = the dispatcher refused to act: nothing left WCS
        var sent = accepted || (create is not null && response?.Result != "Blocked");
        if (!sent) return new(RcsWorkStates.NotSent, null, last, ordered.LastOrDefault(m => m.Result is "NotSent" or "Blocked")?.Error);
        RcsWorkStatus Of(string state, string? problem = null) => new(state, create?.At, last, problem);

        if (workState == "Completed") return Of(RcsWorkStates.Completed);
        if (workState == "Cancelled") return Of(RcsWorkStates.Cancelled);
        var rejected = !accepted && response?.Result == "Rejected";
        if (workState == "Failed") return Of(rejected ? RcsWorkStates.Rejected : RcsWorkStates.Failed, rejected ? response!.Error : null);
        if (!accepted) return rejected ? Of(RcsWorkStates.Rejected, response!.Error) : Of(RcsWorkStates.NoAnswer, response?.Error);

        var interrupted = ordered.LastOrDefault(m => m.MessageType is SimulatedEvent + "LegInterrupted" or SimulatedEvent + "RcsInterrupted");
        if (interrupted is not null && !ordered.Any(m => m.Seq > interrupted.Seq && m.Result != "Rejected"
                && m.MessageType is SimulatedEvent + "Recovered" or SimulatedEvent + "PickConfirmed" or SimulatedEvent + "DropConfirmed"))
            return Of(RcsWorkStates.Interrupted);

        var executing = picked || workState is "Executing" or "WaitingForPlc" or "WaitingForDestination" or "Continuing" || ordered.Any(m => m.MessageType == SimulatedEvent + "Started");
        return Of(executing ? RcsWorkStates.Executing : RcsWorkStates.Accepted);
    }
}
