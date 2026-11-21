using WCS.Domain.Fleet;

namespace WCS.Application.Fleet;

/// <summary>
/// One entry of the <c>data[]</c> array of the RCS <c>queryAgvStatus</c> response (RCS-2000 API Developer Guide V3.3, 5.2.14, pp. 46-48). Every value is a string exactly as the
/// RCS sends it; units are those of the API (X/Y millimetre, speed mm/s, direction degrees, battery percent). Nothing is converted or interpreted here.
/// </summary>
public sealed record RcsFleetAmr(string RobotCode, string? RobotDir, string? RobotIp, string? Battery, string? PosX, string? PosY, string? MapCode, string? Speed,
    string? Status, string? ExclType, string? Stop, string? PodCode, string? PodDir, IReadOnlyList<string> Path);

/// <summary>Outcome of ONE fleet query. A failed query never carries AMRs: the caller keeps what it stored before and marks it as not updated.</summary>
public sealed record RcsFleetResult(bool Success, IReadOnlyList<RcsFleetAmr> Amrs, string? ErrorCode = null, string? ErrorMessage = null, long? LatencyMs = null, int Skipped = 0, int? HttpStatus = null,
    string? RcsCode = null, string? RcsMessage = null, string? RequestCode = null, string? Endpoint = null)
{
    public static RcsFleetResult Ok(IReadOnlyList<RcsFleetAmr> amrs, long? latencyMs = null, int skipped = 0) => new(true, amrs, null, null, latencyMs, skipped);
    public static RcsFleetResult Fail(string code, string? message, long? latencyMs = null) => new(false, [], code, message, latencyMs);
}

/// <summary>Stable error codes of the fleet query (shown to Admins, translated by the UI).</summary>
public static class FleetErrorCodes
{
    public const string NotConfigured = "RCS_NOT_CONFIGURED";
    public const string Timeout = "RCS_TIMEOUT";
    public const string Unreachable = "RCS_UNREACHABLE";
    /// <summary>The TCP / HTTP connection could not be made (refused, no route, DNS...). Not a timeout and not an answer of the RCS.</summary>
    public const string ConnectionFailed = "RCS_CONNECTION_FAILED";
    public const string Http = "RCS_HTTP_ERROR";
    public const string InvalidResponse = "RCS_RESPONSE_INVALID";
    public const string Rejected = "RCS_CODE_NOT_SUCCESS";
    public const string NotSupported = "RCS_FLEET_NOT_SUPPORTED";
}

/// <summary>
/// CR-027: where the list of AMRs comes from. <see cref="Source"/> says whether the data is simulated or real; the two are stored and shown apart.
/// The real implementation only sends <c>queryAgvStatus</c> (a read-only query): it never creates, continues or cancels a task.
/// </summary>
public interface IRcsFleetQuery
{
    FleetSource Source { get; }
    Task<RcsFleetResult> QueryAsync(CancellationToken ct = default);
}

public interface IRcsFleetRepository
{
    Task<RcsFleetState?> GetStateAsync(FleetSource source, CancellationToken ct = default);
    void AddState(RcsFleetState state);
    Task<IReadOnlyList<RcsAmrSnapshot>> ListAsync(FleetSource source, CancellationToken ct = default);
    void Add(RcsAmrSnapshot snapshot);
}

public sealed class RcsFleetOptions
{
    public const string SectionName = "Rcs:Fleet";
    /// <summary>"Simulator" (default: the in-memory RCS simulator; empty unless a test or Development fills it) or "Real" (HTTP queryAgvStatus to the RCS).</summary>
    public string Source { get; set; } = "Simulator";
    /// <summary>Seconds between two queries. The HIK API document asks for 5 s below 100 AMRs, 10 s for 100-200, 15 s for 200-300.</summary>
    public int IntervalSeconds { get; set; } = 5;
    /// <summary>Seconds to wait for one query before it counts as a timeout.</summary>
    public int TimeoutSeconds { get; set; } = 5;
    /// <summary>Data older than this is shown as stale. 0 = three intervals (at least 15 s).</summary>
    public int StaleAfterSeconds { get; set; }
    public RcsFleetRealOptions Real { get; set; } = new();

    public int EffectiveInterval => Math.Max(1, IntervalSeconds);
    public int EffectiveStaleAfter => StaleAfterSeconds > 0 ? StaleAfterSeconds : Math.Max(15, EffectiveInterval * 3);
    public FleetSource SourceKind => string.Equals(Source, "Real", StringComparison.OrdinalIgnoreCase) ? FleetSource.Real : FleetSource.Simulator;
}

/// <summary>Where the real query goes. The defaults are the ones written in the API document (5.2.14); the host comes from the saved RCS connection setting.</summary>
public sealed class RcsFleetRealOptions
{
    /// <summary>Override of the RCS port. 0 (default) = the port saved in the RCS connection (the value the site confirmed); nothing is assumed. (The API document 5.2.14 shows "http://[address]:8182/rcms-dps/rest/queryAgvStatus".) If both are missing the query reports a configuration error.</summary>
    public int Port { get; set; }
    public string Path { get; set; } = "/rcms-dps/rest/queryAgvStatus";
}

// ---- what the API returns to the browser
public sealed record FleetAmrView(string RobotCode, string State, string? RobotDir, string? RobotIp, string? Battery, string? PosX, string? PosY, string? MapCode, string? Speed,
    string? Status, string? ExclType, string? Stop, string? PodCode, string? PodDir, IReadOnlyList<string> Path, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt,
    string? LinkedAgvCode, Guid? ActiveTaskId, string? ActiveTaskState);

/// <param name="Connection">Connected = the last query succeeded and is recent; Disconnected = the last query failed; NoData = no query has ever run.</param>
/// <param name="SimulatedData">True when the list comes from the RCS simulator: it must never be presented as real AMRs.</param>
public sealed record FleetView(string Source, bool SimulatedData, string Connection, DateTimeOffset? LastAttemptAt, DateTimeOffset? LastSuccessAt, string? LastErrorCode,
    string? LastErrorMessage, int ConsecutiveFailures, int IntervalSeconds, int StaleAfterSeconds, int Count, int FreshCount, IReadOnlyList<FleetAmrView> Amrs,
    string DispatchMode = "Legacy", int MaxInFlightTasks = 0, int TasksInFlight = 0, int TasksAwaitingAssignment = 0, int TasksQueued = 0,
    string? DispatchModePending = null);   // CR-028 (additive): the dispatch mode and the task counters of RcsManaged dispatch

public static class FleetStates
{
    public const string Fresh = "Fresh";
    public const string Stale = "Stale";
    public const string Missing = "Missing";
    public const string Connected = "Connected";
    public const string Disconnected = "Disconnected";
    public const string NoData = "NoData";
}
