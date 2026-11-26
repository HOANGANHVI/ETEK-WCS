using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.RealDispatch;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>Process-wide state of the simulation (in memory on purpose: a WCS restart always leaves the simulation OFF).</summary>
public sealed class PlcSimulationState
{
    private readonly object _gate = new();
    public bool Active { get; private set; }
    public string? ActivatedBy { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public void Set(bool active, string? by, DateTimeOffset now) { lock (_gate) { Active = active; ActivatedBy = active ? by : null; ActivatedAt = active ? now : null; } }
}

public sealed record SimulationStatusDto(bool Active, string? ActivatedBy, DateTimeOffset? ActivatedAt, string ExecutionMode, bool RealRcsDispatchEnabled, bool EnforceReadiness, string Banner,
    string MappingSource, IReadOnlyList<string> Notes);

public sealed record SimulationFlowDto(string Code, string Name, string RequestType, string RackKind, string PublicationState, string? Readiness, int? PublishedVersion,
    IReadOnlyList<SimulationTriggerDto> Triggers, bool StartableNow, string? NotStartableReason);

public sealed record SimulationTriggerDto(string LocationCode, string? PortId, string? PlcId, bool PortMappingApproved, bool? PlcOnline, bool PortBusy);

public sealed record SimulationStartResult(string Outcome, string Code, string Message, OrderDto? Order, StationTransactionDto? Transaction, string RequestId);

/// <summary>
/// CR-035 / CR-040. Admin-only manual PLC simulation, opened by an explicit confirmation. It replaces ONLY the physical PLC as the source of PLC → WCS signals: the request goes through the
/// SAME <see cref="IPlcRequestService"/> a PLC runtime calls (same validation, Station Transaction, Process Engine, queue). It never answers for the RCS or an AMR, never writes a PLC address
/// and never touches inventory itself. It grants nothing on the RCS side: where an order is sent is decided by the operating mode and its own conditions (CR-038). Refused for a port whose
/// PLC is online (no mixing of a physical PLC and the simulation on the same resources).
/// </summary>
public sealed class PlcSimulationService(IUnitOfWork uow, IRoutingRepository routing, IPlcRequestService plcRequests, IPlcInputHandler inputHandler, IRouteEngine engine, ILegDispatcher dispatcher,
    PlcSimulationState state, PlcSimulatedInputs inputs, IOptions<RealDispatchOptions> realRcs, IOptions<RouteEngineOptions> routeOptions, IExecutionModeProvider mode, IClock clock, IPlcLinkStatus? link = null)
{
    public const string Banner = "PLC SIMULATION";
    public const string MappingSource = "TEST CONFIGURATION: logical signals of IO Map v2 (Pickup_Request / Delivery_Request / Request_ID / Rack_Full / Rack_Empty, PlcLocationCodes 1-4). Not the 20/11/2026 mapping (CR-029 PENDING_APPROVAL); no PLC address is read or written.";

    public SimulationStatusDto Status()
    {
        var current = mode.Current.Mode;
        var notes = new List<string>
        {
            current == WCS.Domain.Rcs.ExecutionMode.Real
                ? "Operating mode REAL: signals can be set and read, but a simulated request creates no work and nothing is sent to the RCS."
                : "Operating mode Simulator: requests accepted from this simulation are not sent to a real RCS.",
            "Only PLC to WCS signals are simulated. Nothing answers for the RCS or an AMR, and nothing is written to a PLC.",
        };
        return new(state.Active, state.ActivatedBy, state.ActivatedAt, current.ToString(), realRcs.Value.Enabled, routeOptions.Value.EnforceReadiness, Banner, MappingSource, notes);
    }

    public SimulationStatusDto Activate(string user, bool confirmed)
    {
        if (!confirmed) throw AppException.Validation("SIMULATION_CONFIRMATION_REQUIRED", "The Admin must confirm before the PLC simulation is switched on.");
        state.Set(true, user, clock.UtcNow);
        return Status();
    }

    public SimulationStatusDto Deactivate() { state.Set(false, null, clock.UtcNow); inputs.Clear(); return Status(); }

    private void EnsureActive()
    {
        if (!state.Active) throw AppException.Rejected("SIMULATION_NOT_ACTIVE", "PLC simulation is not active.");
    }

    // ------------------------------------------------------------------ CR-040: one signal at a time
    public Task<IReadOnlyList<SimStationDto>> ListStationsAsync(CancellationToken ct = default) => uow.ExecuteAsync<IReadOnlyList<SimStationDto>>(async t =>
    {
        var list = new List<SimStationDto>();
        foreach (var port in await routing.ListLinkedPortsAsync(t))
        {
            var signals = (await routing.ListPlcInputPointsAsync(port.Id, t)).Select(p =>
            {
                var v = inputs.Get(port.Id, p.SignalName);
                return new SimSignalDto(p.SignalName, p.DataType.ToString(), p.Description, v?.Value, v?.At, PlcRequestAssembler.IntakeSignals.Contains(p.SignalName),
                    string.IsNullOrWhiteSpace(p.ActualAddress) ? null : p.ActualAddress);
            }).ToList();
            var active = await routing.GetActiveTransactionForPortAsync(port.Id, t);
            list.Add(new(port.Id, port.PlcId, port.LocationCode!, link?.IsOnline(port.PlcId), await routing.IsPortMappingApprovedAsync(port.Id, t), active?.ToDto(), signals));
        }
        return list;
    }, ct);

    /// <summary>Sets one simulated PLC → WCS value. Only signals of the station that the IO map declares as PLC → WCS exist; setting one starts nothing.</summary>
    public async Task<SimStationDto> SetSignalAsync(string stationId, string signal, string? value, CancellationToken ct = default)
    {
        EnsureActive();
        await uow.ExecuteAsync(async t =>
        {
            var port = await SimulatablePortAsync(stationId, t);
            var point = (await routing.ListPlcInputPointsAsync(port.Id, t)).FirstOrDefault(p => p.SignalName == signal)
                        ?? throw AppException.Rejected("SIGNAL_NOT_A_PLC_INPUT", $"{signal} is not a PLC to WCS signal of station {port.Id}; only those can be simulated.");
            inputs.Set(port.Id, point.SignalName, Normalize(point.DataType, value), clock.UtcNow);
            return 0;
        }, ct);
        return (await ListStationsAsync(ct)).First(s => s.StationId == stationId);
    }

    /// <summary>CR-041: back to the normal state - every simulated value of the station is removed. Nothing already accepted is changed or cancelled.</summary>
    public async Task<SimStationDto> ResetStationAsync(string stationId, CancellationToken ct = default)
    {
        EnsureActive();
        var port = await uow.ExecuteAsync(t => SimulatablePortAsync(stationId, t), ct);
        inputs.Clear(port.Id);
        return (await ListStationsAsync(ct)).First(s => s.StationId == port.Id);
    }

    /// <summary>Hands the current input signals of one station to the shared PLC input handler, exactly as one read of a PLC would.</summary>
    public async Task<SimulationStartResult> EvaluateStationAsync(string stationId, bool safetyAcknowledged, CancellationToken ct = default)
    {
        EnsureActive();
        if (!safetyAcknowledged) throw AppException.Validation("SAFETY_ACK_REQUIRED", "The operator must confirm that the test area is safe: with the real mode an AMR may move.");
        var port = await uow.ExecuteAsync(t => SimulatablePortAsync(stationId, t), ct);
        var (snapshot, r) = await inputHandler.HandleAsync(PlcInputSource.Simulation, port.PlcId, port.Id, inputs.Of(port.Id), ct);
        return new(r.Outcome.ToString(), r.Code, r.Message, r.Order, r.Transaction, snapshot.RequestId);
    }

    private async Task<WCS.Domain.Plc.PlcStation> SimulatablePortAsync(string stationId, CancellationToken ct)
    {
        var port = await routing.GetPortAsync(stationId, ct) ?? throw AppException.NotFound("PORT_NOT_FOUND", $"Station {stationId} not found.");
        if (port.LocationCode is null || !port.Enabled) throw AppException.Rejected("PORT_NOT_LINKED", $"Station {port.Id} is disabled or not linked to a location.");
        if (link?.IsOnline(port.PlcId) == true) throw AppException.Conflict("SIMULATION_PLC_ONLINE", $"PLC {port.PlcId} is online: the simulation never shares a PLC/port with a real PLC.");
        return port;
    }

    private static string Normalize(WCS.Domain.Plc.PlcDataType type, string? value)
    {
        var v = (value ?? "").Trim();
        if (type == WCS.Domain.Plc.PlcDataType.Bool)
            return v.ToLowerInvariant() switch { "true" or "on" or "1" => "true", "false" or "off" or "0" => "false", _ => throw AppException.Validation("SIGNAL_VALUE_INVALID", "A bit signal is ON or OFF.") };
        if (type == WCS.Domain.Plc.PlcDataType.String)
            return v.Length <= 100 ? v : throw AppException.Validation("SIGNAL_VALUE_INVALID", "The text is too long (max 100).");
        return int.TryParse(v, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw AppException.Validation("SIGNAL_VALUE_INVALID", "A number is required.");
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
        var r = await plcRequests.HandleRequestAsync(new PlcRequestSnapshot(trig.PlcId, trig.PortId, requestId, pickup, !pickup, full, empty, null, null, Simulated: true), ct);
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
            if ((await routing.GetOrderAsync(leg.OrderId, t))?.ExecutionMode == WCS.Domain.Rcs.ExecutionMode.Real)
                throw AppException.Rejected("SIMULATION_REAL_ORDER", "The order runs in Real mode: the simulation page confirms nothing for it. Use the operator confirmation on the multi-leg page.");
            return leg.AgvRequestCode;
        }, ct);
        var info = await dispatcher.QueryAsync(requestCode, ct);   // the REAL RCS answers; outside any transaction
        if (info.Status != RcsTaskStatus.Executing)
            throw AppException.Rejected("RCS_NOT_EXECUTING", $"The real RCS reports the task as {info.Status}; a manual confirmation is accepted only while it is Executing (it never overrides the RCS).");
        return drop ? await engine.ConfirmDropAsync(legId, ConfirmationSource.Operator, user, externalRackId, ct) : await engine.ConfirmPickAsync(legId, ConfirmationSource.Operator, user, null, ct);
    }
}
