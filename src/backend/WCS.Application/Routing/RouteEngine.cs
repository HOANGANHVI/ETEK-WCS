using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.RealDispatch;
using WCS.Application.Common;
using WCS.Application.Diagnostics;
using WCS.Application.Fleet;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

public interface IRouteEngine
{
    Task<StartOrderResult> StartAsync(StartOrderCommand cmd, CancellationToken ct = default);
    /// <summary>Re-evaluates one order (creates the next leg, re-checks waiting conditions). Idempotent.</summary>
    Task AdvanceAsync(Guid orderId, CancellationToken ct = default);
    /// <summary>One scheduler pass: advance every open order and dispatch every leg that still needs an AGV. Idempotent; also the WCS-restart recovery path.</summary>
    Task<int> RunOnceAsync(CancellationToken ct = default);
    Task DispatchLegAsync(Guid legId, CancellationToken ct = default);
    Task<EventResult> ConfirmPickAsync(Guid legId, ConfirmationSource source, string? user, string? eventKey = null, CancellationToken ct = default);
    Task<EventResult> ConfirmDropAsync(Guid legId, ConfirmationSource source, string? user, string? externalRackId = null, CancellationToken ct = default);
    Task<OrderDto> CancelAsync(Guid orderId, string reason, string? user, CancellationToken ct = default);
}

