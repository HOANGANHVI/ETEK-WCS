using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using WCS.Application.Logging;
using WCS.Domain.Logging;
using WCS.Infrastructure.Persistence;

namespace WCS.Api.Logging;

/// <summary>Turns a Serilog event into a <see cref="SystemLogEntry"/> (or nothing). Pure: unit tested without a database.</summary>
public static class LogClassifier
{
    private static readonly string[] Ignored = ["SourceContext", "ActionId", "ActionName", "RequestId", "ConnectionId", "CorrelationId", "Host", "EventCode", "Device", "Category", "ParentId", "SpanId", "TraceId"];
    private static readonly string[] SkipSources = ["Microsoft.EntityFrameworkCore", "Npgsql", "WCS.Api.Logging"];

    public static bool ShouldPersist(LogEvent e)
    {
        var source = Prop(e, "SourceContext") ?? "";
        if (SkipSources.Any(p => source.StartsWith(p, StringComparison.Ordinal))) return false;       // never log the logger's own storage
        if (Prop(e, "Category") == LogCategories.Plc) return false;                                    // PLC events live in plc_comm_log
        if (e.Level >= LogEventLevel.Warning) return true;
        return e.Properties.ContainsKey("EventCode");                                                   // tagged Information events (startup summary, db state ...)
    }

    public static SystemLogEntry? ToEntry(LogEvent e)
    {
        if (!ShouldPersist(e)) return null;
        var source = Prop(e, "SourceContext") ?? "WCS";
        var eventCode = Prop(e, "EventCode");
        var message = LogSanitizer.Redact(e.RenderMessage(), 2000) ?? "";
        var exception = e.Exception is null ? null : LogSanitizer.Redact(e.Exception.ToString());
        var level = e.Level switch { LogEventLevel.Fatal => LogLevels.Critical, LogEventLevel.Error => LogLevels.Error, LogEventLevel.Warning => LogLevels.Warning, _ => LogLevels.Information };
        var device = Prop(e, "Device") ?? Prop(e, "PlcId") ?? Prop(e, "Agv") ?? Prop(e, "RobotCode");
        return new SystemLogEntry(e.Timestamp, Classify(e, source, eventCode, message), level, source, device, eventCode, message, exception,
            Prop(e, "CorrelationId") ?? Prop(e, "TraceId"), Details(e), Prop(e, "Host"), messageTemplate: e.MessageTemplate.Text);
    }

    public static string Classify(LogEvent e, string source, string? eventCode, string message)
    {
        if (Prop(e, "Category") is { } c && LogCategories.IsValid(c)) return c;
        if (eventCode?.StartsWith("DB_", StringComparison.Ordinal) == true || source.Contains("DbHealth", StringComparison.Ordinal) || IsDatabaseException(e.Exception)) return LogCategories.Database;
        if (source.Contains("AgvStatus", StringComparison.Ordinal) || source.Contains("Rcs", StringComparison.OrdinalIgnoreCase)
            || source.Contains("TaskOrchestrator", StringComparison.Ordinal) && message.Contains("RCS", StringComparison.Ordinal)) return LogCategories.RcsAgv;
        if (source.Contains("Auth", StringComparison.Ordinal) || source.Contains("Audit", StringComparison.Ordinal) || source.Contains("Bootstrap", StringComparison.Ordinal)) return LogCategories.SecurityAudit;
        return LogCategories.System;
    }

    private static bool IsDatabaseException(Exception? ex)
    {
        for (var i = 0; ex is not null && i < 6; i++, ex = ex.InnerException)
        {
            var n = ex.GetType().FullName ?? "";
            if (n.StartsWith("Npgsql.", StringComparison.Ordinal) || n.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string? Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) ? (v is ScalarValue { Value: { } o } ? o.ToString() : v.ToString().Trim('"')) : null;

    private static string? Details(LogEvent e)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (k, v) in e.Properties)
        {
            if (Ignored.Contains(k) || v is not ScalarValue s || s.Value is null) continue;
            dict[k] = s.Value is string str ? LogSanitizer.Redact(str, 500) : s.Value is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : s.Value.ToString();
        }
        return dict.Count == 0 ? null : JsonSerializer.Serialize(dict);
    }
}

