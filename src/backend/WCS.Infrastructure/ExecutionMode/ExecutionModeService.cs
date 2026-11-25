using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Fleet;
using WCS.Application.Flows;
using WCS.Application.RealDispatch;
using WCS.Domain.Rcs;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.RealDispatch;
using WCS.Domain.Routing;

namespace WCS.Infrastructure.ModeControl;

/// <summary>
/// CR-038: the only way to change the operating mode. A change is checked against the real state of the system, serialised (an advisory lock for concurrent requests, a row lock on the
/// mode row that task creation shares), idempotent by RequestId, audited, and never cancels or alters a task to make itself possible.
/// </summary>
internal sealed class ExecutionModeService(WcsDbContext db, IExecutionModeProvider provider, IClock clock, IOptions<RealDispatchOptions> realOptions, IFlowCatalog flows, IFleetStatusReader fleet,
    HikRcsClient hik, ILogger<ExecutionModeService> logger) : IExecutionModeService
{
    private const long TransitionLockKey = 7370032;
    private static readonly TaskState[] FinalTask = [TaskState.Completed, TaskState.Failed, TaskState.Cancelled];
    private static readonly OrderState[] FinalOrder = [OrderState.Completed, OrderState.Failed, OrderState.Cancelled];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private RealDispatchOptions Real => realOptions.Value;

    // ------------------------------------------------------------------------------------------------ read
    public async Task<ExecutionModeView> GetAsync(CancellationToken ct = default)
    {
        var s = await db.ExecutionModeStates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct) ?? ExecutionModeState.Default(clock.UtcNow);
        return new ExecutionModeView(s.Mode, s.IsExplicit, s.RecoveryRequired, s.RecoveryReason, s.UpdatedAt, s.UpdatedBy, Real.Enabled);
    }

    public async Task<IReadOnlyList<ModeTransitionView>> HistoryAsync(int take, CancellationToken ct = default) =>
        await db.ExecutionModeTransitions.AsNoTracking().OrderByDescending(x => x.At).Take(Math.Clamp(take, 1, 200))
            .Select(x => new ModeTransitionView(x.Id, x.RequestId, x.FromMode, x.ToMode, x.Actor, x.Reason, x.Succeeded, x.Detail, x.At)).ToListAsync(ct);

    public async Task<ModeReadiness> ReadinessAsync(ExecutionMode target, CancellationToken ct = default)
    {
        var current = (await GetAsync(ct)).Mode;
        var view = await ReadFleetAsync(ct);
        var (conditions, blocking) = await EvaluateAsync(current, target, view, ct);
        return new ModeReadiness(current, target, conditions.All(c => c.Ok) && blocking.Count == 0, conditions, blocking);
    }

    private async Task<FleetView?> ReadFleetAsync(CancellationToken ct)
    {
        try { return await fleet.ReadAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "The fleet could not be read for the mode check."); return null; }
    }

    // ------------------------------------------------------------------------------------------------ conditions
    private async Task<(List<ModeCondition> Conditions, List<BlockingWork> Blocking)> EvaluateAsync(ExecutionMode current, ExecutionMode target, FleetView? fleetView, CancellationToken ct)
    {
        var c = new List<ModeCondition>();
        var blocking = new List<BlockingWork>();
        if (current == target) { c.Add(new("ALREADY_IN_MODE", false, $"The system is already in {target} mode.")); return (c, blocking); }

        var state = await db.ExecutionModeStates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        c.Add(new("RECOVERY_NOT_REQUIRED", state is null || !state.RecoveryRequired, state is { RecoveryRequired: true } ? $"Recovery is required: {state.RecoveryReason}" : "No recovery pending."));

        if (target == ExecutionMode.Real)
        {
            var o = Real;
            c.Add(new("REAL_DISPATCH_ENABLED", o.Enabled, o.Enabled ? "Rcs:RealDispatch:Enabled is true." : "Rcs:RealDispatch:Enabled is false in the deployment configuration (the deployment does not allow Real AGV)."));
            var global = RealDispatchReadiness.GlobalProblems(o).Where(p => !p.Contains(":Enabled is false", StringComparison.Ordinal)).ToList();
            c.Add(new("REAL_GLOBAL_CONFIG", global.Count == 0, global.Count == 0 ? "Global RCS dispatch configuration is valid." : string.Join(" ", global)));
            var endpoint = await hik.EndpointProblemAsync(ct);
            c.Add(new("RCS_CONNECTION", endpoint is null, endpoint ?? "The RCS connection is saved and switched on."));

            var processProblems = new List<string>(); var locationProblems = new List<string>();
            foreach (var rule in flows.All)
            {
                if (RealDispatchReadiness.ProcessProblem(rule.Flow, o.ProcessOf(rule.Flow)) is { } pp) processProblems.Add(pp);
                var codes = rule.AllowedRequestStations.Concat(rule.SourceGroups.SelectMany(g => g)).Concat(rule.DestinationGroups.SelectMany(g => g)).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var code in codes)
                    if (RealDispatchReadiness.LocationProblem(code, o.Locations.TryGetValue(code, out var m) ? m : null) is { } lp && !locationProblems.Contains(lp)) locationProblems.Add(lp);
            }
            c.Add(new("PROCESS_MAPPING", processProblems.Count == 0, processProblems.Count == 0 ? "Every approved flow has a valid process -> taskTyp mapping." : string.Join(" ", processProblems)));
            c.Add(new("LOCATION_MAPPING", locationProblems.Count == 0, locationProblems.Count == 0 ? "Every location of the approved flows has a valid positionCode." : string.Join(" ", locationProblems)));

            var published = await db.BusinessProcessVersions.AsNoTracking().Where(v => v.State == ProcessConfigState.Published && db.BusinessProcesses.Any(p => p.Id == v.ProcessId && p.Enabled)).Select(v => v.Id).ToListAsync(ct);
            if (published.Count > 0)
            {
                var legTemplate = RealDispatchReadiness.LegTemplateProblem(o);
                c.Add(new("LEG_TEMPLATE", legTemplate is null, legTemplate ?? "The RCS leg template for configurable processes is valid."));
                var stepCodes = await db.RouteSteps.AsNoTracking().Where(st => published.Contains(st.VersionId)).Select(st => st.WaitingPositionCode).ToListAsync(ct);
                var candCodes = await (from cd in db.RouteCandidates.AsNoTracking() join st in db.RouteSteps.AsNoTracking() on cd.StepId equals st.Id where published.Contains(st.VersionId) select cd.LocationCode).ToListAsync(ct);
                var trigger = await db.BusinessProcesses.AsNoTracking().Where(p => p.Enabled).Select(p => p.TriggerLocationCodesCsv).ToListAsync(ct);
                var legProblems = RealDispatchReadiness.LegLocationProblems(o, stepCodes.Concat(candCodes).Concat(trigger.SelectMany(t => (t ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))));
                c.Add(new("PROCESS_LEG_LOCATIONS", legProblems.Count == 0, legProblems.Count == 0 ? "Every location of the published configurable processes has a positionCode." : string.Join(" ", legProblems.Take(5))));
            }
            var cb = o.Callback;
            var cbOk = cb.Enabled && cb.AuthenticationConfigured;
            c.Add(new("CALLBACK_CONFIG", cbOk, cbOk ? "The RCS callback is enabled and authenticated." : "The RCS callback must be enabled with a shared secret and/or allowed source addresses (pick-up and drop confirmations depend on it)."));

            var fleetOk = fleetView is { Source: "Real", SimulatedData: false, Connection: "Connected" };
            c.Add(new("FLEET_REAL_FRESH", fleetOk, fleetOk ? "The real fleet data is fresh." : fleetView is null ? "The fleet could not be read." : $"The fleet is not real and fresh (source {fleetView.Source}, simulated {fleetView.SimulatedData}, connection {fleetView.Connection})."));
        }

        var work = await ActiveWorkAsync(current == ExecutionMode.Real ? ExecutionMode.Real : ExecutionMode.Simulator, current == ExecutionMode.Real, ct);
        blocking.AddRange(work);
        c.Add(new(target == ExecutionMode.Real ? "NO_ACTIVE_SIMULATOR_WORK" : "NO_ACTIVE_REAL_WORK", work.Count == 0,
            work.Count == 0 ? $"No unfinished {current} task or order." : $"{work.Count} unfinished {current} task(s) / order(s) must finish first (nothing is cancelled automatically)."));
        return (c, blocking);
    }

    /// <summary>Unfinished work of the mode that is about to be left; for Real it also lists dispatches whose outcome is unknown or not reconciled.</summary>
    private async Task<List<BlockingWork>> ActiveWorkAsync(ExecutionMode mode, bool includeUnresolved, CancellationToken ct)
    {
        // work created before CR-038 (no pinned mode) blocks BOTH directions: nothing can be sent for it, and it must not be routed to a dispatcher by guesswork
        var tasks = await db.Tasks.AsNoTracking().Where(t => !FinalTask.Contains(t.State) && (t.ExecutionMode == mode || t.ExecutionMode == null))
            .Select(t => new { t.Id, t.RequestKey, t.State, t.RcsDispatchState, t.ExecutionMode }).Take(50).ToListAsync(ct);
        var list = tasks.Select(t => new BlockingWork("Task", t.Id.ToString(), t.RequestKey, t.State.ToString(),
            t.ExecutionMode is null ? "LEGACY: created before CR-038 (no execution mode)" : t.RcsDispatchState is RcsDispatchState.Sending or RcsDispatchState.SyncError ? "RCS outcome not determined" : "Not finished")).ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => !FinalOrder.Contains(o.State) && (o.ExecutionMode == mode || o.ExecutionMode == null))
            .Select(o => new { o.Id, o.RequestKey, o.State, o.ExecutionMode }).Take(50).ToListAsync(ct);
        list.AddRange(orders.Select(o => new BlockingWork("Order", o.Id.ToString(), o.RequestKey, o.State.ToString(), o.ExecutionMode is null ? "LEGACY: created before CR-038 (no execution mode)" : "Not finished")));
        if (includeUnresolved)
        {
            var recon = await db.Tasks.AsNoTracking().Where(t => t.RequiresReconciliation && !t.ReconciliationCompleted && FinalTask.Contains(t.State) && (t.ExecutionMode == ExecutionMode.Real || t.ExecutionMode == null))
                .Select(t => new { t.Id, t.RequestKey, t.State }).Take(50).ToListAsync(ct);
            list.AddRange(recon.Select(t => new BlockingWork("Task", t.Id.ToString(), t.RequestKey, t.State.ToString(), "Physical reconciliation not done")));
        }
        return list;
    }

    // ------------------------------------------------------------------------------------------------ transition
    public async Task<ModeTransitionResult> TransitionAsync(ModeTransitionRequest request, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 64) throw Rejected("REQUEST_ID_REQUIRED", "RequestId is required (at most 64 characters): it makes a repeated click harmless.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) throw Rejected("REASON_REQUIRED", "A reason is required (at most 500 characters).");
        if (!Enum.IsDefined(request.Target)) throw Rejected("MODE_INVALID", "Unknown mode.");
        var requestId = request.RequestId.Trim(); var reason = request.Reason.Trim();

        var fleetView = request.Target == ExecutionMode.Real ? await ReadFleetAsync(ct) : null;   // read BEFORE the transaction (the fleet service runs its own)

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7370032)", ct);
        var repeated = await db.ExecutionModeTransitions.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == requestId, ct);
        if (repeated is not null)
        {
            await tx.CommitAsync(ct);
            var failed = repeated.Succeeded || repeated.Detail is null ? [] : JsonSerializer.Deserialize<List<ModeCondition>>(repeated.Detail, Json) ?? [];
            return new ModeTransitionResult(repeated.Succeeded, true, repeated.FromMode, repeated.ToMode, failed, []);
        }

        var state = await LockedStateAsync(ct);
        var from = state.Mode;
        var (conditions, blocking) = await EvaluateAsync(from, request.Target, fleetView, ct);
        if (request.Target == ExecutionMode.Real && !request.ConfirmRealAgv)
            conditions.Add(new("CONFIRM_REAL_AGV", false, "The operator must confirm that the system may control real AGVs."));
        var notOk = conditions.Where(x => !x.Ok).ToList();

        if (notOk.Count > 0)
        {
            db.ExecutionModeTransitions.Add(ExecutionModeTransition.Create(requestId, from, request.Target, actor, reason, false, JsonSerializer.Serialize(notOk, Json), clock.UtcNow));
            db.AuditLog.Add(Audit(actor, "EXECUTION_MODE_CHANGE", AuditResult.Denied, $"Refused: {from} -> {request.Target}. {string.Join(" | ", notOk.Select(x => x.Code))}. Reason: {reason}", from, request.Target));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ModeTransitionResult(false, false, from, request.Target, notOk, blocking);
        }

        state.SwitchTo(request.Target, actor, clock.UtcNow);
        db.ExecutionModeTransitions.Add(ExecutionModeTransition.Create(requestId, from, request.Target, actor, reason, true, null, clock.UtcNow));
        db.AuditLog.Add(Audit(actor, "EXECUTION_MODE_CHANGE", AuditResult.Success, $"{from} -> {request.Target}. Reason: {reason}", from, request.Target));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        Publish(state);
        logger.LogWarning("Execution mode changed {From} -> {To} by {Actor}: {Reason}. {EventCode}", from, request.Target, actor, reason, "EXECUTION_MODE_CHANGED");
        return new ModeTransitionResult(true, false, from, request.Target, [], []);
    }

    /// <summary>The mode row, locked FOR UPDATE (task creation holds FOR SHARE on it, so none slips in between the check and the switch). Created when missing.</summary>
    private async Task<ExecutionModeState> LockedStateAsync(CancellationToken ct)
    {
        var s = await db.ExecutionModeStates.FromSqlRaw("SELECT * FROM execution_mode_state WHERE \"Id\" = 1 FOR UPDATE").FirstOrDefaultAsync(ct);
        if (s is not null) return s;
        s = ExecutionModeState.Default(clock.UtcNow);
        db.ExecutionModeStates.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    // ------------------------------------------------------------------------------------------------ start-up and recovery
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7370032)", ct);
        var state = await LockedStateAsync(ct);
        if (state.Mode == ExecutionMode.Real && !state.RecoveryRequired)
        {
            var unresolved = await UnresolvedRealAsync(ct);
            if (unresolved.Count > 0)
            {
                state.RequireRecovery($"{unresolved.Count} Real dispatch(es) have no confirmed outcome after the restart (for example {unresolved[0].Code}). Nothing is resent automatically.", clock.UtcNow);
                db.AuditLog.Add(Audit("SYSTEM", "EXECUTION_MODE_RECOVERY_REQUIRED", AuditResult.Success, state.RecoveryReason!, state.Mode, state.Mode));
                logger.LogError("Execution mode RECOVERY_REQUIRED: {Count} unresolved Real dispatch(es). New dispatch is blocked until an Admin reconciles them. {EventCode}", unresolved.Count, "EXECUTION_MODE_RECOVERY_REQUIRED");
                await db.SaveChangesAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
        Publish(state);
    }

    public async Task<ModeTransitionResult> AcknowledgeRecoveryAsync(string requestId, string reason, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw Rejected("REASON_REQUIRED", "A reason is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7370032)", ct);
        var state = await LockedStateAsync(ct);
        if (!state.RecoveryRequired) { await tx.CommitAsync(ct); return new ModeTransitionResult(true, true, state.Mode, state.Mode, [], []); }
        var unresolved = await UnresolvedRealAsync(ct);
        if (unresolved.Count > 0)
        {
            await tx.CommitAsync(ct);
            return new ModeTransitionResult(false, false, state.Mode, state.Mode, [new ModeCondition("UNRESOLVED_REAL_DISPATCH", false, $"{unresolved.Count} Real dispatch(es) are still unresolved.")], unresolved);
        }
        state.ClearRecovery(actor, clock.UtcNow);
        db.AuditLog.Add(Audit(actor, "EXECUTION_MODE_RECOVERY_ACK", AuditResult.Success, $"Recovery acknowledged. Reason: {reason.Trim()}", state.Mode, state.Mode));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        Publish(state);
        return new ModeTransitionResult(true, false, state.Mode, state.Mode, [], []);
    }

    /// <summary>Real tasks whose RCS outcome is not known: a send under way or failed to sync, a dispatch without an RCS answer, or a finished task still to be reconciled physically.</summary>
    private async Task<List<BlockingWork>> UnresolvedRealAsync(CancellationToken ct)
    {
        var rows = await db.Tasks.AsNoTracking().Where(t => (t.ExecutionMode == ExecutionMode.Real || t.ExecutionMode == null)
                && ((!FinalTask.Contains(t.State) && (t.RcsDispatchState == RcsDispatchState.Sending || t.RcsDispatchState == RcsDispatchState.SyncError || (t.State == TaskState.Dispatching && t.RcsTaskCode == null)))
                    || (t.RequiresReconciliation && !t.ReconciliationCompleted)))
            .Select(t => new { t.Id, t.RequestKey, t.State }).Take(50).ToListAsync(ct);
        return rows.Select(t => new BlockingWork("Task", t.Id.ToString(), t.RequestKey, t.State.ToString(), "RCS outcome not determined or not reconciled")).ToList();
    }

    // ------------------------------------------------------------------------------------------------ work created before CR-038
    /// <summary>
    /// Work created before CR-038 has no pinned mode and is sent nowhere. An Admin may pin the UNFINISHED ones to Simulator (never to Real). A task / order that shows signs of having reached the
    /// real RCS (an RCS task code that is not a simulator code) is NOT adopted: it must be finished or reconciled by hand. Nothing is resent, cancelled or changed otherwise.
    /// </summary>
    public async Task<LegacyAdoptionResult> AdoptLegacyAsSimulatorAsync(string reason, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw Rejected("REASON_REQUIRED", "A reason is required (at most 500 characters).");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7370032)", ct);
        var state = await LockedStateAsync(ct);   // also serialises with task creation (FOR SHARE) and with mode changes
        var notAdopted = new List<BlockingWork>();
        var tasks = await db.Tasks.Where(t => t.ExecutionMode == null && !FinalTask.Contains(t.State)).ToListAsync(ct);
        var adoptedTasks = 0;
        foreach (var t in tasks)
        {
            if (t.RcsTaskCode is { Length: > 0 } code && !code.StartsWith("SIM-", StringComparison.Ordinal)) { notAdopted.Add(new BlockingWork("Task", t.Id.ToString(), t.RequestKey, t.State.ToString(), "Has an RCS task code that is not a simulator code: it may exist in the real RCS")); continue; }
            t.PinExecutionMode(ExecutionMode.Simulator); adoptedTasks++;
        }
        var orders = await db.TransportOrders.Where(o => o.ExecutionMode == null && !FinalOrder.Contains(o.State)).ToListAsync(ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var realLooking = (await db.TransportLegs.AsNoTracking().Where(l => orderIds.Contains(l.OrderId) && l.AgvTaskCode != null && !l.AgvTaskCode.StartsWith("SIM-")).Select(l => l.OrderId).Distinct().ToListAsync(ct)).ToHashSet();
        var adoptedOrders = 0;
        foreach (var o in orders)
        {
            if (realLooking.Contains(o.Id)) { notAdopted.Add(new BlockingWork("Order", o.Id.ToString(), o.RequestKey, o.State.ToString(), "A leg has an RCS task code that is not a simulator code: it may exist in the real RCS")); continue; }
            o.PinExecutionMode(ExecutionMode.Simulator); adoptedOrders++;
        }
        db.AuditLog.Add(Audit(actor, "EXECUTION_MODE_ADOPT_LEGACY", AuditResult.Success, $"Adopted as Simulator: {adoptedTasks} task(s), {adoptedOrders} order(s); not adopted: {notAdopted.Count}. Reason: {reason.Trim()}", state.Mode, ExecutionMode.Simulator));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new LegacyAdoptionResult(adoptedTasks, adoptedOrders, notAdopted);
    }

    // ------------------------------------------------------------------------------------------------ helpers
    private AuditEntry Audit(string actor, string action, AuditResult result, string detail, ExecutionMode from, ExecutionMode to) =>
        new(clock.UtcNow, actor, actor == "SYSTEM" ? "System" : "Admin", action, "ExecutionMode", to.ToString(), result, detail, JsonSerializer.Serialize(new { mode = from.ToString() }, Json), JsonSerializer.Serialize(new { mode = to.ToString() }, Json), null);

    private void Publish(ExecutionModeState s) => provider.Update(new ExecutionModeSnapshot(s.Mode, s.IsExplicit, s.RecoveryRequired, s.RecoveryReason, s.UpdatedAt, s.UpdatedBy));

    private static WCS.Application.Common.AppException Rejected(string code, string message) => WCS.Application.Common.AppException.Rejected(code, message);
}
