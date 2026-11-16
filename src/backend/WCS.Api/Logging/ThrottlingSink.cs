using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace WCS.Api.Logging;

/// <summary>
/// CR-007: stops one repeating failure (for example "Scheduler tick failed" every second while the database is down) from flooding the log. Warning and above are
/// throttled per (source, message template, exception type and message): the first occurrence passes, identical ones inside the window are counted, and the next
/// one that passes carries "(+N identical suppressed)". Information and below are never touched. PLC events (Category=PLC) bypass the throttle: they are already
/// rate-limited at the source and every one of them matters (for example WRITE_AMBIGUOUS). Logging only: no business flow depends on this class.
/// </summary>
public sealed class ThrottlingSink : ILogEventSink, IDisposable
{
    private sealed class State { public DateTimeOffset LastEmit = DateTimeOffset.MinValue; public int Suppressed; }

    private readonly ILogEventSink _inner;
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, State> _states = new();
    private static readonly MessageTemplateParser Parser = new();

    public ThrottlingSink(ILogEventSink inner, TimeSpan window, Func<DateTimeOffset>? clock = null)
    {
        _inner = inner; _window = window; _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            if (logEvent.Level < LogEventLevel.Warning || Bypass(logEvent) || _window <= TimeSpan.Zero) { _inner.Emit(logEvent); return; }

            var now = _clock();
            var state = _states.GetOrAdd(Key(logEvent), _ => new State());
            int suppressed;
            lock (state)
            {
                if (now - state.LastEmit < _window) { state.Suppressed++; return; }
                suppressed = state.Suppressed; state.Suppressed = 0; state.LastEmit = now;
            }
            if (_states.Count > 2000) Prune(now);
            _inner.Emit(suppressed > 0 ? WithSuppressed(logEvent, suppressed) : logEvent);
        }
        catch (Exception)
        {
            // never let the log pipeline break the application: fall back to the plain event
            try { _inner.Emit(logEvent); } catch { /* nothing else to do */ }
        }
    }

    private static bool Bypass(LogEvent e) =>
        e.Properties.TryGetValue("Category", out var c) && c is ScalarValue { Value: string s } && s == "PLC"
        || e.Properties.ContainsKey("NoThrottle");

    private static string Key(LogEvent e)
    {
        var source = e.Properties.TryGetValue("SourceContext", out var sc) ? sc.ToString() : "";
        var ex = e.Exception;
        var detail = ex is null ? Truncate(e.RenderMessage(), 120) : ex.GetType().FullName + "|" + Truncate(ex.Message, 120);
        return source + "|" + e.MessageTemplate.Text + "|" + detail;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    private static LogEvent WithSuppressed(LogEvent e, int count)
    {
        var template = Parser.Parse(e.MessageTemplate.Text + " (+{SuppressedRepeats} identical events suppressed)");
        var props = e.Properties.Select(p => new LogEventProperty(p.Key, p.Value)).Append(new LogEventProperty("SuppressedRepeats", new ScalarValue(count)));
        return new LogEvent(e.Timestamp, e.Level, e.Exception, template, props, e.TraceId ?? default, e.SpanId ?? default);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (k, v) in _states)
            if (now - v.LastEmit > _window * 10) _states.TryRemove(k, out _);
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}

/// <summary>Sends every event to all sinks; a failing sink never prevents the others (stdout must keep working when the database sink fails).</summary>
public sealed class CompositeSink(IEnumerable<ILogEventSink> sinks) : ILogEventSink, IDisposable
{
    private readonly ILogEventSink[] _sinks = sinks.ToArray();

    public void Emit(LogEvent logEvent)
    {
        foreach (var s in _sinks)
        {
            try { s.Emit(logEvent); }
            catch (Exception ex) { Serilog.Debugging.SelfLog.WriteLine("Log sink {0} failed: {1}", s.GetType().Name, ex.Message); }
        }
    }

    public void Dispose() { foreach (var s in _sinks) (s as IDisposable)?.Dispose(); }
}
