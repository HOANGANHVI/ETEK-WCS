using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Diagnostics;
using WCS.Application.RealDispatch;
using WCS.Domain.Rcs;
using WCS.Infrastructure.Maintenance;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.ModeControl;

/// <summary>CR-038: the cached copy of the mode row. Before the row was read it says "Simulator, not chosen": outside Production that behaves as before CR-038, and in Production nothing is dispatched until a mode was chosen.</summary>
internal sealed class ExecutionModeProvider : IExecutionModeProvider
{
    private ExecutionModeSnapshot _current = new(WCS.Domain.Rcs.ExecutionMode.Simulator, false, false, null, DateTimeOffset.MinValue, "SYSTEM");
    public ExecutionModeSnapshot Current => Volatile.Read(ref _current);
    public bool Loaded { get; private set; }
    public void Update(ExecutionModeSnapshot snapshot) { Volatile.Write(ref _current, snapshot); Loaded = true; }
}

/// <summary>CR-038: the mode a NEW task / order is created with, read FOR SHARE inside the creating transaction (a mode change takes FOR UPDATE and waits for it).</summary>
internal sealed class ExecutionModeGate(WcsDbContext db, IExecutionModeProvider provider) : IExecutionModeGate
{
    public async Task<WCS.Domain.Rcs.ExecutionMode> ModeForNewWorkAsync(CancellationToken ct = default)
    {
        MaintenanceStore.RequireTransaction(db);
        var rows = await db.Database.SqlQueryRaw<string>("SELECT \"Mode\" AS \"Value\" FROM execution_mode_state WHERE \"Id\" = 1 FOR SHARE").ToListAsync(ct);
        if (rows.Count == 1 && Enum.TryParse<WCS.Domain.Rcs.ExecutionMode>(rows[0], out var mode)) return mode;
        return provider.Current.Mode;   // no row yet (tests, first start before initialisation)
    }
}

/// <summary>CR-038: which dispatcher a call uses = the mode pinned on the task / order that owns the request code.</summary>
internal sealed class ExecutionModeResolver(IServiceScopeFactory scopes, IExecutionModeProvider provider) : IExecutionModeResolver
{
    public async Task<WCS.Domain.Rcs.ExecutionMode?> ForRequestCodeAsync(string? requestCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestCode)) return provider.Current.Mode;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        var task = await db.Tasks.AsNoTracking().Where(t => t.RcsRequestCode == requestCode).Select(t => new { t.ExecutionMode }).FirstOrDefaultAsync(ct);
        if (task is not null) return task.ExecutionMode;   // null = created before CR-038: never sent anywhere
        var order = await (from l in db.TransportLegs.AsNoTracking() join o in db.TransportOrders.AsNoTracking() on l.OrderId equals o.Id where l.AgvRequestCode == requestCode select new { o.ExecutionMode }).FirstOrDefaultAsync(ct);
        if (order is not null) return order.ExecutionMode;
        return provider.Current.Mode;
    }

}

/// <summary>CR-038: removes secrets from a payload BEFORE it is stored or shown.</summary>
public static class RcsPayloadMasker
{
    public const string Mask = "***";
    private static readonly Regex SecretKey = new("token|password|passwd|secret|authorization|apikey|api_key|credential", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Inline = new("(?i)(\"?(?:token\\w*|password|secret\\w*|authorization)\"?\\s*[:=]\\s*)(\"[^\"]*\"|(?:(?:Bearer|Basic)\\s+)?[^\\s,;&}]+)", RegexOptions.Compiled);

    public static string MaskText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        try
        {
            var node = JsonNode.Parse(text);
            if (node is not null) { Walk(node); return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }); }
        }
        catch (JsonException) { /* not JSON: fall through to the plain text rule */ }
        return Inline.Replace(text, m => m.Groups[1].Value + "\"" + Mask + "\"");
    }

    private static void Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (SecretKey.IsMatch(key)) o[key] = Mask;
                    else if (o[key] is { } child) Walk(child);
                }
                break;
            case JsonArray a:
                foreach (var item in a) if (item is not null) Walk(item);
                break;
        }
    }
}

/// <summary>CR-038: writes RCS messages in their own short transaction. Never throws; never stores a secret; cuts an oversized payload.</summary>
internal sealed class RcsMessageRecorder(IServiceScopeFactory scopes, IClock clock, ILogger<RcsMessageRecorder> logger) : IRcsMessageRecorder
{
    public const int MaxPayloadChars = 32_000;

