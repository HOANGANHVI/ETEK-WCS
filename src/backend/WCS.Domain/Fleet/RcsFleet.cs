namespace WCS.Domain.Fleet;

/// <summary>Where a fleet snapshot came from. Simulator rows and Real rows are stored apart and never shown as each other (CR-027).</summary>
public enum FleetSource { Simulator = 1, Real = 2 }

/// <summary>
/// CR-027: the last known state of ONE AMR as reported by the RCS (<c>queryAgvStatus</c>, RCS-2000 API V3.3, 5.2.14). It is a read model: it never replaces the <c>agvs</c> table
/// (the business AGV1/AGV2 rows that tasks point to) and never has a task attached. Values are kept exactly as the RCS sends them (strings, API units: mm, mm/s, degrees, %).
/// A row is never deleted when the AMR disappears from a response; <see cref="LastSeenAt"/> simply stops moving.
/// </summary>
public class RcsAmrSnapshot
{
    private RcsAmrSnapshot() { }

    public RcsAmrSnapshot(FleetSource source, string robotCode, DateTimeOffset now)
    {
        Source = source; RobotCode = robotCode; FirstSeenAt = now; LastSeenAt = now; UpdatedAt = now;
    }

    public FleetSource Source { get; private set; }
    public string RobotCode { get; private set; } = default!;
    public string? RobotDir { get; private set; }
    public string? RobotIp { get; private set; }
    public string? Battery { get; private set; }
    public string? PosX { get; private set; }
    public string? PosY { get; private set; }
    public string? MapCode { get; private set; }
    public string? Speed { get; private set; }
    /// <summary>RCS "AMR status" code (API Appendix B), kept as sent: unknown codes are shown as unknown, never guessed.</summary>
    public string? Status { get; private set; }
    public string? ExclType { get; private set; }
    public string? Stop { get; private set; }
    public string? PodCode { get; private set; }
    public string? PodDir { get; private set; }
    /// <summary>JSON array of the "[x,y,dir]" strings the RCS sent.</summary>
    public string? PathJson { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    /// <summary>The time of the last successful response that contained this AMR.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Applies one response entry. Returns true when a reported value changed (a move of <see cref="LastSeenAt"/> alone is not a change).</summary>
    public bool Apply(string? robotDir, string? robotIp, string? battery, string? posX, string? posY, string? mapCode, string? speed, string? status, string? exclType,
        string? stop, string? podCode, string? podDir, string? pathJson, DateTimeOffset now)
    {
        var changed = RobotDir != robotDir || RobotIp != robotIp || Battery != battery || PosX != posX || PosY != posY || MapCode != mapCode || Speed != speed
                      || Status != status || ExclType != exclType || Stop != stop || PodCode != podCode || PodDir != podDir || PathJson != pathJson;
        RobotDir = robotDir; RobotIp = robotIp; Battery = battery; PosX = posX; PosY = posY; MapCode = mapCode; Speed = speed; Status = status; ExclType = exclType;
        Stop = stop; PodCode = podCode; PodDir = podDir; PathJson = pathJson;
        LastSeenAt = now;
        if (changed) UpdatedAt = now;
        return changed;
    }
}

/// <summary>CR-027: the health of the fleet query itself, one row per <see cref="FleetSource"/>.</summary>
public class RcsFleetState
{
    private RcsFleetState() { }
    public RcsFleetState(FleetSource source) { Source = source; }

    public FleetSource Source { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public bool LastAttemptOk { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int LastAmrCount { get; private set; }
    public long? LastLatencyMs { get; private set; }

    public void Succeeded(DateTimeOffset now, int amrCount, long? latencyMs)
    {
        LastAttemptAt = now; LastSuccessAt = now; LastAttemptOk = true; LastErrorCode = null; LastErrorMessage = null; ConsecutiveFailures = 0; LastAmrCount = amrCount; LastLatencyMs = latencyMs;
    }

    public void Failed(DateTimeOffset now, string code, string? message, long? latencyMs)
    {
        LastAttemptAt = now; LastAttemptOk = false; LastErrorCode = code; LastErrorMessage = message is { Length: > 300 } ? message[..300] : message; ConsecutiveFailures++; LastLatencyMs = latencyMs;
    }
}
