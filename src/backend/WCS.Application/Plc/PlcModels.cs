using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>PLC device kinds supported by the driver (CR-002 scope: M bit devices and D word devices only).</summary>
public enum PlcDevice { M = 1, D = 2 }

/// <summary>A parsed device address such as M9000 or D1500.</summary>
public readonly record struct DeviceAddress(PlcDevice Device, int Number)
{
    public const int MaxNumber = 0xFFFFFF; // 3 bytes in the MC 3E "head device number"

    public override string ToString() => $"{Device}{Number}";

    public static bool TryParse(string? text, out DeviceAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToUpperInvariant();
        if (t.Length < 2 || t.Length > 9 || (t[0] != 'M' && t[0] != 'D')) return false;
        if (!t.Skip(1).All(char.IsAsciiDigit) || !int.TryParse(t.AsSpan(1), out var n) || n < 0 || n > MaxNumber) return false;
        address = new DeviceAddress(t[0] == 'M' ? PlcDevice.M : PlcDevice.D, n);
        return true;
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

/// <summary>Immutable description of a point handed to an endpoint (no database entities cross this boundary).</summary>
public sealed record IoPointRef(Guid Id, string PlcId, string StationId, string SignalName, PlcSignalDirection Direction, PlcDataType DataType,
    string? Address, int? WordLength, string? Encoding, bool WriteAllowed, string Group);

/// <summary>Result of reading one point.</summary>
public sealed record PointReading(Guid PointId, PointValue? Value, PointQuality Quality, string? Error);

public enum EndpointState { Disconnected = 0, Connecting = 1, Connected = 2, Faulted = 3, NeedsConfiguration = 4, Disabled = 5 }

/// <summary>Communication counters of one PLC connection (diagnostics; no payloads).</summary>
public sealed record PlcDiagnostics(long Requests, long Failures, long Timeouts, long Reconnects, int? LastEndCode, string? LastError,
    long? LastLatencyMs, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastErrorAt, long BytesSent, long BytesReceived);

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
    int? ModuleIoNumber, int? PlcStationNumber, int? MonitoringTimer, int? TimeoutMs, int? PollingIntervalMs);

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
    public PlcIntegrationOptions Integration { get; set; } = new();
}

/// <summary>Extra keys required before any write to a real PLC. All must be provided by the deployment, none has a default.</summary>
public sealed class PlcWriteOptions
{
    public bool Enabled { get; set; }
    /// <summary>Reference of the PLC engineer's written approval (document/ticket id). Required for REAL_READ_WRITE.</summary>
    public string? EngineerApprovalReference { get; set; }
    /// <summary>Set to true only after the handshake sheet has been completed and approved.</summary>
    public bool HandshakeApproved { get; set; }
}

public sealed class PlcIntegrationOptions
{
    /// <summary>Feed station requests of approved flow mappings into the WCS task flow. Default off (monitor-only).</summary>
    public bool Enabled { get; set; }
    /// <summary>Approved mappings "station + request signal -> process flow". Empty by default: no station creates tasks.</summary>
    public List<StationFlowMapping> StationFlows { get; set; } = [];
}

public sealed class StationFlowMapping
{
    public string Station { get; set; } = "";
    /// <summary>Pickup_Request or Delivery_Request.</summary>
    public string Signal { get; set; } = "";
    public string Flow { get; set; } = "";
}
