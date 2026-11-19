using Microsoft.Extensions.Logging;
using WCS.Application.Diagnostics;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Intake;

namespace WCS.Application.Services;

/// <summary>
/// Intake of station requests that arrive through the PLC connections (CR-008B-2).
///
/// Why a store: the PLC service reports a request on the RISING EDGE of a bit only, WCS never writes <c>Request_Ack</c> (no approved handshake) and the request is
/// therefore NOT presented again. A request that WCS cannot turn into a task right now (RCS not connected, technical failure) would be lost. So every detected
/// request is first written to <c>station_requests</c> (unique <c>RequestKey</c> = <c>PlcId:Station:Request_ID</c>) and then processed from the store:
///  - Pending requests wait while RCS is not connected (no attempt is consumed) and are retried with the RCS backoff after a technical failure;
///  - a request is Accepted only after <c>CreateTaskAsync</c> has committed (or returned the existing task of the same key) - a crash in between only repeats an idempotent call;
///  - a permanent business rejection (any other <see cref="AppException"/>) is recorded as Rejected and acknowledged, as before;
///  - the same key detected again (restart, bit toggled with the same Request_ID) never creates a second record or task.
/// </summary>
public sealed class StationRequestProcessor(IStationSignalService stations, ITaskOrchestrator orchestrator, IRcsDispatchGuard rcsGuard, IMaintenanceGate maintenance,
    IStationRequestRepository requests, IUnitOfWork uow, IClock clock, IOptions<SchedulerOptions> options, ILogger<StationRequestProcessor> logger)
{
    private const int BatchSize = 100;

    /// <summary>Returns the number of requests accepted in this pass (including re-detected requests that were already accepted).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var handled = 0;
        var alreadyAccepted = new List<StationRequest>();
        var alreadyRejected = new List<StationRequest>();
        var repeatedWhilePending = new List<StationRequest>();

        // 1) durable capture of what the signal service detected
        IReadOnlyList<StationRequest> detected = [];
        try { detected = await stations.PollRequestsAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Polling station requests failed; the stored pending requests are still processed");
        }
        foreach (var request in detected)
        {
            try
            {
                var existing = await uow.ExecuteAsync(async t =>
                {
                    var found = await requests.GetByKeyAsync(request.RequestKey, t);
                    if (found is null) { requests.Add(new StationRequestRecord(request.RequestKey, request.StationCode, request.Flow, clock.UtcNow)); return (StationRequestStatus?)null; }
                    return found.Status;
                }, ct);
                if (existing == StationRequestStatus.Accepted) alreadyAccepted.Add(request);
                else if (existing == StationRequestStatus.Rejected) alreadyRejected.Add(request);
                else if (existing == StationRequestStatus.Pending) repeatedWhilePending.Add(request);   // same key twice in a row: decided together with the stored one
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Storing station request {Key} from {Station} failed (it is detected again after a restart)", request.RequestKey, request.StationCode);
            }
        }

        // 2) process the pending requests - only while RCS can take a task; the requests simply wait otherwise (nothing is lost, no attempt is used)
        var link = rcsGuard.Check();
        if (!link.Allowed) logger.LogDebug("Station requests wait: {Code}", link.Code);
        else if (await maintenance.IsEnabledAsync(ct)) logger.LogDebug("Station requests wait: {Code}", MaintenanceCodes.Code);   // CR-026 (OQ-1): stay Pending - not accepted, not rejected, not acknowledged, no attempt used
        else handled += await ProcessPendingAsync(ct);

        // 3) a request that was already decided is re-acknowledged (idempotent, as a duplicate used to be); a repeat of a request that was pending takes the decision of the stored one
        foreach (var r in repeatedWhilePending)
        {
            var status = await uow.ExecuteAsync(async t => (await requests.GetByKeyAsync(r.RequestKey, t))?.Status, ct);
            if (status == StationRequestStatus.Accepted) alreadyAccepted.Add(r);
            else if (status == StationRequestStatus.Rejected) alreadyRejected.Add(r);
        }
        foreach (var r in alreadyAccepted) { if (await TryAckAsync(r)) handled++; }
        foreach (var r in alreadyRejected) await TryAckAsync(r);
        return handled;
    }

    private async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var o = options.Value;
        var pending = await uow.ExecuteAsync(t => requests.ListPendingAsync(BatchSize, t), ct);
        var handled = 0;
        foreach (var p in pending)
        {
            if (p.Attempts > 0 && !RcsRetryPolicy.IsDue(p.Attempts, p.LastAttemptAt, clock.UtcNow, o)) continue;
            var request = new StationRequest(p.StationCode, p.Flow, p.RequestKey);
            using var _frame = DiagnosticContext.Begin(DiagnosticContext.NewCorrelationId(), stationId: p.StationCode);   // CR-021: the task created for this PLC request keeps this id
            try
            {
                var result = await orchestrator.CreateTaskAsync(new CreateTaskCommand(p.RequestKey, p.Flow, p.StationCode, "PLC"), ct);
                await Decide(p.RequestKey, r => r.MarkAccepted(result.Task.Id, clock.UtcNow), ct);   // the task is already committed; this only records it
                using (DiagnosticContext.Begin(null, taskId: result.Task.Id.ToString("D")))
                    logger.LogInformation("Station request {Key} from {Station} accepted: flow {Flow}, task {TaskId}. {EventCode}", p.RequestKey, p.StationCode, p.Flow, result.Task.Id, "STATION_REQUEST_ACCEPTED");
                await TryAckAsync(request);
                handled++;
            }
            catch (AppException ex) when (ex.Code is "RCS_NOT_CONNECTED" or MaintenanceCodes.Code)
            {
                // not a rejection and not an attempt: the link state (or Maintenance, CR-026) changed between the check and the call. Keep the request.
                logger.LogDebug("Station request {Key} kept: {Code}", p.RequestKey, ex.Code);
            }
            catch (AppException ex)
            {
                // permanent business rejection (unsupported flow / station, ...): decided, recorded and acknowledged
                logger.LogWarning("Station request {Key} from {Station} rejected: {Code} {Message}. {EventCode}", p.RequestKey, p.StationCode, ex.Code, ex.Message, "STATION_REQUEST_REJECTED");
                await Decide(p.RequestKey, r => r.MarkRejected(ex.Code, ex.Message, clock.UtcNow), ct);
                await TryAckAsync(request);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // technical failure of ONE request: it stays pending (backoff, bounded) and never stops the others
                var attempts = p.Attempts + 1;
                logger.LogError(ex, "Station request {Key} from {Station} failed (attempt {Attempt}); it stays pending. {EventCode}", p.RequestKey, p.StationCode, attempts, "STATION_REQUEST_FAILED");
                var giveUp = attempts >= Math.Max(1, o.RcsMaxAttempts);
                await Decide(p.RequestKey, r =>
                {
                    r.RegisterFailure(ex.Message, clock.UtcNow);
                    if (giveUp) r.MarkFailed($"automatic processing stopped after {attempts} attempts: {ex.Message}", clock.UtcNow);
                }, ct);
                if (giveUp) logger.LogError("Station request {Key} stopped after {Attempts} attempts; an operator has to decide. {EventCode}", p.RequestKey, attempts, "STATION_REQUEST_ABANDONED");
            }
        }
        return handled;
    }

    private async Task Decide(string key, Action<StationRequestRecord> change, CancellationToken ct)
    {
        try
        {
            await uow.ExecuteAsync(async t =>
            {
                var record = await requests.GetByKeyAsync(key, t);
                if (record is { IsPending: true }) change(record);
                return 0;
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Recording the decision for station request {Key} failed (the next pass repeats the idempotent call)", key);
        }
    }

    private async Task<bool> TryAckAsync(StationRequest request)
    {
        try { await stations.AcknowledgeRequestAsync(request, CancellationToken.None); return true; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Acknowledging station request {Key} failed", request.RequestKey);
            return false;
        }
    }
}
