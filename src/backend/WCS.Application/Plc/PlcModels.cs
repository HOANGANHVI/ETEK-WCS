using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>PLC device kinds supported by the driver. M/X/Y are bit devices, D/W are word devices (CR-003; CR-002 had M and D only).</summary>
public enum PlcDevice { M = 1, D = 2, W = 3, X = 4, Y = 5 }

/// <summary>
/// Numbering base of the devices whose notation differs between PLC families. D and M are always decimal. For X/Y and W the base is a
/// PLC engineer decision (NEEDS_CONFIRMATION) and is never defaulted: MELSEC-Q writes X, Y and W in HEXADECIMAL, while the reference
/// library documents X/Y as octal (FX/iQ-F convention) and W as decimal. An address using a device whose base is null is rejected.
/// </summary>
public sealed record AddressBases(int? Xy = null, int? W = null)
{
    public static readonly AddressBases None = new();
    public static bool IsValidXy(int b) => b is 8 or 16;
    public static bool IsValidW(int b) => b is 10 or 16;
}

public enum AddressProblem { None = 0, Format = 1, BaseUnconfirmed = 2 }

public readonly record struct AddressParse(DeviceAddress? Address, AddressProblem Problem, string? Message);

/// <summary>A parsed device address such as M9000, D1500, X10 or Y20 (the number is the value sent in the MC frame, always decimal here).</summary>
public readonly record struct DeviceAddress(PlcDevice Device, int Number)
{
    public const int MaxNumber = 0xFFFFFF; // 3 bytes in the MC 3E "head device number"

    public bool IsBitDevice => Device is PlcDevice.M or PlcDevice.X or PlcDevice.Y;
    public bool IsWordDevice => !IsBitDevice;

    public override string ToString() => $"{Device}{Number}";

    public static AddressParse Parse(string? text, AddressBases? bases = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(null, AddressProblem.Format, "Địa chỉ trống.");
        var t = text.Trim().ToUpperInvariant();
        if (t.Length < 2 || t.Length > 9) return new(null, AddressProblem.Format, $"Địa chỉ '{text}' không hợp lệ.");
        var device = t[0] switch { 'M' => PlcDevice.M, 'D' => PlcDevice.D, 'W' => PlcDevice.W, 'X' => PlcDevice.X, 'Y' => PlcDevice.Y, _ => (PlcDevice?)null };
        if (device is null) return new(null, AddressProblem.Format, $"Địa chỉ '{text}' không hợp lệ (thiết bị hỗ trợ: M, D, W, X, Y).");
        int radix;
        switch (device)
        {
            case PlcDevice.M or PlcDevice.D: radix = 10; break;
            case PlcDevice.X or PlcDevice.Y:
                if (bases?.Xy is not { } xy || !AddressBases.IsValidXy(xy))
                    return new(null, AddressProblem.BaseUnconfirmed, $"Cơ số địa chỉ X/Y (bát phân hay thập lục phân) chưa được kỹ sư PLC xác nhận (NEEDS_CONFIRMATION) nên không thể đổi '{t}' sang số thiết bị.");
                radix = xy; break;
            default:
                if (bases?.W is not { } w || !AddressBases.IsValidW(w))
                    return new(null, AddressProblem.BaseUnconfirmed, $"Cơ số địa chỉ W (thập phân hay thập lục phân) chưa được kỹ sư PLC xác nhận (NEEDS_CONFIRMATION) nên không thể đổi '{t}' sang số thiết bị.");
                radix = w; break;
        }
        long n = 0;
        for (var i = 1; i < t.Length; i++)
        {
            var c = t[i];
            var d = c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
            if (d < 0 || d >= radix) return new(null, AddressProblem.Format, $"Địa chỉ '{text}' không hợp lệ: ký tự '{c}' không thuộc hệ cơ số {radix}.");
            n = n * radix + d;
            if (n > MaxNumber) return new(null, AddressProblem.Format, $"Địa chỉ '{text}' vượt quá phạm vi số thiết bị của khung MC (0xFFFFFF).");
        }
        return new(new DeviceAddress(device.Value, (int)n), AddressProblem.None, null);
    }

    /// <summary>Compatibility overload (CR-002): only M and D can be parsed without a confirmed numbering base.</summary>
    public static bool TryParse(string? text, out DeviceAddress address) => TryParse(text, null, out address);

    public static bool TryParse(string? text, AddressBases? bases, out DeviceAddress address)
    {
        var r = Parse(text, bases);
        address = r.Address ?? default;
        return r.Address is not null;
    }

    /// <summary>Canonical stored text: M/D in plain decimal, X/Y/W upper-case in the notation the engineer used (leading zeros removed, never converted).</summary>
    public static string Normalize(string text, AddressBases? bases = null)
    {
        var t = text.Trim().ToUpperInvariant();
        if (t.Length >= 2 && (t[0] is 'X' or 'Y' or 'W') && Parse(t, bases).Address is not null)
        {
            var digits = t[1..].TrimStart('0');
            return t[0] + (digits.Length == 0 ? "0" : digits);
        }
        return TryParse(t, bases, out var a) ? a.ToString() : t;
    }
}

