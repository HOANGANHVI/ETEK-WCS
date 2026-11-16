using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Domain.Common;

namespace WCS.Application.Services;

/// <summary>Refreshes AGV connection status from the RCS client and publishes changes in real time.</summary>
public sealed class AgvStatusService(IUnitOfWork uow, IAgvRepository agvs, IRcsClient rcs, IRcsDispatchGuard guard, IWcsNotifier notifier, IClock clock,
    ILogger<AgvStatusService> logger)
{
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        foreach (var snapshot in await agvs.ListAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(snapshot.RcsRobotCode)) continue; // robot code not configured yet
            AgvConnectionStatus status; string? detail;
            var link = guard.Check();
            if (!link.Allowed)
            {
                // CR-008A: the only RCS adapter is a simulator; its "Online" must never be shown as a real AGV.
                (status, detail) = (AgvConnectionStatus.Unknown, link.Code);
            }
            else try
            {
                var s = await rcs.QueryAgvStatusAsync(snapshot.RcsRobotCode, ct);
                (status, detail) = s is null ? (AgvConnectionStatus.Unknown, "No status returned")
                    : (s.Online ? AgvConnectionStatus.Online : AgvConnectionStatus.Offline, s.Detail);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "AGV status query failed for {Agv}", snapshot.Code);
                (status, detail) = (AgvConnectionStatus.Error, ex.Message);
            }

            var changed = await uow.ExecuteAsync(async t =>
            {
                var agv = await agvs.GetAsync(snapshot.Id, t);
                if (agv is null) return (WcsEvent?)null;
                var different = agv.ConnectionStatus != status || agv.StatusDetail != detail;
                agv.UpdateStatus(status, detail, clock.UtcNow);
                return different ? new WcsEvent("AgvStatusChanged", agv.ToDto(null)) : null;
            }, ct);
            if (changed is not null) await notifier.PublishAsync([changed], ct);
        }
    }
}
