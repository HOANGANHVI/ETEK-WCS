using WCS.Domain.Common;

namespace WCS.Domain.Rcs;

/// <summary>CR-038: how a task / order reaches the AGV fleet. Pinned on the task when it is created and never changed afterwards.</summary>
public enum ExecutionMode { Simulator = 1, Real = 2 }

public enum RcsMessageDirection { Out = 1, In = 2 }

/// <summary>
/// CR-038: THE operating mode of this WCS (a single row, Id = 1). New environments start in <see cref="ExecutionMode.Simulator"/> with <see cref="IsExplicit"/> = false:
/// nobody has chosen it yet, so Production keeps refusing tasks (CR-008A) until an Admin chooses a mode. <see cref="RecoveryRequired"/> blocks every new dispatch after a restart
/// that found unresolved Real dispatches, until an Admin has reconciled them.
/// </summary>
public class ExecutionModeState
{
    private ExecutionModeState() { }

    public static ExecutionModeState Default(DateTimeOffset now) => new() { Id = 1, Mode = ExecutionMode.Simulator, IsExplicit = false, UpdatedAt = now, UpdatedBy = "SYSTEM" };

    public int Id { get; private set; }
    public ExecutionMode Mode { get; private set; }
    /// <summary>False until an Admin has chosen a mode (the default row is only a starting value).</summary>
    public bool IsExplicit { get; private set; }
    public bool RecoveryRequired { get; private set; }
    public string? RecoveryReason { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = "SYSTEM";

    public void SwitchTo(ExecutionMode mode, string by, DateTimeOffset now)
    {
        Mode = mode; IsExplicit = true; UpdatedBy = by; UpdatedAt = now;
    }

    public void RequireRecovery(string reason, DateTimeOffset now) { RecoveryRequired = true; RecoveryReason = reason.Length > 500 ? reason[..500] : reason; UpdatedAt = now; }
    public void ClearRecovery(string by, DateTimeOffset now) { RecoveryRequired = false; RecoveryReason = null; UpdatedBy = by; UpdatedAt = now; }
}

/// <summary>CR-038: one request to change the mode (successful or refused). <see cref="RequestId"/> is unique: repeating a request returns the first answer and changes nothing.</summary>
public class ExecutionModeTransition
{
    private ExecutionModeTransition() { }

    public static ExecutionModeTransition Create(string requestId, ExecutionMode from, ExecutionMode to, string actor, string reason, bool succeeded, string? detail, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), RequestId = requestId, FromMode = from, ToMode = to, Actor = actor, Reason = reason, Succeeded = succeeded, Detail = detail, At = now };

    public Guid Id { get; private set; }
    public string RequestId { get; private set; } = default!;
    public ExecutionMode FromMode { get; private set; }
    public ExecutionMode ToMode { get; private set; }
    public string Actor { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public bool Succeeded { get; private set; }
    /// <summary>For a refusal: the blockers (JSON). For a success: null.</summary>
    public string? Detail { get; private set; }
    public DateTimeOffset At { get; private set; }
}

/// <summary>
/// CR-038: one message between WCS and the RCS (or between WCS and the simulator, which is said on EVERY row by <see cref="Mode"/>). Append-only. The payload is stored exactly as
/// exchanged after secrets were masked (no token, password or callback secret is ever stored). A row is written by its own short transaction, so it exists even when the call failed.
/// </summary>
public class RcsMessage
{
    private RcsMessage() { }

    public static RcsMessage Create(ExecutionMode mode, RcsMessageDirection direction, string messageType, string protocol, string? requestCode, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), Mode = mode, Direction = direction, MessageType = messageType, Protocol = protocol, RequestCode = requestCode, CreatedAt = now };

    public Guid Id { get; private set; }
    /// <summary>Arrival order, assigned by the database: the order of the timeline even when two messages carry the same timestamp.</summary>
    public long Seq { get; private set; }
    public Guid? TaskId { get; private set; }
    public string? RequestKey { get; private set; }
    /// <summary>The WCS request code of the call (the one the RCS gets as taskCode); the join key to a task or a leg.</summary>
    public string? RequestCode { get; private set; }
    public string? RcsTaskCode { get; private set; }
    public ExecutionMode Mode { get; private set; }
    public RcsMessageDirection Direction { get; private set; }
    public string MessageType { get; private set; } = default!;
    /// <summary>For example "HIK RCS-2000 HTTP", "RCS callback (HTTP)", "SIMULATOR (in-process)".</summary>
    public string Protocol { get; private set; } = default!;
    public string? Endpoint { get; private set; }
    public string? HttpMethod { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public string? Payload { get; private set; }
    public bool PayloadTruncated { get; private set; }
    /// <summary>Sent, NotSent, Accepted, Rejected, Unknown, Timeout, ConnectionError, HttpError, Received, Applied ...</summary>
    public string? Result { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long? DurationMs { get; private set; }

    public RcsMessage With(Guid? taskId = null, string? requestKey = null, string? rcsTaskCode = null, string? endpoint = null, string? httpMethod = null, int? httpStatus = null,
        string? payload = null, bool truncated = false, string? result = null, string? errorCode = null, string? errorMessage = null, string? correlationId = null, long? durationMs = null)
    {
        TaskId = taskId ?? TaskId; RequestKey = requestKey ?? RequestKey; RcsTaskCode = rcsTaskCode ?? RcsTaskCode; Endpoint = endpoint ?? Endpoint; HttpMethod = httpMethod ?? HttpMethod;
        HttpStatusCode = httpStatus ?? HttpStatusCode; Payload = payload ?? Payload; PayloadTruncated = truncated || PayloadTruncated; Result = result ?? Result;
        ErrorCode = errorCode ?? ErrorCode; ErrorMessage = errorMessage ?? ErrorMessage; CorrelationId = correlationId ?? CorrelationId; DurationMs = durationMs ?? DurationMs;
        return this;
    }
}
