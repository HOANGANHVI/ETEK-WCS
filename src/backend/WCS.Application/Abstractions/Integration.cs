using WCS.Domain.Common;

namespace WCS.Application.Abstractions;

// ---------------------------------------------------------------------------------------------
// Vendor-neutral integration contracts. Business logic depends ONLY on these. Concrete adapters
// (Mitsubishi MC Protocol, HIK RCS-2000) are NOT implemented in Phase 1 (no addresses/APIs confirmed).
// ---------------------------------------------------------------------------------------------

public enum PlcConnectionStatus { Disconnected = 0, Connecting = 1, Connected = 2, Faulted = 3 }

/// <summary>Logical PLC signal reference. The key->device address mapping lives in configuration owned by the PLC team.</summary>
public sealed record PlcSignalValue(string Key, int Value);

/// <summary>integration-requirements.md 5.2 PLC adapter. Independent from process flow and from any PLC library.</summary>
public interface IPlcAdapter
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PlcSignalValue>> ReadSignalsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);
    Task WriteSignalsAsync(IReadOnlyCollection<PlcSignalValue> values, CancellationToken ct = default);
    PlcConnectionStatus GetConnectionStatus();
    Task ReconnectAsync(CancellationToken ct = default);
}

public enum TransferDirection { Load = 1, Unload = 2 }

/// <summary>A request raised by a station through the single PLC connection (request identity prevents duplicates).</summary>
public sealed record StationRequest(string StationCode, string Flow, string RequestKey);

/// <summary>Maps logical station handshake signals onto the PLC adapter (5.3). Per-station, no shared request bit.</summary>
public interface IStationSignalService
{
    Task<IReadOnlyList<StationRequest>> PollRequestsAsync(CancellationToken ct = default);
    Task AcknowledgeRequestAsync(StationRequest request, CancellationToken ct = default);
    /// <summary>Ask the PLC to allow the Load/Unload mechanism for the AGV at this station.</summary>
    Task RequestTransferPermissionAsync(string stationCode, TransferDirection direction, Guid taskId, CancellationToken ct = default);
}

public enum RcsCallOutcome { Accepted = 1, Rejected = 2, Unknown = 3 }

public sealed record RcsCreateTaskRequest(string RequestCode, ProcessFlow Flow, RackKind RackKind, string? RobotCode,
    string SourceLocationCode, string RequestLocationCode);

public sealed record RcsCallResult(RcsCallOutcome Outcome, string? RcsTaskCode, string? Message);

/// <summary>Vendor-neutral task status. Mapping from HIK codes (0..10) belongs to the real adapter (3.8).</summary>
public enum RcsTaskStatus { NotFound = 0, Created = 1, Executing = 2, Completed = 3, Cancelling = 4, Cancelled = 5, Interrupted = 6, Unknown = 7 }

public sealed record RcsAgvStatus(string RobotCode, bool Online, string? Detail);

/// <summary>integration-requirements.md 5.4 RCS client (create / continue / cancel / query).</summary>
public interface IRcsClient
{
    Task<RcsCallResult> CreateTaskAsync(RcsCreateTaskRequest request, CancellationToken ct = default);
    Task<RcsCallResult> ContinueTaskAsync(string requestCode, string? rcsTaskCode, string destinationLocationCode, CancellationToken ct = default);
    Task<RcsCallResult> CancelTaskAsync(string requestCode, string? rcsTaskCode, CancellationToken ct = default);
    /// <summary>Query by WCS request code (used before any re-send after a timeout / unknown result).</summary>
    Task<(RcsTaskStatus Status, string? RcsTaskCode)> QueryTaskStatusAsync(string requestCode, CancellationToken ct = default);
    Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default);
    /// <summary>
    /// CR-027: the whole AMR fleet in ONE query (queryAgvStatus without a robot list). Default: not supported (an adapter that cannot list the fleet says so; it never invents one).
    /// </summary>
    Task<WCS.Application.Fleet.RcsFleetResult> QueryFleetAsync(CancellationToken ct = default) =>
        Task.FromResult(WCS.Application.Fleet.RcsFleetResult.Fail(WCS.Application.Fleet.FleetErrorCodes.NotSupported, "This RCS client cannot list the fleet."));
}

/// <summary>CR-021: an <see cref="IRcsClient"/> that wraps another one (for example to log calls). Safety checks that ask "which adapter is this?" must look through it with <see cref="Unwrap"/>.</summary>
public interface IRcsClientDecorator
{
    IRcsClient Inner { get; }
    static IRcsClient Unwrap(IRcsClient client) { while (client is IRcsClientDecorator d) client = d.Inner; return client; }
}

/// <summary>CR-008A: whether tasks may currently be sent to the RCS adapter. Code is a stable identifier (RCS_NOT_CONNECTED when only the simulator exists in Production).</summary>
public sealed record RcsDispatchStatus(bool Allowed, string Code, string Message);

/// <summary>CR-008A (AUDIT-001): Production must never dispatch a task to the RCS simulator. Business code asks this guard before creating/dispatching a task.</summary>
public interface IRcsDispatchGuard
{
    RcsDispatchStatus Check();
}
