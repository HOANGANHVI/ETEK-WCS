namespace WCS.Domain.RcsDiagnostics;

/// <summary>
/// CR-032: one manual call of an RCS-2000 API made from the Admin-only RCS Diagnostics module. It is a DIAGNOSTIC record only: it is never operational state, never read by dispatch, inventory or the fleet monitor.
/// The request and the response are stored MASKED (token / password / secret fields are replaced) and the response is bounded.
/// </summary>
public class RcsApiTest
{
    private RcsApiTest() { }

    public RcsApiTest(Guid id, DateTimeOffset executedAt, string username, string role, string api, string group, string requestId, string? endpoint,
        string outcome, bool success)
    {
        Id = id; ExecutedAt = executedAt; Username = username; UserRole = role; Api = api; Group = group; RequestId = requestId; Endpoint = endpoint; Outcome = outcome; Success = success;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset ExecutedAt { get; private set; }
    public string Username { get; private set; } = default!;
    public string UserRole { get; private set; } = default!;
    public string Api { get; private set; } = default!;
    public string Group { get; private set; } = default!;
    /// <summary>The <c>reqCode</c> sent (unique per execution unless the Admin chose it).</summary>
    public string RequestId { get; private set; } = default!;
    /// <summary>host:port/path, no scheme credentials, no query.</summary>
    public string? Endpoint { get; private set; }
    public string? RequestJson { get; private set; }
    public string? ResponseJson { get; private set; }
    public bool ResponseTruncated { get; private set; }
    public int? HttpStatus { get; private set; }
    public string? RcsCode { get; private set; }
    public string? RcsMessage { get; private set; }
    public long DurationMs { get; private set; }
    /// <summary>API_SUCCESS, TCP_CONNECTION_FAILED, TIMEOUT, HTTP_FAILURE, RCS_CODE_NOT_SUCCESS, JSON_PARSE_FAILED, RESPONSE_INVALID, NOT_CONFIGURED.</summary>
    public string Outcome { get; private set; } = default!;
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void Complete(string? requestJson, string? responseJson, bool truncated, int? httpStatus, string? rcsCode, string? rcsMessage, long durationMs, string? error)
    {
        RequestJson = requestJson; ResponseJson = responseJson; ResponseTruncated = truncated; HttpStatus = httpStatus; RcsCode = rcsCode; RcsMessage = rcsMessage; DurationMs = durationMs; ErrorMessage = error;
    }
}

/// <summary>
/// CR-032: one call RECEIVED from the RCS on a WCS callback endpoint (agvCallback, warnCallback, bindNotify, applyReturnForValid, applyReturnForBin). On the current build WCS provides NO such endpoint, so this table stays empty;
/// it exists so the Callback Monitor can show real received callbacks as soon as a receiver is built. Nothing is ever fabricated into it.
/// </summary>
public class RcsCallbackLogEntry
{
    private RcsCallbackLogEntry() { }

    public RcsCallbackLogEntry(Guid id, DateTimeOffset receivedAt, string callback, string? path, string? sourceAddress)
    {
        Id = id; ReceivedAt = receivedAt; Callback = callback; Path = path; SourceAddress = sourceAddress;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string Callback { get; private set; } = default!;
    public string? Path { get; private set; }
    public string? SourceAddress { get; private set; }
    public string? RequestJson { get; private set; }
    public string? ResponseJson { get; private set; }
    public int? HttpStatus { get; private set; }
    public bool Processed { get; private set; }
    public string? ErrorMessage { get; private set; }
}

/// <summary>
/// CR-032: a deployment-wide value the Admin saved for a diagnostics parameter (for example the map alias, the third-party client code). It only pre-fills the Request Builder; it is never sent unless it is in the request the Admin executes.
/// No row = nothing saved (the configured default, if any, applies).
/// </summary>
public class RcsDiagParam
{
    private RcsDiagParam() { }

    public RcsDiagParam(string key, string value, string updatedBy, DateTimeOffset updatedAt) { Key = key; Value = value; UpdatedBy = updatedBy; UpdatedAt = updatedAt; }

    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public string UpdatedBy { get; private set; } = default!;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Set(string value, string updatedBy, DateTimeOffset at) { Value = value; UpdatedBy = updatedBy; UpdatedAt = at; }
}
