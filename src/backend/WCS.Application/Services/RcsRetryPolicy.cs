using WCS.Application.Abstractions;

namespace WCS.Application.Services;

/// <summary>
/// CR-008A: technical backoff for RCS calls that are repeated by the scheduler. It spaces attempts (initial delay, doubling, capped) so a failing RCS is not called every tick;
/// it is NOT a business timeout and never changes a task state by itself.
/// </summary>
public static class RcsRetryPolicy
{
    public static TimeSpan DelayAfter(int attempts, SchedulerOptions o)
    {
        if (attempts <= 0) return TimeSpan.Zero;
        var initial = Math.Max(0, o.RcsRetryInitialDelayMs);
        var max = Math.Max(initial, o.RcsRetryMaxDelayMs);
        double ms = initial;
        for (var i = 1; i < attempts && ms < max; i++) ms *= 2;
        return TimeSpan.FromMilliseconds(Math.Min(ms, max));
    }

    public static bool IsDue(int attempts, DateTimeOffset? lastAttemptAt, DateTimeOffset now, SchedulerOptions o) =>
        attempts <= 0 || lastAttemptAt is null || now - lastAttemptAt.Value >= DelayAfter(attempts, o);
}
