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
    IClock clock, IOptions<RcsFleetOptions> options, IOptions<DispatchOptions> dispatch, ILogger<FleetService> logger)
{
    private RcsFleetOptions O => options.Value;
    private FleetSource Source => query.Source;

    /// <summary>One poll. Called by the existing AGV status loop on every tick; the interval is applied here, so a faster tick never queries faster than configured.</summary>
    public async Task<bool> RefreshAsync(CancellationToken ct = default, bool force = false)
    {
        var now = clock.UtcNow;
        var state = await uow.ExecuteAsync(t => repo.GetStateAsync(Source, t), ct);
        if (!force && state?.LastAttemptAt is { } last && now - last < TimeSpan.FromSeconds(O.EffectiveInterval)) return false;

        RcsFleetResult result;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, O.TimeoutSeconds)));
            try { result = await query.QueryAsync(timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = RcsFleetResult.Fail(FleetErrorCodes.Timeout, $"No answer within {O.TimeoutSeconds} s."); }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = RcsFleetResult.Fail(FleetErrorCodes.Unreachable, ex.Message); }
        }

        bool wasOk = false, changed = false; int count = 0;
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
                    changed = true;                                                           // a new AMR appeared
                }
                if (snap.Apply(a.RobotDir, a.RobotIp, a.Battery, a.PosX, a.PosY, a.MapCode, a.Speed, a.Status, a.ExclType, a.Stop, a.PodCode, a.PodDir, path, now)) changed = true;
            }
            // an AMR that is not in this response keeps its row and its old LastSeenAt: it is then shown as "missing", not deleted
            if (known.Values.Any(k => k.LastSeenAt == s.LastSuccessAt && result.Amrs.All(a => a.RobotCode != k.RobotCode))) changed = true;
            s.Succeeded(now, result.Amrs.Count, result.LatencyMs);
            count = result.Amrs.Count;
            if (!before.LastAttemptOk || before.LastErrorCode is not null || before.LastAmrCount != s.LastAmrCount) changed = true;
            return 0;
        }, ct);

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
            managed.Count(m => m.RcsTaskCode is not null && m.RobotCode is null), managed.Count(m => m.State == TaskState.Queued));
    }, ct);
}
