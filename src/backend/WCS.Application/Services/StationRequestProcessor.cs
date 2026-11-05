using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Common;

namespace WCS.Application.Services;

/// <summary>
/// Intake of station requests that arrive through the (single) PLC connection. Request identity is the idempotency key, so a
/// request polled twice never creates two tasks. The request is acknowledged only after WCS has recorded it.
/// </summary>
public sealed class StationRequestProcessor(IStationSignalService stations, ITaskOrchestrator orchestrator,
    ILogger<StationRequestProcessor> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var handled = 0;
        foreach (var request in await stations.PollRequestsAsync(ct))
        {
            try
            {
                await orchestrator.CreateTaskAsync(new CreateTaskCommand(request.RequestKey, request.Flow, request.StationCode, "PLC"), ct);
                await stations.AcknowledgeRequestAsync(request, ct);
                handled++;
            }
            catch (AppException ex)
            {
                // Business rejection (e.g. unsupported flow / station): acknowledge to avoid an endless re-poll, but keep the log.
                logger.LogWarning("Station request {Key} from {Station} rejected: {Code} {Message}", request.RequestKey, request.StationCode, ex.Code, ex.Message);
                await stations.AcknowledgeRequestAsync(request, ct);
            }
        }
        return handled;
    }
}
