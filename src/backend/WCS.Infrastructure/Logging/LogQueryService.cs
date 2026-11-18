using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Logging;
using WCS.Domain.Logging;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Logging;

/// <summary>
/// CR-007 read side: one query surface over system_log, plc_comm_log and audit_log through the view <c>v_log_entries</c>. Filtering, ordering and paging run in
/// PostgreSQL (the three source tables are indexed on OccurredAt). Everything returned is masked again by <see cref="LogSanitizer"/>. Admin only (enforced by the API).
/// </summary>
public sealed class LogQueryService(WcsDbContext db, IClock clock) : ILogQueryService
{
    public const int MaxPageSize = 200;
    private const int MaxKeyword = 100;

    private static string Like(string s) => "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private IQueryable<LogRow> Filter(LogSearchQuery q)
    {
        if (q.Category is not null && !LogCategories.IsValid(q.Category)) throw AppException.Validation("category", "Nhóm log không hợp lệ.");
        if (q.Level is not null && !LogLevels.IsValid(q.Level)) throw AppException.Validation("level", "Mức độ không hợp lệ.");
        if (q.MinLevel is not null && !LogLevels.IsValid(q.MinLevel)) throw AppException.Validation("minLevel", "Mức độ tối thiểu không hợp lệ.");
        if (q.From is { } f && q.To is { } t && f > t) throw AppException.Validation("from", "Thời điểm bắt đầu phải trước thời điểm kết thúc.");

        IQueryable<LogRow> rows = db.LogRows.AsNoTracking();
        if (q.From is { } from) rows = rows.Where(x => x.OccurredAt >= from);
        if (q.To is { } to) rows = rows.Where(x => x.OccurredAt <= to);
        if (q.Category is { } cat) rows = rows.Where(x => x.Category == cat);
        if (q.Level is { } lvl) rows = rows.Where(x => x.Level == lvl);
        if (q.MinLevel is { } min) { var rank = LogLevels.Rank(min); rows = rows.Where(x => x.LevelRank >= rank); }
        if (!string.IsNullOrWhiteSpace(q.Device)) { var d = Like(q.Device.Trim()); rows = rows.Where(x => x.Device != null && EF.Functions.ILike(x.Device, d, "\\")); }
        if (!string.IsNullOrWhiteSpace(q.Source)) { var s = Like(q.Source.Trim()); rows = rows.Where(x => EF.Functions.ILike(x.Source, s, "\\")); }
        if (!string.IsNullOrWhiteSpace(q.EventCode)) { var e = Like(q.EventCode.Trim()); rows = rows.Where(x => x.Event != null && EF.Functions.ILike(x.Event, e, "\\")); }
        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var kw = q.Keyword.Trim(); if (kw.Length > MaxKeyword) kw = kw[..MaxKeyword];
            var k = Like(kw);
            rows = rows.Where(x => EF.Functions.ILike(x.Message, k, "\\") || (x.Event != null && EF.Functions.ILike(x.Event, k, "\\")) || (x.Device != null && EF.Functions.ILike(x.Device, k, "\\")));
        }
        return rows;
    }

    private static LogEntryView ToView(LogRow r) =>
        new(r.EntryId, r.OccurredAt, r.Category, r.Level, r.Source, r.Device, r.Event, LogSanitizer.Redact(r.Message, 2000)!,
            r.MessageTemplate, LogSanitizer.Redact(r.DetailsJson, 4000));

    public async Task<LogPage> SearchAsync(LogSearchQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);
        var rows = Filter(query);
        var total = await rows.LongCountAsync(ct);
        var items = await rows.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.EntryId).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new LogPage(items.Select(ToView).ToList(), total, page, size);
    }

    public async Task<LogStats> StatsAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        var end = to ?? clock.UtcNow; var start = from ?? end.AddHours(-24);
        if (start > end) throw AppException.Validation("from", "Thời điểm bắt đầu phải trước thời điểm kết thúc.");
        var window = db.LogRows.AsNoTracking().Where(x => x.OccurredAt >= start && x.OccurredAt <= end);

        var byLevel = await window.GroupBy(x => x.Level).Select(g => new { g.Key, N = g.LongCount() }).ToListAsync(ct);
        var byCategory = await window.GroupBy(x => x.Category).Select(g => new { g.Key, N = g.LongCount() }).ToListAsync(ct);
        var latest = await window.Where(x => x.LevelRank >= 3).OrderByDescending(x => x.OccurredAt).Take(8).ToListAsync(ct);
        var problems = await window.Where(x => x.LevelRank >= 2 && x.Device != null)
            .GroupBy(x => new { x.Device, x.Category })
            .Select(g => new { g.Key.Device, g.Key.Category, Errors = g.LongCount(x => x.LevelRank >= 3), Warnings = g.LongCount(x => x.LevelRank == 2), Last = g.Max(x => x.OccurredAt) })
            .OrderByDescending(x => x.Errors).ThenByDescending(x => x.Last).Take(10).ToListAsync(ct);
        var devices = await db.LogRows.AsNoTracking().Where(x => x.OccurredAt >= end.AddDays(-30) && x.Device != null).Select(x => x.Device!).Distinct().OrderBy(x => x).Take(100).ToListAsync(ct);

        var levels = LogLevels.All.ToDictionary(l => l, l => byLevel.FirstOrDefault(x => x.Key == l)?.N ?? 0L);
        var cats = LogCategories.All.ToDictionary(c => c, c => byCategory.FirstOrDefault(x => x.Key == c)?.N ?? 0L);
        return new LogStats(start, end, levels.Values.Sum(), levels, cats, latest.Select(ToView).ToList(),
            problems.Select(p => new ProblemDevice(p.Device!, p.Category, p.Errors, p.Warnings, p.Last)).ToList(), devices);
    }

    // ------------------------------------------------------------------------------------------------------------- detail
    public async Task<LogDetailView?> DetailAsync(string id, CancellationToken ct = default)
    {
        var colon = id.IndexOf(':');
        if (colon <= 0 || !Guid.TryParse(id[(colon + 1)..], out var guid)) return null;
        switch (id[..colon])
        {
            case "sys":
            {
                var e = await db.SystemLog.AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid, ct);
                if (e is null) return null;
                var f = Facts(e.DetailsJson);
                return new LogDetailView(id, "system", e.OccurredAt, e.Category, e.Level, e.Source, e.Device, f.Station, e.EventCode, S(e.Message, 2000)!, S(e.Exception), e.CorrelationId,
                    f.Ip, f.Port, f.Previous, f.New, f.Kind, f.EndCode, f.Reason, e.Host, null, null, null, null, null, null, null, S(e.DetailsJson), e.MessageTemplate);
            }
            case "plc":
            {
                var e = await db.PlcCommLog.AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid, ct);
                if (e is null) return null;
                var f = Facts(e.DetailsJson);
                var level = e.Level.ToString() switch { "Error" => "Error", "Warning" => "Warning", _ => "Information" };
                return new LogDetailView(id, "plc", e.OccurredAt, LogCategories.Plc, level, "PLC.Runtime", e.PlcId, e.StationId, e.Event, S(e.Message, 2000)!, null, null,
                    f.Ip, f.Port, f.Previous, f.New, f.Kind, f.EndCode, f.Reason, null, null, null, null, null, null, null, null, S(e.DetailsJson));
            }
            case "aud":
            {
                var e = await db.AuditLog.AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid, ct);
                if (e is null) return null;
                var level = e.Result == WCS.Domain.Users.AuditResult.Success ? "Information" : "Warning";
                return new LogDetailView(id, "audit", e.OccurredAt, LogCategories.SecurityAudit, level, "Audit", e.Actor, null, e.Action, S(e.Detail ?? $"{e.Action} {e.Result}", 2000)!, null, null,
                    null, null, null, null, null, null, null, null, e.Actor, e.ActorRole, e.Result.ToString(), e.TargetType, e.TargetId, S(e.BeforeJson), S(e.AfterJson), null)
                { Ip = e.IpAddress };
            }
            default: return null;
        }
    }

    private static string? S(string? text, int max = 8000) => LogSanitizer.Redact(text, max);

    private readonly record struct Fact(string? Ip, int? Port, string? Previous, string? New, string? Kind, string? EndCode, string? Reason, string? Station);

    /// <summary>Reads the well-known keys out of the details JSON of PLC events (camelCase) and system events (property names).</summary>
    private static Fact Facts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return default;
            string? Str(params string[] keys) { foreach (var k in keys) if (doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString(); return null; }
            int? Num(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (!doc.RootElement.TryGetProperty(k, out var v)) continue;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
                    if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var m)) return m;
                }
                return null;
            }
            return new Fact(Str("ip", "PlcIp"), Num("port", "PlcPort"), Str("previousState", "PreviousState"), Str("newState", "NewState"), Str("kind", "Kind"), Str("endCode", "EndCode"), Str("reason", "Reason"), Str("station", "Station"));
        }
        catch (JsonException) { return default; }
    }
}
