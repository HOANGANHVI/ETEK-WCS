using WCS.Domain.Rcs;

namespace WCS.Application.Abstractions;

/// <summary>CR-038: the operating mode as the rest of the system sees it (a cached copy of the single row; changed only by <see cref="IExecutionModeService"/>).</summary>
public sealed record ExecutionModeSnapshot(ExecutionMode Mode, bool IsExplicit, bool RecoveryRequired, string? RecoveryReason, DateTimeOffset UpdatedAt, string UpdatedBy);

/// <summary>
/// CR-038: synchronous, cheap read of the current mode (the RCS dispatch guard is synchronous). Before the database row has been read (tests, start-up) the value is
/// "Simulator, not chosen": outside Production exactly how the system behaved before CR-038; in Production nothing is dispatched until a mode was chosen.
/// </summary>
public interface IExecutionModeProvider
{
    ExecutionModeSnapshot Current { get; }
    bool Loaded { get; }
    void Update(ExecutionModeSnapshot snapshot);
}

/// <summary>CR-038: inside a unit of work that creates a task / order: reads the mode with a SHARE lock so a mode change (FOR UPDATE) cannot slip between the check and the insert.</summary>
public interface IExecutionModeGate
{
    Task<ExecutionMode> ModeForNewWorkAsync(CancellationToken ct = default);
}

/// <summary>
/// CR-038: which dispatcher a call must use. Looked up by the WCS request code of the task / leg (fixed when it was created); no code, or a code nobody owns = the current mode.
/// <c>null</c> = the owner was created BEFORE CR-038 and has no pinned mode: it is NEVER sent anywhere (neither simulator nor real) until an Admin adopts it as Simulator.
/// </summary>
public interface IExecutionModeResolver
{
    Task<ExecutionMode?> ForRequestCodeAsync(string? requestCode, CancellationToken ct = default);
}

public sealed record ModeCondition(string Code, bool Ok, string Message);
public sealed record BlockingWork(string Kind, string Id, string Code, string State, string Reason);
public sealed record ModeReadiness(ExecutionMode Current, ExecutionMode Target, bool CanSwitch, IReadOnlyList<ModeCondition> Conditions, IReadOnlyList<BlockingWork> BlockingWork);

public sealed record ModeTransitionRequest(string RequestId, ExecutionMode Target, string Reason, bool ConfirmRealAgv);
public sealed record ModeTransitionResult(bool Succeeded, bool Repeated, ExecutionMode From, ExecutionMode To, IReadOnlyList<ModeCondition> Failed, IReadOnlyList<BlockingWork> BlockingWork);

public sealed record ModeTransitionView(Guid Id, string RequestId, ExecutionMode From, ExecutionMode To, string Actor, string Reason, bool Succeeded, string? Detail, DateTimeOffset At);
public sealed record ExecutionModeView(ExecutionMode Mode, bool IsExplicit, bool RecoveryRequired, string? RecoveryReason, DateTimeOffset UpdatedAt, string UpdatedBy, bool RealDispatchConfigured);

/// <summary>CR-038: the only way to change the mode. Every change is checked, serialised, idempotent by RequestId and audited.</summary>
public interface IExecutionModeService
{
    Task<ExecutionModeView> GetAsync(CancellationToken ct = default);
    Task<ModeReadiness> ReadinessAsync(ExecutionMode target, CancellationToken ct = default);
    Task<ModeTransitionResult> TransitionAsync(ModeTransitionRequest request, string actor, CancellationToken ct = default);
    Task<IReadOnlyList<ModeTransitionView>> HistoryAsync(int take, CancellationToken ct = default);
    /// <summary>Loads (or creates) the row at start-up and, when it says Real, looks for unresolved Real dispatches: if any, the mode becomes RECOVERY_REQUIRED.</summary>
    Task InitializeAsync(CancellationToken ct = default);
    /// <summary>Clears RECOVERY_REQUIRED, but only when no unresolved Real dispatch is left.</summary>
    Task<ModeTransitionResult> AcknowledgeRecoveryAsync(string requestId, string reason, string actor, CancellationToken ct = default);
    /// <summary>Pins the unfinished tasks / orders created before CR-038 to Simulator (never to Real). Work that shows signs of having reached the real RCS is NOT adopted and is returned.</summary>
    Task<LegacyAdoptionResult> AdoptLegacyAsSimulatorAsync(string reason, string actor, CancellationToken ct = default);
}

/// <summary>What a recorded RCS call looked like. The recorder masks secrets and cuts the payload; the caller passes the data as exchanged.</summary>
public sealed record RcsMessageEntry(
    ExecutionMode Mode, RcsMessageDirection Direction, string MessageType, string Protocol, string? RequestCode,
    string? Endpoint = null, string? HttpMethod = null, int? HttpStatus = null, string? Payload = null, string? Result = null,
    string? ErrorCode = null, string? ErrorMessage = null, string? RcsTaskCode = null, long? DurationMs = null);

/// <summary>CR-038: writes one message in its own short transaction. It NEVER throws (a recording problem must not change the outcome of a dispatch) and never stores a secret.</summary>
public interface IRcsMessageRecorder
{
    Task RecordAsync(RcsMessageEntry entry, CancellationToken ct = default);
}

public sealed record RcsMessageSummary(long Seq, Guid Id, DateTimeOffset At, ExecutionMode Mode, RcsMessageDirection Direction, string MessageType, string Protocol, string? Result, int? HttpStatus, string? ErrorCode, string? RequestCode);
public sealed record RcsMessageDetail(Guid Id, Guid? TaskId, string? RequestKey, string? RequestCode, string? RcsTaskCode, ExecutionMode Mode, RcsMessageDirection Direction, string MessageType, string Protocol,
    string? Endpoint, string? HttpMethod, int? HttpStatus, string? Payload, bool PayloadTruncated, string? Result, string? ErrorCode, string? ErrorMessage, string? CorrelationId, DateTimeOffset At, long? DurationMs);

public interface IRcsMessageQuery
{
    /// <summary>Messages that belong to no task (for example a callback for an unknown task code): shown as they are, never attached to a task by guesswork.</summary>
    Task<IReadOnlyList<RcsMessageSummary>> UnlinkedAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<RcsMessageSummary>?> ForTaskAsync(Guid taskId, CancellationToken ct = default);   // null = no such task
    Task<RcsMessageDetail?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>CR-040: the messages recorded for these WCS request codes (the legs of a transport order), in arrival order.</summary>
    Task<IReadOnlyList<RcsMessageSummary>> ByRequestCodesAsync(IReadOnlyList<string> requestCodes, CancellationToken ct = default);
}

public static class ExecutionModeCodes
{
    public const string RecoveryRequired = "EXECUTION_MODE_RECOVERY_REQUIRED";
    public const string NotChosen = "RCS_NOT_CONNECTED";   // unchanged code: the mode was never chosen in Production
    public const string RealNotReady = "RCS_REAL_NOT_READY";
}

/// <summary>CR-038: the fleet as the mode check needs it (Real source, not simulated, connection state). The default reads the shared fleet service; a test may replace it.</summary>
public interface IFleetStatusReader
{
    Task<WCS.Application.Fleet.FleetView?> ReadAsync(CancellationToken ct = default);
}

public sealed record LegacyAdoptionResult(int AdoptedTasks, int AdoptedOrders, IReadOnlyList<BlockingWork> NotAdopted);
