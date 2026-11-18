using System.Globalization;
using System.Runtime.InteropServices;
using WCS.Application.Monitoring;

namespace WCS.Infrastructure.Monitoring;

/// <summary>
/// Metrics of the WCS container itself from its own cgroup (v2 or v1) and /proc: CPU use against the container's allowance, memory used against its limit,
/// container uptime (the age of PID 1) and the filesystem of the data volume. It reads only files the container already sees (no privileges, no Docker
/// socket, no host mounts) and reports nothing about the host. Outside a container every metric is Unavailable (NOT_IN_CONTAINER).
/// </summary>
public sealed class CgroupContainerMetricsProvider(string procRoot, string cgroupRoot, Func<bool> inContainer, Func<string?> runtime, string? diskPath, int? logicalProcessors = null) : IContainerMetricsProvider
{
    private static readonly string[] Keys = [MetricKeys.CpuPercent, MetricKeys.CpuCores, MetricKeys.MemoryUsedBytes, MetricKeys.MemoryTotalBytes, MetricKeys.MemoryAvailableBytes,
        MetricKeys.MemoryPercent, MetricKeys.UptimeSeconds, MetricKeys.DiskTotalBytes, MetricKeys.DiskUsedBytes, MetricKeys.DiskFreeBytes, MetricKeys.DiskPercent];
    private const double ClockTicksPerSecond = 100;      // USER_HZ on every mainstream Linux kernel
    private double? _prevUsageUsec;
    private DateTimeOffset _prevAt;

    public CgroupContainerMetricsProvider(string? diskPath) : this("/proc", "/sys/fs/cgroup", MonitoringSupport.InContainer, MonitoringSupport.ContainerRuntime, diskPath) { }

    public ProviderResult Sample(DateTimeOffset now)
    {
        var inside = inContainer();
        var info = new ProviderInfo("Linux", RuntimeInformation.OSDescription, runtime(), inside, inside ? Environment.MachineName : null);
        if (!inside) return new ProviderResult(MetricSource.Container, MonitoringSupport.AllUnavailable(MetricReasons.NotInContainer, Keys), info);

        var v2 = File.Exists(Path.Combine(cgroupRoot, "cgroup.controllers"));
        var v1 = !v2 && Directory.Exists(Path.Combine(cgroupRoot, "memory"));
        if (!v2 && !v1) return new ProviderResult(MetricSource.Container, MonitoringSupport.AllUnavailable(MetricReasons.CgroupUnavailable, Keys), info);

        var m = new Dictionary<string, RawMetric>();
        var (cores, usageUsec) = v2 ? CpuV2() : CpuV1();
        var cpuCores = cores ?? logicalProcessors ?? Environment.ProcessorCount;
        m[MetricKeys.CpuCores] = RawMetric.Of(cpuCores);
        if (usageUsec is null) m[MetricKeys.CpuPercent] = RawMetric.Unavailable(MetricReasons.ReadFailed);
        else if (_prevUsageUsec is null || now <= _prevAt) m[MetricKeys.CpuPercent] = RawMetric.Unavailable(MetricReasons.WarmingUp);
        else
        {
            var wallUsec = (now - _prevAt).TotalMilliseconds * 1000;
            var pct = (usageUsec.Value - _prevUsageUsec.Value) / (wallUsec * cpuCores) * 100;
            m[MetricKeys.CpuPercent] = pct < 0 ? RawMetric.Unavailable(MetricReasons.ReadFailed) : RawMetric.Of(Math.Min(100, pct));   // a counter reset yields a negative delta: not a reading
        }
        if (usageUsec is not null) { _prevUsageUsec = usageUsec; _prevAt = now; }

        var (used, limit) = v2 ? MemoryV2() : MemoryV1();
        if (used is null) foreach (var k in new[] { MetricKeys.MemoryUsedBytes, MetricKeys.MemoryTotalBytes, MetricKeys.MemoryAvailableBytes, MetricKeys.MemoryPercent }) m[k] = RawMetric.Unavailable(MetricReasons.ReadFailed);
        else
        {
            m[MetricKeys.MemoryUsedBytes] = RawMetric.Of(used.Value);
            if (limit is null) foreach (var k in new[] { MetricKeys.MemoryTotalBytes, MetricKeys.MemoryAvailableBytes, MetricKeys.MemoryPercent }) m[k] = RawMetric.Unavailable(MetricReasons.NoMemoryLimit);
            else
            {
                m[MetricKeys.MemoryTotalBytes] = RawMetric.Of(limit.Value);
                m[MetricKeys.MemoryAvailableBytes] = RawMetric.Of(Math.Max(0, limit.Value - used.Value));
                m[MetricKeys.MemoryPercent] = RawMetric.Of(Math.Min(100, used.Value / limit.Value * 100));
            }
        }

        m[MetricKeys.UptimeSeconds] = Uptime() is { } up ? RawMetric.Of(up) : RawMetric.Unavailable(MetricReasons.ReadFailed);
        MonitoringSupport.AddDisk(m, diskPath);
        return new ProviderResult(MetricSource.Container, m, info);
    }

