using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Fleet;
using WCS.Domain.Tasks;

namespace WCS.Application.Fleet;

/// <summary>
/// CR-027 Phase 1: keeps the read model of the AMR fleet that the RCS reports (queryAgvStatus) and answers the AGV Monitoring screen.
/// It only READS from the RCS, never writes the <c>agvs</c> table, never touches a task, never creates an AMR of its own and never deletes a snapshot.
/// </summary>
public sealed class FleetService(IRcsFleetQuery query, IRcsFleetRepository repo, IAgvRepository agvs, ITransportTaskRepository tasks, IUnitOfWork uow, IWcsNotifier notifier,
    IClock clock, IOptions<RcsFleetOptions> options, IOptions<DispatchOptions> dispatch, DispatchModeState modeState, ILogger<FleetService> logger)
{
    private RcsFleetOptions O => options.Value;
    private FleetSource Source => query.Source;

    /// <summary>One poll. Called by the existing AGV status loop on every tick; the interval is applied here, so a faster tick never queries faster than configured.</summary>
    /// <summary>One poll at a time per process: a manual refresh and the 5-second loop never overlap (the second caller returns at once).</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);
    // The console / file line is written for EVERY poll (and bypasses the 60-second throttle of the log pipeline); the searchable database copy (system_log) is rate-limited here so a long outage or a long
    // run does not fill it: a failure is stored when it is new, changed or 60 s after the last stored one; a success only when it follows a failure or 60 s after the last stored one.
    private static DateTimeOffset _lastStoredFailure = DateTimeOffset.MinValue, _lastStoredSuccess = DateTimeOffset.MinValue;
    private static string? _lastFailureCode;
    private static readonly TimeSpan StoreEvery = TimeSpan.FromSeconds(60);

    private IDisposable? Scope(bool store) =>
        logger.BeginScope(store ? new Dictionary<string, object?> { ["NoThrottle"] = true } : new Dictionary<string, object?> { ["NoThrottle"] = true, ["NoPersist"] = true });

    public async Task<bool> RefreshAsync(CancellationToken ct = default, bool force = false)
    {
        if (!await Gate.WaitAsync(0, ct)) return false;
        try { return await RefreshCoreAsync(ct, force); }
        finally { Gate.Release(); }
    }

    /// <summary>Removes anything that looks like a credential from a text that goes to a log (the client never puts one there; this is the second line of defence).</summary>
    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var t = System.Text.RegularExpressions.Regex.Replace(text, "(?i)(\\w*(?:token|password|passwd|secret|authorization)\\w*)\\s*[\"']?\\s*[:=]\\s*[\"']?[^\\s\"',;}]+", "$1=***");
        return t.Length > 200 ? t[..200] : t;
    }

    private async Task<bool> RefreshCoreAsync(CancellationToken ct, bool force)
    {
        var now = clock.UtcNow;
        var state = await uow.ExecuteAsync(t => repo.GetStateAsync(Source, t), ct);
        // a tick of the 5-second timer may arrive a few milliseconds early: without the tolerance every second tick would be skipped (a 10-second poll)
        if (!force && state?.LastAttemptAt is { } last && now - last < TimeSpan.FromSeconds(O.EffectiveInterval) - TimeSpan.FromMilliseconds(500)) return false;

        using (Scope(false)) logger.LogInformation("RCS fleet poll started (source {Source}, interval {IntervalSeconds} s, timeout {TimeoutSeconds} s). {EventCode}", Source, O.EffectiveInterval, Math.Max(1, O.TimeoutSeconds), "RCS_FLEET_POLL_STARTED");
        RcsFleetResult result;
        var pollWatch = System.Diagnostics.Stopwatch.StartNew();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, O.TimeoutSeconds)));
            try { result = await query.QueryAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = RcsFleetResult.Fail(FleetErrorCodes.Timeout, $"No answer within {O.TimeoutSeconds} s."); }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = RcsFleetResult.Fail(FleetErrorCodes.Unreachable, ex.Message); }
        }

        bool wasOk = false, changed = false; int count = 0, failures = 0, newAmrs = 0, missingAmrs = 0;
        await uow.ExecuteAsync(async t =>
        {
            changed = false;
            var s = await repo.GetStateAsync(Source, t);
            if (s is null) { s = new RcsFleetState(Source); repo.AddState(s); }
            wasOk = s.LastAttemptOk;
            var before = (s.LastAttemptOk, s.LastErrorCode, s.LastAmrCount);
            if (!result.Success)
            {
                s.Failed(now, result.ErrorCode ?? FleetErrorCodes.Unreachable, result.ErrorMessage, result.LatencyMs);   // the stored AMRs stay as they were: they are shown as not updated, never replaced by invented ones
                changed = before.LastAttemptOk != s.LastAttemptOk || before.LastErrorCode != s.LastErrorCode;
                failures = s.ConsecutiveFailures;
                return 0;
            }
            var known = (await repo.ListAsync(Source, t)).ToDictionary(x => x.RobotCode, StringComparer.Ordinal);
            foreach (var a in result.Amrs)
            {
                var path = a.Path.Count == 0 ? null : JsonSerializer.Serialize(a.Path);
                if (!known.TryGetValue(a.RobotCode, out var snap))
                {
                    snap = new RcsAmrSnapshot(Source, a.RobotCode, now);
                    repo.Add(snap);
                    changed = true; newAmrs++;                                                // a new AMR appeared
                }
                if (snap.Apply(a.RobotDir, a.RobotIp, a.Battery, a.PosX, a.PosY, a.MapCode, a.Speed, a.Status, a.ExclType, a.Stop, a.PodCode, a.PodDir, path, now)) changed = true;
            }
            missingAmrs = known.Values.Count(k => result.Amrs.All(a => a.RobotCode != k.RobotCode));
            // an AMR that is not in this response keeps its row and its old LastSeenAt: it is then shown as "missing", not deleted
            if (known.Values.Any(k => k.LastSeenAt == s.LastSuccessAt && result.Amrs.All(a => a.RobotCode != k.RobotCode))) changed = true;
            s.Succeeded(now, result.Amrs.Count, result.LatencyMs);
            count = result.Amrs.Count;
            if (!before.LastAttemptOk || before.LastErrorCode is not null || before.LastAmrCount != s.LastAmrCount) changed = true;
            return 0;
        }, ct);

        var durationMs = result.LatencyMs ?? pollWatch.ElapsedMilliseconds;
        if (result.Success)
        {
            var storeOk = !wasOk || now - _lastStoredSuccess >= StoreEvery; if (storeOk) _lastStoredSuccess = now; _lastFailureCode = null;
            using (Scope(storeOk))
                logger.LogInformation("RCS fleet poll succeeded: endpoint {Endpoint:l}, HTTP {HttpStatus}, {DurationMs} ms, {TotalAmr} AMR(s) [{RobotCodes:l}], request {RequestCode:l}, last successful poll {LastSuccessfulPollAt:O}. {EventCode}",
                    result.Endpoint, result.HttpStatus, durationMs, count, string.Join(",", result.Amrs.Select(a => a.RobotCode)), result.RequestCode, now, "RCS_FLEET_POLL_SUCCESS");
            using (Scope(false))
                logger.LogInformation("RCS fleet snapshot updated: {TotalAmr} AMR(s) in the response, {NewAmr} new, {MissingAmr} not in this response. {EventCode}", count, newAmrs, missingAmrs, "RCS_FLEET_SNAPSHOT_UPDATED");
        }
        else
        {
            var storeFail = failures <= 1 || result.ErrorCode != _lastFailureCode || now - _lastStoredFailure >= StoreEvery;
            if (storeFail) { _lastStoredFailure = now; } _lastFailureCode = result.ErrorCode;
            using (Scope(storeFail))
                logger.LogWarning("RCS fleet poll failed: {ErrorCode:l} ({ErrorMessage:l}), endpoint {Endpoint:l}, HTTP {HttpStatus}, RCS code {RcsCode:l}, RCS message {RcsMessage:l}, {DurationMs} ms, request {RequestCode:l}, consecutive failures {ConsecutiveFailures}. {EventCode}",
                    result.ErrorCode, Sanitize(result.ErrorMessage), result.Endpoint, result.HttpStatus, result.RcsCode, result.RcsMessage, durationMs, result.RequestCode, failures, "RCS_FLEET_POLL_FAILED");
        }
        if (!result.Success && wasOk)
            logger.LogWarning("RCS fleet query failed ({Code}); the last AMR data is kept and shown as not updated. {EventCode}", result.ErrorCode, "RCS_FLEET_QUERY_FAILED");
        else if (result.Success && !wasOk && state is not null && state.LastAttemptAt is not null)
            logger.LogInformation("RCS fleet query works again: {Count} AMR(s). {EventCode}", count, "RCS_FLEET_QUERY_RECOVERED");
        if (result.Skipped > 0) logger.LogWarning("RCS fleet response: {Skipped} entr(ies) without a usable robotCode or repeated were ignored. {EventCode}", result.Skipped, "RCS_FLEET_ENTRIES_IGNORED");
        if (changed) await notifier.PublishAsync([new WcsEvent("FleetChanged", new { source = Source.ToString() })], ct);
        return true;
    }

    /// <summary>The fleet as the screen shows it. <paramref name="includeNetwork"/> = Admin (the AMR IP address is a network parameter).</summary>
    public Task<FleetView> GetAsync(bool includeNetwork, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var now = clock.UtcNow;
        var state = await repo.GetStateAsync(Source, t);
        var snaps = await repo.ListAsync(Source, t);
        var staleAfter = TimeSpan.FromSeconds(O.EffectiveStaleAfter);
        var connection = state?.LastAttemptAt is null ? FleetStates.NoData : state.LastAttemptOk && state.LastSuccessAt is { } ok && now - ok <= staleAfter ? FleetStates.Connected : FleetStates.Disconnected;
        var tooOld = state?.LastSuccessAt is not { } success || now - success > staleAfter;

        // read-only link to the business AGV (matched by robot code) and its active task, when there is one
        var byRobot = (await agvs.ListAsync(t)).Where(a => !string.IsNullOrWhiteSpace(a.RcsRobotCode)).GroupBy(a => a.RcsRobotCode!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        // CR-028: tasks of the RcsManaged dispatch are linked to the AMR by the robot code the RCS reported; a task without one is "awaiting assignment", never attributed to an AMR
        var managed = dispatch.Value.IsRcsManaged ? await tasks.ListActiveRcsManagedAsync(1000, t) : [];
        var list = new List<FleetAmrView>();
        foreach (var s in snaps.OrderBy(x => x.RobotCode, StringComparer.Ordinal))
        {
            var missing = state?.LastSuccessAt is { } ls && s.LastSeenAt < ls;                 // the last successful response did not contain it
            var st = missing ? FleetStates.Missing : (tooOld || state?.LastAttemptOk == false) ? FleetStates.Stale : FleetStates.Fresh;
            string? agvCode = null; Guid? taskId = null; string? taskState = null;
            if (byRobot.TryGetValue(s.RobotCode, out var agv))
            {
                agvCode = agv.Code;
                if (await tasks.GetActiveForAgvAsync(agv.Id, t) is { } task) { taskId = task.Id; taskState = task.State.ToString(); }
            }
            if (taskId is null && managed.FirstOrDefault(m => string.Equals(m.RobotCode, s.RobotCode, StringComparison.Ordinal)) is { } mine) { taskId = mine.Id; taskState = mine.State.ToString(); }
            IReadOnlyList<string> path = s.PathJson is null ? [] : JsonSerializer.Deserialize<List<string>>(s.PathJson) ?? [];
            list.Add(new FleetAmrView(s.RobotCode, st, s.RobotDir, includeNetwork ? s.RobotIp : null, s.Battery, s.PosX, s.PosY, s.MapCode, s.Speed, s.Status, s.ExclType, s.Stop,
                s.PodCode, s.PodDir, includeNetwork ? path : [], s.FirstSeenAt, s.LastSeenAt, agvCode, taskId, taskState));
        }
        return new FleetView(Source.ToString(), Source == FleetSource.Simulator, connection, state?.LastAttemptAt, state?.LastSuccessAt, state?.LastErrorCode, state?.LastErrorMessage,
            state?.ConsecutiveFailures ?? 0, O.EffectiveInterval, O.EffectiveStaleAfter, list.Count, list.Count(a => a.State == FleetStates.Fresh), list,
            dispatch.Value.IsRcsManaged ? DispatchOptions.RcsManagedMode : DispatchOptions.Legacy, dispatch.Value.MaxInFlightTasks,
            managed.Count(m => m.State != TaskState.Queued && m.State != TaskState.Pending && m.State != TaskState.Validating),
            managed.Count(m => m.RcsTaskCode is not null && m.RobotCode is null), managed.Count(m => m.State == TaskState.Queued),
            modeState.RestartPending ? modeState.ConfiguredMode : null);
    }, ct);
}
