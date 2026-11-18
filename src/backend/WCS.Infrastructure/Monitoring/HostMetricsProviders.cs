using System.Globalization;
using System.Runtime.InteropServices;
using WCS.Application.Monitoring;

namespace WCS.Infrastructure.Monitoring;

/// <summary>
/// Linux: /proc/stat, /proc/meminfo, /proc/uptime and /sys (hwmon, thermal). Inside a container these files describe the KERNEL the container runs on. That is the
/// real host on a Linux Docker Engine, but on Docker Desktop (WSL2 / linuxkit) it is the Linux VM that runs the containers, not the Windows/macOS machine: in that
/// case the result is labelled <see cref="MetricSource.DockerVm"/> and the real host stays unavailable (it needs a Host Agent). The VM is never passed off as the host.
/// </summary>
public sealed class LinuxHostMetricsProvider(string procRoot, string sysRoot, Func<bool> inContainer, string? diskPath) : IHostMetricsProvider
{
    private static readonly string[] CpuSensorNames = ["coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal", "soc_thermal", "x86_pkg_temp"];
    private (double Busy, double Total)? _prev;

    public LinuxHostMetricsProvider(string? diskPath) : this("/proc", "/sys", MonitoringSupport.InContainer, diskPath) { }

    /// <summary>Kernel release strings that identify a VM kernel used by Docker Desktop (and other desktop container VMs).</summary>
    public static bool IsDockerDesktopVmKernel(string? osRelease) =>
        osRelease is not null && new[] { "microsoft", "wsl", "linuxkit", "docker-desktop" }.Any(s => osRelease.Contains(s, StringComparison.OrdinalIgnoreCase));

    public ProviderResult Sample(DateTimeOffset now)
    {
        var inside = inContainer();
        var release = MonitoringSupport.ReadText(Path.Combine(procRoot, "sys", "kernel", "osrelease"));
        var vm = inside && IsDockerDesktopVmKernel(release);
        var source = vm ? MetricSource.DockerVm : MetricSource.Host;
        var info = new ProviderInfo("Linux", RuntimeInformation.OSDescription, MonitoringSupport.ContainerRuntime(), inside, inside ? null : Environment.MachineName,
            vm ? "DOCKER_DESKTOP_VM" : inside ? "LINUX_HOST_SEEN_FROM_CONTAINER" : null);
        var m = new Dictionary<string, RawMetric>();

        // CPU: busy share between two samples of the aggregate "cpu" line (iowait counts as idle)
        var stat = MonitoringSupport.ReadText(Path.Combine(procRoot, "stat"));
        var cpuLine = stat?.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (cpuLine is null) { m[MetricKeys.CpuPercent] = RawMetric.Unavailable(MetricReasons.ReadFailed); m[MetricKeys.CpuCores] = RawMetric.Unavailable(MetricReasons.ReadFailed); }
        else
        {
            var f = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
            var total = f.Take(8).Sum();                                   // user nice system idle iowait irq softirq steal (guest time is already inside user)
            var idle = (f.Length > 3 ? f[3] : 0) + (f.Length > 4 ? f[4] : 0);
            var cur = (Busy: total - idle, Total: total);
            if (_prev is null) m[MetricKeys.CpuPercent] = RawMetric.Unavailable(MetricReasons.WarmingUp);
            else
            {
                var dTotal = cur.Total - _prev.Value.Total;
                m[MetricKeys.CpuPercent] = dTotal <= 0 ? RawMetric.Unavailable(MetricReasons.WarmingUp) : RawMetric.Of(Math.Clamp((cur.Busy - _prev.Value.Busy) / dTotal * 100, 0, 100));
            }
            _prev = cur;
            m[MetricKeys.CpuCores] = RawMetric.Of(stat!.Split('\n').Count(l => l.Length > 3 && l.StartsWith("cpu", StringComparison.Ordinal) && char.IsDigit(l[3])));
        }

        // RAM: MemTotal and MemAvailable (kernel estimate of memory usable without swapping); used = total - available
        var meminfo = Path.Combine(procRoot, "meminfo");
        if (MonitoringSupport.ReadKeyed(meminfo, "MemTotal") is { } totalKb && MonitoringSupport.ReadKeyed(meminfo, "MemAvailable") is { } availKb && totalKb > 0)
        {
            double total = totalKb * 1024, avail = availKb * 1024, used = Math.Max(0, total - avail);
            m[MetricKeys.MemoryTotalBytes] = RawMetric.Of(total); m[MetricKeys.MemoryAvailableBytes] = RawMetric.Of(avail);
            m[MetricKeys.MemoryUsedBytes] = RawMetric.Of(used); m[MetricKeys.MemoryPercent] = RawMetric.Of(used / total * 100);
        }
        else foreach (var k in new[] { MetricKeys.MemoryTotalBytes, MetricKeys.MemoryAvailableBytes, MetricKeys.MemoryUsedBytes, MetricKeys.MemoryPercent }) m[k] = RawMetric.Unavailable(MetricReasons.ReadFailed);

        var up = MonitoringSupport.ReadText(Path.Combine(procRoot, "uptime"))?.Split(' ')[0];
        m[MetricKeys.UptimeSeconds] = up is not null && double.TryParse(up, NumberStyles.Float, CultureInfo.InvariantCulture, out var u) ? RawMetric.Of(u) : RawMetric.Unavailable(MetricReasons.ReadFailed);

        MonitoringSupport.AddDisk(m, diskPath);
        m[MetricKeys.TemperatureC] = CpuTemperature() is { } temp ? RawMetric.Of(temp) : RawMetric.Unavailable(MetricReasons.NoTemperatureSensor);
        return new ProviderResult(source, m, info);
    }

