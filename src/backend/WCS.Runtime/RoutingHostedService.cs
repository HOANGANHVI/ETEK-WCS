using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Application.Routing;

namespace WCS.Runtime;

/// <summary>
/// CR-010 background loop. Each tick: (1) restart/link recovery of Station Transactions per PLC (a PLC is independent of the others),
/// (2) transactions that were accepted but never got a Transport Task are started again, (3) every open Transport Task is re-evaluated and pending legs are dispatched.
/// All three are idempotent, so the loop is also the WCS-restart recovery: nothing depends on memory or on edge detection. Errors never stop the loop.
/// </summary>
public sealed class RoutingHostedService(IServiceScopeFactory scopes, IPlcRuntime plcRuntime, IOptions<SchedulerOptions> options, ILogger<RoutingHostedService> logger) : BackgroundService
{
    private readonly Dictionary<string, bool> _online = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(250, options.Value.TickIntervalMs)));
        do
        {
            await Guarded("PLC link recovery", () => MonitorLinksAsync(stoppingToken));
            await Guarded("PLC request resume", async () => { await using var s = scopes.CreateAsyncScope(); await s.ServiceProvider.GetRequiredService<IPlcRequestService>().ResumeOrphansAsync(stoppingToken); });
            await Guarded("Route engine", async () => { await using var s = scopes.CreateAsyncScope(); await s.ServiceProvider.GetRequiredService<IRouteEngine>().RunOnceAsync(stoppingToken); });
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task MonitorLinksAsync(CancellationToken ct)
    {
        foreach (var w in plcRuntime.Workers(false))
        {
            var online = string.Equals(w.State, nameof(EndpointState.Connected), StringComparison.OrdinalIgnoreCase);
            var known = _online.TryGetValue(w.PlcId, out var before);
            _online[w.PlcId] = online;
            if (known && before == online) continue;
            await using var scope = scopes.CreateAsyncScope();
            var tx = scope.ServiceProvider.GetRequiredService<StationTransactionService>();
            if (!online)
            {
                var n = await tx.SuspendPlcAsync(w.PlcId, known ? "PLC_LINK_LOST" : "WCS_RESTART_PLC_NOT_CONNECTED", ct);
                if (n > 0) logger.LogWarning("PLC {Plc} is not connected: {Count} Station Transaction(s) kept and marked suspended", w.PlcId, n);
            }
            else
            {
                var reports = await tx.RecoverPlcAsync(w.PlcId, scope.ServiceProvider.GetRequiredService<IPlcObservationSource>(), ct);
                if (reports.Count > 0) logger.LogInformation("PLC {Plc} connected: recovery decisions {Decisions}", w.PlcId, string.Join(", ", reports.Select(r => $"{r.PortId}:{r.Decision}")));
            }
        }
    }

    private async Task Guarded(string what, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "{What} failed (the loop continues)", what); }
    }
}
