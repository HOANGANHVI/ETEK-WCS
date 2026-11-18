using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Monitoring;

namespace WCS.Infrastructure.Monitoring;

/// <summary>
/// One background sampler for every client: it reads the container and host providers every few seconds, applies the thresholds, and keeps a short in-memory
/// history for the charts. The API only returns the latest sample, so the number of open dashboards does not change the cost. A sample older than three
/// intervals is never served as a current value (every metric turns Unavailable / SAMPLER_STALLED), and nothing is estimated or carried over.
/// </summary>
public sealed class ResourceMonitor(IContainerMetricsProvider container, IHostMetricsProvider host, IOptions<ResourceMonitoringOptions> options, IClock clock,
    ILogger<ResourceMonitor> logger) : BackgroundService, IResourceMonitor
{
    private sealed record Sample(DateTimeOffset At, ProviderResult Container, ProviderResult Host, Dictionary<string, Metric> ContainerMetrics, Dictionary<string, Metric> HostMetrics);

    private readonly object _gate = new();
    private readonly Queue<HistoryPoint> _history = new();
    private Sample? _latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled) { logger.LogInformation("Resource monitoring is disabled (ResourceMonitoring:Enabled=false)"); return; }
        try
        {
            await Task.Yield();
            Tick();                                         // primes the CPU counters (a CPU percentage needs two readings)
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            Tick();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(o.EffectiveIntervalSeconds));
            while (await timer.WaitForNextTickAsync(stoppingToken)) Tick();
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    /// <summary>Takes one sample now (called by the loop; public so tests can drive it without timers).</summary>
    public void Tick()
    {
        var o = options.Value; var now = clock.UtcNow;
        var c = Safe(() => container.Sample(now), MetricSource.Container, "container");
        var h = Safe(() => host.Sample(now), MetricSource.Host, "host");
        var cm = Build(c, now, o); var hm = Build(h, now, o);
        var point = new HistoryPoint(now, Value(cm, MetricKeys.CpuPercent), Value(cm, MetricKeys.MemoryPercent), Value(hm, MetricKeys.CpuPercent), Value(hm, MetricKeys.MemoryPercent));
        lock (_gate)
        {
            _latest = new Sample(now, c, h, cm, hm);
            _history.Enqueue(point);
            while (_history.Count > o.HistoryCapacity) _history.Dequeue();
        }
    }

    private ProviderResult Safe(Func<ProviderResult> read, MetricSource source, string what)
    {
        try { return read(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading the {What} metrics failed; they are reported as unavailable", what);
            return new ProviderResult(source, MonitoringSupport.AllUnavailable(MetricReasons.ReadFailed, MetricKeys.All), new ProviderInfo("Unknown", RuntimeInformation.OSDescription, null, false, null));
        }
    }

    private static Dictionary<string, Metric> Build(ProviderResult r, DateTimeOffset at, ResourceMonitoringOptions o)
    {
        var d = new Dictionary<string, Metric>();
        foreach (var (key, raw) in r.Metrics)
            d[key] = raw.Value is { } v && double.IsFinite(v)
                ? new Metric(v, MetricKeys.Unit(key), r.Source, MetricStatusEvaluator.Evaluate(key, v, o), at)
                : new Metric(null, MetricKeys.Unit(key), r.Source, MetricStatus.Unavailable, at, raw.ReasonCode ?? MetricReasons.ReadFailed);
        return d;
    }

    private static double? Value(Dictionary<string, Metric> m, string key) => m.TryGetValue(key, out var x) ? x.Value : null;

    public ResourceSnapshot GetSnapshot()
    {
        var o = options.Value; var now = clock.UtcNow;
        Sample? s; HistoryPoint[] history;
        lock (_gate) { s = _latest; history = [.. _history]; }

        if (!o.Enabled || s is null)
        {
            var reason = !o.Enabled ? MetricReasons.Disabled : MetricReasons.NotYetSampled;
            var none = MetricKeys.All.ToDictionary(k => k, k => new Metric(null, MetricKeys.Unit(k), MetricSource.Host, MetricStatus.Unavailable, now, reason));
            return new ResourceSnapshot(now, null, null, o.EffectiveIntervalSeconds, o.Enabled, EnvironmentOf(null, null), none, none, history);
        }

        var age = (now - s.At).TotalSeconds;
        var stale = age > o.EffectiveIntervalSeconds * 3 + 2;
        IReadOnlyDictionary<string, Metric> Serve(Dictionary<string, Metric> m) => !stale ? m
            : m.ToDictionary(kv => kv.Key, kv => new Metric(null, kv.Value.Unit, kv.Value.Source, MetricStatus.Unavailable, kv.Value.LastUpdatedAt, MetricReasons.Stale));
        return new ResourceSnapshot(now, s.At, Math.Round(age, 1), o.EffectiveIntervalSeconds, true, EnvironmentOf(s.Container, s.Host), Serve(s.ContainerMetrics), Serve(s.HostMetrics), history);
    }

    private static EnvironmentInfo EnvironmentOf(ProviderResult? c, ProviderResult? h)
    {
        var inside = c?.Info.InContainer ?? h?.Info.InContainer ?? MonitoringSupport.InContainer();
        var scope = h?.Source ?? MetricSource.Host;
        var platform = h?.Info.Platform ?? c?.Info.Platform ?? (OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other");
        // inside a container the machine name is the container id: it is shown as such and never presented as the host name
        var hostname = inside ? null : h?.Info.Hostname ?? Environment.MachineName;
        var containerHost = inside ? Environment.MachineName : null;
        var agentRequired = scope == MetricSource.DockerVm || (inside && platform == "Windows");
        return new EnvironmentInfo(platform, h?.Info.OperatingSystem ?? RuntimeInformation.OSDescription, MonitoringSupport.Arch(), inside,
            c?.Info.Runtime ?? h?.Info.Runtime, hostname, containerHost, scope, agentRequired, h?.Info.Note);
    }
}
