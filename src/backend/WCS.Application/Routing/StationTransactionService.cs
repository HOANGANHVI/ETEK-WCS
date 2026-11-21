using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>
/// Station Transaction lifecycle (one active transaction per port; ports are independent). Methods without a CancellationToken-only signature that take
/// a unit-of-work token run INSIDE the caller's unit of work (used by the engine); the public Admin/PLC operations open their own.
/// This service never writes a PLC signal: physical handshake writes stay disabled until the PLC engineer approves the mapping and the procedure.
/// </summary>
public sealed class StationTransactionService(IUnitOfWork uow, IRoutingRepository routing, IClock clock, IWcsNotifier notifier)
{
    // ------------------------------------------------------------------ inside the engine's unit of work

    public async Task LinkToOrderAsync(Guid transactionId, Guid orderId, CancellationToken ct)
    {
        var tx = await routing.GetTransactionAsync(transactionId, ct) ?? throw AppException.NotFound("TRANSACTION_NOT_FOUND", "Station Transaction not found.");
        tx.LinkTo(orderId, null, clock.UtcNow);
    }

    /// <summary>True when the order/leg may use the port now: the location has no port, or the port is free, or its active transaction already belongs to this order/leg.</summary>
    public async Task<bool> CanOpenAsync(TransportOrder order, TransportLeg leg, string locationCode, CancellationToken ct)
    {
        var port = await routing.FindPortByLocationAsync(locationCode, ct);
        if (port is null) return true;
        var active = await routing.GetActiveTransactionForPortAsync(port.Id, ct);
        return active is null || (active.OrderId == order.Id && (active.LegId is null || active.LegId == leg.Id));
    }

    /// <summary>Opens (or links the PLC-created) Station Transaction of the leg at the port of <paramref name="locationCode"/>. A location without a port needs none.</summary>
    public async Task OpenForLegAsync(TransportOrder order, TransportLeg leg, string locationCode, StationOperation operation, CancellationToken ct, Guid? agvId = null, bool markAgvAssigned = true)
    {
        var port = await routing.FindPortByLocationAsync(locationCode, ct);
        if (port is null) return;
        var now = clock.UtcNow;
        var active = await routing.GetActiveTransactionForPortAsync(port.Id, ct);
        if (active is not null)
        {
            if (active.OrderId != order.Id || (active.LegId is not null && active.LegId != leg.Id))
                throw AppException.Conflict("PORT_BUSY", $"Port {port.Id} already has an active Station Transaction.");
            active.LinkTo(order.Id, leg.Id, now);
        }
        else
        {
            active = StationTransaction.Create(port.Id, port.PlcId, locationCode, operation, order.Id, leg.Id, null, now, leg.RackKind);
            routing.Add(active);
        }
        if (agvId is { } a) active.AssignAgv(a, now);
        if (markAgvAssigned && active.State < StationTransactionState.AgvAssigned && active.State != StationTransactionState.ReconciliationRequired) active.Advance(StationTransactionState.AgvAssigned, now);
    }

    /// <summary>The rack was picked / dropped at the port: the physical handover is done (the AMR may now leave).</summary>
    public async Task OnHandoverAsync(TransportLeg leg, string locationCode, CancellationToken ct)
    {
        var port = await routing.FindPortByLocationAsync(locationCode, ct);
        if (port is null) return;
        var active = await routing.GetActiveTransactionForPortAsync(port.Id, ct);
        if (active is null || active.LegId != leg.Id || active.State == StationTransactionState.ReconciliationRequired) return;
        if (active.State < StationTransactionState.HandoverCompleted) active.Advance(StationTransactionState.HandoverCompleted, clock.UtcNow);
    }

    public async Task FailForLegAsync(Guid legId, string reason, CancellationToken ct)
    {
        foreach (var tx in await routing.ListTransactionsAsync(true, null, null, 200, ct))
            if (tx.LegId == legId) tx.Fail(reason, clock.UtcNow);
    }

    // ------------------------------------------------------------------ own unit of work

    /// <summary>AGV / PLC events that move the transaction forward without completing it (approaching, at the port, handling).</summary>
    public async Task<StationTransactionDto> AdvanceAsync(Guid transactionId, StationTransactionState next, CancellationToken ct = default)
    {
        var dto = await AdvanceCoreAsync(transactionId, next, ct); await NotifyAsync(ct); return dto;
    }

    private Task<StationTransactionDto> AdvanceCoreAsync(Guid transactionId, StationTransactionState next, CancellationToken ct) => uow.ExecuteAsync(async t =>
    {
        if (next is StationTransactionState.Completed or StationTransactionState.Failed or StationTransactionState.ReconciliationRequired
            or StationTransactionState.AgvExited or StationTransactionState.Created)
            throw AppException.Validation("STATE_NOT_ALLOWED", $"{next} cannot be set directly; use the dedicated operation.");
        var tx = await Get(transactionId, t);
        tx.Advance(next, clock.UtcNow);
        return tx.ToDto();
    }, ct);