/// <summary>Data-quality of one IO value as seen by WCS.</summary>
public enum PointQuality
{
    /// <summary>No read happened yet.</summary>
    Unknown = 0,
    /// <summary>The point has no confirmed address (NEEDS_CONFIGURATION): it is never read or written.</summary>
    NotConfigured = 1,
    /// <summary>Fresh value read from the PLC.</summary>
    Good = 2,
    /// <summary>The last read of this point failed (PLC answered with an error, bad data, ...).</summary>
    Bad = 3,
    /// <summary>The PLC link is down; the value shown is the last known one.</summary>
    Disconnected = 4,
    /// <summary>Value produced by the simulator (never a real device).</summary>
    Simulated = 5,
}

/// <summary>A value read from or written to the PLC. <see cref="Value"/> is bool (BOOL), int (INT) or string (STRING).</summary>
public sealed record PointValue(PlcDataType Type, object Value)
{
    public static PointValue OfBool(bool v) => new(PlcDataType.Bool, v);
    public static PointValue OfInt(int v) => new(PlcDataType.Int, v);
    public static PointValue OfString(string v) => new(PlcDataType.String, v);
    public override string ToString() => Value switch { bool b => b ? "1" : "0", _ => Value.ToString() ?? "" };
}

/// <summary>
/// Immutable description of a point handed to an endpoint (no database entities cross this boundary). <c>Address</c> is the address entered
/// by an Admin (shown in the Admin view); <c>Pollable</c> is true only when that address was also APPROVED by the PLC engineer (CR-003):
/// a real PLC is never read or written at an address that is not approved.
/// </summary>
public sealed record IoPointRef(Guid Id, string PlcId, string StationId, string SignalName, PlcSignalDirection Direction, PlcDataType DataType,
    string? Address, int? WordLength, string? Encoding, bool WriteAllowed, string Group, bool Pollable = true);

/// <summary>Result of reading one point.</summary>
public sealed record PointReading(Guid PointId, PointValue? Value, PointQuality Quality, string? Error);

public enum EndpointState { Disconnected = 0, Connecting = 1, Connected = 2, Faulted = 3, NeedsConfiguration = 4, Disabled = 5 }

/// <summary>Communication counters of one PLC connection (diagnostics; no payloads).</summary>
public sealed record PlcDiagnostics(long Requests, long Failures, long Timeouts, long Reconnects, int? LastEndCode, string? LastError,
    long? LastLatencyMs, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastErrorAt, long BytesSent, long BytesReceived,
    PlcExchange? LastExchange = null, PlcExchange? LastFailedExchange = null, string? LastErrorCode = null, string? LastSocketError = null);

/// <summary>
/// Technical record of one request/response pair on a PLC connection (CR-003). Contains protocol data only (hex of the frames, no secrets).
/// ADMIN ONLY: it is never part of an Operator view.
/// </summary>
public sealed record PlcExchange(DateTimeOffset At, string PlcId, string Operation, string Command, string Device, int StartAddress, int Points,
    long DurationMs, int? EndCode, string Outcome, string? Error, string RequestHex, string? ResponseHex, bool IsAmbiguous);

/// <summary>
/// A write was (possibly) transmitted but no confirming response was received. The PLC may or may not have executed it, so the write is
/// NEVER retried automatically; the value must be read back and checked by a person or an approved procedure.
/// </summary>
public sealed class PlcWriteAmbiguousException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One PLC connection as seen by the runtime: connect, read all confirmed points of a station, write one point. Implemented by the
/// Mitsubishi MC driver (real) and by the in-memory simulator. A single worker is the only caller, so no two commands overlap.
/// </summary>
public interface IPlcEndpoint : IAsyncDisposable
{
    EndpointState State { get; }
    PlcDiagnostics Diagnostics { get; }
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync();
    /// <summary>Reads every point of the station that has a confirmed address; others are reported as NotConfigured without any traffic.</summary>
    Task<IReadOnlyList<PointReading>> ReadStationAsync(IReadOnlyList<IoPointRef> points, CancellationToken ct);
    Task WritePointAsync(IoPointRef point, PointValue value, CancellationToken ct);
}