    // ---- cgroup v2: cpu.stat (usage_usec), cpu.max ("max 100000" | "300000 100000"), memory.current / memory.max / memory.stat (inactive_file)
    private (double? Cores, double? UsageUsec) CpuV2()
    {
        var usage = MonitoringSupport.ReadKeyed(Path.Combine(cgroupRoot, "cpu.stat"), "usage_usec");
        double? cores = null;
        var max = MonitoringSupport.ReadText(Path.Combine(cgroupRoot, "cpu.max"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (max is { Length: 2 } && max[0] != "max" && double.TryParse(max[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var quota)
            && double.TryParse(max[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var period) && quota > 0 && period > 0) cores = quota / period;
        return (cores, usage);
    }

    private (double? Used, double? Limit) MemoryV2()
    {
        var current = MonitoringSupport.ReadNumber(Path.Combine(cgroupRoot, "memory.current"));
        if (current is null) return (null, null);
        // like "docker stats": the page cache that can be dropped (inactive_file) is not "used"
        var inactive = MonitoringSupport.ReadKeyed(Path.Combine(cgroupRoot, "memory.stat"), "inactive_file") ?? 0;
        var max = MonitoringSupport.ReadText(Path.Combine(cgroupRoot, "memory.max"));
        double? limit = max is not null && max != "max" && double.TryParse(max, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && l > 0 ? l : null;
        return (Math.Max(0, current.Value - inactive), limit);
    }

    // ---- cgroup v1: cpuacct.usage (ns), cpu.cfs_quota_us / cpu.cfs_period_us, memory.usage_in_bytes / memory.limit_in_bytes / memory.stat (total_inactive_file)
    private string? V1(string controller, string file)
    {
        foreach (var dir in new[] { controller, controller + ",cpuacct", "cpu,cpuacct", "cpuacct" })
        {
            var p = Path.Combine(cgroupRoot, dir, file);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private (double? Cores, double? UsageUsec) CpuV1()
    {
        var usageFile = V1("cpuacct", "cpuacct.usage");
        double? usage = usageFile is null ? null : MonitoringSupport.ReadNumber(usageFile) / 1000;       // ns -> us
        double? cores = null;
        var q = V1("cpu", "cpu.cfs_quota_us"); var pf = V1("cpu", "cpu.cfs_period_us");
        if (q is not null && pf is not null && MonitoringSupport.ReadNumber(q) is { } quota and > 0 && MonitoringSupport.ReadNumber(pf) is { } period and > 0) cores = quota / period;
        return (cores, usage);
    }

    private (double? Used, double? Limit) MemoryV1()
    {
        var usageFile = V1("memory", "memory.usage_in_bytes");
        if (usageFile is null || MonitoringSupport.ReadNumber(usageFile) is not { } current) return (null, null);
        var statFile = V1("memory", "memory.stat");
        var inactive = statFile is null ? 0 : MonitoringSupport.ReadKeyed(statFile, "total_inactive_file") ?? MonitoringSupport.ReadKeyed(statFile, "inactive_file") ?? 0;
        var limitFile = V1("memory", "memory.limit_in_bytes");
        double? limit = limitFile is not null && MonitoringSupport.ReadNumber(limitFile) is { } l and > 0 and < 1e15 ? l : null;   // "unlimited" is a huge number (~9.2e18)
        return (Math.Max(0, current - inactive), limit);
    }

    /// <summary>Container uptime = system uptime - start time of PID 1 (the container's init process). /proc/1/stat field 22 is in clock ticks since boot.</summary>
    private double? Uptime()
    {
        var up = MonitoringSupport.ReadText(Path.Combine(procRoot, "uptime"))?.Split(' ')[0];
        var stat = MonitoringSupport.ReadText(Path.Combine(procRoot, "1", "stat"));
        if (up is null || stat is null || !double.TryParse(up, NumberStyles.Float, CultureInfo.InvariantCulture, out var sys)) return null;
        var close = stat.LastIndexOf(')');                    // the command name may contain spaces / parentheses
        if (close < 0) return null;
        var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);   // fields[0] is field 3 (state), so field 22 is fields[19]
        if (fields.Length < 20 || !double.TryParse(fields[19], NumberStyles.Float, CultureInfo.InvariantCulture, out var ticks)) return null;
        var age = sys - ticks / ClockTicksPerSecond;
        return age >= 0 ? age : null;
    }
}
