namespace WCS.Application.Logging;

/// <summary>Search filter of the Logging &amp; Diagnostics screen. All fields optional; paging is done by the database.</summary>
public sealed record LogSearchQuery(DateTimeOffset? From, DateTimeOffset? To, string? Category, string? Level, string? MinLevel, string? Device, string? Source,
    string? EventCode, string? Keyword, int Page = 1, int PageSize = 50);

public sealed record LogEntryView(string Id, DateTimeOffset OccurredAt, string Category, string Level, string Source, string? Device, string? Event, string Message);

public sealed record LogPage(IReadOnlyList<LogEntryView> Items, long Total, int Page, int PageSize);

public sealed record ProblemDevice(string Device, string Category, long Errors, long Warnings, DateTimeOffset LastAt);

/// <summary>Dashboard figures for one time window.</summary>
public sealed record LogStats(DateTimeOffset From, DateTimeOffset To, long Total, IReadOnlyDictionary<string, long> ByLevel, IReadOnlyDictionary<string, long> ByCategory,
    IReadOnlyList<LogEntryView> LatestErrors, IReadOnlyList<ProblemDevice> ProblemDevices, IReadOnlyList<string> Devices);

/// <summary>Everything known about one entry (Admin only). Secrets are masked; fields that do not apply to the entry kind are null.</summary>
public sealed record LogDetailView(string Id, string EntryKind, DateTimeOffset OccurredAt, string Category, string Level, string Source, string? Device, string? Station, string? Event,
    string Message, string? Exception, string? CorrelationId, string? Ip, int? Port, string? PreviousState, string? NewState, string? FailureKind, string? EndCode, string? Reason,
    string? Host, string? Actor, string? ActorRole, string? Result, string? TargetType, string? TargetId, string? BeforeJson, string? AfterJson, string? DetailsJson);

public interface ILogQueryService
{
    Task<LogPage> SearchAsync(LogSearchQuery query, CancellationToken ct = default);
    Task<LogStats> StatsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
    /// <summary>Id format: <c>sys:&lt;guid&gt;</c>, <c>plc:&lt;guid&gt;</c> or <c>aud:&lt;guid&gt;</c>.</summary>
    Task<LogDetailView?> DetailAsync(string id, CancellationToken ct = default);
}
