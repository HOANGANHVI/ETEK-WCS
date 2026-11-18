using System.Text.RegularExpressions;

namespace WCS.Application.Diagnostics;

/// <summary>The identifiers that tie log events of different modules together while one unit of work runs. Every field is optional; nothing is invented.</summary>
public sealed record DiagFrame(string? CorrelationId, string? TaskId = null, string? AgvId = null, string? PlcId = null, string? StationId = null, string? RackId = null);

/// <summary>
/// CR-021 batch B: the ambient diagnostic context (AsyncLocal). An HTTP request starts it with its CorrelationId; a task step (scheduler tick, RCS call, signal, cancel) re-enters the TASK's own
/// CorrelationId, so the whole life of a task shares one id even though it runs on many requests and workers. The logging pipeline copies the frame into every log event (see the Serilog
/// enricher in the API project). It carries identifiers only and never changes behaviour; <see cref="Begin"/> returns a scope that restores the previous frame, so a frame never leaks to the caller.
/// </summary>
public static class DiagnosticContext
{
    private static readonly AsyncLocal<DiagFrame?> Holder = new();
    private static readonly Regex Valid = new(@"^[A-Za-z0-9._\-]{8,64}$", RegexOptions.Compiled);

    public static DiagFrame? Current => Holder.Value;
    public static string? CorrelationId => Holder.Value?.CorrelationId;

    /// <summary>A new, opaque, URL-safe id (32 hex chars).</summary>
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    /// <summary>An id received from outside (header) is accepted only when it is short and made of harmless characters; otherwise null (the caller then makes its own).</summary>
    public static string? Accept(string? external) => external is not null && Valid.IsMatch(external) ? external : null;

    /// <summary>Enters a frame. Fields left null inherit from the current frame (so a PLC id can be added to a task's frame), except CorrelationId which, when given, replaces it.</summary>
    public static IDisposable Begin(string? correlationId, string? taskId = null, string? agvId = null, string? plcId = null, string? stationId = null, string? rackId = null)
    {
        var prev = Holder.Value;
        Holder.Value = new DiagFrame(correlationId ?? prev?.CorrelationId, taskId ?? prev?.TaskId, agvId ?? prev?.AgvId, plcId ?? prev?.PlcId, stationId ?? prev?.StationId, rackId ?? prev?.RackId);
        return new Scope(prev);
    }

    private sealed class Scope(DiagFrame? previous) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (_done) return; _done = true; Holder.Value = previous; }
    }
}

/// <summary>
/// CR-021 batch B: a task's / order's CorrelationId never changes once set, so it is remembered in memory (bounded) instead of being read again at every scheduler tick. A row without a
/// correlation (created before CR-021) is NOT cached: it simply has none and the caller's frame applies.
/// </summary>
public static class CorrelationCache
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> Map = new();
    private const int Max = 20_000;

    public static async ValueTask<string?> GetAsync(Guid id, Func<Task<string?>> load)
    {
        if (Map.TryGetValue(id, out var v)) return v;
        var loaded = await load();
        if (loaded is not null) { if (Map.Count >= Max) Map.Clear(); Map[id] = loaded; }
        return loaded;
    }

    public static void Remember(Guid id, string? correlationId) { if (correlationId is not null) { if (Map.Count >= Max) Map.Clear(); Map[id] = correlationId; } }
}
