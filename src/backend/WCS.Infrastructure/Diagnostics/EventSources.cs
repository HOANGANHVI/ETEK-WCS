using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WCS.Application.Diagnostics;
using WCS.Domain.Inventory;
using WCS.Domain.Logging;
using WCS.Domain.Plc;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Diagnostics;

/// <summary>
/// CR-021: read-only readers of the stored log sources. Rows are streamed in pages ordered by (OccurredAt, Id) with no tracking and no locks held beyond each short query, so a large export
/// never competes with the PLC / AGV / Task code for anything but ordinary read capacity. Every row is mapped to <see cref="DiagEvent"/>; a field the source lacks stays null.
/// </summary>
internal abstract class EventSourceBase<T>(WcsDbContext db) : IDiagnosticEventSource where T : class
{
    protected WcsDbContext Db => db;
    public abstract string Name { get; }
    public abstract IReadOnlyList<string> Modules { get; }
    public abstract string Retention { get; }
    protected abstract IQueryable<T> Table();
    /// <summary>Database-side narrowing by scope / related ids. Default: none (the exporter filters the mapped events).</summary>
    protected virtual IQueryable<T> Narrow(IQueryable<T> q, DiagQuery dq) => q;
    protected abstract DiagEvent Map(T row);
    protected abstract (DateTimeOffset At, Guid Id) Key(T row);

    private static readonly Expression<Func<T, DateTimeOffset>> AtExpr = x => EF.Property<DateTimeOffset>(x, "OccurredAt");
    private static readonly Expression<Func<T, Guid>> IdExpr = x => EF.Property<Guid>(x, "Id");

    private IQueryable<T> Ranged(DiagQuery dq, DateTimeOffset? from)
    {
        var q = Table().AsNoTracking();
        var f = from ?? dq.From;
        if (f is { } lo) q = q.Where(x => EF.Property<DateTimeOffset>(x, "OccurredAt") >= lo);
        if (dq.To is { } hi) q = q.Where(x => EF.Property<DateTimeOffset>(x, "OccurredAt") <= hi);
        return Narrow(q, dq);
    }

    public async Task<SourceStats> StatsAsync(DiagQuery query, CancellationToken ct)
    {
        var q = Ranged(query, null);
        var count = await q.LongCountAsync(ct);
        var oldest = await Table().AsNoTracking().Select(x => (DateTimeOffset?)EF.Property<DateTimeOffset>(x, "OccurredAt")).MinAsync(ct);
        var newest = count == 0 ? null : await q.Select(x => (DateTimeOffset?)EF.Property<DateTimeOffset>(x, "OccurredAt")).MaxAsync(ct);
        return new SourceStats(count, oldest, newest);
    }

    public async Task<DateTimeOffset?> TailStartAsync(DiagQuery query, long keep, CancellationToken ct)
    {
        var q = Ranged(query, null);
        return await q.OrderByDescending(AtExpr).Skip((int)Math.Max(0, keep - 1)).Select(x => (DateTimeOffset?)EF.Property<DateTimeOffset>(x, "OccurredAt")).FirstOrDefaultAsync(ct);
    }

    public async IAsyncEnumerable<DiagEvent> ReadAsync(DiagQuery query, DateTimeOffset? from, int pageSize, [EnumeratorCancellation] CancellationToken ct)
    {
        var baseQuery = Ranged(query, from);
        DateTimeOffset? last = null; var tie = new List<Guid>();
        while (true)
        {
            var q = baseQuery;
            if (last is { } l) q = q.Where(x => EF.Property<DateTimeOffset>(x, "OccurredAt") >= l);
            if (tie.Count > 0) { var ids = tie.ToArray(); q = q.Where(x => !ids.Contains(EF.Property<Guid>(x, "Id"))); }
            var rows = await q.OrderBy(AtExpr).ThenBy(IdExpr).Take(pageSize).ToListAsync(ct);
            if (rows.Count == 0) yield break;
            foreach (var r in rows) yield return Map(r);
            var (lastAt, _) = Key(rows[^1]);
            if (last != lastAt) { last = lastAt; tie.Clear(); }
            foreach (var r in rows) { var (at, id) = Key(r); if (at == lastAt) tie.Add(id); }
            if (rows.Count < pageSize) yield break;
        }
    }

    // ---------------------------------------------------------------- shared helpers
    protected static string? G(Guid? id) => id?.ToString("D");