    public async Task RecordAsync(RcsMessageEntry e, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            Guid? taskId = null; string? requestKey = null; string? rcsTaskCode = e.RcsTaskCode;
            if (!string.IsNullOrWhiteSpace(e.RequestCode))
            {
                var t = await db.Tasks.AsNoTracking().Where(x => x.RcsRequestCode == e.RequestCode).Select(x => new { x.Id, x.RequestKey, x.RcsTaskCode }).FirstOrDefaultAsync(CancellationToken.None);
                if (t is not null) { taskId = t.Id; requestKey = t.RequestKey; rcsTaskCode ??= t.RcsTaskCode; }
                else
                {
                    var o = await (from l in db.TransportLegs.AsNoTracking() join ord in db.TransportOrders.AsNoTracking() on l.OrderId equals ord.Id where l.AgvRequestCode == e.RequestCode select new { ord.Id, ord.RequestKey }).FirstOrDefaultAsync(CancellationToken.None);
                    if (o is not null) { taskId = o.Id; requestKey = o.RequestKey; }
                }
            }
            string? payload = null; var truncated = false;
            if (e.Payload is not null)
            {
                payload = RcsPayloadMasker.MaskText(e.Payload);
                if (payload.Length > MaxPayloadChars) { payload = payload[..MaxPayloadChars]; truncated = true; }
            }
            var row = RcsMessage.Create(e.Mode, e.Direction, e.MessageType, e.Protocol, e.RequestCode, clock.UtcNow)
                .With(taskId, requestKey, rcsTaskCode, e.Endpoint, e.HttpMethod, e.HttpStatus, payload, truncated, e.Result, e.ErrorCode, Cut(e.ErrorMessage, 500), DiagnosticContext.CorrelationId, e.DurationMs);
            db.RcsMessages.Add(row);
            await db.SaveChangesAsync(CancellationToken.None);   // the message is kept even when the caller was cancelled
            if (scope.ServiceProvider.GetService<IWcsNotifier>() is { } notifier)
                await notifier.PublishAsync([new WcsEvent("RcsMessageRecorded", new { id = row.Id, taskId, mode = e.Mode.ToString(), direction = e.Direction.ToString() })], CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "An RCS message could not be recorded ({MessageType} {Direction}, request {RequestCode}). The call itself is not affected. {EventCode}", e.MessageType, e.Direction, e.RequestCode, "RCS_MESSAGE_RECORD_FAILED");
        }
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

internal sealed class RcsMessageQuery(WcsDbContext db) : IRcsMessageQuery
{
    public async Task<IReadOnlyList<RcsMessageSummary>?> ForTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await db.Tasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => new { t.RcsRequestCode }).FirstOrDefaultAsync(ct);
        var isOrder = task is null && await db.TransportOrders.AsNoTracking().AnyAsync(o => o.Id == taskId, ct);
        if (task is null && !isOrder) return null;
        var code = task?.RcsRequestCode;
        var rows = await db.RcsMessages.AsNoTracking().Where(m => m.TaskId == taskId || (code != null && m.RequestCode == code))
            .OrderBy(m => m.Seq)
            .Select(m => new RcsMessageSummary(m.Seq, m.Id, m.CreatedAt, m.Mode, m.Direction, m.MessageType, m.Protocol, m.Result, m.HttpStatusCode, m.ErrorCode, m.RequestCode)).ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<RcsMessageSummary>> ByRequestCodesAsync(IReadOnlyList<string> requestCodes, CancellationToken ct = default) =>
        requestCodes.Count == 0 ? [] : await db.RcsMessages.AsNoTracking().Where(m => m.RequestCode != null && requestCodes.Contains(m.RequestCode)).OrderBy(m => m.Seq).Take(500)
            .Select(m => new RcsMessageSummary(m.Seq, m.Id, m.CreatedAt, m.Mode, m.Direction, m.MessageType, m.Protocol, m.Result, m.HttpStatusCode, m.ErrorCode, m.RequestCode)).ToListAsync(ct);

    public async Task<IReadOnlyList<RcsMessageSummary>> UnlinkedAsync(int take, CancellationToken ct = default) =>
        await db.RcsMessages.AsNoTracking().Where(m => m.TaskId == null).OrderByDescending(m => m.Seq).Take(Math.Clamp(take, 1, 200))
            .Select(m => new RcsMessageSummary(m.Seq, m.Id, m.CreatedAt, m.Mode, m.Direction, m.MessageType, m.Protocol, m.Result, m.HttpStatusCode, m.ErrorCode, m.RequestCode)).ToListAsync(ct);

    public Task<RcsMessageDetail?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.RcsMessages.AsNoTracking().Where(m => m.Id == id).Select(m => new RcsMessageDetail(m.Id, m.TaskId, m.RequestKey, m.RequestCode, m.RcsTaskCode, m.Mode, m.Direction, m.MessageType, m.Protocol,
            m.Endpoint, m.HttpMethod, m.HttpStatusCode, m.Payload, m.PayloadTruncated, m.Result, m.ErrorCode, m.ErrorMessage, m.CorrelationId, m.CreatedAt, m.DurationMs)).FirstOrDefaultAsync(ct);
}

internal sealed class FleetStatusReader(WCS.Application.Fleet.FleetService fleet) : IFleetStatusReader
{
    public async Task<WCS.Application.Fleet.FleetView?> ReadAsync(CancellationToken ct = default) => await fleet.GetAsync(false, ct);
}
