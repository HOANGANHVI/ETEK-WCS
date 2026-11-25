using System.Collections.Concurrent;
using System.Globalization;

namespace WCS.Application.Routing;

/// <summary>
/// CR-040: turns the PLC → WCS input signals of ONE station into the request a PLC runtime hands to <see cref="IPlcRequestService"/>. It is the only place that reads these signals by name
/// (the names of IO Map v2), so a simulated and a physical source cannot be interpreted differently. It decides nothing about the business: validation and processing stay in the request service.
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

/// <summary>Simulated input values per station, in memory only: a WCS restart or switching the simulation off clears them. Nothing is written to a PLC.</summary>
public sealed class PlcSimulatedInputs
{
    private readonly ConcurrentDictionary<(string Station, string Signal), (string Value, DateTimeOffset At)> _values = new();
    public void Set(string station, string signal, string value, DateTimeOffset at) => _values[(station, signal)] = (value, at);
    public (string Value, DateTimeOffset At)? Get(string station, string signal) => _values.TryGetValue((station, signal), out var v) ? v : null;
    public IReadOnlyDictionary<string, string> Of(string station) => _values.Where(kv => kv.Key.Station == station).ToDictionary(kv => kv.Key.Signal, kv => kv.Value.Value, StringComparer.Ordinal);
    public void Clear() => _values.Clear();
}

public sealed record SimSignalDto(string Signal, string DataType, string Description, string? Value, DateTimeOffset? ChangedAt, bool ReadByIntake);
public sealed record SimStationDto(string StationId, string PlcId, string LocationCode, bool? PlcOnline, bool PortMappingApproved, StationTransactionDto? ActiveTransaction, IReadOnlyList<SimSignalDto> Signals);
