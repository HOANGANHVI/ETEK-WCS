using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WCS.Application.Monitoring;
using WCS.Infrastructure.Monitoring;
using WCS.Tests.Support;

namespace WCS.Tests.Monitoring;

/// <summary>A throw-away fake Linux tree (/proc, /sys, /sys/fs/cgroup) in a temp folder, written with the formats the real kernel uses.</summary>
public sealed class FakeLinux : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("wcs-mon-").FullName;
    public string Proc => Path.Combine(Root, "proc");
    public string Sys => Path.Combine(Root, "sys");
    public string Cg => Path.Combine(Root, "cgroup");
    public FakeLinux() { Directory.CreateDirectory(Proc); Directory.CreateDirectory(Sys); Directory.CreateDirectory(Cg); }
    public string Write(string rel, string text) { var p = Path.Combine(Root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); return p; }
    public void Dispose() { try { Directory.Delete(Root, true); } catch { /* temp */ } }

    /// <summary>/proc/uptime and /proc/1/stat: PID 1 started <paramref name="ageSeconds"/> before "now" (the stat line carries a command name with spaces and parentheses on purpose).</summary>
    public void Uptime(double systemUptime, double ageSeconds)
    {
        Write("proc/uptime", systemUptime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " 12345.67\n");
        var start = (long)((systemUptime - ageSeconds) * 100);
        Write("proc/1/stat", $"1 (dot net (host)) S 0 1 1 0 -1 4194560 100 0 0 0 5 5 0 0 20 0 30 0 {start} 123456789 4000 18446744073709551615 0 0 0 0 0 0 0 0 0 0 0 0 17 0 0 0 0 0 0\n");
    }
}

