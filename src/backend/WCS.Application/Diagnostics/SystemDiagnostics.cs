using System.Text.Json;
using System.Text.Json.Serialization;

namespace WCS.Application.Diagnostics;

/// <summary>
/// CR-021: one NORMALISED diagnostic event. Every stored source (system_log, plc_comm_log, audit_log, task_history, routing_events, inventory_transactions) is mapped onto this shape at EXPORT time,
/// so older rows are not lost and no second log store exists. A field the source does not have is null: nothing is inferred or invented.
/// </summary>
public sealed record DiagEvent
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string Level { get; init; } = "Information";
    public string EventName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Module { get; init; } = "";
    public string? Component { get; init; }
    /// <summary>Which stored source this row came from (table name): lets an analyst go back to the original row.</summary>
    public string Source { get; init; } = "";
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public string? TaskId { get; init; }
    public string? AgvId { get; init; }
    public string? PlcId { get; init; }
    public string? StationId { get; init; }
    public string? RackId { get; init; }
    public string? Actor { get; init; }
    public string? Operation { get; init; }
    public string? Result { get; init; }
    public long? DurationMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
    public string? StackTrace { get; init; }
    /// <summary>Source-specific extra data as a JSON text (already masked).</summary>
    public string? Context { get; init; }
    /// <summary>True when the row was NOT selected by the requested scope itself but pulled in because it shares a CorrelationId or TaskId with a selected row.</summary>
    public bool Related { get; init; }
}

public static class DiagLevels
{
    public const string Information = "Information", Warning = "Warning", Error = "Error", Critical = "Critical";
    public static readonly IReadOnlyList<string> All = [Information, Warning, Error, Critical];
    public static int Rank(string? l) => l switch { Critical => 4, Error => 3, Warning => 2, _ => 1 };
    public static bool IsValid(string? l) => l is not null && All.Contains(l);
}