/// <summary>Everything needed to build the endpoint of one PLC (copied out of the database entity).</summary>
public sealed record PlcEndpointConfig(string PlcId, string IpAddress, int? Port, string? Protocol, int? NetworkNumber, int? PcNumber,
    int? ModuleIoNumber, int? PlcStationNumber, int? MonitoringTimer, int? TimeoutMs, int? PollingIntervalMs,
    int? ConnectTimeoutMs = null, int? ReadTimeoutMs = null, int? WriteTimeoutMs = null, int? XyAddressBase = null, int? WAddressBase = null)
{
    public AddressBases Bases => new(XyAddressBase, WAddressBase);
}

public interface IPlcEndpointFactory
{
    IPlcEndpoint Create(PlcRuntimeMode mode, PlcEndpointConfig config);
}

/// <summary>Deployment-level PLC options (appsettings "Plc"). All real-hardware behaviour is opt-in.</summary>
public sealed class PlcRuntimeOptions
{
    public const string SectionName = "Plc";
    /// <summary>Simulator (default), RealReadOnly or RealReadWrite. Never switched automatically.</summary>
    public PlcRuntimeMode Mode { get; set; } = PlcRuntimeMode.Simulator;
    /// <summary>Start the PLC workers with the API. Tests switch it off.</summary>
    public bool RuntimeEnabled { get; set; } = true;
    /// <summary>Second key for real connections: without it no socket to a physical PLC is ever opened.</summary>
    public bool AllowLiveConnection { get; set; }
    public PlcWriteOptions Write { get; set; } = new();
    /// <summary>Technical polling period used by the simulator only (real PLCs use PollingIntervalMs from the confirmed configuration).</summary>
    public int SimulatorPollingIntervalMs { get; set; } = 1000;
    public int ReconnectInitialDelayMs { get; set; } = 1000;
    public int ReconnectMaxDelayMs { get; set; } = 15000;
    public int LogRetentionDays { get; set; } = 30;
    /// <summary>CR-020: a Good value not refreshed for longer than this is shown as Stale on the monitoring screen (polling normally refreshes it every cycle).</summary>
    public int StaleAfterSeconds { get; set; } = 15;
    /// <summary>Diagnostics: MC request/response pairs kept per PLC in PostgreSQL (older rows are pruned).</summary>
    public int ExchangeHistoryPerPlc { get; set; } = 300;
    /// <summary>Diagnostics: successful exchanges are stored at most once per PLC per this many seconds (failures are always stored). 0 = store all.</summary>
    public int ExchangeOkSampleSeconds { get; set; } = 5;
    public PlcIntegrationOptions Integration { get; set; } = new();
    public PlcHandshakeOptions Handshake { get; set; } = new();
}

/// <summary>
/// Deployment key required before any write to a real PLC (CR-019): only <see cref="Enabled"/> (plus Mode=RealReadWrite and AllowLiveConnection). The former engineer-approval reference and handshake-approval
/// settings were removed: the single approval is the Admin's per-point write approval. Old environment variables for them are simply ignored by configuration binding.
/// </summary>
public sealed class PlcWriteOptions
{
    public bool Enabled { get; set; }
    /// <summary>CR-044 (OW-2): the points a real PLC may be written at, declared by the deployment. Empty by default: no real write.</summary>
    public List<PlcWriteDeclaration> AllowList { get; set; } = [];
    /// <summary>CR-044 (OW-2): registers no real write may reach, even when declared ("D1600" or "D1600-D1601"). Set in appsettings.json.</summary>
    public List<string> ReservedAddresses { get; set; } = [];
}

public sealed class PlcIntegrationOptions
{
    /// <summary>Feed station requests of approved flow mappings into the WCS task flow. Default off (monitor-only).</summary>
    public bool Enabled { get; set; }
    /// <summary>Approved mappings "station + request signal -> process flow". Empty by default: no station creates tasks.</summary>
    public List<StationFlowMapping> StationFlows { get; set; } = [];

    /// <summary>CR-042 (OQ-42-03): which engine takes the requests of a station ("Task" = Phase 1 flows, "TransportOrder" = database processes). Empty = see <see cref="StationEngineRouter"/>.</summary>
    public List<StationEngineAssignment> StationEngines { get; set; } = [];
}

public sealed class StationEngineAssignment
{
    public string Station { get; set; } = "";
    public string Engine { get; set; } = "";
}

public sealed class StationFlowMapping
{
    public string Station { get; set; } = "";
    /// <summary>Pickup_Request or Delivery_Request.</summary>
    public string Signal { get; set; } = "";
    public string Flow { get; set; } = "";
}

/// <summary>Raised when a real connection is requested while confirmed parameters are still missing (never replaced by guessed values).</summary>
public sealed class NeedsConfigurationException(IReadOnlyList<string> missing)
    : InvalidOperationException("NEEDS_CONFIGURATION: " + string.Join(", ", missing))
{
    public IReadOnlyList<string> Missing { get; } = missing;
}