    /// <summary>OR of "text column contains any of these values" (EF cannot translate Any/Contains over a list of substrings).</summary>
    protected static Expression<Func<TRow, bool>> AnyContains<TRow>(Expression<Func<TRow, string?>> column, IEnumerable<string> values)
    {
        var p = column.Parameters[0]; Expression? body = null;
        var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        foreach (var v in values)
        {
            var call = Expression.AndAlso(Expression.NotEqual(column.Body, Expression.Constant(null, typeof(string))), Expression.Call(column.Body, contains, Expression.Constant(v)));
            body = body is null ? call : Expression.OrElse(body, call);
        }
        return Expression.Lambda<Func<TRow, bool>>(body ?? Expression.Constant(false), p);
    }
}

internal sealed class SystemLogSource(WcsDbContext db) : EventSourceBase<SystemLogEntry>(db)
{
    public override string Name => "system_log";
    public override IReadOnlyList<string> Modules => [DiagModules.System, DiagModules.Database, DiagModules.Rcs, DiagModules.Agv, DiagModules.Task, DiagModules.Routing, DiagModules.Station,
        DiagModules.Inventory, DiagModules.Auth, DiagModules.Api, DiagModules.Deployment, DiagModules.Config];
    public override string Retention => "90 days (Logging:SystemLog:RetentionDays, minimum 7); only Warning and above or events tagged with an event code are stored";
    protected override IQueryable<SystemLogEntry> Table() => Db.SystemLog;
    protected override (DateTimeOffset, Guid) Key(SystemLogEntry r) => (r.OccurredAt, r.Id);

    protected override IQueryable<SystemLogEntry> Narrow(IQueryable<SystemLogEntry> q, DiagQuery dq)
    {
        if (dq.IsRelatedPass)
        {
            var corr = dq.RelatedCorrelationIds?.ToArray() ?? [];
            var tasks = dq.RelatedTaskIds?.Take(50).ToArray() ?? [];
            var byTask = AnyContains<SystemLogEntry>(x => x.DetailsJson, tasks);
            return tasks.Length > 0 ? q.Where(x => (x.CorrelationId != null && corr.Contains(x.CorrelationId))).Union(q.Where(byTask)) : q.Where(x => x.CorrelationId != null && corr.Contains(x.CorrelationId));
        }
        var s = dq.Scope;
        if (s.CorrelationId is { } c) q = q.Where(x => x.CorrelationId == c);
        if (s.TaskId is { } t) q = q.Where(x => x.DetailsJson != null && x.DetailsJson.Contains(t));
        if (s.PlcId is { } p) q = q.Where(x => x.Device == p);
        if (s.Device is { } d) q = q.Where(x => x.Device == d);
        if (s.MinLevel is { } l) { var ok = LogLevels.All.Where(x => LogLevels.Rank(x) >= LogLevels.Rank(l)).ToArray(); q = q.Where(x => ok.Contains(x.Level)); }
        return q;
    }

    protected override DiagEvent Map(SystemLogEntry r)
    {
        var ids = Ids.From(r.DetailsJson);
        var (exType, exMsg, stack) = Ids.SplitException(r.Exception);
        var warn = LogLevels.Rank(r.Level) >= 2;
        return new DiagEvent
        {
            TimestampUtc = r.OccurredAt, Level = r.Level, EventName = r.EventCode ?? "LOG", Category = r.Category, Module = DiagModules.ForSystemLog(r.Category, r.Source),
            Component = Ids.ShortName(r.Source), Source = "system_log", Message = r.Message, CorrelationId = r.CorrelationId, TaskId = ids.Task,
            AgvId = ids.Agv ?? (r.Category == LogCategories.RcsAgv ? r.Device : null), PlcId = r.Device is { } d && d.StartsWith("PLC", StringComparison.OrdinalIgnoreCase) ? d : ids.Plc,
            StationId = ids.Station, RackId = ids.Rack, ErrorCode = warn ? r.EventCode : null, ExceptionType = exType, ExceptionMessage = exMsg, StackTrace = stack,
            Result = warn ? "Problem" : null, Context = r.DetailsJson,
        };
    }
}

internal sealed class PlcCommLogSource(WcsDbContext db) : EventSourceBase<PlcCommLogEntry>(db)
{
    public override string Name => "plc_comm_log";
    public override IReadOnlyList<string> Modules => [DiagModules.Plc];
    public override string Retention => "30 days (Plc:LogRetentionDays)";
    protected override IQueryable<PlcCommLogEntry> Table() => Db.PlcCommLog;
    protected override (DateTimeOffset, Guid) Key(PlcCommLogEntry r) => (r.OccurredAt, r.Id);