/// <summary>The module taxonomy of the package (CR-021 section 3). A module is a diagnostic grouping, not a code namespace.</summary>
public static class DiagModules
{
    public const string System = "System", Database = "Database", Rcs = "Rcs", Agv = "Agv", Plc = "Plc", Task = "Task", Routing = "Routing", Station = "Station",
        Inventory = "Inventory", Auth = "Auth", Audit = "Audit", Api = "Api", Deployment = "Deployment", Config = "Config";
    public static readonly IReadOnlyList<string> All = [System, Database, Rcs, Agv, Plc, Task, Routing, Station, Inventory, Auth, Audit, Api, Deployment, Config];
    public static bool IsValid(string? m) => m is not null && All.Contains(m, StringComparer.OrdinalIgnoreCase);
    public static string Canonical(string m) => All.First(x => x.Equals(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Module of a system_log row, from its stored category and logger name. Unknown loggers stay in System.</summary>
    public static string ForSystemLog(string category, string source)
    {
        var s = source ?? "";
        bool Has(string x) => s.Contains(x, StringComparison.OrdinalIgnoreCase);
        return category switch
        {
            "DATABASE" => Database,
            "DEPLOYMENT" => Deployment,
            "SECURITY_AUDIT" => Auth,
            "PLC" => Plc,
            "RCS_AGV" => Has("AgvStatus") || Has("AgvHold") ? Agv : Rcs,
            _ when Has("TaskOrchestrator") || Has("TaskScheduler") || Has("SchedulerHosted") => Task,
            _ when Has("Rcs") => Rcs,
            _ when Has("Agv") => Agv,
            _ when Has("RouteEngine") || Has("RoutingHosted") || Has("Routing") => Routing,
            _ when Has("StationRequest") || Has("StationTransaction") || Has("PlcRequest") => Station,
            _ when Has("Inventory") || Has("Reconciliation") => Inventory,
            _ when Has("ConfigService") || Has("ConnectionConfig") => Config,
            _ when Has("ApiPlumbing") || Has("AspNetCore") || Has("SignalR") || Has("WcsHub") || Has("RequestLog") => Api,
            _ => System,
        };
    }
}

/// <summary>What to export. Every field optional; no field = no restriction.</summary>
public sealed record DiagScope
{
    public IReadOnlyList<string> Modules { get; init; } = [];
    public string? MinLevel { get; init; }
    public string? PlcId { get; init; }
    public string? Device { get; init; }
    public string? CorrelationId { get; init; }
    public string? TaskId { get; init; }
    public bool IsWholeSystem => Modules.Count == 0 && MinLevel is null && PlcId is null && Device is null && CorrelationId is null && TaskId is null;
}

/// <summary>A source query: time range, scope, and optionally a set of IDs for the "related events" pass (then only the ID match applies, whatever the module).</summary>
public sealed record DiagQuery(DateTimeOffset? From, DateTimeOffset? To, DiagScope Scope, IReadOnlySet<string>? RelatedCorrelationIds = null, IReadOnlySet<string>? RelatedTaskIds = null)
{
    public bool IsRelatedPass => RelatedCorrelationIds is not null || RelatedTaskIds is not null;
}

/// <summary>What a source tells about itself and about the range it was asked for (shown in Manifest and Summary).</summary>
public sealed record SourceStats(long RowsInRange, DateTimeOffset? OldestStored, DateTimeOffset? NewestInRange);

/// <summary>
/// A read-only reader of one stored log source. It streams rows in time order (oldest first) in pages: it never materialises the whole range, never takes a lock the PLC/AGV/Task code uses,
/// and a failure is reported by throwing so the exporter can mark THIS source failed without failing the package.
/// </summary>
public interface IDiagnosticEventSource
{
    /// <summary>Stable name = the stored source (table), e.g. "system_log".</summary>
    string Name { get; }
    IReadOnlyList<string> Modules { get; }
    /// <summary>Plain-language retention of this source, quoted in the Manifest ("all time" = the oldest stored row, not a promise of completeness).</summary>
    string Retention { get; }
    Task<SourceStats> StatsAsync(DiagQuery query, CancellationToken ct);
    /// <summary>Start of the range that keeps only the newest <paramref name="keep"/> rows, or null when everything fits.</summary>
    Task<DateTimeOffset?> TailStartAsync(DiagQuery query, long keep, CancellationToken ct);
    IAsyncEnumerable<DiagEvent> ReadAsync(DiagQuery query, DateTimeOffset? from, int pageSize, CancellationToken ct);
}

/// <summary>Live state at export time (read-only). Failures are reported per part, never thrown past the exporter.</summary>
public interface IDiagnosticHealthSource
{
    Task<IReadOnlyDictionary<string, object?>> SnapshotAsync(CancellationToken ct);
}

/// <summary>Limits and locations of the system export. Data, not code.</summary>
public sealed class SystemDiagnosticOptions
{
    public const string SectionName = "Diagnostics:System";
    /// <summary>Folder of the finished ZIP files (not under any web root; no static file serving). Default: a folder under the OS temp path.</summary>
    public string? Directory { get; set; }
    public int RetentionHours { get; set; } = 24;
    /// <summary>Newest rows kept per source; a source with more rows in the range is cut at the OLDEST end and the Manifest says so.</summary>
    public long MaxEventsPerSource { get; set; } = 1_000_000;
    /// <summary>Uncompressed bytes written into one package. Reaching it stops adding rows and is reported.</summary>
    public long MaxUncompressedBytes { get; set; } = 1L * 1024 * 1024 * 1024;
    public int PageSize { get; set; } = 2000;
    public int JobTimeoutMinutes { get; set; } = 20;
    public int MaxTimelineRows { get; set; } = 100_000;
    /// <summary>CorrelationIds/TaskIds collected from the selected rows to pull related rows of other modules.</summary>
    public int MaxRelatedIds { get; set; } = 500;
    public int MaxQueuedJobs { get; set; } = 2;
    /// <summary>Days of PLC events to include in the PLC folder (the existing PLC export keeps its own 31-day cap).</summary>
    public int PlcFolderMaxDays { get; set; } = 31;
}

public static class DiagJson
{
    public static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    /// <summary>Manifest, health and configuration: a missing value is written as an explicit null (the reader must see "unknown", not an absent key).</summary>
    public static readonly JsonSerializerOptions Full = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static readonly JsonSerializerOptions Indented = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