/// <summary>
/// Executes a published Business Process version (CR-010). The engine knows no location name: sources, destinations, priorities and conditions all come
/// from the process snapshot stored on the order. It drives Transport Task -> Leg -> Station Transaction, keeps inventory consistent with the confirmed physical
/// events only, and never changes <c>TaskStateMachine</c> or <c>InventoryService</c> (legs have their own reservations through the same tables).
/// External calls (AGV dispatch) happen outside database transactions.
/// </summary>
public sealed class RouteEngine(
    IUnitOfWork uow, IRoutingRepository routing, ILocationRepository locations, IRackRepository racks, IReservationRepository reservations,
    IInventoryTransactionRepository inventoryTx, IAgvRepository agvs, ITransportTaskRepository legacyTasks, RouteConditionEvaluator eval,
    StationTransactionService stations, ILegDispatcher dispatcher, IRcsDispatchGuard guard, IMaintenanceGate maintenance, IExecutionModeGate modeGate, IClock clock, IWcsNotifier notifier,
    IOptions<DispatchOptions> dispatchOptions, IOptions<RouteEngineOptions> routeOptions, ModelGuard modelGuard, ModelGuardThrottle guardThrottle, RackPlacement placement, RcsRobotMapper robotMapper, LegAssignmentThrottle legThrottle, IOptions<RealDispatchOptions> realOptions, ProcessValidator validator, ILogger<RouteEngine> logger) : IRouteEngine
{
    private const string Wcs = "WCS";
    private const int MaxStepsPerAdvance = 50;

    private static RackStatus StatusOf(RackKind k) => k == RackKind.Full ? RackStatus.Full : RackStatus.Empty;
    private static AgvRole RoleOf(RackKind k) => k == RackKind.Full ? AgvRole.FullRack : AgvRole.EmptyRack;

    private void Event(TransportOrder o, TransportLeg? l, Guid? tx, string kind, string? detail, string source = Wcs) =>
        routing.Add(new RoutingEvent(o.Id, l?.Id, tx, kind, detail is { Length: > 1000 } ? detail[..1000] : detail, source, clock.UtcNow));

    /// <summary>CR-036: writes what the model guards found during this evaluation as routing events (rate limited) and clears the findings.</summary>
    private void FlushGuardEvents(TransportOrder order)
    {
        foreach (var f in modelGuard.Findings)
            if (guardThrottle.ShouldEmit($"{order.Id}|{f.Code}|{f.Detail}", clock.UtcNow))
                Event(order, null, null, "MODEL_GUARD", $"{modelGuard.Mode}: {f.Code} - {f.Detail}");
        modelGuard.ClearFindings();
    }

    // ================================================================================ start

    public async Task<StartOrderResult> StartAsync(StartOrderCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.RequestKey)) throw AppException.Validation("REQUEST_KEY_REQUIRED", "RequestKey is required.");
        var location = await locations.GetByCodeAsync(cmd.RequestLocationCode, ct)
                       ?? throw AppException.NotFound("LOCATION_NOT_FOUND", $"Location '{cmd.RequestLocationCode}' not found.");

        var id = await uow.ExecuteAsync(async t =>
        {
            if (await routing.GetOrderByRequestKeyAsync(cmd.RequestKey.Trim(), t) is { } existing) return (Guid?)null;   // idempotent
            await maintenance.EnsureOpenAsync(t);   // CR-026 (OQ-1): no new Order during Maintenance (row-locked read in this transaction)
            var mode = await modeGate.ModeForNewWorkAsync(t);   // CR-038
            var (process, version) = await SelectProcessAsync(cmd, location.Code, t);
            var snapshot = ProcessSnapshot.Of(process, version);
            if (mode == WCS.Domain.Rcs.ExecutionMode.Real)
            {
                // CR-038: a Real order is accepted only when EVERY leg it may need can be sent (template + a positionCode for each location of the process): never half sent, then found unsupported
                var problems = new List<string>();
                if (RealDispatchReadiness.LegTemplateProblem(realOptions.Value) is { } tp) problems.Add(tp);
                var codes = snapshot.Steps.SelectMany(st => st.Candidates.Select(c => c.LocationCode).Append(st.WaitingPositionCode)).Append(location.Code);
                problems.AddRange(RealDispatchReadiness.LegLocationProblems(realOptions.Value, codes));
                if (problems.Count > 0) throw AppException.Rejected("REAL_LEG_MAPPING_INCOMPLETE", "This process cannot be sent to the real RCS yet; nothing was created or sent. " + string.Join(" ", problems.Take(3)));
            }
            var order = TransportOrder.Create(cmd.RequestKey, snapshot, location.Code, cmd.RequestedBy, clock.UtcNow);
            order.PinExecutionMode(mode);   // CR-038: the order (and its legs) keep this dispatcher for their whole life
            order.SetCorrelation(DiagnosticContext.CorrelationId ?? DiagnosticContext.NewCorrelationId());   // CR-021
            CorrelationCache.Remember(order.Id, order.CorrelationId);
            routing.Add(order);
            if (cmd.TransactionId is { } txId) await stations.LinkToOrderAsync(txId, order.Id, t);
            Event(order, null, null, "ORDER_CREATED", $"process={process.Code} v{version.Number}");
            return order.Id;
        }, ct);

        if (id is { } oid)
        {
            await AdvanceAsync(oid, ct);
            return new StartOrderResult(await GetDtoAsync(oid, ct), true);
        }
        var dupId = await uow.ExecuteAsync(async t => (await routing.GetOrderByRequestKeyAsync(cmd.RequestKey.Trim(), t))!.Id, ct);
        logger.LogInformation("Duplicate request {RequestKey} returned existing order {OrderId}", cmd.RequestKey, dupId);
        return new StartOrderResult(await GetDtoAsync(dupId, ct), false);
    }

    /// <summary>CR-039: for a process that requires Readiness the device conditions are checked at intake too (an approval can be withdrawn after Ready was set).</summary>
    private async Task<string?> UnmetDeviceConditionAsync(BusinessProcess p, BusinessProcessVersion v, CancellationToken ct)
    {
        if (!p.RequiresReadiness) return null;
        var full = await routing.GetVersionAsync(v.Id, ct) ?? v;
        return (await validator.DeviceConditionsAsync(p, full, ct)).FirstOrDefault()?.Message;
    }

    private async Task<(BusinessProcess, BusinessProcessVersion)> SelectProcessAsync(StartOrderCommand cmd, string locationCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cmd.ProcessCode))
        {
            var p = await routing.GetProcessByCodeAsync(cmd.ProcessCode.Trim().ToUpperInvariant(), ct)
                    ?? throw AppException.NotFound("PROCESS_NOT_FOUND", $"Process '{cmd.ProcessCode}' not found.");
            if (!p.Enabled) throw AppException.Rejected("PROCESS_DISABLED", $"Process {p.Code} is disabled.");
            if (p.ReviewBlocked) throw AppException.Rejected("PROCESS_REVIEW_BLOCKED", $"Process {p.Code} is REVIEW and not approved for execution.");
            var v = await routing.GetPublishedVersionAsync(p.Id, ct) ?? throw AppException.Rejected("PROCESS_NOT_PUBLISHED", $"Process {p.Code} has no published version.");
            if ((routeOptions.Value.EnforceReadiness || p.RequiresReadiness) && v.Readiness != OperationalReadiness.Ready)
                throw AppException.Rejected("PROCESS_NOT_READY", $"Process {p.Code} v{v.Number} is Published but its Operational Readiness is {v.Readiness}; the request is not accepted.");   // CR-035 D-12, CR-039
            if (await UnmetDeviceConditionAsync(p, v, ct) is { } unmet)
                throw AppException.Rejected("PROCESS_NOT_READY", $"Process {p.Code} v{v.Number} cannot accept a request: {unmet}");
            if (!p.TriggerLocationCodes.Contains(locationCode, StringComparer.OrdinalIgnoreCase))
                throw AppException.Rejected("LOCATION_NOT_ALLOWED", $"Location {locationCode} cannot start process {p.Code}.");
            return (p, v);
        }
        var found = await routing.FindPublishedAsync(cmd.RequestType, cmd.RackKind, locationCode, ct);
        found = found.Where(x => !x.Process.ReviewBlocked).ToList();
        if (found.Count > 0 && (routeOptions.Value.EnforceReadiness || found.Any(x => x.Process.RequiresReadiness)))
        {   // CR-035 D-12 / CR-039: a process that needs Readiness is matched only when Published + Ready; no other version is substituted
            var ready = found.Where(x => x.Version.Readiness == OperationalReadiness.Ready || !(routeOptions.Value.EnforceReadiness || x.Process.RequiresReadiness)).ToList();
            if (ready.Count == 0) throw AppException.Rejected("PROCESS_NOT_READY", $"A process answers a {cmd.RequestType} request at {locationCode} but none is Ready; the request is not accepted.");
            found = ready;
        }
        if (found.Count == 1 && await UnmetDeviceConditionAsync(found[0].Process, found[0].Version, ct) is { } why)
            throw AppException.Rejected("PROCESS_NOT_READY", $"Process {found[0].Process.Code} cannot accept a request: {why}");
        return found.Count switch
        {
            0 => throw AppException.Rejected("PROCESS_NOT_FOUND", $"No published process answers a {cmd.RequestType} request at {locationCode}."),
            1 => (found[0].Process, found[0].Version),
            _ => throw AppException.Rejected("PROCESS_AMBIGUOUS", $"More than one published process answers a {cmd.RequestType} request at {locationCode}; give an explicit process code."),
        };
    }

    // ================================================================================ diagnostics (CR-021)
    // (the caller enters the frame itself: an AsyncLocal set inside an async helper would not reach the caller)
    private async Task<string?> CorrelationOfOrderAsync(Guid orderId, CancellationToken ct) =>
        await CorrelationCache.GetAsync(orderId, async () => (await routing.GetOrderAsync(orderId, ct))?.CorrelationId);

    private async Task<string?> CorrelationOfLegAsync(Guid legId, CancellationToken ct)
    {
        var leg = await routing.GetLegAsync(legId, ct);
        return leg is null ? null : await CorrelationOfOrderAsync(leg.OrderId, ct);
    }

    // ================================================================================ advance

    public async Task AdvanceAsync(Guid orderId, CancellationToken ct = default)
    {
        using var _frame = DiagnosticContext.Begin(await CorrelationOfOrderAsync(orderId, ct));   // CR-021
        var events = new List<WcsEvent>();
        var continues = new List<(string RequestCode, string? TaskCode, string Destination)>();
        await uow.ExecuteAsync(async t =>
        {
            events.Clear(); continues.Clear();
            var order = await routing.GetOrderAsync(orderId, t);
            if (order is null || order.IsTerminal) return 0;
            await AdvanceCoreAsync(order, continues, t);
            events.Add(new WcsEvent("OrderChanged", new { order.Id, State = order.State.ToString() }));
            return 0;
        }, ct);
        foreach (var c in continues) await TryContinueAsync(c.RequestCode, c.TaskCode, c.Destination, ct);
        await notifier.PublishAsync(events, ct);
        await DispatchPendingAsync(ct);
    }

    /// <summary>The state-machine loop of one order. Runs inside a unit of work; every external effect is only collected.</summary>
    private async Task AdvanceCoreAsync(TransportOrder order, List<(string, string?, string)> continues, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var snap = order.Snapshot;
        if (order.State == OrderState.ReconciliationRequired) return;       // only an Admin decision leaves this state
        if (order.State == OrderState.Created) order.TransitionTo(OrderState.Accepted, "Accepted", WaitKind.None, now);

        for (var guardCount = 0; guardCount < MaxStepsPerAdvance; guardCount++)
        {
            var step = snap.Step(order.CurrentStepKey);
            if (step is null) { FailOrder(order, "STEP_NOT_FOUND"); return; }

            switch (step.Type)
            {
                case RouteStepType.ProcessCompletion:
                    order.TransitionTo(OrderState.Completed, "Process completed", WaitKind.None, now);
                    Event(order, null, null, "ORDER_COMPLETED", null);
                    return;

                case RouteStepType.Condition:
                {
                    var ok = await EvaluateStepConditionsAsync(step, order.RackKind, ct);
                    var next = ok ? step.NextStepKey : step.AlternativeNextStepKey;
                    Event(order, null, null, "CONDITION_EVALUATED", $"{step.StepKey}={ok} -> {next}");
                    order.MoveToStep(next, now);
                    continue;
                }

                case RouteStepType.Waiting:
                {
                    if (await EvaluateStepConditionsAsync(step, order.RackKind, ct))
                    {
                        order.MoveToStep(step.NextStepKey, now);
                        if (order.State == OrderState.Waiting) order.TransitionTo(OrderState.InProgress, "Waiting condition met", WaitKind.None, now);
                        Event(order, null, null, "WAIT_CONDITION_MET", step.StepKey);
                        continue;
                    }
                    var lastDest = order.Legs.Where(l => l.State == LegState.Completed).OrderByDescending(l => l.Sequence).FirstOrDefault()?.DestinationLocationCode;
                    var atBuffer = lastDest is not null && await locations.GetByCodeAsync(lastDest, ct) is { Type: LocationType.Buffer or LocationType.ManualBuffer };
                    if (order.State != OrderState.Waiting || order.WaitKind != (atBuffer ? WaitKind.AtBuffer : WaitKind.ConditionNotMet))
                        Event(order, null, null, "ORDER_WAITING", $"{step.StepKey}: {(atBuffer ? "rack stored at buffer " + lastDest : "condition not met")}");
                    order.TransitionTo(OrderState.Waiting, $"Waiting at {step.StepKey}", atBuffer ? WaitKind.AtBuffer : WaitKind.ConditionNotMet, now);
                    return;
                }

                default: // movement step
                {
                    if (order.Legs.FirstOrDefault(l => l.StepKey == step.StepKey && l.State == LegState.Completed) is not null)
                    { order.MoveToStep(step.NextStepKey, now); continue; }

                    var leg = order.Legs.FirstOrDefault(l => l.StepKey == step.StepKey && !l.IsTerminal);
                    if (leg is null)
                    {
                        // CR-026: the FIRST leg of an order (nothing has started yet) is not created during Maintenance; the next legs of an order that already runs are, so it can finish
                        if (order.Legs.Count == 0 && await maintenance.HoldNewWorkAsync(ct)) return;
                        var outcome = await CreateLegAsync(order, step, ct);
                        if (outcome == LegCreation.Continue) continue;   // alternative taken
                        if (outcome != LegCreation.Created) return;      // waiting / failed / reconciliation
                        return;                                           // leg created; dispatch happens next
                    }
                    await ProgressExistingLegAsync(order, leg, step, continues, ct);
                    return;
                }
            }
        }
        FailOrder(order, "ROUTE_STEP_LIMIT");   // defence in depth: validation already refuses loops
    }

    private async Task<bool> EvaluateStepConditionsAsync(StepSnapshot step, RackKind kind, CancellationToken ct)
    {
        foreach (var c in step.Conditions)
            if (c.LocationCode is null || !await eval.EvaluateAsync(c, c.LocationCode, kind, ct)) return false;
        return true;
    }

    private void FailOrder(TransportOrder order, string reason)
    {
        order.TransitionTo(OrderState.Failed, reason, WaitKind.None, clock.UtcNow);
        Event(order, null, null, "ORDER_FAILED", reason);
    }

    private enum LegCreation { Created, Waiting, Failed, Reconciliation, Continue }

    // -------------------------------------------------------------------------------- leg creation

    private async Task<LegCreation> CreateLegAsync(TransportOrder order, StepSnapshot step, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var kind = order.RackKind;
        modelGuard.ClearFindings();
        var mc = new ModelContext(order.Snapshot.ProductModelId);   // CR-036
        var sel = modelGuard.SelectionFor(mc.ProcessModelId);        // null unless Mode = Enforce

        // ---- source
        string? sourceCode = null;
        Rack? rack = null;
        switch (step.SourceMode)
        {
            case SourceMode.Request:
                sourceCode = order.RequestLocationCode;
                if (!await eval.SourceUsableAsync(sourceCode, kind, [], order.Id, ct, mc)) { return await NoCandidateAsync(order, step, WaitKind.SourceUnavailable, "No usable rack at the request location", ct); }
                break;
            case SourceMode.Candidates:
                foreach (var c in step.Sources)
                    if (await eval.SourceUsableAsync(c.LocationCode, kind, c.Conditions, order.Id, ct, mc)) { sourceCode = c.LocationCode; break; }
                if (sourceCode is null) return await NoCandidateAsync(order, step, WaitKind.SourceUnavailable, "No usable source candidate", ct);
                break;
            case SourceMode.PreviousDestination:
            {
                var prev = order.Legs.Where(l => l.State == LegState.Completed).OrderByDescending(l => l.Sequence).FirstOrDefault();
                if (prev?.DestinationLocationCode is null || prev.RackId is null) { FailOrder(order, "NO_PREVIOUS_LEG"); return LegCreation.Failed; }
                sourceCode = prev.DestinationLocationCode;
                var storedRack = await racks.GetAsync(prev.RackId.Value, ct);
                var prevLoc = await locations.GetByCodeAsync(sourceCode, ct);
                var guardCode = storedRack is not null && modelGuard.On ? ModelGuard.RackViolation(storedRack, mc.ProcessModelId) : null;   // CR-036
                if (guardCode is not null) modelGuard.Report(guardCode, $"rack {storedRack!.Id} at previous destination {sourceCode}");
                if (storedRack is null || prevLoc is null || storedRack.CurrentLocationId != prevLoc.Id || storedRack.Status == RackStatus.Unknown || (modelGuard.Enforcing && guardCode is not null))
                {
                    // the rack is not where WCS stored it (or it may not be used automatically): never choose another position - reconcile
                    FlushGuardEvents(order);
                    order.TransitionTo(OrderState.ReconciliationRequired, modelGuard.Enforcing && guardCode is not null ? guardCode : "RACK_NOT_AT_PREVIOUS_DESTINATION", WaitKind.None, now);
                    Event(order, null, null, "RECONCILIATION_REQUIRED", $"rack {prev.RackId} is not at {sourceCode}");
                    return LegCreation.Reconciliation;
                }
                rack = storedRack;
                break;
            }
        }

        var srcLoc = (await locations.GetByCodeAsync(sourceCode!, ct))!;
        rack ??= sel is null ? await racks.FindUnreservedAsync(srcLoc.Id, StatusOf(kind), ct) : await racks.FindUnreservedAsync(srcLoc.Id, StatusOf(kind), sel, ct);   // CR-036: filtered only when enforcing
        if (rack is null) return await NoCandidateAsync(order, step, WaitKind.SourceUnavailable, $"No unreserved {kind} rack at {sourceCode}", ct);

        if (modelGuard.Mode == ModelIsolationMode.Warn && ModelGuard.RackViolation(rack, mc.ProcessModelId) is { } warnCode) modelGuard.Report(warnCode, $"rack {rack.Id} at {sourceCode}");

        // ---- destination (BeforePick) or deferred (AfterPick)
        string? destCode = null; bool noBind = false;
        if (step.Resolution == DestinationResolution.BeforePick)
        {
            (destCode, noBind) = await ChooseDestinationAsync(order, step, kind, ct);
            if (destCode is null) return await NoCandidateAsync(order, step, WaitKind.DestinationUnavailable, "No usable destination", ct);
        }

        var leg = TransportLeg.Create(order.Id, order.NextLegSequence(), step.StepKey, step.Type, sourceCode, destCode, kind, noBind, now);
        leg.BindRack(rack.Id, now);
        routing.Add(leg);
        order.Legs.Add(leg);
        reservations.Add(Reservation.ForSourceRack(leg.Id, rack.Id, srcLoc.Id, now));
        srcLoc.Touch(now);
        if (destCode is not null) await ReserveDestinationAsync(leg, destCode, ct);
        FlushGuardEvents(order);   // CR-036 (Warn findings of a leg that was created)
        leg.TransitionTo(LegState.Ready, "Leg ready for dispatch", WaitKind.None, now);
        order.TransitionTo(OrderState.InProgress, $"Leg {leg.Sequence} created", WaitKind.None, now);
        Event(order, leg, null, "LEG_CREATED", $"{leg.SourceLocationCode} -> {leg.DestinationLocationCode ?? "(after pick)"}");
        return LegCreation.Created;
    }

    private async Task<(string? Code, bool NoBind)> ChooseDestinationAsync(TransportOrder order, StepSnapshot step, RackKind kind, CancellationToken ct)
    {
        if (step.DestinationMode == DestinationMode.Request)
            return await eval.DestinationUsableAsync(order.RequestLocationCode, kind, [], order.Id, ct, new ModelContext(order.Snapshot.ProductModelId)) ? (order.RequestLocationCode, false) : (null, false);
        foreach (var c in step.Destinations)   // already ordered by priority then order; ONLY declared candidates are ever considered
            if (await eval.DestinationUsableAsync(c.LocationCode, kind, c.Conditions, order.Id, ct, new ModelContext(order.Snapshot.ProductModelId))) return (c.LocationCode, c.NoBind);
        return (null, false);
    }

    private async Task ReserveDestinationAsync(TransportLeg leg, string destCode, CancellationToken ct)
    {
        var loc = (await locations.GetByCodeAsync(destCode, ct))!;
        reservations.Add(Reservation.ForDestination(leg.Id, loc.Id, clock.UtcNow));
        loc.Touch(clock.UtcNow);   // concurrent allocations of the last slot conflict on the location row version and one of them re-evaluates
    }

    /// <summary>Applies the step's WaitBehavior when no candidate can be used.</summary>
    private Task<LegCreation> NoCandidateAsync(TransportOrder order, StepSnapshot step, WaitKind wait, string detail, CancellationToken ct)
    {
        var now = clock.UtcNow;
        FlushGuardEvents(order);   // CR-036: why candidates were excluded (model / manual QC)
        switch (step.WaitBehavior)
        {
            case WaitBehavior.Fail:
                FailOrder(order, "NO_CANDIDATE_AVAILABLE");
                return Task.FromResult(LegCreation.Failed);
            case WaitBehavior.Alternative when step.AlternativeNextStepKey is not null:
                Event(order, null, null, "ALTERNATIVE_STEP", $"{step.StepKey} -> {step.AlternativeNextStepKey}: {detail}");
                order.MoveToStep(step.AlternativeNextStepKey, now);
                return Task.FromResult(LegCreation.Continue);
            default:
                if (order.State != OrderState.Waiting || order.WaitKind != wait) Event(order, null, null, "ORDER_WAITING", $"{step.StepKey}: {detail}");
                order.TransitionTo(OrderState.Waiting, detail, wait, now);
                return Task.FromResult(LegCreation.Waiting);
        }
    }

    // -------------------------------------------------------------------------------- existing leg

    private async Task ProgressExistingLegAsync(TransportOrder order, TransportLeg leg, StepSnapshot step, List<(string, string?, string)> continues, CancellationToken ct)
    {
        // A leg whose rack is on the AMR and that still has no destination re-evaluates its candidates (WAITING_FOR_DESTINATION semantics).
        if (leg.PickConfirmedAt is not null && leg.DestinationLocationCode is null)
        {
            var (code, noBind) = await ChooseDestinationAsync(order, step, leg.RackKind, ct);
            if (code is null) { HoldWithRack(order, leg, step); return; }
            leg.SetDestination(code, noBind, clock.UtcNow);
            await ReserveDestinationAsync(leg, code, ct);
            await stations.OpenForLegAsync(order, leg, code, StationOperation.Delivery, ct);
            if (leg.State == LegState.Waiting) leg.TransitionTo(LegState.InProgress, "Destination assigned", WaitKind.None, clock.UtcNow);
            if (order.State == OrderState.Waiting) order.TransitionTo(OrderState.InProgress, "Destination assigned", WaitKind.None, clock.UtcNow);
            Event(order, leg, null, "DESTINATION_ASSIGNED", code);
            continues.Add((leg.AgvRequestCode, leg.AgvTaskCode, code));
        }
    }

    /// <summary>The AMR holds the rack and no destination is usable: nothing is stored anywhere, the AGV stays committed, inventory keeps the rack IN_TRANSIT.</summary>
    private void HoldWithRack(TransportOrder order, TransportLeg leg, StepSnapshot step)
    {
        var now = clock.UtcNow;
        var detail = step.WaitingPositionCode is { } wp ? $"No destination; AMR waits at {wp} holding the rack" : "No destination; AMR holds the rack";
        if (leg.State != LegState.Waiting || leg.WaitKind != WaitKind.DestinationUnavailable_AgvHoldingRack) Event(order, leg, null, "WAITING_FOR_DESTINATION", detail);
        leg.TransitionTo(LegState.Waiting, detail, WaitKind.DestinationUnavailable_AgvHoldingRack, now);
        order.TransitionTo(OrderState.Waiting, detail, WaitKind.DestinationUnavailable_AgvHoldingRack, now);
    }

    // ================================================================================ dispatch (PER_AGV)

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var ids = await uow.ExecuteAsync(t => routing.ListOpenOrderIdsAsync(200, t), ct);
        foreach (var id in ids) await AdvanceAsync(id, ct);
        await SyncLegRobotsAsync(ct);
        return ids.Count;
    }

    /// <summary>
    /// CR-028 (RcsManaged): asks the RCS which AMR carries each leg that was sent (queryTaskStatus agvCode) and records it on THAT leg (`RobotCode`, `AssignedAt`). The robot of the Transport Task / Order is never
    /// copied to a leg, AgvId is never set (PER_AGV does not apply), AGV1/AGV2 are never assumed, and a code that is not in the fleet read model is kept and flagged (no AGV is created).
    /// A different AMR reported while the rack is on the AMR is NOT followed (no approved hand-over): the event LEG_ROBOT_CHANGE_REFUSED is recorded and the leg keeps its robot.
    /// </summary>
    public async Task<int> SyncLegRobotsAsync(CancellationToken ct = default)
    {
        if (!dispatchOptions.Value.IsRcsManaged) return 0;
        var o = dispatchOptions.Value; var now = clock.UtcNow; var changed = 0;
        var ids = await uow.ExecuteAsync(t => routing.ListManagedLegIdsToSyncAsync(200, t), ct);
        foreach (var legId in ids)
        {
            var plan = await uow.ExecuteAsync<(string? RobotCode, string AgvRequestCode)?>(async t =>
            {
                var leg = await routing.GetLegAsync(legId, t);
                if (leg is null || leg.IsTerminal || leg.AgvTaskCode is null) return null;
                var ord = await routing.GetOrderAsync(leg.OrderId, t);
                if (ord is null || ord.IsTerminal || ord.State == OrderState.ReconciliationRequired) return null;   // an Order under reconciliation: nothing is updated automatically
                return (leg.RobotCode, leg.AgvRequestCode);
            }, ct);
            if (plan is null) { legThrottle.Forget(legId); continue; }
            var pendingConfirmation = legThrottle.LastRefused(legId) is not null;   // a different AMR was reported: ask again soon to confirm or clear it
            var every = TimeSpan.FromSeconds(Math.Max(1, plan.Value.RobotCode is null || pendingConfirmation ? o.AssignmentCheckSeconds : o.AssignedRecheckSeconds));
            if (!legThrottle.IsDue(legId, now, every)) continue;
            using var _frame = DiagnosticContext.Begin(await CorrelationOfLegAsync(legId, ct));
            RcsTaskInfo? info = null; string? error = null;
            try { info = await dispatcher.QueryAsync(plan.Value.AgvRequestCode, ct); }   // external call outside any transaction
            catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; }
            var failed = info is null || info.Status is RcsTaskStatus.Unknown or RcsTaskStatus.NotFound;
            var wasFailing = legThrottle.WasFailing(legId); var lastRefused = legThrottle.LastRefused(legId); var countBefore = legThrottle.RefusedCount(legId); var completedNoted = legThrottle.CompletedNoted(legId);
            string? refused = null; var refusedCount = 0; var needed = Math.Max(2, o.RobotChangeConfirmChecks);
            var map = failed ? null : await uow.ExecuteAsync(t => robotMapper.MapAsync(info!.AgvCode, t), ct);
            var did = await uow.ExecuteAsync(async t =>
            {
                var leg = await routing.GetLegAsync(legId, t);
                if (leg is null || leg.IsTerminal) return false;
                var order = (await routing.GetOrderAsync(leg.OrderId, t))!;
                if (order.IsTerminal || order.State == OrderState.ReconciliationRequired) return false;
                if (!failed && info!.Status == RcsTaskStatus.Completed && leg.DropConfirmedAt is null && !completedNoted)
                {   // the RCS says the task is done, but the drop was NOT confirmed: that is not a hand-over. Nothing advances; the leg waits for the confirmed drop (see CR-028 section 13)
                    Event(order, leg, null, "LEG_RCS_COMPLETED_WITHOUT_DROP", "The RCS reports the task completed; the drop of the rack has not been confirmed. The leg is not completed and the next leg is not dispatched.", "RCS");
                    completedNoted = true;
                }
                if (failed)
                {
                    if (!wasFailing) Event(order, leg, null, "LEG_ROBOT_SYNC_ERROR", info?.Status.ToString() ?? error, "RCS");   // a synchronisation problem, NOT a failed leg
                    return false;
                }
                var previous = leg.RobotCode;
                switch (leg.RecordRobot(info!.AgvCode, clock.UtcNow))
                {
                    case LegRobotOutcome.Assigned:
                        Event(order, leg, null, "LEG_ROBOT_ASSIGNED", leg.RobotCode, "RCS");
                        if (!map!.InFleet) Event(order, leg, null, "LEG_ROBOT_NOT_IN_FLEET", leg.RobotCode, "RCS");
                        await stations.MarkAgvAssignedForLegAsync(leg.Id, t);
                        // information only: legs of one order are separate RCS tasks; say when the AMR differs from the one of the previous leg (no action is taken)
                        var before = order.Legs.Where(l => l.Sequence < leg.Sequence && l.RobotCode is not null).OrderByDescending(l => l.Sequence).FirstOrDefault();
                        if (before is not null && before.RobotCode != leg.RobotCode)
                            Event(order, leg, null, "LEG_ROBOT_DIFFERS_FROM_PREVIOUS_LEG", $"leg {before.Sequence}: {before.RobotCode} -> leg {leg.Sequence}: {leg.RobotCode}", "RCS");
                        return true;
                    case LegRobotOutcome.Changed:
                        Event(order, leg, null, "LEG_ROBOT_CHANGED", $"{previous} -> {leg.RobotCode}", "RCS");
                        if (!map!.InFleet) Event(order, leg, null, "LEG_ROBOT_NOT_IN_FLEET", leg.RobotCode, "RCS");
                        return true;
                    case LegRobotOutcome.RefusedRackOnAmr:
                        refused = info.AgvCode;
                        refusedCount = (lastRefused == refused ? countBefore : 0) + 1;
                        // 1st sighting: a WARNING only (the answer may be inconsistent / transient). The robot, the rack and the hand-over are not touched.
                        if (refusedCount == 1) Event(order, leg, null, "LEG_ROBOT_CHANGE_REFUSED", $"rack is on {leg.RobotCode}; the RCS reports {refused}. Not followed (no hand-over is approved); it is checked again", "RCS");
                        if (refusedCount >= needed)
                        {   // the RCS repeats the same different AMR while the rack is on the old one: that is the confirmation. Stop, do not guess: an Admin reconciles with the physical reality.
                            Event(order, leg, null, "LEG_ROBOT_CHANGE_CONFIRMED", $"the RCS reported {refused} in {refusedCount} consecutive queries while the rack is on {leg.RobotCode}", "RCS");
                            order.TransitionTo(OrderState.ReconciliationRequired, "RACK_ON_AMR_RCS_REPORTS_OTHER_AMR", WaitKind.None, clock.UtcNow);
                            Event(order, null, null, "RECONCILIATION_REQUIRED", $"leg {leg.Sequence}: the rack is on {leg.RobotCode} but the RCS reports {refused}; RobotCode, rack position and hand-over are NOT updated", "RCS");
                            return true;
                        }
                        return false;
                    default: return false;
                }
            }, ct);
            if (failed) { refused = lastRefused; refusedCount = countBefore; }   // an inconclusive query neither confirms nor clears a sighting
            legThrottle.Checked(legId, now, failed, refused, refusedCount, completedNoted);
            if (did) { changed++; await notifier.PublishAsync([new WcsEvent("OrderChanged", new { legId })], ct); }
        }
        return changed;
    }

    private async Task DispatchPendingAsync(CancellationToken ct)
    {
        var legIds = await uow.ExecuteAsync(t => routing.ListReadyLegIdsAsync(50, t), ct);
        foreach (var id in legIds) await DispatchLegAsync(id, ct);
    }

    public async Task DispatchLegAsync(Guid legId, CancellationToken ct = default)
    {
        using var _frame = DiagnosticContext.Begin(await CorrelationOfLegAsync(legId, ct));   // CR-021
        var link = guard.Check();
        var plan = await uow.ExecuteAsync(async t =>
        {
            var leg = await routing.GetLegAsync(legId, t);
            if (leg is null || leg.IsTerminal || leg.AgvTaskCode is not null) return null;
            if (leg.State is not (LegState.Ready or LegState.InProgress)) return null;
            var order = (await routing.GetOrderAsync(leg.OrderId, t))!;
            if (order.IsTerminal || order.State == OrderState.ReconciliationRequired) return null;
            if (leg.Sequence == 1 && await maintenance.HoldNewWorkAsync(t)) return null;   // CR-026: the first leg is not sent to the AGV during Maintenance; later legs of a running order are

            if (!link.Allowed)
            {   // CR-008A: never dispatch where no real AGV link exists; the leg waits, nothing is lost
                if (leg.State == LegState.InProgress && leg.PickConfirmedAt is null) leg.TransitionTo(LegState.Ready, link.Code, WaitKind.None, clock.UtcNow);
                else leg.TransitionTo(leg.State, link.Code, WaitKind.None, clock.UtcNow);
                return null;
            }

            if (dispatchOptions.Value.IsRcsManaged)
            {   // CR-028: WCS does not choose the AMR - no role lookup, no PER_AGV, no dispatch hold, no AGV1/AGV2. The RCS chooses; the leg carries no robot code and no AgvId.
                //         One leg = one independent RCS task (the contract of ILegDispatcher). Only the PORT check stays: a port with another active Station Transaction is a PLC / Station matter.
                if (leg.State == LegState.Ready)
                {
                    if (!await stations.CanOpenAsync(order, leg, leg.SourceLocationCode!, t)
                        || (leg.DestinationLocationCode is { } mdc && !await stations.CanOpenAsync(order, leg, mdc, t)))
                    {
                        if (leg.StateReason != "PORT_BUSY") Event(order, leg, null, "LEG_QUEUED", "A port of the leg has an active Station Transaction");
                        leg.TransitionTo(LegState.Ready, "PORT_BUSY", WaitKind.None, clock.UtcNow);
                        return null;
                    }
                    leg.TransitionTo(LegState.InProgress, "Sent to the RCS (the RCS chooses the AMR)", WaitKind.None, clock.UtcNow);
                    await stations.OpenForLegAsync(order, leg, leg.SourceLocationCode!, StationOperation.Pickup, t, null, false);   // no AMR yet: the transaction is not "AgvAssigned"
                    if (leg.DestinationLocationCode is { } md) await stations.OpenForLegAsync(order, leg, md, StationOperation.Delivery, t, null, false);
                    Event(order, leg, null, "LEG_DISPATCHING", "RCS");
                }
                return new LegDispatchRequest(leg.Id, leg.AgvRequestCode, null, leg.RackKind, leg.SourceLocationCode!, leg.DestinationLocationCode);
            }

            var agv = await agvs.GetByRoleAsync(RoleOf(leg.RackKind), t);
            if (agv is null || !agv.Enabled) { leg.TransitionTo(leg.State, "AGV_UNAVAILABLE", WaitKind.None, clock.UtcNow); return null; }

            if (leg.AgvId is null)
            {   // PER_AGV: busy = a legacy active task, another active leg, or a dispatch hold. The database also refuses two active legs for one AGV.
                var busy = await legacyTasks.GetActiveForAgvAsync(agv.Id, t) is not null
                           || await routing.GetActiveLegForAgvAsync(agv.Id, t) is not null
                           || await agvs.IsOnDispatchHoldAsync(agv.Id, t);
                if (busy)
                {
                    if (leg.StateReason != "AGV_BUSY") Event(order, leg, null, "LEG_QUEUED", "AGV busy (PER_AGV)");
                    leg.TransitionTo(LegState.Ready, "AGV_BUSY", WaitKind.None, clock.UtcNow);
                    return null;
                }
                if (!await stations.CanOpenAsync(order, leg, leg.SourceLocationCode!, t)
                    || (leg.DestinationLocationCode is { } dc && !await stations.CanOpenAsync(order, leg, dc, t)))
                {
                    if (leg.StateReason != "PORT_BUSY") Event(order, leg, null, "LEG_QUEUED", "A port of the leg has an active Station Transaction");
                    leg.TransitionTo(LegState.Ready, "PORT_BUSY", WaitKind.None, clock.UtcNow);
                    return null;
                }
                leg.AssignAgv(agv.Id, clock.UtcNow);
                if (leg.State == LegState.Ready) leg.TransitionTo(LegState.InProgress, "AGV assigned", WaitKind.None, clock.UtcNow);
                await stations.OpenForLegAsync(order, leg, leg.SourceLocationCode!, StationOperation.Pickup, t, agv.Id);
                if (leg.DestinationLocationCode is { } d) await stations.OpenForLegAsync(order, leg, d, StationOperation.Delivery, t, agv.Id);
                Event(order, leg, null, "LEG_DISPATCHING", agv.Code);
            }
            return new LegDispatchRequest(leg.Id, leg.AgvRequestCode, agv.RcsRobotCode, leg.RackKind, leg.SourceLocationCode!, leg.DestinationLocationCode);
        }, ct);
        if (plan is null) return;

        LegDispatchResult result;
        try { result = await dispatcher.DispatchAsync(plan, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Leg dispatch failed for {LegId}", legId);
            result = new LegDispatchResult(LegDispatchOutcome.Unknown, null, ex.Message);
        }

        await uow.ExecuteAsync(async t =>
        {
            var leg = await routing.GetLegAsync(legId, t);
            if (leg is null || leg.IsTerminal) return 0;
            var order = (await routing.GetOrderAsync(leg.OrderId, t))!;
            switch (result.Outcome)
            {
                case LegDispatchOutcome.Accepted when !string.IsNullOrWhiteSpace(result.AgvTaskCode):
                    leg.AttachAgvTask(result.AgvTaskCode!, clock.UtcNow);
                    Event(order, leg, null, "LEG_ACCEPTED", result.AgvTaskCode);
                    break;
                case LegDispatchOutcome.Rejected:
                    // nothing was sent: free the AGV and the reservations, fail the leg and the order (never pick another route silently)
                    await ReleaseLegReservationsAsync(leg, t);
                    leg.TransitionTo(LegState.Failed, $"AGV_REJECTED: {result.Reason}", WaitKind.None, clock.UtcNow);
                    FailOrder(order, "LEG_REJECTED_BY_AGV_SYSTEM");
                    break;
                default:
                    // Blocked / Unknown: keep the leg; the next pass retries (the dispatcher is idempotent per request code)
                    Event(order, leg, null, "LEG_DISPATCH_PENDING", result.Reason);
                    break;
            }
            return 0;
        }, ct);
    }

    private async Task TryContinueAsync(string requestCode, string? taskCode, string destination, CancellationToken ct)
    {
        try { await dispatcher.ContinueAsync(requestCode, taskCode, destination, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Continue of leg {Code} failed; it is retried by the next pass", requestCode); }
    }

    // ================================================================================ physical events

    public async Task<EventResult> ConfirmPickAsync(Guid legId, ConfirmationSource source, string? user, string? eventKey = null, CancellationToken ct = default)
    {
        using var _frame = DiagnosticContext.Begin(await CorrelationOfLegAsync(legId, ct));   // CR-021
        var continues = new List<(string, string?, string)>();
        var result = await uow.ExecuteAsync(async t =>
        {
            continues.Clear();
            var leg = await routing.GetLegAsync(legId, t);
            if (leg is null) return new EventResult(EventOutcome.Rejected, "Leg not found.", null);
            if (leg.PickConfirmedAt is not null) return new EventResult(EventOutcome.Duplicate, "Pick already confirmed.", null);
            if (leg.State != LegState.InProgress) return new EventResult(EventOutcome.Rejected, $"Pick not valid in leg state {leg.State}.", null);
            var order = (await routing.GetOrderAsync(leg.OrderId, t))!;
            var now = clock.UtcNow;

            var res = (await reservations.ListActiveByTaskAsync(leg.Id, t)).FirstOrDefault(r => r.Kind == ReservationKind.SourceRack);
            var rack = res?.RackId is { } rid ? await racks.GetAsync(rid, t) : null;
            if (rack is null || res is null) return new EventResult(EventOutcome.Rejected, "The leg holds no source rack reservation.", null);
            var src = await locations.GetByCodeAsync(leg.SourceLocationCode!, t);
            var before = rack.Status;
            rack.BeginTransit(leg.Id, now);                  // rack is on the AMR: IN_TRANSIT, no location
            res.Consume(now);
            src?.Touch(now);
            leg.ConfirmPick(now);
            order.BindRack(rack.Id, now);
            inventoryTx.Add(new InventoryTransaction(InventoryEventType.PickConfirmed, rack.Id, src?.Id, null, leg.Id, before, rack.Status, source, user, BindOutcome.NotRequested, $"leg {leg.Sequence} of {order.ProcessCode}", now));
            Event(order, leg, null, "PICK_CONFIRMED", leg.SourceLocationCode, source.ToString());
            await stations.OnHandoverAsync(leg, leg.SourceLocationCode!, t);

            if (leg.DestinationLocationCode is null)
            {   // AfterPick: choose now (still inside the same unit of work)
                var step = order.Snapshot.Step(leg.StepKey)!;
                await ProgressExistingLegAsync(order, leg, step, continues, t);
                if (leg.DestinationLocationCode is null && step.WaitBehavior == WaitBehavior.Fail)
                {   // never drop the rack or fail silently while it is on the AMR
                    leg.TransitionTo(LegState.Waiting, "NO_DESTINATION_AND_FAIL_BEHAVIOR", WaitKind.DestinationUnavailable_AgvHoldingRack, now);
                    order.TransitionTo(OrderState.ReconciliationRequired, "RACK_ON_AGV_NO_DESTINATION", WaitKind.None, now);
                    Event(order, leg, null, "RECONCILIATION_REQUIRED", "Rack is on the AMR and no destination exists");
                }
            }
            return new EventResult(EventOutcome.Applied, "Pick confirmed.", null);
        }, ct);
        foreach (var c in continues) await TryContinueAsync(c.Item1, c.Item2, c.Item3, ct);
        if (result.Outcome == EventOutcome.Applied) await notifier.PublishAsync([new WcsEvent("OrderChanged", new { legId })], ct);
        return result with { Order = result.Outcome == EventOutcome.Applied ? await OrderOfLegAsync(legId, ct) : null };
    }

    public async Task<EventResult> ConfirmDropAsync(Guid legId, ConfirmationSource source, string? user, string? externalRackId = null, CancellationToken ct = default)
    {
        using var _frame = DiagnosticContext.Begin(await CorrelationOfLegAsync(legId, ct));   // CR-021
        var continues = new List<(string, string?, string)>();
        var result = await uow.ExecuteAsync(async t =>
        {
            continues.Clear();
            var leg = await routing.GetLegAsync(legId, t);
            if (leg is null) return new EventResult(EventOutcome.Rejected, "Leg not found.", null);
            if (leg.DropConfirmedAt is not null) return new EventResult(EventOutcome.Duplicate, "Drop already confirmed.", null);
            if (leg.PickConfirmedAt is null) return new EventResult(EventOutcome.Rejected, "Drop cannot be confirmed before pick.", null);
            if (leg.DestinationLocationCode is null) return new EventResult(EventOutcome.Rejected, "The leg has no destination.", null);
            if (leg.State is not (LegState.InProgress or LegState.Waiting)) return new EventResult(EventOutcome.Rejected, $"Drop not valid in leg state {leg.State}.", null);
            var order = (await routing.GetOrderAsync(leg.OrderId, t))!;
            var now = clock.UtcNow;

            var rack = await racks.GetAsync(leg.RackId!.Value, t);
            var dest = await locations.GetByCodeAsync(leg.DestinationLocationCode, t);
            if (rack is null || dest is null) return new EventResult(EventOutcome.Rejected, "Rack or destination not found.", null);
            var before = rack.Status;
            // CR-036: a physical drop is recorded as it happened. A model mismatch is NOT a reason to refuse it: the destination is flagged for reconciliation and an event is written.
            var handover = modelGuard.On ? ModelGuard.HandoverViolation(rack, dest, order.Snapshot.ProductModelId) : null;
            rack.PlaceAt(dest.Id, StatusOf(leg.RackKind), now);   // the rack now stands at the destination (a buffer is a real storage location)
            if (placement.OnPlaced(rack, dest, leg.Id, source, user, now) is { } dispositionTx) inventoryTx.Add(dispositionTx);   // CR-036: manual QC station -> ManualHandling (one way)
            if (handover is not null)
            {
                if (modelGuard.Enforcing) dest.SetReconciliationRequired(true, now);
                Event(order, leg, null, handover, $"{modelGuard.Mode}: rack model {rack.ProductModelId?.ToString() ?? "unknown"}, destination {dest.Code} model {dest.ProductModelId?.ToString() ?? "none"}, process model {order.Snapshot.ProductModelId?.ToString() ?? "none"}");
            }
            var bind = BindOutcome.NotRequested;
            if (leg.DestinationNoBind) bind = BindOutcome.NotBoundByProcess;                        // "no bind rack" in the process: never bound here
            else if (!string.IsNullOrWhiteSpace(externalRackId)) { rack.Bind(externalRackId!); bind = BindOutcome.Bound; }
            foreach (var r in await reservations.ListActiveByTaskAsync(leg.Id, t)) r.Consume(now);
            dest.Touch(now);
            leg.ConfirmDrop(now);
            leg.TransitionTo(LegState.Completed, "Rack dropped", WaitKind.None, now);   // the AGV is free from this moment
            inventoryTx.Add(new InventoryTransaction(InventoryEventType.DropConfirmed, rack.Id, null, dest.Id, leg.Id, before, rack.Status, source, user, bind, $"leg {leg.Sequence} of {order.ProcessCode}", now));
            Event(order, leg, null, "DROP_CONFIRMED", leg.DestinationLocationCode, source.ToString());
            await stations.OnHandoverAsync(leg, leg.DestinationLocationCode, t);
            if (order.State == OrderState.Waiting) order.TransitionTo(OrderState.InProgress, "Drop confirmed", WaitKind.None, now);
            await AdvanceCoreAsync(order, continues, t);   // next step in the same unit of work (completion, waiting at a buffer, next leg)
            return new EventResult(EventOutcome.Applied, "Drop confirmed.", null);
        }, ct);
        foreach (var c in continues) await TryContinueAsync(c.Item1, c.Item2, c.Item3, ct);
        if (result.Outcome == EventOutcome.Applied) { await notifier.PublishAsync([new WcsEvent("OrderChanged", new { legId })], ct); await DispatchPendingAsync(ct); }
        return result with { Order = result.Outcome == EventOutcome.Applied ? await OrderOfLegAsync(legId, ct) : null };
    }

    // ================================================================================ cancel

    public async Task<OrderDto> CancelAsync(Guid orderId, string reason, string? user, CancellationToken ct = default)
    {
        using var _frame = DiagnosticContext.Begin(await CorrelationOfOrderAsync(orderId, ct));   // CR-021
        await uow.ExecuteAsync(async t =>
        {
            var order = await routing.GetOrderAsync(orderId, t) ?? throw AppException.NotFound("ORDER_NOT_FOUND", "Transport Task not found.");
            if (order.IsTerminal) return 0;
            var now = clock.UtcNow;
            // a rack that is on an AMR (or an AMR in motion) cannot be cancelled blindly: the physical outcome would be unknown
            if (order.Legs.Any(l => l.RackOnAgv || (l.State == LegState.InProgress && l.AgvTaskCode is not null)))
                throw AppException.Rejected("ORDER_NOT_CANCELLABLE", "A leg is in motion or the rack is on the AMR; cancel it in the AGV system and reconcile.");
            foreach (var leg in order.Legs.Where(l => !l.IsTerminal))
            {
                await ReleaseLegReservationsAsync(leg, t);
                leg.TransitionTo(LegState.Cancelled, reason, WaitKind.None, now);
                await stations.FailForLegAsync(leg.Id, "ORDER_CANCELLED", t);
            }
            order.TransitionTo(OrderState.Cancelled, reason, WaitKind.None, now);
            Event(order, null, null, "ORDER_CANCELLED", $"{reason} by {user}");
            return 0;
        }, ct);
        await notifier.PublishAsync([new WcsEvent("OrderChanged", new { orderId })], ct);
        return await GetDtoAsync(orderId, ct);
    }

    private async Task ReleaseLegReservationsAsync(TransportLeg leg, CancellationToken ct)
    {
        foreach (var r in await reservations.ListActiveByTaskAsync(leg.Id, ct)) r.Release(clock.UtcNow);
    }

    // ================================================================================ read helpers

    private Task<OrderDto> GetDtoAsync(Guid id, CancellationToken ct) => uow.ExecuteAsync(async t =>
    {
        var o = await routing.GetOrderAsync(id, t) ?? throw AppException.NotFound("ORDER_NOT_FOUND", "Transport Task not found.");
        return o.ToDto(await routing.ListTransactionsAsync(false, null, id, 50, t));
    }, ct);

    private Task<OrderDto?> OrderOfLegAsync(Guid legId, CancellationToken ct) => uow.ExecuteAsync(async t =>
    {
        var leg = await routing.GetLegAsync(legId, t);
        if (leg is null) return null;
        var o = await routing.GetOrderAsync(leg.OrderId, t);
        return o?.ToDto(await routing.ListTransactionsAsync(false, null, o.Id, 50, t));
    }, ct);
}