    protected override IQueryable<PlcCommLogEntry> Narrow(IQueryable<PlcCommLogEntry> q, DiagQuery dq)
    {
        if (dq.IsRelatedPass) return q.Where(_ => false);                       // PLC events carry no CorrelationId / TaskId: nothing is linked without a basis
        var s = dq.Scope;
        if (s.CorrelationId is not null || s.TaskId is not null) return q.Where(_ => false);
        if (s.PlcId is { } p) q = q.Where(x => x.PlcId == p);
        if (s.Device is { } d) q = q.Where(x => x.PlcId == d || x.StationId == d);
        if (s.MinLevel is { } l) { var min = Math.Clamp(DiagLevels.Rank(l) - 1, 1, 3); q = q.Where(x => (int)x.Level >= min); }
        return q;
    }

    protected override DiagEvent Map(PlcCommLogEntry r) => new()
    {
        TimestampUtc = r.OccurredAt, Level = r.Level switch { PlcLogLevel.Error => DiagLevels.Error, PlcLogLevel.Warning => DiagLevels.Warning, _ => DiagLevels.Information },
        EventName = r.Event, Category = LogCategories.Plc, Module = DiagModules.Plc, Component = "PlcRuntime", Source = "plc_comm_log", Message = r.Message, PlcId = r.PlcId, StationId = r.StationId,
        Operation = r.Stage, ErrorCode = r.DiagCode, Context = r.DetailsJson,
    };
}

internal sealed class AuditLogSource(WcsDbContext db) : EventSourceBase<AuditEntry>(db)
{
    public override string Name => "audit_log";
    public override IReadOnlyList<string> Modules => [DiagModules.Audit, DiagModules.Auth];
    public override string Retention => "no automatic deletion (kept until removed by an operator)";
    protected override IQueryable<AuditEntry> Table() => Db.AuditLog;
    protected override (DateTimeOffset, Guid) Key(AuditEntry r) => (r.OccurredAt, r.Id);

    protected override IQueryable<AuditEntry> Narrow(IQueryable<AuditEntry> q, DiagQuery dq)
    {
        if (dq.IsRelatedPass) { var t = dq.RelatedTaskIds?.ToArray() ?? []; return q.Where(x => x.TargetType == "Task" && x.TargetId != null && t.Contains(x.TargetId)); }
        var s = dq.Scope;
        if (s.CorrelationId is not null) return q.Where(_ => false);
        if (s.TaskId is { } id) q = q.Where(x => x.TargetType == "Task" && x.TargetId == id);
        if (s.PlcId is { } p) q = q.Where(x => x.TargetId != null && x.TargetId == p);
        return q;
    }

    protected override DiagEvent Map(AuditEntry r) => new()
    {
        TimestampUtc = r.OccurredAt, Level = r.Result == AuditResult.Success ? DiagLevels.Information : DiagLevels.Warning, EventName = r.Action,
        Category = LogCategories.SecurityAudit, Module = r.Action.StartsWith("AUTH_", StringComparison.Ordinal) ? DiagModules.Auth : DiagModules.Audit, Component = r.TargetType, Source = "audit_log",
        Message = r.Detail, TaskId = r.TargetType == "Task" ? r.TargetId : null, PlcId = r.TargetType is "PlcConnection" ? r.TargetId : null, Actor = r.Actor, Operation = r.Action, Result = r.Result.ToString(),
        Context = JsonSerializer.Serialize(new { actorRole = r.ActorRole, targetType = r.TargetType, targetId = r.TargetId, ip = r.IpAddress, before = Cut(r.BeforeJson), after = Cut(r.AfterJson) }, DiagJson.Compact),
    };

    /// <summary>Masks first, on the raw JSON (once it is embedded as a string its quotes are escaped and key=value masking would no longer recognise it), then truncates.</summary>
    private static string? Cut(string? s) => WCS.Application.Logging.LogSanitizer.Redact(s, 2000);
}

internal sealed class TaskHistorySource(WcsDbContext db) : EventSourceBase<TaskHistoryEntry>(db)
{
    public override string Name => "task_history";
    public override IReadOnlyList<string> Modules => [DiagModules.Task];
    public override string Retention => "no automatic deletion";
    protected override IQueryable<TaskHistoryEntry> Table() => Db.TaskHistory;
    protected override (DateTimeOffset, Guid) Key(TaskHistoryEntry r) => (r.OccurredAt, r.Id);