    /// <summary>CPU package temperature in °C from hwmon / thermal zones, or null when no valid CPU sensor is exposed. Never estimated.</summary>
    private double? CpuTemperature()
    {
        try
        {
            var hwmon = Path.Combine(sysRoot, "class", "hwmon");
            if (Directory.Exists(hwmon))
                foreach (var dir in Directory.GetDirectories(hwmon).OrderBy(d => d, StringComparer.Ordinal))
                {
                    var name = MonitoringSupport.ReadText(Path.Combine(dir, "name"));
                    if (name is null || !CpuSensorNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    var inputs = Directory.GetFiles(dir, "temp*_input").OrderBy(f => f, StringComparer.Ordinal).Select(f => (File: f, Milli: MonitoringSupport.ReadNumber(f)))
                        .Where(x => x.Milli is > 0 and < 150_000).ToList();
                    if (inputs.Count == 0) continue;
                    // prefer the package / die / control temperature; otherwise the hottest core
                    var preferred = inputs.FirstOrDefault(x => MonitoringSupport.ReadText(x.File.Replace("_input", "_label")) is { } l
                        && (l.Contains("Package", StringComparison.OrdinalIgnoreCase) || l.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || l.Contains("Tdie", StringComparison.OrdinalIgnoreCase)));
                    return (preferred.File is not null ? preferred.Milli : inputs.Max(x => x.Milli))!.Value / 1000;
                }
            var thermal = Path.Combine(sysRoot, "class", "thermal");
            if (Directory.Exists(thermal))
                foreach (var zone in Directory.GetDirectories(thermal, "thermal_zone*").OrderBy(d => d, StringComparer.Ordinal))
                {
                    var type = MonitoringSupport.ReadText(Path.Combine(zone, "type"));
                    if (type is null || !CpuSensorNames.Contains(type, StringComparer.OrdinalIgnoreCase)) continue;
                    if (MonitoringSupport.ReadNumber(Path.Combine(zone, "temp")) is > 0 and < 150_000 and var milli) return milli / 1000;
                }
        }
        catch { /* an unreadable sensor tree is "no sensor", not a failure */ }
        return null;
    }
}

/// <summary>
/// Windows (WCS running natively on Windows): GetSystemTimes, GlobalMemoryStatusEx, GetTickCount64 and the drive of the data folder. These are the Windows host's
/// own figures. A CPU temperature is not available through a supported, permission-free Windows API, so it is reported Unavailable rather than guessed.
/// CPU: the figure is the performance counter "Processor Information(_Total)\% Processor Utility" - the very number Task Manager (Windows 10 1709 and later) shows.
/// The older "% Processor Time" (idle-time based, what GetSystemTimes yields) ignores the processor clock: a CPU that idles at a low frequency reads ~1-2 % there
/// but ~10 % in Task Manager. Only when the counter does not exist on this Windows (not for a transient read error) the idle-time figure is used instead and flagged in <see cref="ProviderInfo.Note"/>.
/// </summary>
public sealed class WindowsHostMetricsProvider(string? diskPath) : IHostMetricsProvider, IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low; public uint High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)] private struct PdhFmtCounterValue { public uint CStatus; public double DoubleValue; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    private const uint PdhFmtDouble = 0x200;
    private const string UtilityCounter = @"\Processor Information(_Total)\% Processor Utility";

    private IntPtr _query, _counter;
    private bool _pdhTried, _pdhPrimed;

