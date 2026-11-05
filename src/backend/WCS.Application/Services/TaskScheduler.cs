using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Domain.Tasks;

namespace WCS.Application.Services;

/// <summary>
/// PER_AGV scheduler. One pass: validate pending tasks, fill free AGVs from the queue (FIFO - NEEDS_CONFIRMATION: no approved
/// priority policy), recover dispatches, re-evaluate WAITING_FOR_DESTINATION, retry continue, reconcile cancels, enforce timeouts.
/// Each item is isolated so one failure cannot stall the other AGV.
/// </summary>
public sealed class TaskScheduler(
    ITransportTaskRepository tasks,
    IAgvRepository agvs,
    ITaskOrchestrator orchestrator,
    ISchedulerStatus status,
    IClock clock,
    ILogger<TaskScheduler> logger) : ITaskScheduler
{
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        string? error = null;
        async Task Safe(string what, Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = $"{what}: {ex.Message}";
                logger.LogError(ex, "Scheduler step failed: {Step}", what);
            }
        }

        foreach (var t in await tasks.ListByStatesAsync([TaskState.Pending, TaskState.Validating], 100, ct))
            await Safe($"process {t.Id}", () => orchestrator.ProcessTaskAsync(t.Id, ct));

        foreach (var agv in await agvs.ListAsync(ct))
            await Safe($"dispatch {agv.Code}", () => orchestrator.TryDispatchNextAsync(agv.Id, ct));

        foreach (var t in await tasks.ListByStatesAsync([TaskState.Dispatching], 100, ct).ContinueWith(r => r.Result.Where(x => x.RcsTaskCode is null).ToList(), ct))
            await Safe($"recover-dispatch {t.Id}", () => orchestrator.DispatchToRcsAsync(t.Id, ct));

        foreach (var t in await tasks.ListByStatesAsync([TaskState.WaitingForDestination], 100, ct))
            await Safe($"destination {t.Id}", () => orchestrator.TryResolveDestinationAsync(t.Id, ct));

        foreach (var t in await tasks.ListByStatesAsync([TaskState.Continuing], 100, ct))
            await Safe($"continue {t.Id}", () => orchestrator.SendContinueAsync(t.Id, ct));

        foreach (var t in await tasks.ListByStatesAsync([TaskState.CancelPending], 100, ct))
            await Safe($"cancel-reconcile {t.Id}", () => orchestrator.ReconcileCancelPendingAsync(t.Id, ct));

        var open = await tasks.ListByStatesAsync(Enum.GetValues<TaskState>().Where(s => !TaskStateMachine.IsTerminal(s)).ToList(), 500, ct);
        foreach (var t in open)
            await Safe($"timeout {t.Id}", () => orchestrator.EnforceTimeoutAsync(t.Id, ct));

        status.Report(clock.UtcNow, error);
    }
}

public sealed class SchedulerStatus : ISchedulerStatus
{
    private long _ticks;
    public DateTimeOffset? LastRunAt => _ticks == 0 ? null : new DateTimeOffset(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    public string? LastError { get; private set; }
    public void Report(DateTimeOffset at, string? error)
    {
        Interlocked.Exchange(ref _ticks, at.UtcTicks);
        LastError = error;
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