    protected override IQueryable<TaskHistoryEntry> Narrow(IQueryable<TaskHistoryEntry> q, DiagQuery dq)
    {
        if (dq.IsRelatedPass) { var ids = (dq.RelatedTaskIds ?? new HashSet<string>()).Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray(); return q.Where(x => ids.Contains(x.TaskId)); }
        var s = dq.Scope;
        if (s.CorrelationId is not null || s.PlcId is not null) return q.Where(_ => false);
        if (s.TaskId is { } t) { var g = Guid.TryParse(t, out var x) ? x : Guid.Empty; q = q.Where(h => h.TaskId == g); }
        return q;
    }

    protected override DiagEvent Map(TaskHistoryEntry r) => new()
    {
        TimestampUtc = r.OccurredAt, Level = r.ToState == TaskState.Failed ? DiagLevels.Warning : DiagLevels.Information, EventName = r.EventName, Category = "TASK", Module = DiagModules.Task, Component = r.Source,
        Source = "task_history", Message = r.Detail, TaskId = G(r.TaskId), Operation = r.FromState is null ? r.ToState?.ToString() : $"{r.FromState}->{r.ToState}", Result = r.ToState?.ToString(), ErrorCode = r.ToState == TaskState.Failed ? r.DetailCode : null,
        Context = JsonSerializer.Serialize(new { fromState = r.FromState?.ToString(), toState = r.ToState?.ToString(), detailCode = r.DetailCode, detailArgs = r.DetailArgsJson }, DiagJson.Compact),
    };
}

internal sealed class RoutingEventSource(WcsDbContext db) : EventSourceBase<RoutingEvent>(db)
{
    public override string Name => "routing_events";
    public override IReadOnlyList<string> Modules => [DiagModules.Routing, DiagModules.Station];
    public override string Retention => "no automatic deletion";
    protected override IQueryable<RoutingEvent> Table() => Db.RoutingEvents;
    protected override (DateTimeOffset, Guid) Key(RoutingEvent r) => (r.OccurredAt, r.Id);

    protected override IQueryable<RoutingEvent> Narrow(IQueryable<RoutingEvent> q, DiagQuery dq)
    {
        if (dq.IsRelatedPass) return q.Where(_ => false);                       // routing rows carry no TaskId (batch B adds the order's CorrelationId)
        var s = dq.Scope;
        return s.CorrelationId is not null || s.TaskId is not null || s.PlcId is not null ? q.Where(_ => false) : q;
    }

    protected override DiagEvent Map(RoutingEvent r)
    {
        var k = r.Kind ?? "";
        var problem = k.Contains("FAIL", StringComparison.OrdinalIgnoreCase) || k.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || k.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase) || k.Contains("REJECT", StringComparison.OrdinalIgnoreCase);
        return new DiagEvent
        {
            TimestampUtc = r.OccurredAt, Level = problem ? DiagLevels.Warning : DiagLevels.Information, EventName = k, Category = "ROUTING", Module = k.Contains("STATION", StringComparison.OrdinalIgnoreCase) || r.TransactionId is not null ? DiagModules.Station : DiagModules.Routing,
            Component = r.Source, Source = "routing_events", Message = r.Detail, ErrorCode = problem ? k : null,
            Context = JsonSerializer.Serialize(new { orderId = G(r.OrderId), legId = G(r.LegId), transactionId = G(r.TransactionId) }, DiagJson.Compact),
        };
    }
}

internal sealed class InventoryTransactionSource(WcsDbContext db) : EventSourceBase<InventoryTransaction>(db)
{
    public override string Name => "inventory_transactions";
    public override IReadOnlyList<string> Modules => [DiagModules.Inventory];
    public override string Retention => "no automatic deletion";
    protected override IQueryable<InventoryTransaction> Table() => Db.InventoryTransactions;
    protected override (DateTimeOffset, Guid) Key(InventoryTransaction r) => (r.OccurredAt, r.Id);

