namespace WCS.Domain.Logging;

/// <summary>The six log groups shown by the Logging &amp; Diagnostics screen (CR-007).</summary>
public static class LogCategories
{
    public const string System = "SYSTEM";
    public const string Plc = "PLC";
    public const string Database = "DATABASE";
    public const string Deployment = "DEPLOYMENT";
    public const string RcsAgv = "RCS_AGV";
    public const string SecurityAudit = "SECURITY_AUDIT";

    public static readonly IReadOnlyList<string> All = [System, Plc, Database, Deployment, RcsAgv, SecurityAudit];
    public static bool IsValid(string? c) => c is not null && All.Contains(c);
}

public static class LogLevels
{
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";

    public static readonly IReadOnlyList<string> All = [Information, Warning, Error, Critical];
    public static bool IsValid(string? l) => l is not null && All.Contains(l);
    public static int Rank(string? l) => l switch { Critical => 4, Error => 3, Warning => 2, _ => 1 };
}

/// <summary>
/// Operational log entry (CR-007): SYSTEM, DATABASE, DEPLOYMENT and RCS_AGV events that are not already stored elsewhere. PLC events stay in
/// <c>plc_comm_log</c> and security/audit events in <c>audit_log</c>; the unified view <c>v_log_entries</c> presents all three together without duplicating data.
/// Texts are masked by the sanitizer BEFORE they get here; this class only enforces lengths.
/// </summary>
public class SystemLogEntry
{
    private SystemLogEntry() { }

    public SystemLogEntry(DateTimeOffset occurredAt, string category, string level, string source, string? device, string? eventCode, string message,
        string? exception = null, string? correlationId = null, string? detailsJson = null, string? host = null, string? externalId = null, string? messageTemplate = null)
    {
        Id = Guid.NewGuid(); OccurredAt = occurredAt.ToUniversalTime();   // PostgreSQL timestamptz accepts only offset 0
        Category = LogCategories.IsValid(category) ? category : LogCategories.System;
        Level = LogLevels.IsValid(level) ? level : LogLevels.Information;
        Source = Cut(source, 200) ?? "unknown"; Device = Cut(device, 60); EventCode = Cut(eventCode, 60);
        Message = Cut(message, 2000) ?? ""; Exception = Cut(exception, 8000); CorrelationId = Cut(correlationId, 100);
        DetailsJson = Cut(detailsJson, 8000); Host = Cut(host, 100); ExternalId = Cut(externalId, 64); MessageTemplate = Cut(messageTemplate, 500);
    }

    /// <summary>Truncates and removes NUL characters (PostgreSQL text cannot store them: one such character would make a whole batch fail).</summary>
    private static string? Cut(string? s, int n)
    {
        if (s is null) return null;
        if (s.Contains('\0')) s = s.Replace("\0", "");
        return s.Length <= n ? s : s[..n];
    }

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Category { get; private set; } = default!;
    public string Level { get; private set; } = default!;
    /// <summary>Module that produced the event (logger source context, or deploy/&lt;script&gt;).</summary>
    public string Source { get; private set; } = default!;
    /// <summary>PLC id, AGV code, database, host ... when the event concerns one device.</summary>
    public string? Device { get; private set; }
    public string? EventCode { get; private set; }
    public string Message { get; private set; } = default!;
    public string? Exception { get; private set; }
    public string? CorrelationId { get; private set; }
    /// <summary>Structured properties of the event (scalars only, masked).</summary>
    public string? DetailsJson { get; private set; }
    /// <summary>The stable log message template (e.g. "AGV {Agv} placed on dispatch hold ..."). With <see cref="DetailsJson"/> it lets the UI phrase the event in the user's language; <see cref="Message"/> stays the raw text. Null for events stored before this field existed.</summary>
    public string? MessageTemplate { get; private set; }
    public string? Host { get; private set; }
    /// <summary>Idempotency key for imported events (deployment scripts): unique when set.</summary>
    public string? ExternalId { get; private set; }
}
