using WCS.Domain.Tasks;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Common;

namespace WCS.Application.Services;

public sealed record AgvHoldReleaseResult(AgvDto Agv, Guid? TaskId, string? PreviousReason, DateTimeOffset? HeldAt);

/// <summary>
/// CR-008B-5 (D-8): the only way to lift an AGV dispatch hold. The Admin must confirm BOTH that the inventory / locations of the task were reconciled and that the AGV was
/// checked on site (safe, no longer running the old task, ready for new work). Finishing the inventory reconciliation alone never lifts the hold.
/// </summary>
public sealed class AgvHoldService(IAgvRepository agvs, ITransportTaskRepository tasks, IUnitOfWork uow, ITaskOrchestrator orchestrator, IClock clock, ILogger<AgvHoldService> logger)
{
    public async Task<AgvHoldReleaseResult> ReleaseAsync(Guid agvId, bool inventoryReconciled, bool agvPhysicallyVerified, string note, string actor, CancellationToken ct = default)
    {
        if (!inventoryReconciled || !agvPhysicallyVerified)
            throw AppException.Rejected("HOLD_RELEASE_CONFIRMATION_REQUIRED",
                "Releasing an AGV dispatch hold requires BOTH confirmations: the rack/location reconciliation is done AND the AGV was checked on site.");
        if (string.IsNullOrWhiteSpace(note)) throw AppException.Validation("NOTE_REQUIRED", "A note is required.");

        var result = await uow.ExecuteAsync(async t =>
        {
            var agv = await agvs.GetAsync(agvId, t) ?? throw AppException.NotFound("AGV_NOT_FOUND", "AGV not found.");
            if (!agv.DispatchHold) throw AppException.Rejected("AGV_NOT_ON_HOLD", $"{agv.Code} is not on dispatch hold.");
            var taskId = agv.DispatchHoldTaskId; var reason = agv.DispatchHoldReason; var at = agv.DispatchHoldAt;
            // The cause may be a task that is still active (RCS ended without a drop confirmation): the AGV is not free to take new work while it is.
            if (taskId is { } id && await tasks.GetAsync(id, t) is { IsTerminal: false })
                throw AppException.Rejected("HOLD_TASK_NOT_FINISHED", "The task that caused the hold is still active; finish or cancel it first.");
            agv.ReleaseDispatchHold();
            if (taskId is { } tid && await tasks.GetAsync(tid, t) is { } task)
                task.Note("AGV_DISPATCH_HOLD_RELEASED", $"{actor}: inventory reconciled=yes, AGV verified on site=yes. {note.Trim()}", "Operator", clock.UtcNow,
                    HistoryDetail.Of("AGV_DISPATCH_HOLD_RELEASED", ("actor", actor), ("note", note.Trim())));
            return new AgvHoldReleaseResult(agv.ToDto(null), taskId, reason, at);
        }, ct);
        // The release is already committed. Starting the next task right away is a convenience: a failure here must not turn a successful release into an error (the scheduler retries on its next tick).
        try { await orchestrator.TryDispatchNextAsync(agvId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "AGV {AgvId} was released but the immediate dispatch failed; the scheduler will retry", agvId); }
        return result;
    }
}