    protected override IQueryable<InventoryTransaction> Narrow(IQueryable<InventoryTransaction> q, DiagQuery dq)
    {
        if (dq.IsRelatedPass) { var ids = (dq.RelatedTaskIds ?? new HashSet<string>()).Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null).Where(g => g != null).ToArray(); return q.Where(x => ids.Contains(x.TaskId)); }
        var s = dq.Scope;
        if (s.CorrelationId is not null || s.PlcId is not null) return q.Where(_ => false);
        if (s.TaskId is { } t) { Guid? g = Guid.TryParse(t, out var x) ? x : null; q = q.Where(h => h.TaskId == g); }
        return q;
    }

    protected override DiagEvent Map(InventoryTransaction r) => new()
    {
        TimestampUtc = r.OccurredAt, Level = DiagLevels.Information, EventName = r.EventType.ToString(), Category = "INVENTORY", Module = DiagModules.Inventory, Component = r.Source.ToString(), Source = "inventory_transactions",
        Message = r.Note, TaskId = G(r.TaskId), RackId = G(r.RackId), Actor = r.UserName, Operation = $"{r.StatusBefore}->{r.StatusAfter}", Result = r.Bind.ToString(),
        Context = JsonSerializer.Serialize(new { fromLocationId = G(r.FromLocationId), toLocationId = G(r.ToLocationId), statusBefore = r.StatusBefore?.ToString(), statusAfter = r.StatusAfter?.ToString(), bind = r.Bind.ToString() }, DiagJson.Compact),
    };
}

/// <summary>Pulls the well-known ids out of a source's JSON detail text and splits a stored exception text. Nothing is guessed: a key that is absent stays null.</summary>
internal static class Ids
{
    public readonly record struct Found(string? Task, string? Agv, string? Plc, string? Station, string? Rack);

    public static Found From(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Object) return default;
            string? task = null, agv = null, plc = null, station = null, rack = null;
            foreach (var p in d.RootElement.EnumerateObject())
            {
                var v = p.Value.ValueKind switch { JsonValueKind.String => p.Value.GetString(), JsonValueKind.Number => p.Value.GetRawText(), _ => null };
                if (string.IsNullOrEmpty(v)) continue;
                switch (p.Name.ToLowerInvariant())
                {
                    case "taskid": case "task": task ??= v; break;
                    case "agv": case "agvid": case "agvcode": case "robotcode": agv ??= v; break;
                    case "plcid": case "plc": plc ??= v; break;
                    case "stationid": case "station": station ??= v; break;
                    case "rackid": case "rack": case "rackcode": rack ??= v; break;
                }
            }
            return new Found(task, agv, plc, station, rack);
        }
        catch (JsonException) { return default; }
    }

    public static (string? Type, string? Message, string? Stack) SplitException(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, null);
        var lines = text.Split('\n');
        var first = lines[0].TrimEnd('\r'); var i = first.IndexOf(':');
        var type = i > 0 && !first[..i].Contains(' ') ? first[..i] : null;
        var msg = type is null ? first : first[(i + 1)..].Trim();
        return (type ?? "Exception", msg, lines.Length > 1 ? string.Join('\n', lines.Skip(1)).Trim() : null);
    }

    public static string? ShortName(string? source) => source is null ? null : source[(source.LastIndexOf('.') + 1)..];
}

/// <summary>Read-only state at export time: database reachability, task counts per state, AGV status. Each part is guarded by the exporter's caller.</summary>
internal sealed class DiagnosticHealthSource(WcsDbContext db) : IDiagnosticHealthSource
{
    public async Task<IReadOnlyDictionary<string, object?>> SnapshotAsync(CancellationToken ct)
    {
        var d = new Dictionary<string, object?>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        d["database"] = new { reachable = await db.Database.CanConnectAsync(ct), probeMs = sw.ElapsedMilliseconds };
        try
        {
            d["tasksByState"] = (await db.Tasks.AsNoTracking().GroupBy(t => t.State).Select(g => new { state = g.Key.ToString(), count = g.Count() }).ToListAsync(ct));
            d["agvs"] = await db.Agvs.AsNoTracking().Select(a => new { a.Code, a.Enabled, connection = a.ConnectionStatus.ToString(), a.StatusDetail, a.LastStatusAt }).ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { d["businessState"] = new { error = ex.GetType().Name }; }
        try
        {
            var since = DateTimeOffset.UtcNow.AddHours(-1);
            d["systemLogLastHour"] = await db.SystemLog.AsNoTracking().Where(x => x.OccurredAt >= since).GroupBy(x => x.Level).Select(g => new { level = g.Key, count = g.Count() }).ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { d["systemLogLastHour"] = new { error = ex.GetType().Name }; }
        return d;
    }
}
