namespace WCS.Application.Monitoring;

/// <summary>Where a number was measured. A value is never presented under a different source than the one that produced it.</summary>
public enum MetricSource
{
    /// <summary>The WCS container itself (cgroup accounting): its own CPU/RAM/uptime/filesystem.</summary>
    Container,
    /// <summary>The Linux VM that runs the containers on Docker Desktop (WSL2 / linuxkit). NOT the Windows/macOS host.</summary>
    DockerVm,
    /// <summary>The machine the operating system really runs on (native Windows, native Linux, or a Linux Docker Engine host).</summary>
    Host,
}

public enum MetricStatus { Normal, Warning, Critical, Unavailable }

/// <summary>One measurement. When <see cref="Status"/> is Unavailable, <see cref="Value"/> is null and <see cref="Reason"/> is a stable code explaining why.</summary>
public sealed record Metric(double? Value, string Unit, MetricSource Source, MetricStatus Status, DateTimeOffset LastUpdatedAt, string? Reason = null);

/// <summary>Raw reading of a provider: a value, or the reason code why it could not be read. Never an estimate.</summary>
public sealed record RawMetric(double? Value, string? ReasonCode = null)
{
    public static RawMetric Of(double value) => new(value);
    public static RawMetric Unavailable(string reason) => new(null, reason);
}

public sealed record ProviderInfo(string Platform, string? OperatingSystem, string? Runtime, bool InContainer, string? Hostname, string? Note = null);

public sealed record ProviderResult(MetricSource Source, IReadOnlyDictionary<string, RawMetric> Metrics, ProviderInfo Info);

/// <summary>Metrics of the WCS container only (never of the host).</summary>
public interface IContainerMetricsProvider
{
    ProviderResult Sample(DateTimeOffset now);
}

/// <summary>Metrics of the machine / VM the operating system runs on. The result states which one it is through <see cref="ProviderResult.Source"/>.</summary>
public interface IHostMetricsProvider
{
    ProviderResult Sample(DateTimeOffset now);
}

/// <summary>Keys of the metric dictionaries (camelCase: they are the JSON property names).</summary>
public static class MetricKeys
{
    public const string CpuPercent = "cpuPercent";
    public const string CpuCores = "cpuCores";
    public const string MemoryUsedBytes = "memoryUsedBytes";
    public const string MemoryTotalBytes = "memoryTotalBytes";
    public const string MemoryAvailableBytes = "memoryAvailableBytes";
    public const string MemoryPercent = "memoryPercent";
    public const string UptimeSeconds = "uptimeSeconds";
    public const string DiskTotalBytes = "diskTotalBytes";
    public const string DiskUsedBytes = "diskUsedBytes";
    public const string DiskFreeBytes = "diskFreeBytes";
    public const string DiskPercent = "diskPercent";
    public const string TemperatureC = "temperatureC";

    public static readonly string[] All =
        [CpuPercent, CpuCores, MemoryUsedBytes, MemoryTotalBytes, MemoryAvailableBytes, MemoryPercent, UptimeSeconds, DiskTotalBytes, DiskUsedBytes, DiskFreeBytes, DiskPercent, TemperatureC];

    public static string Unit(string key) => key switch
    {
        CpuPercent or MemoryPercent or DiskPercent => "%",
        CpuCores => "cores",
        UptimeSeconds => "s",
        TemperatureC => "°C",
        _ => "B",
    };
}

/// <summary>Reason codes of an Unavailable metric (translated by the UI; keep in sync with the "monitor.reason_*" texts).</summary>
public static class MetricReasons
{
    public const string NotInContainer = "NOT_IN_CONTAINER";
    public const string CgroupUnavailable = "CGROUP_UNAVAILABLE";
    public const string NoMemoryLimit = "NO_MEMORY_LIMIT";
    public const string NoTemperatureSensor = "NO_TEMPERATURE_SENSOR";
    public const string HostAgentRequired = "HOST_AGENT_REQUIRED";
    public const string PlatformUnsupported = "PLATFORM_UNSUPPORTED";
    public const string WarmingUp = "WARMING_UP";
    public const string ReadFailed = "READ_FAILED";
    public const string Disabled = "MONITORING_DISABLED";
    public const string Stale = "SAMPLER_STALLED";
    public const string NotYetSampled = "NOT_YET_SAMPLED";
    public const string DiskUnavailable = "DISK_UNAVAILABLE";
}