/// <summary>
/// CR-007: stores SYSTEM / DATABASE / RCS_AGV / SECURITY events (Warning and above, plus events tagged with an EventCode) in <c>system_log</c>. It is a best-effort side channel:
/// <list type="bullet">
/// <item>never throws into the caller and never blocks it (bounded in-memory queue, oldest dropped, drop count reported through SelfLog);</item>
/// <item>keeps entries while the database is unreachable and stores them (with their original time) when it returns; stdout is always written as well;</item>
/// <item>never persists its own or the database driver's events, so a database outage cannot cause a logging loop.</item>
/// </list>
/// </summary>
public sealed class SystemLogSink : ILogEventSink, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private readonly LinkedList<SystemLogEntry> _queue = new();
    private readonly int _capacity;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _flushLock = new(1, 1);   // one flush at a time: the background loop, tests and Dispose must not store the same batch twice
    private readonly Task _loop;
    private long _dropped;

    private readonly Func<IReadOnlyList<SystemLogEntry>, CancellationToken, Task> _store;
    private readonly Func<CancellationToken, Task<bool>> _databaseUp;

    /// <param name="store">Storage override (tests). Default: one SaveChanges on <see cref="WcsDbContext"/>.</param>
    /// <param name="databaseUp">Reachability probe (tests). Default: <c>Database.CanConnectAsync</c>.</param>
    public SystemLogSink(IServiceProvider services, int capacity = 2000, Func<IReadOnlyList<SystemLogEntry>, CancellationToken, Task>? store = null, Func<CancellationToken, Task<bool>>? databaseUp = null)
    {
        _services = services; _capacity = capacity;
        _store = store ?? StoreAsync; _databaseUp = databaseUp ?? DatabaseUpAsync;
        _loop = Task.Run(RunAsync);
    }

    private async Task StoreAsync(IReadOnlyList<SystemLogEntry> batch, CancellationToken ct)
    {
        var scopes = _services.GetService<IServiceScopeFactory>() ?? throw new InvalidOperationException("host is still starting");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.SystemLog.AddRange(batch);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> DatabaseUpAsync(CancellationToken ct)
    {
        try
        {
            var scopes = _services.GetService<IServiceScopeFactory>(); if (scopes is null) return false;
            await using var scope = scopes.CreateAsyncScope();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(3));
            return await scope.ServiceProvider.GetRequiredService<WcsDbContext>().Database.CanConnectAsync(cts.Token);
        }
        catch (Exception) { return false; }
    }

    public long Dropped => Interlocked.Read(ref _dropped);
    public int Pending { get { lock (_gate) return _queue.Count; } }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            var entry = LogClassifier.ToEntry(logEvent);
            if (entry is null) return;
            lock (_gate)
            {
                _queue.AddLast(entry);
                while (_queue.Count > _capacity) { _queue.RemoveFirst(); Interlocked.Increment(ref _dropped); }
            }
            _signal.Release();
        }
        catch (Exception ex) { SelfLog.WriteLine("SystemLogSink.Emit failed: {0}", ex.Message); }
    }

    private async Task RunAsync()
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!_cts.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(TimeSpan.FromSeconds(2), _cts.Token); } catch (OperationCanceledException) { break; }
            if (!await FlushAsync(_cts.Token)) { try { await Task.Delay(delay, _cts.Token); } catch (OperationCanceledException) { break; } delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2)); }
            else delay = TimeSpan.FromSeconds(2);
        }
    }

    /// <summary>Stores up to 100 pending entries per round; returns false when the database could not be reached (entries stay queued).</summary>
    public async Task<bool> FlushAsync(CancellationToken ct)
    {
        try { await _flushLock.WaitAsync(ct); } catch (OperationCanceledException) { return false; }
        try { return await FlushCoreAsync(ct); }
        finally { _flushLock.Release(); }
    }

    private async Task<bool> FlushCoreAsync(CancellationToken ct)
    {
        while (true)
        {
            List<SystemLogEntry> batch;
            lock (_gate) batch = _queue.Take(100).ToList();
            if (batch.Count == 0) return true;
            try
            {
                await _store(batch, ct);
                lock (_gate) foreach (var e in batch) _queue.Remove(e);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                SelfLog.WriteLine("SystemLogSink could not store {0} entries: {1}", batch.Count, ex.GetType().Name);
                // Database unreachable: keep everything and retry later. Database reachable: the batch contains an entry that can never be stored ("poison"),
                // so store the entries one by one and drop only the offender(s) - otherwise one bad entry would block all later events.
                if (!await _databaseUp(ct)) return false;
                foreach (var entry in batch)
                {
                    try { await _store([entry], ct); }
                    catch (OperationCanceledException) { return false; }
                    catch (Exception one) { Interlocked.Increment(ref _dropped); SelfLog.WriteLine("SystemLogSink dropped an entry that cannot be stored: {0}", one.GetType().Name); }
                    lock (_gate) _queue.Remove(entry);
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(1000); } catch { /* stopping */ }
        try { using var t = new CancellationTokenSource(TimeSpan.FromSeconds(3)); FlushAsync(t.Token).GetAwaiter().GetResult(); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
