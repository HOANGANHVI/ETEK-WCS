using Microsoft.Extensions.Options;

namespace WCS.Application.Plc;

/// <summary>Which engine takes the requests a station's PLC signals raise.</summary>
public enum StationEngine
{
    /// <summary>Not assigned: no physical signal creates anything (the current default); the simulation uses the transport order engine.</summary>
    Unassigned = 0,
    /// <summary>Phase 1 task engine (station-to-flow mapping).</summary>
    Task = 1,
    /// <summary>Transport order engine (database processes). The physical trigger rule is not approved yet (OQ-41-02).</summary>
    TransportOrder = 2,
    /// <summary>The configuration is unreadable or contradicts itself: neither engine takes anything.</summary>
    Invalid = 3,
}

public static class StationEngineCodes
{
    public const string IsTask = "STATION_ENGINE_IS_TASK";
    public const string IsOrder = "STATION_ENGINE_IS_ORDER";
    public const string Invalid = "STATION_ENGINE_INVALID";
    public const string TakenByOther = "REQUEST_TAKEN_BY_OTHER_ENGINE";
    public const string NoFlow = "STATION_FLOW_NOT_MAPPED";
}

public interface IStationEngineRouter { StationEngine EngineFor(string stationId); }

/// <summary>Asked by the task engine intake before it takes a request: refuses one that belongs to the transport order engine.</summary>
public interface IStationSignalOwnership { Task EnsureTaskEngineAsync(string station, string requestKey, CancellationToken ct = default); }

/// <summary>
/// CR-042 (OQ-42-03): the single answer to "who takes this station's signals", so that one signal is never taken by two engines. Source: <c>Plc:Integration:StationEngines</c>
/// (the approved per-station cut-over). A station without an entry keeps the behaviour before CR-042: the task engine when the PLC integration is enabled and a flow is mapped to it.
/// </summary>
public sealed class StationEngineRouter(IOptions<PlcRuntimeOptions> options) : IStationEngineRouter
{
    public StationEngine EngineFor(string stationId)
    {
        var integration = options.Value.Integration;
        var entries = integration.StationEngines.Where(x => string.Equals(x.Station?.Trim(), stationId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Engine?.Trim() ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (entries.Count > 1) return StationEngine.Invalid;
        if (entries.Count == 1)
            return string.Equals(entries[0], nameof(StationEngine.Task), StringComparison.OrdinalIgnoreCase) ? StationEngine.Task
                : string.Equals(entries[0], nameof(StationEngine.TransportOrder), StringComparison.OrdinalIgnoreCase) ? StationEngine.TransportOrder
                : StationEngine.Invalid;
        return integration.Enabled && integration.StationFlows.Any(m => string.Equals(m.Station, stationId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Flow))
            ? StationEngine.Task : StationEngine.Unassigned;
    }
}
