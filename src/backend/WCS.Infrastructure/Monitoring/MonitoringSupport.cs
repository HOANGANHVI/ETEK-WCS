using System.Globalization;
using System.Runtime.InteropServices;
using WCS.Application.Monitoring;

namespace WCS.Infrastructure.Monitoring;

/// <summary>Small readers shared by the metrics providers. Every read is defensive: a missing or malformed file is "unavailable", never a guess.</summary>
internal static class MonitoringSupport
{
    public static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch { return null; }
    }

    public static double? ReadNumber(string path)
    {
        var t = ReadText(path);
        return t is not null && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Value of "key N" lines (cpu.stat, memory.stat, /proc/meminfo "Key: N kB").</summary>
    public static double? ReadKeyed(string path, string key)
    {
        var t = ReadText(path);
        if (t is null) return null;
        foreach (var line in t.Split('\n'))
        {
            var parts = line.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == key && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        }
        return null;
    }

    public static bool InContainer() =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) || File.Exists("/.dockerenv");

    /// <summary>Best-effort name of the container runtime (a hint for the UI, never used for decisions).</summary>
    public static string? ContainerRuntime() => File.Exists("/.dockerenv") ? "Docker" : InContainer() ? "Container" : null;

    /// <summary>Filesystem figures of the volume holding <paramref name="path"/> (the most specific mount). Used = total - free, percent as "df" shows it.</summary>
    public static (double Total, double Used, double Free, double Percent)? Disk(string? path)
    {
        try
        {
            var full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path);
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            DriveInfo? best = null; var bestLen = -1;
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ready; try { ready = d.IsReady; } catch { continue; }
                if (!ready) continue;
                var root = d.RootDirectory.FullName;
                if (full.StartsWith(root, cmp) && root.Length > bestLen) { best = d; bestLen = root.Length; }
            }
            if (best is null || best.TotalSize <= 0) return null;
            double total = best.TotalSize, used = best.TotalSize - best.TotalFreeSpace, avail = best.AvailableFreeSpace;
            var denom = used + avail;
            return (total, used, avail, denom <= 0 ? 0 : used / denom * 100);
        }
        catch { return null; }
    }

    /// <summary>Adds the four disk metrics (total/used/free/percent) or marks them all unavailable.</summary>
    public static void AddDisk(Dictionary<string, RawMetric> m, string? path)
    {
        if (Disk(path) is { } d)
        {
            m[MetricKeys.DiskTotalBytes] = RawMetric.Of(d.Total); m[MetricKeys.DiskUsedBytes] = RawMetric.Of(d.Used);
            m[MetricKeys.DiskFreeBytes] = RawMetric.Of(d.Free); m[MetricKeys.DiskPercent] = RawMetric.Of(d.Percent);
        }
        else foreach (var k in new[] { MetricKeys.DiskTotalBytes, MetricKeys.DiskUsedBytes, MetricKeys.DiskFreeBytes, MetricKeys.DiskPercent })
            m[k] = RawMetric.Unavailable(MetricReasons.DiskUnavailable);
    }

    public static Dictionary<string, RawMetric> AllUnavailable(string reason, IEnumerable<string> keys)
    {
        var m = new Dictionary<string, RawMetric>();
        foreach (var k in keys) m[k] = RawMetric.Unavailable(reason);
        return m;
    }

    public static string Arch() => RuntimeInformation.OSArchitecture.ToString();
}

/// <summary>A provider that cannot measure on this platform / in this situation: every metric Unavailable with the reason.</summary>
internal sealed class UnavailableMetricsProvider(MetricSource source, string reason, string[] keys, ProviderInfo info) : IContainerMetricsProvider, IHostMetricsProvider
{
    public ProviderResult Sample(DateTimeOffset now) => new(source, MonitoringSupport.AllUnavailable(reason, keys), info);
}