    /// <summary>Task Manager's CPU figure. Null when the counter does not exist (older Windows) or cannot be read; the first call only primes the counter.</summary>
    private (double? Value, bool Warming) ReadUtility()
    {
        try
        {
            if (!_pdhTried)
            {
                _pdhTried = true;
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0 || PdhAddEnglishCounterW(_query, UtilityCounter, IntPtr.Zero, out _counter) != 0)
                { if (_query != IntPtr.Zero) PdhCloseQuery(_query); _query = _counter = IntPtr.Zero; }
            }
            if (_counter == IntPtr.Zero) return (null, false);
            if (PdhCollectQueryData(_query) != 0) return (null, true);                          // transient: no reading this time (never a different metric)
            if (!_pdhPrimed) { _pdhPrimed = true; return (null, true); }                       // a rate needs two readings
            if (PdhGetFormattedCounterValue(_counter, PdhFmtDouble, out _, out var v) != 0 || v.CStatus != 0 || !double.IsFinite(v.DoubleValue)) return (null, true);   // e.g. PDH_CALC_NEGATIVE_DENOMINATOR right after a sleep/clock change
            return (Math.Clamp(v.DoubleValue, 0, 100), false);                              // utility can exceed 100 % while the clock is boosted
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _counter = IntPtr.Zero; return (null, false); }
    }

    public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = _counter = IntPtr.Zero; } }

    private (ulong Idle, ulong Total)? _prev;

    public ProviderResult Sample(DateTimeOffset now)
    {
        var m = new Dictionary<string, RawMetric>();
        var (utility, utilityWarming) = ReadUtility();
        var info = new ProviderInfo("Windows", RuntimeInformation.OSDescription, null, false, Environment.MachineName, utility is null && !utilityWarming ? "CPU_IDLE_TIME_FALLBACK" : null);
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var cur = (Idle: idle.Value, Total: kernel.Value + user.Value);        // kernel time already includes idle time
                if (utility is { } u) m[MetricKeys.CpuPercent] = RawMetric.Of(u);       // what Task Manager shows
                else if (utilityWarming || _prev is null) m[MetricKeys.CpuPercent] = RawMetric.Unavailable(MetricReasons.WarmingUp);
                else
                {
                    var dTotal = cur.Total - _prev.Value.Total;                         // fallback: classic "% Processor Time"
                    m[MetricKeys.CpuPercent] = dTotal == 0 ? RawMetric.Unavailable(MetricReasons.WarmingUp)
                        : RawMetric.Of(Math.Clamp((1 - (double)(cur.Idle - _prev.Value.Idle) / dTotal) * 100, 0, 100));
                }
                _prev = cur;
            }
            else m[MetricKeys.CpuPercent] = utility is { } u2 ? RawMetric.Of(u2) : RawMetric.Unavailable(MetricReasons.ReadFailed);
            m[MetricKeys.CpuCores] = RawMetric.Of(Environment.ProcessorCount);

            var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref mem) && mem.TotalPhys > 0)
            {
                double total = mem.TotalPhys, avail = mem.AvailPhys, used = total - avail;
                m[MetricKeys.MemoryTotalBytes] = RawMetric.Of(total); m[MetricKeys.MemoryAvailableBytes] = RawMetric.Of(avail);
                m[MetricKeys.MemoryUsedBytes] = RawMetric.Of(used); m[MetricKeys.MemoryPercent] = RawMetric.Of(used / total * 100);
            }
            else foreach (var k in new[] { MetricKeys.MemoryTotalBytes, MetricKeys.MemoryAvailableBytes, MetricKeys.MemoryUsedBytes, MetricKeys.MemoryPercent }) m[k] = RawMetric.Unavailable(MetricReasons.ReadFailed);
            m[MetricKeys.UptimeSeconds] = RawMetric.Of(GetTickCount64() / 1000.0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new ProviderResult(MetricSource.Host, MonitoringSupport.AllUnavailable(MetricReasons.PlatformUnsupported, MetricKeys.All), info);
        }
        MonitoringSupport.AddDisk(m, diskPath);
        m[MetricKeys.TemperatureC] = RawMetric.Unavailable(MetricReasons.NoTemperatureSensor);
        return new ProviderResult(MetricSource.Host, m, info);
    }
}

/// <summary>Chooses the providers for the platform WCS runs on. No operating-system logic anywhere else.</summary>
public static class MetricsProviderFactory
{
    public static IContainerMetricsProvider Container(string? diskPath)
    {
        if (OperatingSystem.IsLinux()) return new CgroupContainerMetricsProvider(diskPath);
        var info = new ProviderInfo(OperatingSystem.IsWindows() ? "Windows" : "Other", RuntimeInformation.OSDescription, MonitoringSupport.ContainerRuntime(), MonitoringSupport.InContainer(), null);
        // not a Linux container: there is no cgroup accounting to read
        return new UnavailableMetricsProvider(MetricSource.Container, MonitoringSupport.InContainer() ? MetricReasons.PlatformUnsupported : MetricReasons.NotInContainer, MetricKeys.All, info);
    }

    public static IHostMetricsProvider Host(string? diskPath)
    {
        if (OperatingSystem.IsLinux()) return new LinuxHostMetricsProvider(diskPath);
        if (OperatingSystem.IsWindows())
        {
            if (!MonitoringSupport.InContainer()) return new WindowsHostMetricsProvider(diskPath);
            // a Windows container cannot see the Windows host's figures: a Host Agent would be needed
            return new UnavailableMetricsProvider(MetricSource.Host, MetricReasons.HostAgentRequired, MetricKeys.All, new ProviderInfo("Windows", RuntimeInformation.OSDescription, "Container", true, null));
        }
        return new UnavailableMetricsProvider(MetricSource.Host, MetricReasons.PlatformUnsupported, MetricKeys.All, new ProviderInfo("Other", RuntimeInformation.OSDescription, null, false, Environment.MachineName));
    }
}
