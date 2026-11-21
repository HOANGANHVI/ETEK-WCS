using System.Collections.Concurrent;

namespace WCS.Application.Routing;

/// <summary>
/// CR-028: remembers, in memory, when the AMR of a leg was last asked from the RCS and what was last refused / failed, so the RCS is not asked on every scheduler tick and a repeated problem is
/// recorded once. Lost on restart (the next pass simply asks again).
/// </summary>
public sealed class LegAssignmentThrottle
{
    private sealed record Entry(DateTimeOffset At, bool Failed, string? Refused);
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public bool IsDue(Guid legId, DateTimeOffset now, TimeSpan every) => !_entries.TryGetValue(legId, out var e) || now - e.At >= every;
    public bool WasFailing(Guid legId) => _entries.TryGetValue(legId, out var e) && e.Failed;
    public string? LastRefused(Guid legId) => _entries.TryGetValue(legId, out var e) ? e.Refused : null;
    public void Checked(Guid legId, DateTimeOffset now, bool failed, string? refused = null) => _entries[legId] = new Entry(now, failed, refused);
    public void Forget(Guid legId) => _entries.TryRemove(legId, out _);
}