    /// <summary>The AMR left the port. Only now - after the handover - the transaction completes and the port is released. The Transport Task is NOT completed here.</summary>
    public async Task<StationTransactionDto> ConfirmAgvExitedAsync(Guid transactionId, CancellationToken ct = default)
    {
        var dto = await ExitCoreAsync(transactionId, ct); await NotifyAsync(ct); return dto;
    }

    private Task<StationTransactionDto> ExitCoreAsync(Guid transactionId, CancellationToken ct) => uow.ExecuteAsync(async t =>
    {
        var tx = await Get(transactionId, t);
        if (tx.State == StationTransactionState.Completed) return tx.ToDto();   // idempotent
        if (tx.State == StationTransactionState.ReconciliationRequired) throw AppException.Rejected("TRANSACTION_NEEDS_RECONCILIATION", "Reconcile the transaction first.");
        if (tx.State < StationTransactionState.HandoverCompleted)
            throw AppException.Rejected("HANDOVER_NOT_COMPLETED", "The handover has not been confirmed; the AMR cannot be recorded as having left the port.");
        tx.Advance(StationTransactionState.AgvExited, clock.UtcNow);
        tx.Advance(StationTransactionState.Completed, clock.UtcNow);
        return tx.ToDto();
    }, ct);

    /// <summary>Convenience used by simulators: the AMR left the port of the leg.</summary>
    public Task<int> ConfirmAgvExitedForLegAsync(Guid legId, string locationCode, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var port = await routing.FindPortByLocationAsync(locationCode, t);
        if (port is null) return 0;
        var tx = await routing.GetActiveTransactionForPortAsync(port.Id, t);
        if (tx is null || tx.LegId != legId) return 0;
        if (tx.State < StationTransactionState.HandoverCompleted) throw AppException.Rejected("HANDOVER_NOT_COMPLETED", "The handover has not been confirmed.");
        if (tx.State == StationTransactionState.HandoverCompleted) { tx.Advance(StationTransactionState.AgvExited, clock.UtcNow); tx.Advance(StationTransactionState.Completed, clock.UtcNow); }
        return 1;
    }, ct);

    public Task<StationTransactionDto> ObserveHandshakeStepAsync(Guid transactionId, int step, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var tx = await Get(transactionId, t);
        tx.ObserveHandshakeStep(step, clock.UtcNow);
        return tx.ToDto();
    }, ct);

    /// <summary>The PLC link went down: every active transaction of the PLC keeps its state and is only flagged. Nothing is cancelled, reset or written.</summary>
    public Task<int> SuspendPlcAsync(string plcId, string reason, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var list = await routing.ListTransactionsAsync(true, plcId, null, 500, t);
        foreach (var tx in list) tx.Suspend(reason, clock.UtcNow);
        return list.Count;
    }, ct);

    /// <summary>The PLC reconnected: read the real PLC state and reconcile it with the stored transaction before anything is written (see <see cref="HandshakeRecovery"/>).</summary>
    public async Task<IReadOnlyList<RecoveryReport>> RecoverPlcAsync(string plcId, IPlcObservationSource source, CancellationToken ct = default)
    {
        var ids = await uow.ExecuteAsync(async t => (await routing.ListTransactionsAsync(true, plcId, null, 500, t)).Select(x => (x.Id, x.PortId)).ToList(), ct);
        var reports = new List<RecoveryReport>();
        foreach (var (id, portId) in ids)
        {
            var obs = await source.ReadAsync(plcId, portId, ct);      // external read happens outside any transaction
            reports.Add(await uow.ExecuteAsync(async t =>
            {
                var tx = await Get(id, t);
                var decision = HandshakeRecovery.Evaluate(tx, obs);
                switch (decision.Kind)
                {
                    case RecoveryKind.NoData: break;                                                  // stays suspended; nothing is decided without data
                    case RecoveryKind.NoChange: tx.ResumeLink(clock.UtcNow); break;
                    case RecoveryKind.Resume: tx.ObserveHandshakeStep(decision.Step!.Value, clock.UtcNow); tx.ResumeLink(clock.UtcNow); break;
                    case RecoveryKind.Reconciliation:
                        tx.RequireReconciliation(decision.Code!, clock.UtcNow); tx.ResumeLink(clock.UtcNow);
                        if (tx.OrderId is { } oid && await routing.GetOrderAsync(oid, t) is { } order && !order.IsTerminal && order.State != OrderState.ReconciliationRequired)
                        {
                            order.TransitionTo(OrderState.ReconciliationRequired, decision.Code, WaitKind.None, clock.UtcNow);
                            routing.Add(new RoutingEvent(order.Id, tx.LegId, tx.Id, "RECONCILIATION_REQUIRED", decision.Code, "PLC_RECOVERY", clock.UtcNow));
                        }
                        break;
                }
                return new RecoveryReport(tx.Id, tx.PortId, decision.Kind.ToString(), decision.Code, decision.Step);
            }, ct));
        }
        return reports;
    }

    /// <summary>Admin decision after reconciliation: continue the transaction from a state the Admin verified physically, or fail it (releasing the port).</summary>
    public async Task<StationTransactionDto> ResolveReconciliationAsync(Guid transactionId, StationTransactionState? resumeState, string user, CancellationToken ct = default)
    {
        var dto = await ResolveCoreAsync(transactionId, resumeState, user, ct); await NotifyAsync(ct); return dto;
    }

    private Task<StationTransactionDto> ResolveCoreAsync(Guid transactionId, StationTransactionState? resumeState, string user, CancellationToken ct) => uow.ExecuteAsync(async t =>
    {
        var tx = await Get(transactionId, t);
        if (resumeState is { } s) tx.ResumeFromReconciliation(s, clock.UtcNow);
        else tx.Fail($"RECONCILED_CLOSED by {user}", clock.UtcNow);
        return tx.ToDto();
    }, ct);

    public Task<IReadOnlyList<StationTransactionDto>> ListAsync(bool activeOnly, string? plcId, Guid? orderId, int take, CancellationToken ct = default) =>
        uow.ExecuteAsync<IReadOnlyList<StationTransactionDto>>(async t => (await routing.ListTransactionsAsync(activeOnly, plcId, orderId, take, t)).Select(x => x.ToDto()).ToList(), ct);

    private Task NotifyAsync(CancellationToken ct) => notifier.PublishAsync([new WcsEvent("OrderChanged", new { stationTransaction = true })], ct);

    private async Task<StationTransaction> Get(Guid id, CancellationToken ct) =>
        await routing.GetTransactionAsync(id, ct) ?? throw AppException.NotFound("TRANSACTION_NOT_FOUND", "Station Transaction not found.");
}

