using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Plc;

namespace WCS.Application.Routing;

/// <summary>
/// CR-040: turns the PLC → WCS input signals of ONE station into the request a PLC runtime hands to <see cref="IPlcRequestService"/>. It is the only place that reads these signals by name
/// (the names of the retired IO Map v2; the standard IO Map of 22/11/2026 is not implemented yet), so a simulated and a physical source cannot be interpreted differently. It decides nothing about the business: validation and processing stay in the request service.
/// The physical PLC runtime does not call it yet: when a signal change counts as a request (edge / level, reset) is not approved (CR-029).
/// </summary>
public static class PlcRequestAssembler
{
    public const string PickupRequest = "Pickup_Request";
    public const string DeliveryRequest = "Delivery_Request";
    public const string RequestId = "Request_ID";
    public const string RackFull = "Rack_Full";
    public const string RackEmpty = "Rack_Empty";
    public const string DestinationAvailable = "Destination_Available";
    public const string RackType = "Rack_Type";

    /// <summary>The signals the request intake reads. Every other PLC → WCS signal is only shown: no approved processing exists for it.</summary>
    public static readonly string[] IntakeSignals = [PickupRequest, DeliveryRequest, RequestId, RackFull, RackEmpty, DestinationAvailable, RackType];

    public static PlcRequestSnapshot FromSignals(string plcId, string stationId, IReadOnlyDictionary<string, string> signals) => new(plcId, stationId,
        Text(signals, RequestId), Bit(signals, PickupRequest), Bit(signals, DeliveryRequest), Number(signals, RackFull), Number(signals, RackEmpty), Number(signals, DestinationAvailable), Number(signals, RackType));

