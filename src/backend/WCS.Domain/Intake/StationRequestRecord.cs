namespace WCS.Domain.Intake;

/// <summary>CR-008B-2: life cycle of a station request that WCS has detected but not necessarily accepted yet.</summary>
public enum StationRequestStatus
{
    /// <summary>Detected and stored; no task exists yet. Will be (re)processed - it survives a backend restart.</summary>
    Pending = 1,
    /// <summary>A task exists for the request key (created now or earlier). Never processed again.</summary>
    Accepted = 2,
    /// <summary>Permanently refused by a business rule (unsupported flow/station, ...). Never processed again.</summary>
    Rejected = 3,
    /// <summary>Automatic processing stopped after the retry limit; an operator has to look at it.</summary>
    Failed = 4,
}

/// <summary>
/// CR-008B-2: durable record of a request detected on a station (PLC rising edge or simulator). The PLC edge is seen only once and WCS never writes
/// <c>Request_Ack</c> (no approved handshake), so the request must be kept by WCS itself until a task exists. <see cref="RequestKey"/> is the idempotency key
/// (<c>PlcId:Station:Request_ID</c>): it is unique, so detecting the same request again - even after a restart - never creates a second record or task.
/// </summary>
public class StationRequestRecord
{
    private StationRequestRecord() { }

    public StationRequestRecord(string requestKey, string stationCode, string flow, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(requestKey)) throw new ArgumentException("request key is required", nameof(requestKey));
        Id = Guid.NewGuid();
        RequestKey = requestKey.Trim();
        StationCode = stationCode;
        Flow = flow;
        Status = StationRequestStatus.Pending;
        FirstSeenAt = now;
    }

    public Guid Id { get; private set; }
    public string RequestKey { get; private set; } = default!;
    public string StationCode { get; private set; } = default!;
    public string Flow { get; private set; } = default!;
    public StationRequestStatus Status { get; private set; }
    /// <summary>Processing attempts that failed for a technical reason (RCS_NOT_CONNECTED is a state, not an attempt, and is not counted).</summary>
    public int Attempts { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? TaskId { get; private set; }
    public string? LastError { get; private set; }
    public uint Version { get; private set; }

    public bool IsPending => Status == StationRequestStatus.Pending;

    public void MarkAccepted(Guid taskId, DateTimeOffset now)
    {
        Status = StationRequestStatus.Accepted; TaskId = taskId; DecidedAt = now; LastError = null;
    }

    public void MarkRejected(string code, string message, DateTimeOffset now)
    {
        Status = StationRequestStatus.Rejected; DecidedAt = now; LastError = Cut($"{code}: {message}");
    }

    public void RegisterFailure(string error, DateTimeOffset now)
    {
        Attempts++; LastAttemptAt = now; LastError = Cut(error);
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = StationRequestStatus.Failed; DecidedAt = now; LastError = Cut(error);
    }

    private static string Cut(string s) => s.Length <= 500 ? s : s[..500];
}