public sealed record RecoveryReport(Guid TransactionId, string PortId, string Decision, string? Code, int? Step);

/// <summary>Reads the current handshake bits of one port from the PLC (implemented by the PLC runtime when its mapping is approved). Null = no usable data.</summary>
public interface IPlcObservationSource
{
    Task<PlcHandshakeObservation?> ReadAsync(string plcId, string portId, CancellationToken ct = default);
}

/// <summary>
/// The handshake bits of Trang_tính1 as read from the PLC, by STEP number (1..9): true = the bit of that step is currently set. Which physical bit belongs to which step
/// is configuration owned by the PLC engineer; the recovery logic only needs the step view. <see cref="RequestId"/> is the Request_ID currently shown by the PLC.
/// </summary>
public sealed record PlcHandshakeObservation(string? RequestId, IReadOnlyDictionary<int, bool> StepBits, bool QualityGood = true)
{
    public int HighestSetStep => StepBits.Where(kv => kv.Value).Select(kv => kv.Key).DefaultIfEmpty(0).Max();
}

public enum RecoveryKind { NoData = 0, NoChange = 1, Resume = 2, Reconciliation = 3 }

public sealed record RecoveryDecision(RecoveryKind Kind, int? Step = null, string? Code = null);

/// <summary>
/// Reconnect decision table (CR-010 7 / specification section 11.1). It never infers a physical outcome: when the observation cannot prove where the
/// transaction is, it asks for reconciliation. Pure function - unit tested without any PLC.
/// </summary>
public static class HandshakeRecovery
{
    public static RecoveryDecision Evaluate(StationTransaction tx, PlcHandshakeObservation? obs)
    {
        if (tx.IsFinal) return new RecoveryDecision(RecoveryKind.NoChange);
        if (obs is null || !obs.QualityGood) return new RecoveryDecision(RecoveryKind.NoData);
        if (tx.RequestId is not null && obs.RequestId is not null && !string.Equals(tx.RequestId, obs.RequestId, StringComparison.Ordinal))
            return new RecoveryDecision(RecoveryKind.Reconciliation, Code: "RECON_REQUEST_ID_CHANGED");   // the PLC moved on to another request
        var highest = obs.HighestSetStep;
        if (highest == 0 && tx.HandshakeStep >= 1)
            return new RecoveryDecision(RecoveryKind.Reconciliation, Code: "RECON_PLC_RESET");           // all bits empty mid-way: reset/restart, outcome unknown
        if (highest < tx.HandshakeStep)
            return new RecoveryDecision(RecoveryKind.Reconciliation, Code: "RECON_BIT_REGRESSED");        // bit went back; a written-then-reset bit cannot be told from a lost write
        if (highest == tx.HandshakeStep) return new RecoveryDecision(RecoveryKind.NoChange);
        return new RecoveryDecision(RecoveryKind.Resume, Step: highest);                                   // the PLC progressed while the link was down
    }
}
