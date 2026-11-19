namespace WCS.Application.Abstractions;

/// <summary>
/// CR-026: side effects that are not database writes (realtime events) and that a service raises while it runs inside
/// <see cref="IUnitOfWork.ExecuteBatchAsync{T}"/>. They are collected and run only AFTER the batch has committed; a rolled-back batch discards them,
/// so no client is told about a change that never happened. Outside a batch nothing is deferred.
/// </summary>
public static class BatchEffects
{
    private static readonly AsyncLocal<List<Func<Task>>?> Current = new();

    /// <summary>True while a batch is open on the current logical call chain.</summary>
    public static bool Active => Current.Value is not null;

    /// <summary>Starts collecting (called by the unit of work at the start of every batch attempt).</summary>
    public static List<Func<Task>> Begin() => Current.Value = [];

    /// <summary>Stops collecting without running anything (rollback / retry).</summary>
    public static void End() => Current.Value = null;

    /// <summary>Queues <paramref name="effect"/> when a batch is open. Returns false (and runs nothing) when there is no batch, so the caller must run it itself.</summary>
    public static bool TryDefer(Func<Task> effect)
    {
        if (Current.Value is not { } list) return false;
        list.Add(effect);
        return true;
    }
}

/// <summary>Wraps the real notifier: inside a batch the events wait for the commit, otherwise they are published immediately.</summary>
public sealed class BatchAwareNotifier(IWcsNotifier inner) : IWcsNotifier
{
    public Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default) =>
        BatchEffects.TryDefer(() => inner.PublishAsync(events, CancellationToken.None)) ? Task.CompletedTask : inner.PublishAsync(events, ct);
}
