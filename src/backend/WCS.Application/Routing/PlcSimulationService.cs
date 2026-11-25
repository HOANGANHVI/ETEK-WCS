using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.RealDispatch;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>CR-035: configuration of the Admin manual PLC simulation. <see cref="Allowed"/> is a deployment switch (default false): without it the feature cannot even be activated.</summary>
public sealed class PlcSimulationOptions
{
    public const string SectionName = "PlcSimulation";
    public bool Allowed { get; set; }
}

/// <summary>Process-wide state of the simulation (in memory on purpose: a WCS restart always leaves the simulation OFF).</summary>
public sealed class PlcSimulationState
{
    private readonly object _gate = new();
    public bool Active { get; private set; }
    public string? ActivatedBy { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public void Set(bool active, string? by, DateTimeOffset now) { lock (_gate) { Active = active; ActivatedBy = active ? by : null; ActivatedAt = active ? now : null; } }
}

public sealed record SimulationStatusDto(bool Allowed, bool Active, string? ActivatedBy, DateTimeOffset? ActivatedAt, bool RealRcsDispatchEnabled, bool EnforceReadiness, string Banner,
    string MappingSource, IReadOnlyList<string> Blockers);

public sealed record SimulationFlowDto(string Code, string Name, string RequestType, string RackKind, string PublicationState, string? Readiness, int? PublishedVersion,
    IReadOnlyList<SimulationTriggerDto> Triggers, bool StartableNow, string? NotStartableReason);

public sealed record SimulationTriggerDto(string LocationCode, string? PortId, string? PlcId, bool PortMappingApproved, bool? PlcOnline, bool PortBusy);

public sealed record SimulationStartResult(string Outcome, string Code, string Message, OrderDto? Order, StationTransactionDto? Transaction, string RequestId);

/// <summary>
/// CR-035. Admin-only manual PLC simulation. It replaces ONLY the physical PLC as the source of a request: the logical request goes through the SAME <see cref="IPlcRequestService"/> a PLC
/// runtime would call (same validation, Station Transaction, Process Engine, queue rules). It never simulates the RCS or an AMR, never writes a PLC address and never touches inventory itself.
/// Refused unless the REAL RCS dispatch is enabled; refused for a port whose PLC is online (no mixing a real PLC and the simulation on the same resources).
/// </summary>
public sealed class PlcSimulationService(IUnitOfWork uow, IRoutingRepository routing, IPlcRequestService plcRequests, IRouteEngine engine, ILegDispatcher dispatcher,
    PlcSimulationState state, IOptions<PlcSimulationOptions> simOptions, IOptions<RealDispatchOptions> realRcs, IOptions<RouteEngineOptions> routeOptions, IClock clock, IPlcLinkStatus? link = null)
{
    public const string Banner = "PLC SIMULATION / REAL RCS";
    public const string MappingSource = "TEST CONFIGURATION: logical signals of IO Map v2 (Pickup_Request / Delivery_Request / Request_ID / Rack_Full / Rack_Empty, PlcLocationCodes 1-4). Not the 20/11/2026 mapping (CR-029 PENDING_APPROVAL); no PLC address is read or written.";

    public SimulationStatusDto Status()
    {
        var blockers = new List<string>();
        if (!simOptions.Value.Allowed) blockers.Add("PlcSimulation:Allowed is false (deployment switch).");
        if (!realRcs.Value.Enabled) blockers.Add("Rcs:RealDispatch:Enabled is false: the REAL RCS is required; no RCS simulator is used.");
        else if (realRcs.Value.LegTemplate is null || string.IsNullOrWhiteSpace(realRcs.Value.LegTemplate.TaskTyp))
            blockers.Add("Rcs:RealDispatch:LegTemplate is not configured: legs of configurable processes cannot be sent to the real RCS (Q1PICK template, Pick/Drop triggers and continueTask handling still to be supplied).");
        return new(simOptions.Value.Allowed, state.Active, state.ActivatedBy, state.ActivatedAt, realRcs.Value.Enabled, routeOptions.Value.EnforceReadiness, Banner, MappingSource, blockers);
    }

    public SimulationStatusDto Activate(string user)
    {
        if (!simOptions.Value.Allowed) throw AppException.Rejected("SIMULATION_NOT_ALLOWED", "PLC simulation is not allowed in this deployment (PlcSimulation:Allowed).");
        if (!realRcs.Value.Enabled) throw AppException.Rejected("RCS_REAL_DISPATCH_REQUIRED", "The REAL RCS dispatch must be enabled; the simulation never runs with a simulated RCS.");
        state.Set(true, user, clock.UtcNow);
        return Status();
    }

    public SimulationStatusDto Deactivate() { state.Set(false, null, clock.UtcNow); return Status(); }

    private void EnsureActive()
    {
        if (!state.Active) throw AppException.Rejected("SIMULATION_NOT_ACTIVE", "PLC simulation is not active.");
        if (!realRcs.Value.Enabled) { state.Set(false, null, clock.UtcNow); throw AppException.Rejected("RCS_REAL_DISPATCH_REQUIRED", "The REAL RCS dispatch is not enabled; the simulation was switched off."); }
    }

    public Task<IReadOnlyList<SimulationFlowDto>> ListFlowsAsync(CancellationToken ct = default) => uow.ExecuteAsync<IReadOnlyList<SimulationFlowDto>>(async t =>
    {
        var list = new List<SimulationFlowDto>();
        foreach (var p in await routing.ListProcessesAsync(t))
        {
            var v = await routing.GetPublishedVersionAsync(p.Id, t);
            var triggers = new List<SimulationTriggerDto>();
            foreach (var loc in p.TriggerLocationCodes)
            {
                var port = await routing.FindPortByLocationAsync(loc, t);
                triggers.Add(new(loc, port?.Id, port?.PlcId, port is not null && await routing.IsPortMappingApprovedAsync(port.Id, t), port is null ? null : link?.IsOnline(port.PlcId),
                    port is not null && await routing.GetActiveTransactionForPortAsync(port.Id, t) is not null));
            }
            string? why = null;
            if (!p.Enabled) why = "Process is disabled.";
            else if (p.ReviewBlocked) why = "Process is REVIEW-blocked.";
            else if (v is null) why = "No Published version.";
            else if ((routeOptions.Value.EnforceReadiness || p.RequiresReadiness) && v.Readiness != OperationalReadiness.Ready) why = $"Operational Readiness is {v.Readiness}.";
            else if (triggers.Count == 0) why = "No trigger location.";
            list.Add(new(p.Code, p.Name, p.RequestType.ToString(), p.RackKind.ToString(), v?.State.ToString() ?? "NoPublishedVersion", v?.Readiness.ToString(), v?.Number, triggers, why is null, why));
        }
        return list;
    }, ct);

    /// <summary>Generates ONE PLC request for a declared flow at one of its trigger locations. The destination, rack condition and RCS template come from the flow; nothing is typed in.</summary>
    public async Task<SimulationStartResult> StartRequestAsync(string flowCode, string locationCode, bool safetyAcknowledged, CancellationToken ct = default)
    {
        EnsureActive();
        if (!safetyAcknowledged) throw AppException.Validation("SAFETY_ACK_REQUIRED", "The operator must confirm that the test area is safe: the real AMR may move.");
        var flow = (await ListFlowsAsync(ct)).FirstOrDefault(f => string.Equals(f.Code, flowCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw AppException.NotFound("FLOW_NOT_FOUND", "Process/Flow not found.");
        if (!flow.StartableNow) throw AppException.Rejected("FLOW_NOT_STARTABLE", flow.NotStartableReason ?? "The flow cannot be started.");
        var trig = flow.Triggers.FirstOrDefault(x => string.Equals(x.LocationCode, locationCode?.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw AppException.Rejected("LOCATION_NOT_ALLOWED", $"{locationCode} is not a trigger location of {flow.Code}; the location is not free text.");
        if (trig.PortId is null || trig.PlcId is null) throw AppException.Rejected("PORT_NOT_FOUND", $"No enabled port serves {trig.LocationCode}.");
        if (trig.PlcOnline == true) throw AppException.Conflict("SIMULATION_PLC_ONLINE", $"PLC {trig.PlcId} is online: the simulation never shares a PLC/port with a real PLC.");
        if (trig.PortBusy) throw AppException.Conflict("PORT_BUSY", $"Port {trig.PortId} already has an active Station Transaction.");

        var requestId = $"SIM-{clock.UtcNow:yyyyMMddHHmmssfff}";
        var pickup = flow.RequestType == nameof(RequestType.PickupRequest);
        int? full = null, empty = null;
        if (pickup)
        {   // the register value is the confirmed code of the location (reverse of RouteEngine:PlcLocationCodes); no other code is invented
            var raw = routeOptions.Value.PlcLocationCodes.FirstOrDefault(kv => string.Equals(kv.Value, trig.LocationCode, StringComparison.OrdinalIgnoreCase)).Key;
            if (!int.TryParse(raw, out var code)) throw AppException.Rejected("UNSUPPORTED_LOCATION_CODE", $"{trig.LocationCode} has no confirmed position code; nothing is simulated.");
            if (flow.RackKind == nameof(RackKind.Full)) full = code; else empty = code;
        }
        var r = await plcRequests.HandleRequestAsync(new PlcRequestSnapshot(trig.PlcId, trig.PortId, requestId, pickup, !pickup, full, empty, null, null), ct);
        return new(r.Outcome.ToString(), r.Code, r.Message, r.Order, r.Transaction, requestId);
    }

    /// <summary>Operator confirmation of the physical Pick/Drop of a leg. Allowed only while the REAL RCS reports the task Executing; it never overrides the RCS state and goes through the engine inventory rules.</summary>
    public async Task<EventResult> ConfirmHandoverAsync(Guid legId, bool drop, string? user, string? externalRackId, CancellationToken ct = default)
    {
        EnsureActive();
        var requestCode = await uow.ExecuteAsync(async t =>
        {
            var leg = await routing.GetLegAsync(legId, t) ?? throw AppException.NotFound("LEG_NOT_FOUND", "Leg not found.");
            if (leg.State is LegState.Completed or LegState.Failed or LegState.Cancelled) throw AppException.Rejected("LEG_FINISHED", $"The leg is {leg.State}.");
            if (string.IsNullOrWhiteSpace(leg.AgvTaskCode)) throw AppException.Rejected("RCS_TASK_NOT_CREATED", "The leg has no RCS task yet: nothing can be confirmed.");
            if (drop && leg.PickConfirmedAt is null) throw AppException.Rejected("PICK_NOT_CONFIRMED", "The Pick must be confirmed before the Drop.");
            if (!drop && leg.PickConfirmedAt is not null) throw AppException.Rejected("PICK_ALREADY_CONFIRMED", "The Pick was already confirmed.");
            return leg.AgvRequestCode;
        }, ct);
        var info = await dispatcher.QueryAsync(requestCode, ct);   // the REAL RCS answers; outside any transaction
        if (info.Status != RcsTaskStatus.Executing)
            throw AppException.Rejected("RCS_NOT_EXECUTING", $"The real RCS reports the task as {info.Status}; a manual confirmation is accepted only while it is Executing (it never overrides the RCS).");
        return drop ? await engine.ConfirmDropAsync(legId, ConfirmationSource.Operator, user, externalRackId, ct) : await engine.ConfirmPickAsync(legId, ConfirmationSource.Operator, user, null, ct);
    }
}