public sealed class ResourceMonitoringOptions
{
    public const string SectionName = "ResourceMonitoring";
    public bool Enabled { get; set; } = true;
    /// <summary>Seconds between samples (2-60). One sampler serves every client; the UI never samples by itself.</summary>
    public int SampleIntervalSeconds { get; set; } = 5;
    /// <summary>Minutes of history kept in memory (1-60).</summary>
    public int HistoryMinutes { get; set; } = 10;
    /// <summary>Directory whose filesystem is reported as "disk" (default: the data volume / application directory).</summary>
    public string? DiskPath { get; set; }
    public double CpuWarning { get; set; } = 80;
    public double CpuCritical { get; set; } = 95;
    public double MemoryWarning { get; set; } = 80;
    public double MemoryCritical { get; set; } = 95;
    public double DiskWarning { get; set; } = 80;
    public double DiskCritical { get; set; } = 90;
    /// <summary>Not set = no threshold is defined for temperature (it depends on the hardware); the value is shown without a Warning/Critical judgement.</summary>
    public double? TemperatureWarningC { get; set; }
    public double? TemperatureCriticalC { get; set; }

    public int EffectiveIntervalSeconds => Math.Clamp(SampleIntervalSeconds, 2, 60);
    public int HistoryCapacity => Math.Clamp(HistoryMinutes, 1, 60) * 60 / EffectiveIntervalSeconds;
}

/// <summary>Status of a value against the configured thresholds. The server decides, so the UI never judges numbers itself.</summary>
public static class MetricStatusEvaluator
{
    public static MetricStatus Evaluate(string key, double value, ResourceMonitoringOptions o) => key switch
    {
        MetricKeys.CpuPercent => Level(value, o.CpuWarning, o.CpuCritical),
        MetricKeys.MemoryPercent => Level(value, o.MemoryWarning, o.MemoryCritical),
        MetricKeys.DiskPercent => Level(value, o.DiskWarning, o.DiskCritical),
        MetricKeys.TemperatureC => o.TemperatureCriticalC is { } c && value >= c ? MetricStatus.Critical
            : o.TemperatureWarningC is { } w && value >= w ? MetricStatus.Warning : MetricStatus.Normal,
        _ => MetricStatus.Normal,
    };

    private static MetricStatus Level(double v, double warn, double crit) => v >= crit ? MetricStatus.Critical : v >= warn ? MetricStatus.Warning : MetricStatus.Normal;
}

public sealed record EnvironmentInfo(string Platform, string OperatingSystem, string Architecture, bool InContainer, string? ContainerRuntime,
    string? Hostname, string? ContainerHostname, MetricSource HostScope, bool HostAgentRequired, string? ProcessorNote);

public sealed record HistoryPoint(DateTimeOffset T, double? ContainerCpu, double? ContainerMemory, double? HostCpu, double? HostMemory);

/// <summary>What <c>GET /api/system/resources</c> returns: separated Container and Host metrics, environment, and a short history for the charts.</summary>
public sealed record ResourceSnapshot(DateTimeOffset Time, DateTimeOffset? SampledAt, double? SampleAgeSeconds, int IntervalSeconds, bool Enabled,
    EnvironmentInfo Environment, IReadOnlyDictionary<string, Metric> Container, IReadOnlyDictionary<string, Metric> Host, IReadOnlyList<HistoryPoint> History);

public interface IResourceMonitor
{
    ResourceSnapshot GetSnapshot();
}