    private static string Text(IReadOnlyDictionary<string, string> s, string name) => s.TryGetValue(name, out var v) ? v.Trim() : "";
    private static bool Bit(IReadOnlyDictionary<string, string> s, string name) => s.TryGetValue(name, out var v) && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    private static int? Number(IReadOnlyDictionary<string, string> s, string name) => s.TryGetValue(name, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

public enum PlcInputSource { Physical = 1, Simulation = 2 }

public interface IPlcInputHandler
{
    /// <summary>Normalizes the PLC → WCS signals of one station and hands them to the request intake. Returns what was understood and what WCS answered.</summary>
    Task<(PlcRequestSnapshot Snapshot, PlcRequestResult Result)> HandleAsync(PlcInputSource source, string plcId, string stationId, IReadOnlyDictionary<string, string> signals, CancellationToken ct = default);
}

/// <summary>
/// CR-041: THE entry of PLC input into the business, for both sources. Both are normalized by <see cref="PlcRequestAssembler"/> and processed by <see cref="IPlcRequestService"/>;
/// the only differences are that a simulated request is marked as such (it creates no work in Real mode) and that a PHYSICAL signal set is not processed yet: when a signal change counts
/// as a request (edge / level, reset, handshake) is not approved (CR-029, OQ-41-02), so nothing is derived from it.
/// </summary>
public sealed class PlcInputHandler(IPlcRequestService requests, IStationEngineRouter engines, IOptions<PlcRuntimeOptions> plc, ITaskOrchestrator tasks, IRoutingRepository routing, IUnitOfWork uow, IExecutionModeGate modeGate) : IPlcInputHandler
{
    public const string TriggerNotApproved = "PLC_TRIGGER_NOT_APPROVED";

    public async Task<(PlcRequestSnapshot, PlcRequestResult)> HandleAsync(PlcInputSource source, string plcId, string stationId, IReadOnlyDictionary<string, string> signals, CancellationToken ct = default)
    {
        var snapshot = PlcRequestAssembler.FromSignals(plcId, stationId, signals) with { Simulated = source == PlcInputSource.Simulation };
        if (source == PlcInputSource.Physical)
            return (snapshot, new(PlcRequestOutcome.Rejected, TriggerNotApproved, "The rule that turns physical PLC signals into a request is not approved yet (CR-029): nothing is processed.", null, null));
        return engines.EngineFor(stationId) == StationEngine.Task
            ? (snapshot, await ToTaskAsync(snapshot, stationId, ct))
            : (snapshot, await requests.HandleRequestAsync(snapshot, ct));   // an unassigned station is simulated on the transport order engine
    }

    /// <summary>
    /// CR-042 (OQ-42-03): a simulated read of a station assigned to the task engine: the same approved station-to-flow mapping, the same key (PlcId:Station:Request_ID) and the same
    /// orchestrator as the physical intake. One read = one request; it creates nothing in Real mode.
    /// </summary>
    private async Task<PlcRequestResult> ToTaskAsync(PlcRequestSnapshot s, string station, CancellationToken ct)
    {
        PlcRequestResult Reject(string code, string message) => new(PlcRequestOutcome.Rejected, code, message, null, null);
        if (string.IsNullOrWhiteSpace(s.RequestId)) return Reject("REQUEST_ID_MISSING", "Request_ID could not be read.");
        if (s.PickupRequest && s.DeliveryRequest) return Reject("CONFLICTING_REQUESTS", "Pickup and Delivery are both requested; neither is processed.");
        if (!s.PickupRequest && !s.DeliveryRequest) return Reject("NO_REQUEST", "No request bit is set.");
        var signal = s.PickupRequest ? PlcRequestAssembler.PickupRequest : PlcRequestAssembler.DeliveryRequest;
        var map = plc.Value.Integration.StationFlows.FirstOrDefault(m => m.Station == station && m.Signal == signal && !string.IsNullOrWhiteSpace(m.Flow));
        if (map is null) return Reject(StationEngineCodes.NoFlow, $"No approved flow is mapped to {signal} of station {station}: nothing is created.");
        if (await uow.ExecuteAsync(async t => await routing.GetTransactionByRequestIdAsync(s.PlcId, s.RequestId, t), ct) is { } other && other.PortId == station)
            return Reject(StationEngineCodes.TakenByOther, "The transport order engine already received this Request_ID of this station.");
        if (await uow.ExecuteAsync(t => modeGate.ModeForNewWorkAsync(t), ct) == WCS.Domain.Rcs.ExecutionMode.Real) return Reject(SimulationCodes.RealMode, SimulationCodes.RealModeMessage);
        try
        {
            var r = await tasks.CreateTaskAsync(new CreateTaskCommand($"{s.PlcId}:{station}:{s.RequestId.Trim()}", map.Flow, station, "PLC-SIMULATION", SimulationOnly: true), ct);
            return new(r.Created ? PlcRequestOutcome.Accepted : PlcRequestOutcome.Duplicate, r.Created ? "ACCEPTED" : "DUPLICATE_REQUEST_ID",
                r.Created ? "Request accepted by the task engine." : "This Request_ID was already processed.", null, null, r.Task.Id);
        }
        catch (AppException e) { return Reject(e.Code, e.Message); }
    }
}

/// <summary>CR-041: the physical side: the values the PLC runtime read for one station, as the same name/value set the simulation produces. It reads only; nothing schedules it yet.</summary>
public sealed class PhysicalPlcInputAdapter(IPlcInputHandler handler)
{
    public Task<(PlcRequestSnapshot Snapshot, PlcRequestResult Result)> SubmitAsync(WCS.Application.Plc.StationSnapshot station, CancellationToken ct = default) =>
        handler.HandleAsync(PlcInputSource.Physical, station.PlcId, station.StationId, Normalize(station), ct);

    /// <summary>Only PLC → WCS points that were read with good quality; an unread or bad point is absent, never "OFF" by assumption.</summary>
    public static IReadOnlyDictionary<string, string> Normalize(WCS.Application.Plc.StationSnapshot station) => station.Points
        .Where(p => p.Direction == nameof(WCS.Domain.Plc.PlcSignalDirection.PlcToSoftware) && p.Quality == nameof(WCS.Application.Plc.PointQuality.Good) && p.Value is not null)
        .ToDictionary(p => p.SignalName, p => SignalText(p.Value!), StringComparer.Ordinal);

    private static string SignalText(object value) => value switch
    {
        WCS.Application.Plc.PointValue v => SignalText(v.Value),
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };
}

/// <summary>Simulated input values per station, in memory only: a WCS restart or switching the simulation off clears them. Nothing is written to a PLC.</summary>
public sealed class PlcSimulatedInputs
{
    private readonly ConcurrentDictionary<(string Station, string Signal), (string Value, DateTimeOffset At)> _values = new();
    public void Set(string station, string signal, string value, DateTimeOffset at) => _values[(station, signal)] = (value, at);
    public (string Value, DateTimeOffset At)? Get(string station, string signal) => _values.TryGetValue((station, signal), out var v) ? v : null;
    public IReadOnlyDictionary<string, string> Of(string station) => _values.Where(kv => kv.Key.Station == station).ToDictionary(kv => kv.Key.Signal, kv => kv.Value.Value, StringComparer.Ordinal);
    public void Clear() => _values.Clear();
    public void Clear(string station) { foreach (var k in _values.Keys.Where(k => k.Station == station)) _values.TryRemove(k, out _); }
}

public sealed record SimSignalDto(string Signal, string DataType, string Description, string? Value, DateTimeOffset? ChangedAt, bool ReadByIntake, string? Address = null);
public sealed record SimStationDto(string StationId, string PlcId, string LocationCode, bool? PlcOnline, bool PortMappingApproved, StationTransactionDto? ActiveTransaction, IReadOnlyList<SimSignalDto> Signals,
    string Engine = "Unassigned");
