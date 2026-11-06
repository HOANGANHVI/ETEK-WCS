using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Flows;
using WCS.Domain.Common;
using WCS.Domain.Tasks;

namespace WCS.Application.Services;

/// <summary>
/// Vendor-neutral process orchestrator (BP-01/02/03/05/06). BP-04 is not supported (REVIEW); A3/A4 and D1/D2 are not
/// routed here (UNCHANGED legacy process). All external I/O (RCS, PLC) happens OUTSIDE database transactions, and every
/// externally-originated event is idempotent.
/// </summary>
public sealed class TaskOrchestrator(
    IUnitOfWork uow,
    ITransportTaskRepository tasks,
    IAgvRepository agvs,
    ILocationRepository locations,
    IInventoryService inventory,
    IFlowCatalog flows,
    IRcsClient rcs,
    IStationSignalService stations,
    IProcessedEventRepository processed,
    IReservationRepository reservationRepo,
    IWcsNotifier notifier,
    IClock clock,
    Auth.IAuditLog audit,
    IOptions<ProcessFlowOptions> flowOptions,
    IOptions<SchedulerOptions> schedulerOptions,
    ILogger<TaskOrchestrator> logger) : ITaskOrchestrator
{
    private const string Wcs = "WCS";

    private delegate Task<T> TxWork<T>(List<WcsEvent> events, List<Func<Task>> after, CancellationToken ct);

    // ============================================================================ create

    public async Task<CreateTaskResult> CreateTaskAsync(CreateTaskCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.RequestKey)) throw AppException.Validation("REQUEST_KEY_REQUIRED", "RequestKey is required.");
        if (!flows.TryParseFlow(cmd.Flow, out var flow))
        {
            var normalized = (cmd.Flow ?? "").Replace("-", "").Replace("_", "").Trim();
            throw normalized.Equals("BP04", StringComparison.OrdinalIgnoreCase)
                ? AppException.Rejected("FLOW_IN_REVIEW", "BP-04 (slide 8) is REVIEW and not approved for implementation.")
                : AppException.Rejected("FLOW_NOT_SUPPORTED", $"Flow '{cmd.Flow}' is not an approved flow. A3/A4 and D1/D2 keep the legacy process.");
        }
        var rule = flows.Get(flow);
        var station = await locations.GetByCodeAsync(cmd.StationCode, ct)
                      ?? throw AppException.NotFound("STATION_NOT_FOUND", $"Station '{cmd.StationCode}' not found.");
        if (!rule.AllowedRequestStations.Contains(station.Code))
            throw AppException.Rejected("STATION_NOT_ALLOWED", $"Station {station.Code} cannot request {flow}.");
        var agv = await agvs.GetByRoleAsync(rule.AgvRole, ct)
                  ?? throw AppException.Rejected("AGV_NOT_CONFIGURED", $"No AGV with role {rule.AgvRole} is configured.");
        if (!agv.Enabled) throw AppException.Rejected("AGV_DISABLED", $"{agv.Code} is disabled.");

        var created = await Tx(async (events, _, t) =>
        {
            var existing = await tasks.GetByRequestKeyAsync(cmd.RequestKey.Trim(), t);
            if (existing is not null) return (Guid?)null;
            var task = TransportTask.Create(cmd.RequestKey, flow, rule.RackKind, agv.Id, station.Id, cmd.RequestedBy, clock.UtcNow);
            tasks.Add(task);
            events.Add(new WcsEvent("TaskCreated", task.ToDto()));
            return task.Id;
        }, ct);

        if (created is { } id)
        {
            await ProcessTaskAsync(id, ct);
            return new CreateTaskResult(await GetDto(id, ct), true);
        }
        var existingTask = await Tx((_, _, t) => tasks.GetByRequestKeyAsync(cmd.RequestKey.Trim(), t), ct);
        logger.LogInformation("Duplicate request {RequestKey} returned existing task {TaskId}", cmd.RequestKey, existingTask!.Id);
        return new CreateTaskResult(existingTask.ToDto(), false);
    }

    // ============================================================================ pre-dispatch processing

    public async Task ProcessTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var dispatch = await Tx(async (events, after, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.State is not (TaskState.Pending or TaskState.Validating)) return false;
            var rule = flows.Get(task.Flow);

            if (task.State == TaskState.Pending) task.TransitionTo(TaskState.Validating, "Validating business conditions", Wcs, clock.UtcNow);

            var agv = await agvs.GetAsync(task.AgvId, t);
            if (agv is null || !agv.Enabled)
            {
                await FinishAsync(task, TaskState.Failed, "AGV_UNAVAILABLE", Wcs, events, after, t);
                return false;
            }

            var source = await inventory.ReserveSourceAsync(task, rule, t);
            if (!source.Found)
            {
                if (flowOptions.Value.NoSourceBehavior.Equals("Wait", StringComparison.OrdinalIgnoreCase))
                {
                    if (!task.History.Any(h => h.EventName == "WAITING_FOR_SOURCE"))
                        task.Note("WAITING_FOR_SOURCE", "No source rack available yet; will be re-evaluated", Wcs, clock.UtcNow);
                }
                else await FinishAsync(task, TaskState.Failed, "NO_SOURCE_AVAILABLE", Wcs, events, after, t);
                events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
                return false;
            }

            var busy = await tasks.GetActiveForAgvAsync(agv.Id, t) is not null || await tasks.AnyQueuedForAgvAsync(agv.Id, t);
            if (busy) task.TransitionTo(TaskState.Queued, "AGV busy; request queued", Wcs, clock.UtcNow);
            else task.TransitionTo(TaskState.Dispatching, "AGV free; claimed", Wcs, clock.UtcNow);
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return !busy;
        }, ct);

        if (dispatch) await DispatchToRcsAsync(taskId, ct);
    }

    public async Task TryDispatchNextAsync(Guid agvId, CancellationToken ct = default)
    {
        var next = await Tx(async (events, _, t) =>
        {
            if (await tasks.GetActiveForAgvAsync(agvId, t) is not null) return (Guid?)null; // PER_AGV
            var agv = await agvs.GetAsync(agvId, t);
            if (agv is null || !agv.Enabled) return null;
            var task = await tasks.NextQueuedForAgvAsync(agvId, t); // FIFO: NEEDS_CONFIRMATION (no approved priority policy)
            if (task is null) return null;
            task.TransitionTo(TaskState.Dispatching, "AGV free; claimed from queue", Wcs, clock.UtcNow);
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return task.Id;
        }, ct);
        if (next is { } id) await DispatchToRcsAsync(id, ct);
    }

    // ============================================================================ RCS dispatch

    public async Task DispatchToRcsAsync(Guid taskId, CancellationToken ct = default)
    {
        // phase 1 (tx): prepare
        var prep = await Tx(async (events, after, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.State != TaskState.Dispatching || task.RcsTaskCode is not null) return null;
            if (task.DispatchAttempts >= schedulerOptions.Value.MaxDispatchAttempts)
            {
                // An unknown outcome may still have created a task in RCS: keep resources and require reconciliation.
                await FinishAsync(task, TaskState.Failed, "RCS_DISPATCH_UNCERTAIN", Wcs, events, after, t, forceUncertain: true);
                return null;
            }
            var agv = await agvs.GetAsync(task.AgvId, t);
            var src = await locations.GetAsync(task.SourceLocationId ?? Guid.Empty, t);
            var req = await locations.GetAsync(task.RequestLocationId, t);
            var attemptsBefore = task.DispatchAttempts;
            task.RegisterDispatchAttempt(clock.UtcNow);
            return new DispatchPlan(new RcsCreateTaskRequest(task.RcsRequestCode, task.Flow, task.RackKind, agv?.RcsRobotCode,
                src?.Code ?? "", req?.Code ?? ""), attemptsBefore);
        }, ct);
        if (prep is null) return;

        // phase 2: external calls outside any transaction
        RcsCallResult result;
        try
        {
            result = new RcsCallResult(RcsCallOutcome.Unknown, null, null);
            if (prep.AttemptsBefore > 0)
            {
                // After a timeout / unknown result, never blindly re-send: query first (task-state-machine.md 3.7).
                var (status, code) = await rcs.QueryTaskStatusAsync(prep.Request.RequestCode, ct);
                if (status == RcsTaskStatus.Unknown) { await Note(taskId, "RCS_QUERY_UNKNOWN", "Status query inconclusive; will retry", ct); return; }
                if (status != RcsTaskStatus.NotFound && code is not null)
                    result = new RcsCallResult(RcsCallOutcome.Accepted, code, "Found by query");
                else result = await rcs.CreateTaskAsync(prep.Request, ct);
            }
            else result = await rcs.CreateTaskAsync(prep.Request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "RCS create call failed for {RequestCode}", prep.Request.RequestCode);
            result = new RcsCallResult(RcsCallOutcome.Unknown, null, ex.Message);
        }

        // phase 3 (tx): apply
        await Tx(async (events, after, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.State != TaskState.Dispatching) return false;
            switch (result.Outcome)
            {
                case RcsCallOutcome.Accepted when !string.IsNullOrWhiteSpace(result.RcsTaskCode):
                    task.AttachRcsTask(result.RcsTaskCode!, clock.UtcNow);
                    task.TransitionTo(TaskState.Accepted, "RCS accepted the task", "RCS", clock.UtcNow);
                    break;
                case RcsCallOutcome.Rejected:
                    await FinishAsync(task, TaskState.Failed, $"RCS_REJECTED: {result.Message}", "RCS", events, after, t);
                    break;
                default:
                    task.Note("RCS_DISPATCH_UNKNOWN", result.Message, "RCS", clock.UtcNow);
                    break;
            }
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return true;
        }, ct);
    }

    private sealed record DispatchPlan(RcsCreateTaskRequest Request, int AttemptsBefore);

    // ============================================================================ signals

    public async Task<SignalResult> HandleSignalAsync(TaskSignal signal, CancellationToken ct = default)
    {
        return await Tx<SignalResult>(async (events, after, t) =>
        {
            var task = signal.TaskId is { } id ? await tasks.GetAsync(id, t)
                : signal.RcsTaskCode is not null ? await tasks.GetByRcsTaskCodeAsync(signal.RcsTaskCode, t) : null;
            if (task is null) return new SignalResult(SignalOutcome.Rejected, "Task not found for signal.", null);

            var source = signal.Source.ToString();
            if (!string.IsNullOrWhiteSpace(signal.EventKey) && await processed.ExistsAsync(source, signal.EventKey, t))
                return new SignalResult(SignalOutcome.Duplicate, "Event already processed.", task.ToDto());

            var agv = await agvs.GetAsync(task.AgvId, t);
            if (signal.RobotCode is not null && agv?.RcsRobotCode is not null &&
                !string.Equals(signal.RobotCode, agv.RcsRobotCode, StringComparison.OrdinalIgnoreCase))
                return new SignalResult(SignalOutcome.Rejected, "Robot code does not match the task's AGV.", task.ToDto());

            if (task.IsTerminal)
            {
                task.Note("LATE_EVENT_IGNORED", $"{signal.Kind} after {task.State}", source, clock.UtcNow);
                return new SignalResult(SignalOutcome.Ignored, $"Task already {task.State}.", task.ToDto());
            }

            var rule = flows.Get(task.Flow);
            var result = await ApplySignalAsync(task, rule, agv, signal, events, after, t);

            if (result.Outcome == SignalOutcome.Applied && !string.IsNullOrWhiteSpace(signal.EventKey))
                processed.Add(new ProcessedEvent(source, signal.EventKey, task.Id, clock.UtcNow));
            if (result.Outcome == SignalOutcome.Applied) events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return result with { Task = task.ToDto() };
        }, ct);
    }

    private async Task<SignalResult> ApplySignalAsync(TransportTask task, FlowRule rule, Domain.Agvs.Agv? agv, TaskSignal s,
        List<WcsEvent> events, List<Func<Task>> after, CancellationToken t)
    {
        var now = clock.UtcNow;
        var src = s.Source.ToString();
        SignalResult Applied(string m) => new(SignalOutcome.Applied, m, null);
        SignalResult Dup(string m) => new(SignalOutcome.Duplicate, m, null);
        SignalResult Reject(string m) => new(SignalOutcome.Rejected, m, null);
        SignalResult Ignore(string m)
        {
            task.Note("OUT_OF_ORDER_EVENT_IGNORED", $"{s.Kind} in {task.State}", src, now);
            return new(SignalOutcome.Ignored, m, null);
        }

        switch (s.Kind)
        {
            case SignalKind.AgvStarted:
                if (task.State is TaskState.Accepted or TaskState.Continuing)
                { task.TransitionTo(TaskState.Executing, "AGV executing", src, now); return Applied("Executing"); }
                return task.State == TaskState.Executing ? Dup("Already executing") : Ignore($"AgvStarted not valid in {task.State}");

            case SignalKind.PlcPermissionRequested:
                if (task.State == TaskState.WaitingForPlc) return Dup("Already waiting for PLC");
                if (task.State != TaskState.Executing) return Ignore($"PlcPermissionRequested not valid in {task.State}");
                task.TransitionTo(TaskState.WaitingForPlc, $"Waiting for PLC permission ({task.CurrentLeg})", src, now);
                var locId = task.CurrentLeg == TaskLeg.Pick ? task.SourceLocationId : task.DestinationLocationId;
                if (locId is { } lid && await locations.GetAsync(lid, t) is { } loc)
                {
                    var dir = task.CurrentLeg == TaskLeg.Pick ? TransferDirection.Unload : TransferDirection.Load; // NEEDS_CONFIRMATION with PLC team
                    var taskId = task.Id;
                    after.Add(() => stations.RequestTransferPermissionAsync(loc.Code, dir, taskId, CancellationToken.None));
                }
                return Applied("Waiting for PLC");

            case SignalKind.PlcPermissionGranted:
                if (task.State == TaskState.WaitingForPlc)
                { task.SetPlcPermission(true, now); task.TransitionTo(TaskState.Executing, "PLC permission granted", src, now); return Applied("Permission granted"); }
                return task.State == TaskState.Executing && task.PlcPermissionGranted ? Dup("Already granted") : Ignore($"PlcPermissionGranted not valid in {task.State}");

            case SignalKind.PickConfirmed:
            {
                if (task.PickConfirmedAt is not null) return Dup("Pick already confirmed");
                if (task.State is not (TaskState.Executing or TaskState.CancelPending)) return Reject($"PickConfirmed not valid in {task.State}");
                var srcLoc = await locations.GetAsync(task.SourceLocationId ?? Guid.Empty, t);
                if (task.State == TaskState.Executing && srcLoc is { RequiresPlcPermission: true } && !task.PlcPermissionGranted)
                    return Reject("PLC permission is required before pick (business-rules.md 1.6)");

                await inventory.ConfirmPickAsync(task, s.Source, s.User, t);
                task.ConfirmPick(now);
                task.Note("PICK_CONFIRMED", s.Detail, src, now);

                if (task.State == TaskState.CancelPending)
                {
                    task.RequireReconciliation("Pick confirmed while cancelling; rack is IN_TRANSIT", now);
                    return Applied("Pick recorded during cancel; reconciliation required");
                }
                if (await inventory.ReserveDestinationAsync(task, rule, t) is { } dest)
                {
                    task.TransitionTo(TaskState.Continuing, $"Destination assigned: {dest.Code}", Wcs, now);
                    var id = task.Id;
                    after.Add(() => SendContinueAsync(id));
                }
                else task.TransitionTo(TaskState.WaitingForDestination, "No destination available", Wcs, now);
                return Applied("Pick confirmed");
            }

            case SignalKind.DropConfirmed:
            {
                if (task.DropConfirmedAt is not null) return Dup("Drop already confirmed");
                if (task.PickConfirmedAt is null) return Reject("Drop cannot be confirmed before pick");
                if (task.State is not (TaskState.Executing or TaskState.CancelPending)) return Reject($"DropConfirmed not valid in {task.State}");
                var dest = await locations.GetAsync(task.DestinationLocationId ?? Guid.Empty, t);
                if (task.State == TaskState.Executing && dest is { RequiresPlcPermission: true } && !task.PlcPermissionGranted)
                    return Reject("PLC permission is required before drop (business-rules.md 1.6)");

                await inventory.ConfirmDropAsync(task, rule, s.Source, s.User, s.ExternalRackId, t);
                task.ConfirmDrop(now);
                task.TransitionTo(TaskState.Completed, "Drop confirmed; inventory updated", src, now);
                after.Add(() => TryDispatchNextAsync(task.AgvId));
                return Applied("Completed");
            }

            case SignalKind.RcsCancelConfirmed:
                if (task.State != TaskState.CancelPending) task.TransitionTo(TaskState.CancelPending, "RCS reported cancellation", src, now);
                await FinishAsync(task, TaskState.Cancelled, s.Detail ?? "Cancel confirmed by RCS", src, events, after, t);
                return Applied("Cancelled");

            case SignalKind.RcsInterrupted:
                await FinishAsync(task, TaskState.Failed, s.Detail ?? "RCS reported the task interrupted", src, events, after, t);
                return Applied("Failed");

            case SignalKind.RcsEnded:
                if (task.DropConfirmedAt is not null) return Dup("Already completed by drop confirmation");
                // RCS 'end' alone is not physical proof of delivery (business-rules.md 1.6): never complete from it.
                if (!task.RequiresReconciliation) task.RequireReconciliation("RCS reported end without WCS drop confirmation", now);
                return Applied("RCS end recorded without completion; reconciliation required");

            default:
                return Reject("Unknown signal");
        }
    }

    // ============================================================================ continue / destination

    public async Task TryResolveDestinationAsync(Guid taskId, CancellationToken ct = default)
    {
        var resolved = await Tx(async (events, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.State != TaskState.WaitingForDestination) return false;
            if (await inventory.ReserveDestinationAsync(task, flows.Get(task.Flow), t) is not { } dest) return false;
            task.TransitionTo(TaskState.Continuing, $"Destination assigned: {dest.Code}", Wcs, clock.UtcNow);
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return true;
        }, ct);
        if (resolved) await SendContinueAsync(taskId, ct);
    }

    public async Task SendContinueAsync(Guid taskId, CancellationToken ct = default)
    {
        var plan = await Tx(async (_, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.State != TaskState.Continuing || task.RcsTaskCode is null) return null;
            var dest = await locations.GetAsync(task.DestinationLocationId ?? Guid.Empty, t);
            var isRetry = task.History.Any(h => h.EventName == "RCS_CONTINUE_UNKNOWN");
            return dest is null ? null : new ContinuePlan(task.RcsRequestCode, task.RcsTaskCode, dest.Code, isRetry);
        }, ct);
        if (plan is null) return;

        RcsCallResult? result = null;
        if (plan.IsRetry)
        {
            // A previous continue attempt had an unknown outcome: ask RCS first (task-state-machine.md 3.5), never blindly resend.
            try
            {
                var (status, _) = await rcs.QueryTaskStatusAsync(plan.RequestCode, ct);
                if (status == RcsTaskStatus.Executing) result = new RcsCallResult(RcsCallOutcome.Accepted, null, "Confirmed by status query before retry");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "RCS status query before continue retry failed"); }
        }
        if (result is null)
        {
            try { result = await rcs.ContinueTaskAsync(plan.RequestCode, plan.RcsTaskCode, plan.DestinationCode, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "RCS continue call failed for {RequestCode}", plan.RequestCode);
                result = new RcsCallResult(RcsCallOutcome.Unknown, null, ex.Message);
            }
        }
        if (result.Outcome == RcsCallOutcome.Unknown)
        {
            // Query before any retry (task-state-machine.md 3.5).
            try
            {
                var (status, _) = await rcs.QueryTaskStatusAsync(plan.RequestCode, ct);
                if (status == RcsTaskStatus.Executing) result = new RcsCallResult(RcsCallOutcome.Accepted, null, "Confirmed by status query");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "RCS status query failed"); }
        }

        await Tx(async (events, after, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.State != TaskState.Continuing) return false;
            switch (result.Outcome)
            {
                case RcsCallOutcome.Accepted:
                    task.TransitionTo(TaskState.Executing, "RCS continued the task", "RCS", clock.UtcNow); break;
                case RcsCallOutcome.Rejected:
                    await FinishAsync(task, TaskState.Failed, $"RCS_CONTINUE_REJECTED: {result.Message}", "RCS", events, after, t); break;
                default:
                    task.Note("RCS_CONTINUE_UNKNOWN", result.Message, "RCS", clock.UtcNow); break;
            }
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return true;
        }, ct);
    }

    private sealed record ContinuePlan(string RequestCode, string RcsTaskCode, string DestinationCode, bool IsRetry);

    // ============================================================================ cancel

    public async Task<TaskDto> CancelTaskAsync(Guid taskId, string reason, string? user, CancellationToken ct = default)
    {
        var needsRcsCancel = await Tx(async (events, after, t) =>
        {
            var task = await tasks.GetAsync(taskId, t) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.");
            var why = $"Cancel requested by {user ?? "system"}: {reason}";
            switch (task.State)
            {
                case TaskState.Cancelled: return false; // idempotent
                case TaskState.Completed or TaskState.Failed:
                    throw AppException.Rejected("TASK_ALREADY_FINISHED", $"Task is already {task.State}.");
                case TaskState.Pending or TaskState.Validating or TaskState.Queued:
                    // Never dispatched: there is no RCS task to cancel and nothing physical to reconcile.
                    task.TransitionTo(TaskState.Cancelled, why, Wcs, clock.UtcNow);
                    await inventory.ReleaseReservationsAsync(task, t);
                    break;
                case TaskState.CancelPending:
                    task.Note("CANCEL_RETRY", why, Wcs, clock.UtcNow);
                    break;
                default:
                    task.TransitionTo(TaskState.CancelPending, why, Wcs, clock.UtcNow);
                    if (task.RcsTaskCode is null && task.DispatchAttempts == 0)
                        await FinishAsync(task, TaskState.Cancelled, "Cancelled before reaching RCS", Wcs, events, after, t);
                    break;
            }
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            if (task.IsTerminal) after.Add(() => TryDispatchNextAsync(task.AgvId));
            return !task.IsTerminal;
        }, ct);

        if (needsRcsCancel) await SendCancelToRcsAsync(taskId, ct);
        return await GetDto(taskId, ct);
    }

    // ============================================================================ user edit / delete (before an AGV has accepted the task)

    /// <summary>
    /// The authoritative permission/state check, run inside the transaction on the freshly loaded task. The state the client saw
    /// (<paramref name="expectedState"/>) is only a hint used to tell the user to reload; it can never grant access.
    /// </summary>
    private static void EnsureUserEditable(TransportTask task, string? expectedState, Auth.ActorInfo actor)
    {
        if (!string.IsNullOrWhiteSpace(expectedState) && !task.State.ToString().Equals(expectedState.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict("TASK_STATE_CHANGED", $"Nhiệm vụ đã chuyển sang trạng thái {task.State}. Vui lòng tải lại dữ liệu.");
        if (task.IsUntouchedByAgvAndRcs) return;
        throw actor.IsAdmin
            ? AppException.Conflict("TASK_NOT_EDITABLE", $"Nhiệm vụ đang ở trạng thái {task.State} nên không thể sửa/xóa trực tiếp. Hãy dùng chức năng hủy hoặc đối soát.")
            : AppException.Forbidden("TASK_LOCKED", "Nhiệm vụ đã được AGV tiếp nhận hoặc đã kết thúc; tài khoản Operator không có quyền sửa/xóa.");
    }

    public async Task<TaskDto> UpdateTaskAsync(Guid taskId, string flowText, string stationCode, string? expectedState, Auth.ActorInfo actor, CancellationToken ct = default)
    {
        return await Tx(async (events, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Không tìm thấy nhiệm vụ.");
            EnsureUserEditable(task, expectedState, actor);

            // The new request is validated exactly like a new one (same rules as CreateTaskAsync).
            if (!flows.TryParseFlow(flowText, out var flow))
            {
                var normalized = (flowText ?? "").Replace("-", "").Replace("_", "").Trim();
                throw normalized.Equals("BP04", StringComparison.OrdinalIgnoreCase)
                    ? AppException.Rejected("FLOW_IN_REVIEW", "BP-04 (slide 8) is REVIEW and not approved for implementation.")
                    : AppException.Rejected("FLOW_NOT_SUPPORTED", $"Flow '{flowText}' is not an approved flow.");
            }
            var rule = flows.Get(flow);
            var station = await locations.GetByCodeAsync((stationCode ?? "").Trim(), t)
                          ?? throw AppException.NotFound("STATION_NOT_FOUND", $"Không tìm thấy trạm '{stationCode}'.");
            if (!rule.AllowedRequestStations.Contains(station.Code))
                throw AppException.Rejected("STATION_NOT_ALLOWED", $"Trạm {station.Code} không được phép yêu cầu quy trình {flow}.");
            var agv = await agvs.GetByRoleAsync(rule.AgvRole, t)
                      ?? throw AppException.Rejected("AGV_NOT_CONFIGURED", $"No AGV with role {rule.AgvRole} is configured.");
            if (!agv.Enabled) throw AppException.Rejected("AGV_DISABLED", $"{agv.Code} is disabled.");
            if (flow == task.Flow && station.Id == task.RequestLocationId) return task.ToDto();   // nothing to change

            var oldStation = (await locations.GetAsync(task.RequestLocationId, t))?.Code ?? "?";
            var oldFlow = task.Flow;
            await inventory.ReleaseReservationsAsync(task, t);
            task.ReconfigureRequest(flow, rule.RackKind, agv.Id, station.Id, $"flow {oldFlow}/{oldStation} -> {flow}/{station.Code}", actor.Username, clock.UtcNow);
            // A task that is already queued holds a source rack: re-select it for the new request. If none exists the whole edit is
            // refused and rolled back. Pending/Validating tasks have no reservation; the normal processing step reserves later.
            if (task.State == TaskState.Queued && !(await inventory.ReserveSourceAsync(task, rule, t)).Found)
                throw AppException.Rejected("NO_SOURCE_AT_STATION", "Không có rack nguồn phù hợp cho yêu cầu mới. Nhiệm vụ không bị thay đổi.");
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return task.ToDto();
        }, ct);
    }

    /// <summary>
    /// HARD delete. Inside one transaction on the freshly loaded task: permission/state re-checked, the audit entry is staged in the same
    /// transaction (it has no foreign key to the task and therefore survives it), reservations/processed events/history of the task are
    /// removed together with the task. A concurrent dispatch changes the row version, so one of the two transactions loses and re-checks.
    /// </summary>
    public async Task<TaskDto> DeleteTaskAsync(Guid taskId, string? expectedState, Auth.ActorInfo actor, CancellationToken ct = default)
    {
        return await Tx(async (events, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Không tìm thấy nhiệm vụ.");
            EnsureUserEditable(task, expectedState, actor);
            var dto = task.ToDto();
            var station = (await locations.GetAsync(task.RequestLocationId, t))?.Code;
            var code = $"NV-{task.Id.ToString("N")[..8].ToUpperInvariant()}";
            audit.Enlist(new Auth.AuditRecord("TASK_DELETE", actor.Username, actor.Role.ToString(), "Task", task.Id.ToString(), WCS.Domain.Users.AuditResult.Success,
                $"Đã xóa cứng nhiệm vụ {code}",
                Before: new { code, task.Id, task.RequestKey, flow = task.Flow.ToString(), state = task.State.ToString(), requestStation = station, task.RequestedBy,
                              task.AgvId, task.SourceLocationId, task.DestinationLocationId, task.CreatedAt, deletedState = task.State.ToString() },
                After: null, IpAddress: actor.IpAddress));
            await inventory.ReleaseReservationsAsync(task, t);
            await reservationRepo.RemoveAllForTaskAsync(task.Id, t);
            await processed.RemoveForTaskAsync(task.Id, t);
            tasks.Remove(task);
            events.Add(new WcsEvent("TaskDeleted", new { id = task.Id }));
            return dto;
        }, ct);
    }

    private async Task SendCancelToRcsAsync(Guid taskId, CancellationToken ct)
    {
        var plan = await Tx(async (_, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            return task is { State: TaskState.CancelPending } ? (task.RcsRequestCode, task.RcsTaskCode) : default((string, string?)?);
        }, ct);
        if (plan is null) return;
        try
        {
            var r = await rcs.CancelTaskAsync(plan.Value.Item1, plan.Value.Item2, ct);
            await Note(taskId, "RCS_CANCEL_REQUESTED", $"{r.Outcome}: {r.Message}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "RCS cancel call failed for task {TaskId}", taskId);
            await Note(taskId, "RCS_CANCEL_UNKNOWN", ex.Message, ct);
        }
    }

    public async Task ReconcileCancelPendingAsync(Guid taskId, CancellationToken ct = default)
    {
        var code = await Tx(async (_, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            return task is { State: TaskState.CancelPending } ? task.RcsRequestCode : null;
        }, ct);
        if (code is null) return;

        RcsTaskStatus status;
        try { (status, _) = await rcs.QueryTaskStatusAsync(code, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "RCS status query failed"); return; }

        switch (status)
        {
            case RcsTaskStatus.Cancelled or RcsTaskStatus.NotFound:
                await HandleSignalAsync(new TaskSignal(SignalKind.RcsCancelConfirmed, taskId, Source: ConfirmationSource.Rcs,
                    Detail: "Cancel confirmed by RCS status query"), ct);
                break;
            case RcsTaskStatus.Executing or RcsTaskStatus.Created:
                await SendCancelToRcsAsync(taskId, ct); // cancel not yet effective: ask again
                break;
        }
    }

    // ============================================================================ timeouts

    public async Task EnforceTimeoutAsync(Guid taskId, CancellationToken ct = default)
    {
        var cancelRcs = await Tx(async (events, after, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null || task.IsTerminal) return false;
            if (!schedulerOptions.Value.StateTimeoutsSeconds.TryGetValue(task.State.ToString(), out var seconds) || seconds <= 0) return false;
            if ((clock.UtcNow - task.StateEnteredAt).TotalSeconds < seconds) return false;

            var reason = $"TIMEOUT_{task.State}";
            if (!task.OccupiesAgv) await FinishAsync(task, TaskState.Failed, reason, Wcs, events, after, t);
            else if (task.State == TaskState.CancelPending) await FinishAsync(task, TaskState.Failed, reason, Wcs, events, after, t, forceUncertain: true);
            else task.TransitionTo(TaskState.CancelPending, reason, Wcs, clock.UtcNow);
            events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
            return task.State == TaskState.CancelPending;
        }, ct);
        if (cancelRcs) await SendCancelToRcsAsync(taskId, ct);
    }

    // ============================================================================ helpers

    /// <summary>
    /// Moves a task to a terminal state. If the physical world may have been touched (RCS task exists / rack picked / outcome
    /// uncertain) the reservations are KEPT and reconciliation is required (inventory-management.md 4.4, 4.8); otherwise they
    /// are released.
    /// </summary>
    private async Task FinishAsync(TransportTask task, TaskState terminal, string reason, string source, List<WcsEvent> events,
        List<Func<Task>> after, CancellationToken t, bool forceUncertain = false)
    {
        var physical = forceUncertain || task.RcsTaskCode is not null || task.PickConfirmedAt is not null;
        var wasQueuedBehind = task.AgvId;
        task.TransitionTo(terminal, reason, source, clock.UtcNow);
        if (physical)
        {
            if (terminal != TaskState.Completed) task.RequireReconciliation(reason, clock.UtcNow);
            await inventory.MarkUncertainAsync(task, t);
        }
        else await inventory.ReleaseReservationsAsync(task, t);
        events.Add(new WcsEvent("TaskStateChanged", task.ToDto()));
        after.Add(() => TryDispatchNextAsync(wasQueuedBehind));
    }

    private async Task Note(Guid taskId, string name, string? detail, CancellationToken ct) =>
        await Tx(async (events, _, t) =>
        {
            var task = await tasks.GetAsync(taskId, t);
            if (task is null) return false;
            task.Note(name, detail, Wcs, clock.UtcNow);
            return true;
        }, ct);

    private async Task<TaskDto> GetDto(Guid id, CancellationToken ct) =>
        (await Tx((_, _, t) => tasks.GetAsync(id, t), ct) ?? throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.")).ToDto();

    private async Task<T> Tx<T>(TxWork<T> work, CancellationToken ct)
    {
        var events = new List<WcsEvent>();
        var after = new List<Func<Task>>();
        var result = await uow.ExecuteAsync(async token =>
        {
            events.Clear(); after.Clear();
            return await work(events, after, token);
        }, ct);
        if (events.Count > 0)
        {
            try { await notifier.PublishAsync(events, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Realtime publish failed"); }
        }
        foreach (var a in after)
        {
            try { await a(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Post-commit action failed"); }
        }
        return result;
    }
}