public class ResourceMonitoringTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-17T10:00:00Z");
    private static readonly ResourceMonitoringOptions Defaults = new();

    // ---------------------------------------------------------------- thresholds
    [Theory]
    [InlineData(MetricKeys.CpuPercent, 79.9, MetricStatus.Normal)] [InlineData(MetricKeys.CpuPercent, 80, MetricStatus.Warning)] [InlineData(MetricKeys.CpuPercent, 94.9, MetricStatus.Warning)] [InlineData(MetricKeys.CpuPercent, 95, MetricStatus.Critical)]
    [InlineData(MetricKeys.MemoryPercent, 79.9, MetricStatus.Normal)] [InlineData(MetricKeys.MemoryPercent, 80, MetricStatus.Warning)] [InlineData(MetricKeys.MemoryPercent, 95, MetricStatus.Critical)]
    [InlineData(MetricKeys.DiskPercent, 79.9, MetricStatus.Normal)] [InlineData(MetricKeys.DiskPercent, 80, MetricStatus.Warning)] [InlineData(MetricKeys.DiskPercent, 89.9, MetricStatus.Warning)] [InlineData(MetricKeys.DiskPercent, 90, MetricStatus.Critical)]
    [InlineData(MetricKeys.UptimeSeconds, 1e9, MetricStatus.Normal)] [InlineData(MetricKeys.MemoryUsedBytes, 1e12, MetricStatus.Normal)]
    public void Default_thresholds_decide_the_status(string key, double value, MetricStatus expected) => Assert.Equal(expected, MetricStatusEvaluator.Evaluate(key, value, Defaults));

    [Fact]
    public void Temperature_has_no_judgement_unless_thresholds_are_configured()
    {
        Assert.Equal(MetricStatus.Normal, MetricStatusEvaluator.Evaluate(MetricKeys.TemperatureC, 99, Defaults));
        var o = new ResourceMonitoringOptions { TemperatureWarningC = 70, TemperatureCriticalC = 85 };
        Assert.Equal((MetricStatus.Normal, MetricStatus.Warning, MetricStatus.Critical),
            (MetricStatusEvaluator.Evaluate(MetricKeys.TemperatureC, 69, o), MetricStatusEvaluator.Evaluate(MetricKeys.TemperatureC, 70, o), MetricStatusEvaluator.Evaluate(MetricKeys.TemperatureC, 85, o)));
    }

    // ---------------------------------------------------------------- container metrics (cgroup)
    private static CgroupContainerMetricsProvider V2(FakeLinux f, bool inside = true, int? cpus = 8) =>
        new(f.Proc, f.Cg, () => inside, () => inside ? "Docker" : null, f.Root, cpus);

    private static void CgroupV2(FakeLinux f, long usageUsec, string cpuMax, long current, string memMax, long inactive)
    {
        f.Write("cgroup/cgroup.controllers", "cpu memory io pids\n");
        f.Write("cgroup/cpu.stat", $"usage_usec {usageUsec}\nuser_usec {usageUsec / 2}\nsystem_usec {usageUsec / 2}\nnr_periods 0\n");
        f.Write("cgroup/cpu.max", cpuMax + "\n");
        f.Write("cgroup/memory.current", current + "\n");
        f.Write("cgroup/memory.max", memMax + "\n");
        f.Write("cgroup/memory.stat", $"anon 1000\nfile 5000\ninactive_file {inactive}\nactive_file 10\n");
    }

    [Fact]
    public void Container_cgroup_v2_cpu_memory_uptime_and_disk_are_read_from_the_containers_own_files()
    {
        using var f = new FakeLinux(); f.Uptime(5000, 3600);
        CgroupV2(f, 100_000_000, "300000 100000", 1_500_000_000, "3221225472", 500_000_000);
        var p = V2(f);
        var first = p.Sample(T0);
        Assert.Equal(MetricReasons.WarmingUp, first.Metrics[MetricKeys.CpuPercent].ReasonCode);      // one reading cannot give a rate
        Assert.Null(first.Metrics[MetricKeys.CpuPercent].Value);

        CgroupV2(f, 115_000_000, "300000 100000", 1_500_000_000, "3221225472", 500_000_000);          // 15 CPU-seconds in 10 s on a 3-core allowance = 50 %
        var r = p.Sample(T0.AddSeconds(10));
        Assert.Equal(MetricSource.Container, r.Source);
        Assert.Equal(50, r.Metrics[MetricKeys.CpuPercent].Value!.Value, 3);
        Assert.Equal(3, r.Metrics[MetricKeys.CpuCores].Value);
        Assert.Equal(1_000_000_000, r.Metrics[MetricKeys.MemoryUsedBytes].Value);                       // current - inactive_file, like "docker stats"
        Assert.Equal(3221225472, r.Metrics[MetricKeys.MemoryTotalBytes].Value);
        Assert.Equal(1_000_000_000d / 3221225472d * 100, r.Metrics[MetricKeys.MemoryPercent].Value!.Value, 6);
        Assert.Equal(3221225472 - 1_000_000_000, r.Metrics[MetricKeys.MemoryAvailableBytes].Value);
        Assert.Equal(3600, r.Metrics[MetricKeys.UptimeSeconds].Value!.Value, 1);                        // system uptime - start time of PID 1
        Assert.True(r.Metrics[MetricKeys.DiskTotalBytes].Value > 0);
        Assert.InRange(r.Metrics[MetricKeys.DiskPercent].Value!.Value, 0, 100);
        Assert.True(r.Info.InContainer);
        Assert.DoesNotContain(MetricKeys.TemperatureC, r.Metrics.Keys);                                  // a container has no temperature of its own
    }

    [Fact]
    public void Container_without_limits_reports_used_memory_and_marks_the_limit_dependent_metrics_unavailable()
    {
        using var f = new FakeLinux(); f.Uptime(100, 50);
        CgroupV2(f, 1000, "max 100000", 800, "max", 0);
        var r = V2(f, cpus: 6).Sample(T0);
        Assert.Equal(6, r.Metrics[MetricKeys.CpuCores].Value);                                           // no quota: the CPUs the container may use
        Assert.Equal(800, r.Metrics[MetricKeys.MemoryUsedBytes].Value);
        foreach (var k in new[] { MetricKeys.MemoryTotalBytes, MetricKeys.MemoryPercent, MetricKeys.MemoryAvailableBytes })
        { Assert.Null(r.Metrics[k].Value); Assert.Equal(MetricReasons.NoMemoryLimit, r.Metrics[k].ReasonCode); }
    }

    [Fact]
    public void Container_cgroup_v1_layout_is_supported_and_a_huge_limit_means_unlimited()
    {
        using var f = new FakeLinux(); f.Uptime(100, 50);
        f.Write("cgroup/cpuacct/cpuacct.usage", "2000000000\n");                // 2 s in ns
        f.Write("cgroup/cpu/cpu.cfs_quota_us", "200000\n"); f.Write("cgroup/cpu/cpu.cfs_period_us", "100000\n");
        f.Write("cgroup/memory/memory.usage_in_bytes", "900\n"); f.Write("cgroup/memory/memory.limit_in_bytes", "2000\n");
        f.Write("cgroup/memory/memory.stat", "cache 10\ntotal_inactive_file 100\n");
        var p = V2(f);
        p.Sample(T0);
        f.Write("cgroup/cpuacct/cpuacct.usage", "4000000000\n");                // +2 s of CPU in 10 s on 2 cores = 10 %
        var r = p.Sample(T0.AddSeconds(10));
        Assert.Equal(10, r.Metrics[MetricKeys.CpuPercent].Value!.Value, 3);
        Assert.Equal(2, r.Metrics[MetricKeys.CpuCores].Value);
        Assert.Equal((800d, 2000d), (r.Metrics[MetricKeys.MemoryUsedBytes].Value, r.Metrics[MetricKeys.MemoryTotalBytes].Value));
        f.Write("cgroup/memory/memory.limit_in_bytes", "9223372036854771712\n");
        Assert.Equal(MetricReasons.NoMemoryLimit, p.Sample(T0.AddSeconds(20)).Metrics[MetricKeys.MemoryTotalBytes].ReasonCode);
    }

    [Fact]
    public void Outside_a_container_every_container_metric_is_unavailable_with_no_value()
    {
        using var f = new FakeLinux();
        CgroupV2(f, 1, "max 100000", 1, "max", 0);
        var r = V2(f, inside: false).Sample(T0);
        Assert.All(r.Metrics.Values, m => { Assert.Null(m.Value); Assert.Equal(MetricReasons.NotInContainer, m.ReasonCode); });
        Assert.False(r.Info.InContainer);
    }

    [Fact]
    public void A_container_without_readable_cgroup_files_is_unavailable_not_zero()
    {
        using var f = new FakeLinux();
        var r = V2(f).Sample(T0);
        Assert.All(r.Metrics.Values, m => { Assert.Null(m.Value); Assert.Equal(MetricReasons.CgroupUnavailable, m.ReasonCode); });
    }

    [Fact]
    public void A_cpu_counter_that_goes_backwards_is_not_reported_as_a_reading()
    {
        using var f = new FakeLinux(); f.Uptime(100, 50);
        CgroupV2(f, 5_000_000, "100000 100000", 10, "100", 0);
        var p = V2(f); p.Sample(T0);
        CgroupV2(f, 1_000_000, "100000 100000", 10, "100", 0);
        var r = p.Sample(T0.AddSeconds(5));
        Assert.Null(r.Metrics[MetricKeys.CpuPercent].Value); Assert.Equal(MetricReasons.ReadFailed, r.Metrics[MetricKeys.CpuPercent].ReasonCode);
    }

    // ---------------------------------------------------------------- host / VM metrics (Linux)
    private static LinuxHostMetricsProvider Host(FakeLinux f, bool inside, string release)
    {
        f.Write("proc/sys/kernel/osrelease", release + "\n");
        return new LinuxHostMetricsProvider(f.Proc, f.Sys, () => inside, f.Root);
    }

    private static void ProcStat(FakeLinux f, long user, long idle, long iowait = 0) =>
        f.Write("proc/stat", $"cpu  {user} 0 0 {idle} {iowait} 0 0 0 0 0\ncpu0 1 0 0 1 0 0 0 0 0 0\ncpu1 1 0 0 1 0 0 0 0 0 0\ncpu2 1 0 0 1 0 0 0 0 0 0\ncpu3 1 0 0 1 0 0 0 0 0 0\nintr 1\n");

    [Fact]
    public void Linux_host_cpu_memory_uptime_and_cores_come_from_proc()
    {
        using var f = new FakeLinux();
        f.Write("proc/meminfo", "MemTotal:       16000000 kB\nMemFree:         1000000 kB\nMemAvailable:    4000000 kB\n");
        f.Write("proc/uptime", "86400.50 1.0\n");
        ProcStat(f, 100, 900);
        var p = Host(f, inside: false, "6.1.0-18-amd64");
        Assert.Equal(MetricReasons.WarmingUp, p.Sample(T0).Metrics[MetricKeys.CpuPercent].ReasonCode);
        ProcStat(f, 400, 1500, 100);                        // +300 busy, +600 idle, +100 iowait (idle) of 1000 total ticks = 30 %
        var r = p.Sample(T0.AddSeconds(5));
        Assert.Equal(MetricSource.Host, r.Source);
        Assert.Equal(30, r.Metrics[MetricKeys.CpuPercent].Value!.Value, 3);
        Assert.Equal(4, r.Metrics[MetricKeys.CpuCores].Value);
        Assert.Equal((16000000d * 1024, 4000000d * 1024, 12000000d * 1024), (r.Metrics[MetricKeys.MemoryTotalBytes].Value, r.Metrics[MetricKeys.MemoryAvailableBytes].Value, r.Metrics[MetricKeys.MemoryUsedBytes].Value));
        Assert.Equal(75, r.Metrics[MetricKeys.MemoryPercent].Value!.Value, 6);
        Assert.Equal(86400.5, r.Metrics[MetricKeys.UptimeSeconds].Value);
    }

    [Theory]
    [InlineData(true, "5.15.133.1-microsoft-standard-WSL2", MetricSource.DockerVm)]    // Docker Desktop on Windows: the VM, NOT the Windows host
    [InlineData(true, "6.6.31-linuxkit", MetricSource.DockerVm)]
    [InlineData(true, "6.1.0-18-amd64", MetricSource.Host)]                             // Linux Docker Engine: the kernel is the host's
    [InlineData(false, "5.15.133.1-microsoft-standard-WSL2", MetricSource.Host)]        // native Linux/WSL process, not in a container
    public void The_scope_of_Linux_figures_is_labelled_so_that_a_VM_is_never_passed_off_as_the_host(bool inside, string release, MetricSource expected)
    {
        using var f = new FakeLinux();
        f.Write("proc/meminfo", "MemTotal: 100 kB\nMemAvailable: 50 kB\n"); f.Write("proc/uptime", "10 1\n"); ProcStat(f, 1, 1);
        Assert.Equal(expected, Host(f, inside, release).Sample(T0).Source);
    }

    [Fact]
    public void CPU_temperature_is_read_from_hwmon_only_when_a_valid_sensor_exists()
    {
        using var f = new FakeLinux();
        f.Write("proc/meminfo", "MemTotal: 100 kB\nMemAvailable: 50 kB\n"); f.Write("proc/uptime", "10 1\n"); ProcStat(f, 1, 1);
        var p = Host(f, false, "6.1.0");
        var none = p.Sample(T0).Metrics[MetricKeys.TemperatureC];
        Assert.Null(none.Value); Assert.Equal(MetricReasons.NoTemperatureSensor, none.ReasonCode);   // no sensor tree at all: nothing is invented

        f.Write("sys/class/hwmon/hwmon0/name", "nvme\n"); f.Write("sys/class/hwmon/hwmon0/temp1_input", "38000\n");           // not a CPU sensor
        Assert.Null(p.Sample(T0).Metrics[MetricKeys.TemperatureC].Value);
        f.Write("sys/class/hwmon/hwmon1/name", "coretemp\n");
        f.Write("sys/class/hwmon/hwmon1/temp1_input", "52000\n"); f.Write("sys/class/hwmon/hwmon1/temp1_label", "Package id 0\n");
        f.Write("sys/class/hwmon/hwmon1/temp2_input", "61000\n"); f.Write("sys/class/hwmon/hwmon1/temp2_label", "Core 0\n");
        Assert.Equal(52, p.Sample(T0).Metrics[MetricKeys.TemperatureC].Value);                                                    // the package temperature, not the hottest core
    }

    [Fact]
    public void Implausible_sensor_values_are_ignored()
    {
        using var f = new FakeLinux();
        f.Write("proc/meminfo", "MemTotal: 100 kB\nMemAvailable: 50 kB\n"); f.Write("proc/uptime", "10 1\n"); ProcStat(f, 1, 1);
        f.Write("sys/class/hwmon/hwmon0/name", "k10temp\n"); f.Write("sys/class/hwmon/hwmon0/temp1_input", "0\n"); f.Write("sys/class/hwmon/hwmon0/temp2_input", "999000\n");
        var t = Host(f, false, "6.1.0").Sample(T0).Metrics[MetricKeys.TemperatureC];
        Assert.Null(t.Value); Assert.Equal(MetricReasons.NoTemperatureSensor, t.ReasonCode);
    }

    [Fact]
    public void Missing_proc_files_make_the_host_metrics_unavailable_not_zero()
    {
        using var f = new FakeLinux();
        var r = Host(f, false, "6.1.0").Sample(T0);
        Assert.Null(r.Metrics[MetricKeys.CpuPercent].Value); Assert.Null(r.Metrics[MetricKeys.MemoryPercent].Value); Assert.Null(r.Metrics[MetricKeys.UptimeSeconds].Value);
    }

    // ---------------------------------------------------------------- Windows host (runs only on Windows)
    [Fact]
    public void Windows_host_provider_reads_real_cpu_memory_uptime_and_disk_and_never_invents_a_temperature()
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = new WindowsHostMetricsProvider(AppContext.BaseDirectory);
        p.Sample(T0);
        ProviderResult r;
        var tries = 0;
        do { Thread.Sleep(400); r = p.Sample(T0.AddMilliseconds(400)); } while (r.Metrics[MetricKeys.CpuPercent].Value is null && ++tries < 5);   // CPU is the Task Manager counter (Processor Utility): a transient read error gives no value, never another metric
        Assert.NotEqual("CPU_IDLE_TIME_FALLBACK", r.Info.Note);                                                                                            // the counter exists on every supported Windows
        Assert.Equal(MetricSource.Host, r.Source);
        Assert.InRange(r.Metrics[MetricKeys.CpuPercent].Value!.Value, 0, 100);
        Assert.True(r.Metrics[MetricKeys.MemoryTotalBytes].Value > 1_000_000_000);
        Assert.InRange(r.Metrics[MetricKeys.MemoryPercent].Value!.Value, 0, 100);
        Assert.True(r.Metrics[MetricKeys.UptimeSeconds].Value > 0);
        Assert.True(r.Metrics[MetricKeys.CpuCores].Value >= 1);
        Assert.True(r.Metrics[MetricKeys.DiskTotalBytes].Value > 0);
        Assert.Null(r.Metrics[MetricKeys.TemperatureC].Value);
        Assert.Equal(MetricReasons.NoTemperatureSensor, r.Metrics[MetricKeys.TemperatureC].ReasonCode);
        Assert.Equal(Environment.MachineName, r.Info.Hostname);
        // cross-check with the operating system's own report of installed memory (within 10 %: GlobalMemoryStatusEx excludes reserved memory)
        Assert.InRange(r.Metrics[MetricKeys.MemoryTotalBytes].Value!.Value / (double)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 0.9, 1.1);
    }

    [Fact]
    public void Disk_probe_returns_consistent_figures_for_the_volume_of_a_folder()
    {
        var d = MonitoringSupport.Disk(Path.GetTempPath());
        Assert.NotNull(d);
        Assert.True(d!.Value.Total > 0);
        Assert.InRange(d.Value.Percent, 0, 100);
        Assert.True(d.Value.Used >= 0 && d.Value.Free >= 0 && d.Value.Used + d.Value.Free <= d.Value.Total * 1.01);
        Assert.Null(MonitoringSupport.Disk(Path.Combine(Path.GetTempPath(), "no-such-volume-\0")));
    }

    // ---------------------------------------------------------------- sampler / snapshot
    private sealed class FakeProvider(MetricSource source, Func<ProviderResult> next) : IContainerMetricsProvider, IHostMetricsProvider
    {
        public ProviderResult Sample(DateTimeOffset now) => next();
        public MetricSource Source { get; } = source;
    }

    private static ProviderResult Result(MetricSource s, double cpu, double mem, bool inside = false, string? note = null, string platform = "Linux") =>
        new(s, new Dictionary<string, RawMetric>
        {
            [MetricKeys.CpuPercent] = RawMetric.Of(cpu), [MetricKeys.MemoryPercent] = RawMetric.Of(mem),
            [MetricKeys.DiskPercent] = RawMetric.Of(85), [MetricKeys.TemperatureC] = RawMetric.Unavailable(MetricReasons.NoTemperatureSensor),
        }, new ProviderInfo(platform, "TestOS", inside ? "Docker" : null, inside, inside ? null : "HOST1", note));

    private static (ResourceMonitor M, TestClock Clock) Monitor(Func<ProviderResult> c, Func<ProviderResult> h, ResourceMonitoringOptions? o = null)
    {
        var clock = new TestClock();
        return (new ResourceMonitor(new FakeProvider(MetricSource.Container, c), new FakeProvider(MetricSource.Host, h), Options.Create(o ?? new ResourceMonitoringOptions()), clock, NullLogger<ResourceMonitor>.Instance), clock);
    }

    [Fact]
    public void Container_and_host_values_stay_separate_each_with_unit_source_status_and_timestamp()
    {
        var (m, _) = Monitor(() => Result(MetricSource.Container, 97, 40, inside: true), () => Result(MetricSource.Host, 10, 82));
        m.Tick();
        var s = m.GetSnapshot();
        Assert.Equal((97d, "%", MetricSource.Container, MetricStatus.Critical), (s.Container[MetricKeys.CpuPercent].Value, s.Container[MetricKeys.CpuPercent].Unit, s.Container[MetricKeys.CpuPercent].Source, s.Container[MetricKeys.CpuPercent].Status));
        Assert.Equal((10d, MetricSource.Host, MetricStatus.Normal), (s.Host[MetricKeys.CpuPercent].Value, s.Host[MetricKeys.CpuPercent].Source, s.Host[MetricKeys.CpuPercent].Status));
        Assert.Equal(MetricStatus.Warning, s.Host[MetricKeys.MemoryPercent].Status);                        // 82 % >= 80
        Assert.Equal(MetricStatus.Warning, s.Container[MetricKeys.DiskPercent].Status);
        Assert.All(s.Container.Values, x => Assert.Equal(MetricSource.Container, x.Source));
        Assert.All(s.Host.Values, x => Assert.Equal(MetricSource.Host, x.Source));
        Assert.NotNull(s.Container[MetricKeys.CpuPercent].LastUpdatedAt);
        var temp = s.Host[MetricKeys.TemperatureC];
        Assert.Equal((null, MetricStatus.Unavailable, MetricReasons.NoTemperatureSensor), (temp.Value, temp.Status, temp.Reason));
        Assert.True(s.Environment.InContainer);
        Assert.Null(s.Environment.Hostname);                                                                   // inside a container the machine name is the container id, not the host name
        Assert.False(string.IsNullOrEmpty(s.Environment.ContainerHostname));
    }

    [Fact]
    public void A_docker_vm_is_reported_as_such_and_the_real_host_is_flagged_as_needing_an_agent()
    {
        var (m, _) = Monitor(() => Result(MetricSource.Container, 1, 1, inside: true), () => Result(MetricSource.DockerVm, 5, 50, inside: true, note: "DOCKER_DESKTOP_VM"));
        m.Tick();
        var s = m.GetSnapshot();
        Assert.Equal(MetricSource.DockerVm, s.Environment.HostScope);
        Assert.True(s.Environment.HostAgentRequired);
        Assert.All(s.Host.Values, x => Assert.Equal(MetricSource.DockerVm, x.Source));
    }

    [Fact]
    public void History_is_capped_and_a_missing_value_is_a_gap_not_a_zero()
    {
        var calls = 0;
        var (m, _) = Monitor(() => Result(MetricSource.Container, 10, 20, true),
            () => ++calls == 2 ? new ProviderResult(MetricSource.Host, MonitoringSupport.AllUnavailable(MetricReasons.ReadFailed, MetricKeys.All), new ProviderInfo("Linux", "x", null, false, "h")) : Result(MetricSource.Host, 30, 40),
            new ResourceMonitoringOptions { SampleIntervalSeconds = 60, HistoryMinutes = 3 });
        for (var i = 0; i < 10; i++) m.Tick();
        var h = m.GetSnapshot().History;
        Assert.Equal(3, h.Count);                                           // 3 minutes at 60 s
        var all = new List<double?>();
        var (m2, _) = Monitor(() => Result(MetricSource.Container, 10, 20, true), () => calls++ % 2 == 0 ? Result(MetricSource.Host, 30, 40) : new ProviderResult(MetricSource.Host, MonitoringSupport.AllUnavailable(MetricReasons.ReadFailed, MetricKeys.All), new ProviderInfo("Linux", "x", null, false, "h")));
        m2.Tick(); m2.Tick();
        all.AddRange(m2.GetSnapshot().History.Select(x => x.HostCpu));
        Assert.Contains(null, all); Assert.Contains(30d, all);
    }

    [Fact]
    public void An_old_sample_is_never_served_as_a_current_value()
    {
        var (m, clock) = Monitor(() => Result(MetricSource.Container, 50, 50, true), () => Result(MetricSource.Host, 50, 50));   // interval 5 s
        m.Tick();
        Assert.Equal(50d, m.GetSnapshot().Container[MetricKeys.CpuPercent].Value);
        clock.Advance(TimeSpan.FromSeconds(10));                                                                                      // within 3 intervals: still current
        Assert.Equal(MetricStatus.Normal, m.GetSnapshot().Container[MetricKeys.CpuPercent].Status);
        clock.Advance(TimeSpan.FromSeconds(60));                                                                                      // the sampler stalled
        var s = m.GetSnapshot();
        Assert.All(s.Container.Values.Concat(s.Host.Values), x => { Assert.Null(x.Value); Assert.Equal(MetricStatus.Unavailable, x.Status); Assert.Equal(MetricReasons.Stale, x.Reason); });
        Assert.True(s.SampleAgeSeconds > 60);
        Assert.NotNull(s.SampledAt);
        Assert.NotEmpty(s.History);                                                                                                   // history stays a record of what was measured, with its timestamps
    }

    [Fact]
    public void Before_the_first_sample_and_when_disabled_everything_is_unavailable_with_a_reason()
    {
        var (m, _) = Monitor(() => Result(MetricSource.Container, 1, 1, true), () => Result(MetricSource.Host, 1, 1));
        var s = m.GetSnapshot();
        Assert.All(s.Container.Values, x => { Assert.Null(x.Value); Assert.Equal(MetricReasons.NotYetSampled, x.Reason); });
        var (d, _) = Monitor(() => Result(MetricSource.Container, 1, 1, true), () => Result(MetricSource.Host, 1, 1), new ResourceMonitoringOptions { Enabled = false });
        d.Tick();
        var ds = d.GetSnapshot();
        Assert.False(ds.Enabled);
        Assert.All(ds.Host.Values, x => Assert.Equal(MetricReasons.Disabled, x.Reason));
    }

    [Fact]
    public void A_provider_that_throws_makes_its_metrics_unavailable_and_does_not_stop_the_other()
    {
        var (m, _) = Monitor(() => throw new IOException("boom"), () => Result(MetricSource.Host, 20, 30));
        m.Tick();
        var s = m.GetSnapshot();
        Assert.All(s.Container.Values, x => { Assert.Null(x.Value); Assert.Equal(MetricReasons.ReadFailed, x.Reason); });
        Assert.Equal(20d, s.Host[MetricKeys.CpuPercent].Value);
    }

    [Fact]
    public void Non_finite_readings_are_treated_as_unavailable()
    {
        var (m, _) = Monitor(() => Result(MetricSource.Container, double.NaN, double.PositiveInfinity, true), () => Result(MetricSource.Host, 1, 1));
        m.Tick();
        var s = m.GetSnapshot();
        Assert.Null(s.Container[MetricKeys.CpuPercent].Value); Assert.Equal(MetricStatus.Unavailable, s.Container[MetricKeys.MemoryPercent].Status);
    }

    // ---------------------------------------------------------------- cost
    [Fact]
    public void One_sample_is_cheap_and_the_history_is_small()
    {
        var (m, _) = Monitor(() => MetricsProviderFactory.Container(null).Sample(T0), () => MetricsProviderFactory.Host(null).Sample(T0));
        var provC = MetricsProviderFactory.Container(null); var provH = MetricsProviderFactory.Host(null);
        var mon = new ResourceMonitor(provC, provH, Options.Create(new ResourceMonitoringOptions()), new TestClock(), NullLogger<ResourceMonitor>.Instance);
        mon.Tick();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++) mon.Tick();
        sw.Stop();
        var perTick = sw.Elapsed.TotalMilliseconds / 20;
        Assert.True(perTick < 100, $"one sample took {perTick:F1} ms");       // measured: a few ms on Windows; the sampler runs once per 5 s
        var json = System.Text.Json.JsonSerializer.Serialize(mon.GetSnapshot());
        Assert.True(json.Length < 64 * 1024, $"snapshot is {json.Length} bytes");
        _ = m;
    }
}
