using WCS.Application.Common;
using WCS.Application.Flows;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Application.Abstractions;

/// <summary>A realtime event emitted after a transaction commits (published through SignalR by the API layer).</summary>
/// <summary>AdminOnly events (diagnostics, logs) are delivered only to the Admin group of the realtime hub.</summary>
public sealed record WcsEvent(string Name, object Payload, bool AdminOnly = false);

public interface IWcsNotifier
{
    Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default);
}

public sealed record NoSourceResult(bool Found, Location? Location, Rack? Rack);

/// <summary>
/// WCS-owned inventory (BR-03). Methods taking a task MUST be called inside <see cref="IUnitOfWork.ExecuteAsync{T}"/>;
/// they only mutate tracked entities and never save by themselves.
/// </summary>
public interface IInventoryService
{
    Task<NoSourceResult> ReserveSourceAsync(TransportTask task, FlowRule rule, CancellationToken ct = default);
    /// <summary>Reserve one slot at the first suitable destination (in configured order). Null when none is available.</summary>
    Task<Location?> ReserveDestinationAsync(TransportTask task, FlowRule rule, CancellationToken ct = default);
    Task ConfirmPickAsync(TransportTask task, ConfirmationSource source, string? user, CancellationToken ct = default);
    Task<BindOutcome> ConfirmDropAsync(TransportTask task, FlowRule rule, ConfirmationSource source, string? user,
        string? externalRackId, CancellationToken ct = default);
    /// <summary>Release every ACTIVE reservation of a task that never reached the physical world.</summary>
    Task ReleaseReservationsAsync(TransportTask task, CancellationToken ct = default);
    /// <summary>Mark racks/locations uncertain after a failure/cancel with physical uncertainty (4.8 steps 1-2).</summary>
    Task MarkUncertainAsync(TransportTask task, CancellationToken ct = default);
    /// <summary>
    /// CR-008B-4: RCS reported an outcome (Completed / Interrupted / no record after accepting) that WCS cannot prove physically, whether or not a pick was confirmed.
    /// Neither "still at the source" nor "arrived" may be claimed: the rack becomes UNKNOWN (its WCS-recorded location is kept so it still counts against capacity) and the
    /// source / destination locations require reconciliation, so no new task is allocated to them. Reservations are NOT released. Nothing is done once the drop is confirmed.
    /// </summary>
    Task MarkPhysicalUnprovenAsync(TransportTask task, CancellationToken ct = default);

    // API-level operations (own transaction)
    Task<IReadOnlyList<Guid>> DeclareInitialStockAsync(string locationCode, RackKind kind, int quantity, string user, CancellationToken ct = default, Guid? productModelId = null);
    Task<int> AdjustStockAsync(string locationCode, RackKind kind, int targetQuantity, string user, string? note, CancellationToken ct = default, Guid? productModelId = null);
    /// <summary>Operator-confirmed physical outcome after an incident (4.8 steps 4-6).</summary>
    Task ReconcileTaskAsync(Guid taskId, string actualLocationCode, string user, string? note, CancellationToken ct = default);
}

public enum SignalKind
{
    AgvStarted = 1,
    PlcPermissionRequested = 2,
    PlcPermissionGranted = 3,
    PickConfirmed = 4,
    DropConfirmed = 5,
    RcsCancelConfirmed = 6,
    RcsInterrupted = 7,
    /// <summary>RCS reports the task as ended. Does NOT complete the task without a confirmed drop.</summary>
    RcsEnded = 8,
}

/// <summary>Vendor-neutral task event produced by adapters (RCS callback, PLC) or by the simulator.</summary>
public sealed record TaskSignal(
    SignalKind Kind,
    Guid? TaskId = null,
    string? RcsTaskCode = null,
    string? RobotCode = null,
    string? EventKey = null,
    ConfirmationSource Source = ConfirmationSource.Rcs,
    string? User = null,
    string? Detail = null,
    string? ExternalRackId = null);

public enum SignalOutcome { Applied = 1, Duplicate = 2, Ignored = 3, Rejected = 4 }

public sealed record SignalResult(SignalOutcome Outcome, string Message, TaskDto? Task);

public sealed record CreateTaskCommand(string RequestKey, string Flow, string StationCode, string? RequestedBy);

public sealed record CreateTaskResult(TaskDto Task, bool Created);

public interface ITaskOrchestrator
{
    Task<CreateTaskResult> CreateTaskAsync(CreateTaskCommand command, CancellationToken ct = default);
    Task<SignalResult> HandleSignalAsync(TaskSignal signal, CancellationToken ct = default);
    Task<TaskDto> CancelTaskAsync(Guid taskId, string reason, string? user, CancellationToken ct = default);
    /// <summary>Changes the requesting station of a task that has not been claimed by an AGV yet. Re-checked against the database state.</summary>
    Task<TaskDto> UpdateTaskAsync(Guid taskId, string flow, string stationCode, string? expectedState, Auth.ActorInfo actor, CancellationToken ct = default);
    /// <summary>HARD delete (the row is removed) of a task that no AGV/RCS has touched. The audit entry is committed with the delete and survives it.</summary>
    Task<TaskDto> DeleteTaskAsync(Guid taskId, string? expectedState, Auth.ActorInfo actor, CancellationToken ct = default);

    // Driven by the scheduler / recovery
    Task ProcessTaskAsync(Guid taskId, CancellationToken ct = default);
    Task TryDispatchNextAsync(Guid agvId, CancellationToken ct = default);
    Task DispatchToRcsAsync(Guid taskId, CancellationToken ct = default);
    Task TryResolveDestinationAsync(Guid taskId, CancellationToken ct = default);
    Task SendContinueAsync(Guid taskId, CancellationToken ct = default);
    Task ReconcileCancelPendingAsync(Guid taskId, CancellationToken ct = default);
    Task EnforceTimeoutAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>CR-028 (RcsManaged only): sends the next queued task(s) to the RCS while the fleet-level limit allows. No-op in Legacy dispatch.</summary>
    Task TryDispatchFleetQueueAsync(CancellationToken ct = default) => Task.CompletedTask;
    /// <summary>CR-028 (RcsManaged only): asks the RCS which AMR each sent task got and records it. No-op in Legacy dispatch.</summary>
    Task SyncAssignmentsAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";
    public int TickIntervalMs { get; set; } = 1000;
    /// <summary>Maximum RCS create attempts before the dispatch is declared uncertain. Attempts are spaced by the retry backoff below (CR-008A).</summary>
    public int MaxDispatchAttempts { get; set; } = 5;
    /// <summary>CR-008A technical retry spacing (NOT a business timeout): first retry after this delay, doubling each attempt up to the maximum.</summary>
    public int RcsRetryInitialDelayMs { get; set; } = 2000;
    public int RcsRetryMaxDelayMs { get; set; } = 60000;
    /// <summary>CR-008A: automatic continue/cancel RCS attempts per state. After this many the automatic retry stops, the task is flagged for reconciliation and an operator decides.</summary>
    public int RcsMaxAttempts { get; set; } = 30;
    /// <summary>Per-state timeouts in seconds. Null/absent = disabled. Real values must be agreed with PLC/RCS teams (5.3).</summary>
    public Dictionary<string, int> StateTimeoutsSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface ITaskScheduler
{
    Task RunOnceAsync(CancellationToken ct = default);
}

/// <summary>Heartbeat for the system-health endpoint.</summary>
public interface ISchedulerStatus
{
    DateTimeOffset? LastRunAt { get; }
    string? LastError { get; }
    void Report(DateTimeOffset at, string? error);
}
