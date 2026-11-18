using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Logging;
using WCS.Application.Plc;

namespace WCS.Application.Diagnostics;

/// <summary>Limits of one export. Data, not code; the ZIP is built in memory, so the size cap is a hard stop.</summary>
public sealed class DiagnosticExportOptions
{
    public const string SectionName = "Diagnostics:Export";
    /// <summary>Newest rows per log file; older rows in the range are not exported (the summary says so).</summary>
    public int MaxRowsPerFile { get; set; } = 5000;
    /// <summary>Uncompressed bytes of all files together. Reaching it stops adding rows.</summary>
    public int MaxUncompressedBytes { get; set; } = 20 * 1024 * 1024;
    public int MaxRangeDays { get; set; } = 31;
}

public sealed record DiagnosticRequest(string? PlcId, DateTimeOffset From, DateTimeOffset To);

public sealed record DiagnosticPackage(byte[] Content, string FileName, IReadOnlyList<string> Files, IReadOnlyList<string> Warnings);

public sealed record PlcStationDiag(string StationId, string? LocationCode, bool Enabled, int Points, int WithAddress, int Approved, int WriteAllowed, IReadOnlyList<string> UnapprovedSample);

public sealed record PlcConnectionDiag(string PlcId, string Name, string IpAddress, int? Port, string? Protocol, int? NetworkNumber, int? PcNumber, int? ModuleIoNumber,
    int? PlcStationNumber, int? MonitoringTimer, int? TimeoutMs, int? ConnectTimeoutMs, int? ReadTimeoutMs, int? WriteTimeoutMs, int? PollingIntervalMs, int? XyBase, int? WBase,
    bool Enabled, IReadOnlyList<string> Missing, IReadOnlyList<PlcStationDiag> Stations);

public sealed record PlcLogRow(DateTimeOffset At, string? PlcId, string? StationId, string Level, string Event, string Message, string? DetailsJson, string? Code, string? Stage);

public sealed record PlcExchangeRow(DateTimeOffset At, string PlcId, string Operation, string Command, string Device, int StartAddress, int Points, long DurationMs, int? EndCode,
    string Outcome, string? Error, string? RequestHex, string? ResponseHex, bool Ambiguous);

/// <summary>Read-only access to the PLC diagnostic data (implemented over PostgreSQL). Nothing here can change a PLC, a setting or an approval.</summary>
public interface IPlcDiagnosticSource
{
    Task<IReadOnlyList<PlcConnectionDiag>> ConnectionsAsync(string? plcId, CancellationToken ct = default);
    Task<(IReadOnlyList<PlcLogRow> Rows, int Total)> LogsAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);
    Task<(IReadOnlyList<PlcExchangeRow> Rows, int Total)> ExchangesAsync(string? plcId, DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);
}

/// <summary>
/// PLC Diagnostic Export (P1): builds the ZIP an Admin sends to the engineers. It only READS: stored events, stored MC exchanges, the connection configuration (without secrets)
/// and the live worker status. Every text goes through <see cref="LogSanitizer"/> before it enters the ZIP. A failing section never fails the whole export unless nothing could be read.
/// It runs on the request thread with its own database context and never touches a PLC worker.
/// </summary>
public sealed class DiagnosticExportService(IPlcDiagnosticSource source, IPlcRuntime runtime, IOptions<PlcRuntimeOptions> plcOptions, IOptions<DiagnosticExportOptions> options, IClock clock)
{
    private static readonly string[] ConnectionStages = [PlcDiagStages.Config, PlcDiagStages.State, PlcDiagStages.Tcp, PlcDiagStages.Retry, PlcDiagStages.Recovery];
    private static readonly string[] ReadWriteStages = [PlcDiagStages.Read, PlcDiagStages.Write, PlcDiagStages.Mc];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<DiagnosticPackage> ExportPlcAsync(DiagnosticRequest req, CancellationToken ct = default)
    {
        var o = options.Value;
        if (req.To <= req.From) throw AppException.Validation("DIAGNOSTIC_RANGE_INVALID", "The end of the range must be after its start.");
        if (req.To - req.From > TimeSpan.FromDays(o.MaxRangeDays)) throw AppException.Validation("DIAGNOSTIC_RANGE_TOO_LARGE", $"The range cannot exceed {o.MaxRangeDays} days.");

        var warnings = new List<string>();
        IReadOnlyList<PlcConnectionDiag> connections = [];
        IReadOnlyList<PlcLogRow> logs = []; var logTotal = 0;
        IReadOnlyList<PlcExchangeRow> exchanges = []; var exchangeTotal = 0;
        var failures = 0;
        async Task<T> Guard<T>(string what, Func<Task<T>> read, T fallback)
        {
            try { return await read(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { failures++; warnings.Add($"{what} could not be read: {ex.GetType().Name}"); return fallback; }
        }
        connections = await Guard("PLC configuration", () => source.ConnectionsAsync(req.PlcId, ct), connections);
        if (req.PlcId is not null && connections.Count == 0 && failures == 0) throw AppException.NotFound("PLC_NOT_FOUND", $"PLC {req.PlcId} not found.");
        (logs, logTotal) = await Guard("PLC events", () => source.LogsAsync(req.PlcId, req.From, req.To, o.MaxRowsPerFile, ct), (logs, 0));
        (exchanges, exchangeTotal) = await Guard("MC exchange history", () => source.ExchangesAsync(req.PlcId, req.From, req.To, o.MaxRowsPerFile, ct), (exchanges, 0));
        if (failures >= 3) throw new AppException(ErrorKind.Rejected, "DIAGNOSTIC_EXPORT_FAILED", "The diagnostic data could not be read; nothing was exported. See the backend log.");

        var now = clock.UtcNow;
        var stamp = now.ToString("yyyyMMdd_HHmmss");
        var scope = req.PlcId ?? "ALL";
        var files = new List<string>();
        long budget = o.MaxUncompressedBytes; var truncatedByBytes = false;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                var clean = Sanitize(text);
                var bytes = Encoding.UTF8.GetBytes(clean);
                if (bytes.Length > budget) { truncatedByBytes = true; clean = clean[..Math.Max(0, (int)Math.Min(clean.Length, budget))] + "\n[TRUNCATED: size limit reached]\n"; bytes = Encoding.UTF8.GetBytes(clean); }
                budget = Math.Max(0, budget - bytes.Length);
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = entry.Open(); w.Write(bytes);
                files.Add(name);
            }

            // ---- live status (in-memory, Admin view)
            var live = runtime.Workers(true).Where(w => req.PlcId is null || w.PlcId == req.PlcId).ToList();
            var po = plcOptions.Value;

            Add("System_Info.txt", SystemInfo(now, po));
            Add("WCS_Version.txt", VersionInfo());
            Add("PLC/PLC_Connections.csv", ConnectionsCsv(connections, live));
            Add("PLC/PLC_Configuration.json", JsonSerializer.Serialize(new
            {
                runtime = new { mode = po.Mode.ToString(), effectiveMode = WritePolicy.EffectiveMode(po).ToString(), allowLiveConnection = po.AllowLiveConnection, runtimeEnabled = po.RuntimeEnabled,
                    writeEnabled = po.Write.Enabled,
                    writeKeysComplete = WritePolicy.WriteKeysComplete(po), integrationEnabled = po.Integration.Enabled, po.ReconnectInitialDelayMs, po.ReconnectMaxDelayMs, po.LogRetentionDays,
                    po.ExchangeHistoryPerPlc, po.ExchangeOkSampleSeconds },
                connections,
            }, Json));

            Add("PLC/PLC_Connection.log", LogText(logs.Where(l => l.Stage is not null && ConnectionStages.Contains(l.Stage))));
            Add("PLC/PLC_Runtime.log", LogText(logs));
            Add("PLC/PLC_Communication.log", ExchangeText(exchanges));
            Add("PLC/PLC_Read_Write.log", LogText(logs.Where(l => l.Stage is not null && ReadWriteStages.Contains(l.Stage))));
            Add("PLC/PLC_Error_Summary.csv", ErrorSummary(logs));

            if (warnings.Count > 0) Add("ERRORS.txt", string.Join("\n", warnings) + "\n");
            Add("Summary.txt", Summary(req, now, connections, live, logs, logTotal, exchanges, exchangeTotal, warnings, truncatedByBytes, o));
        }

        var name = $"WCS_Diagnostic_{(req.PlcId ?? "ALL_PLC")}_{stamp}.zip";
        return new DiagnosticPackage(ms.ToArray(), name, files, warnings);
    }

    // ------------------------------------------------------------------ sections

    private static string Sanitize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
            sb.Append(LogSanitizer.Redact(line.TrimEnd('\r'), 6000) ?? "").Append('\n');
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        if (v is null) return "";
        var needs = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        return needs ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static string Row(params object?[] cells) => string.Join(',', cells.Select(c => Csv(c switch { null => null, DateTimeOffset d => d.ToString("O"), bool b => b ? "true" : "false", _ => c.ToString() })));

    private static string ConnectionsCsv(IReadOnlyList<PlcConnectionDiag> conns, IReadOnlyList<PlcWorkerStatus> live)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Row("PlcId", "Name", "Ip", "Port", "Protocol", "NetworkNo", "PcNo", "ModuleIoNo", "StationNo", "MonitoringTimer", "TimeoutMs", "ConnectTimeoutMs", "ReadTimeoutMs", "WriteTimeoutMs",
            "PollingIntervalMs", "AdminEnabled", "MissingParameters", "Stations", "Points", "PointsWithAddress", "PointsApproved", "WorkerState", "WorkerMode", "WorkerReason", "ConsecutiveFailures",
            "Reconnects", "Cycles", "LastPollAt", "LastSuccessAt", "LastError", "Requests", "Failures", "Timeouts", "LastEndCode", "LastErrorCode", "LastSocketError", "LastLatencyMs"));
        foreach (var c in conns)
        {
            var w = live.FirstOrDefault(x => x.PlcId == c.PlcId); var d = w?.Diagnostics;
            sb.AppendLine(Row(c.PlcId, c.Name, c.IpAddress, c.Port, c.Protocol, c.NetworkNumber, c.PcNumber, c.ModuleIoNumber, c.PlcStationNumber, c.MonitoringTimer, c.TimeoutMs, c.ConnectTimeoutMs,
                c.ReadTimeoutMs, c.WriteTimeoutMs, c.PollingIntervalMs, c.Enabled, string.Join(";", c.Missing), c.Stations.Count, c.Stations.Sum(s => s.Points), c.Stations.Sum(s => s.WithAddress),
                c.Stations.Sum(s => s.Approved), w?.State ?? "NoWorker", w?.Mode, w?.Reason, w?.ConsecutiveFailures, w?.Reconnects, w?.Cycles, w?.LastPollAt, w?.LastSuccessAt, w?.LastError,
                d?.Requests, d?.Failures, d?.Timeouts, d?.LastEndCode, d?.LastErrorCode, d?.LastSocketError, d?.LastLatencyMs));
        }
        return sb.ToString();
    }

    private static string LogText(IEnumerable<PlcLogRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            sb.Append(r.At.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'")).Append(" [").Append(r.Level).Append("] ").Append(r.PlcId ?? "-");
            if (r.StationId is not null) sb.Append('/').Append(r.StationId);
            sb.Append(" stage=").Append(r.Stage ?? "-").Append(" code=").Append(r.Code ?? "-").Append(' ').Append(r.Event).Append(": ").Append(Flat(r.Message));
            if (!string.IsNullOrWhiteSpace(r.DetailsJson)) sb.Append(" | ").Append(Flat(r.DetailsJson));
            sb.AppendLine();
        }
        return sb.Length == 0 ? "(no events in the selected range)\n" : sb.ToString();
    }

    private static string ExchangeText(IEnumerable<PlcExchangeRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var x in rows)
        {
            sb.Append(x.At.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'")).Append(' ').Append(x.PlcId).Append(' ').Append(x.Operation).Append(" cmd=").Append(x.Command).Append(' ')
              .Append(x.Device).Append('@').Append(x.StartAddress).Append(" x").Append(x.Points).Append(" outcome=").Append(x.Outcome)
              .Append(" endCode=").Append(x.EndCode is { } e ? "0x" + e.ToString("X4") : "-").Append(' ').Append(x.DurationMs).Append(" ms");
            if (x.Ambiguous) sb.Append(" AMBIGUOUS");
            if (x.Error is not null) sb.Append(" error=").Append(Flat(x.Error));
            sb.Append(" | REQ ").Append(x.RequestHex ?? "-").Append(" | RESP ").Append(x.ResponseHex ?? "-").AppendLine();
        }
        return sb.Length == 0 ? "(no MC exchange recorded in the selected range)\n" : sb.ToString();
    }

    private static string Flat(string s) => s.Replace("\r", " ").Replace("\n", " ");

    private static string ErrorSummary(IReadOnlyList<PlcLogRow> logs)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Row("PlcId", "Code", "Stage", "Count", "FirstAt", "LastAt", "LastLevel", "LastMessage", "Explanation"));
        foreach (var g in logs.Where(l => l.Code is not null).GroupBy(l => (l.PlcId ?? "-", Code: l.Code!, Stage: l.Stage ?? "-")).OrderBy(g => g.Key.Item1).ThenByDescending(g => g.Count()))
        {
            var last = g.MaxBy(x => x.At)!;
            sb.AppendLine(Row(g.Key.Item1, g.Key.Code, g.Key.Stage, g.Count(), g.Min(x => x.At), last.At, last.Level, Flat(last.Message), PlcDiagCodes.Explain(g.Key.Code)));
        }
        return sb.ToString();
    }

    private static string SystemInfo(DateTimeOffset now, PlcRuntimeOptions po)
    {
        var p = System.Diagnostics.Process.GetCurrentProcess();
        var sb = new StringBuilder();
        sb.AppendLine($"Generated (UTC): {now:O}");
        sb.AppendLine($"Server local time: {DateTimeOffset.Now:O}");
        sb.AppendLine($"Server time zone: {TimeZoneInfo.Local.Id} ({TimeZoneInfo.Local.DisplayName}); UTC offset {TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now)}");
        sb.AppendLine($"Machine name: {Environment.MachineName}");
        sb.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(not set)"}");
        sb.AppendLine($"Process started (UTC): {p.StartTime.ToUniversalTime():O}; uptime {(now - p.StartTime.ToUniversalTime()):d\\.hh\\:mm\\:ss}");
        sb.AppendLine($"Processors: {Environment.ProcessorCount}; working set: {p.WorkingSet64 / 1024 / 1024} MB");
        sb.AppendLine($"PLC mode: {po.Mode} (effective {WritePolicy.EffectiveMode(po)}); Plc:AllowLiveConnection={po.AllowLiveConnection}; PLC runtime enabled={po.RuntimeEnabled}");
        sb.AppendLine("Docker container status and restart counts are NOT included in this package (not part of the PLC diagnostic export).");
        return sb.ToString();
    }

    private static string VersionInfo()
    {
        var asm = typeof(DiagnosticExportService).Assembly;
        var sb = new StringBuilder();
        sb.AppendLine($"WCS build label (WCS_BUILD, the deployed git commit): {Environment.GetEnvironmentVariable("WCS_BUILD") ?? "(not set)"}");
        sb.AppendLine($"Assembly version: {asm.GetName().Version}");
        sb.AppendLine($"Informational version: {(asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion ?? "(none)"}");
        return sb.ToString();
    }

    private static string Summary(DiagnosticRequest req, DateTimeOffset now, IReadOnlyList<PlcConnectionDiag> conns, IReadOnlyList<PlcWorkerStatus> live, IReadOnlyList<PlcLogRow> logs, int logTotal,
        IReadOnlyList<PlcExchangeRow> exchanges, int exchangeTotal, IReadOnlyList<string> warnings, bool truncatedBytes, DiagnosticExportOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WCS PLC DIAGNOSTIC PACKAGE");
        sb.AppendLine($"Generated (UTC): {now:O}");
        sb.AppendLine($"Scope: {(req.PlcId ?? "all PLCs")}; range (UTC): {req.From:O} .. {req.To:O}");
        sb.AppendLine($"Events exported: {logs.Count} of {logTotal} in range (newest {o.MaxRowsPerFile} kept); MC exchanges exported: {exchanges.Count} of {exchangeTotal} stored in range.");
        if (truncatedBytes) sb.AppendLine("NOTE: the size limit was reached; some files are cut.");
        foreach (var w in warnings) sb.AppendLine("WARNING: " + w);
        sb.AppendLine();
        sb.AppendLine("This package only reads stored data. Passwords, tokens and connection strings are masked. Nothing was sent to any PLC.");
        sb.AppendLine("Find a problem by code: see PLC/PLC_Error_Summary.csv (code, stage, count, first/last time, explanation). Stages: CONFIG, STATE, TCP, MC, READ, WRITE, RETRY, RECOVERY.");
        foreach (var c in conns)
        {
            var w = live.FirstOrDefault(x => x.PlcId == c.PlcId);
            var mine = logs.Where(l => l.PlcId == c.PlcId).ToList();
            sb.AppendLine();
            sb.AppendLine($"=== {c.PlcId} ({c.Name}) {c.IpAddress}:{c.Port?.ToString() ?? "(port not set)"} ===");
            sb.AppendLine($"Configured: protocol={c.Protocol ?? "(not set)"}; adminEnabled={c.Enabled}; missing parameters={(c.Missing.Count == 0 ? "none" : string.Join(", ", c.Missing))}");
            sb.AppendLine($"Worker now: state={w?.State ?? "no worker"}; mode={w?.Mode}; reason={w?.Reason ?? "-"}; consecutive failures={w?.ConsecutiveFailures}; reconnects={w?.Reconnects}; last success={w?.LastSuccessAt?.ToString("O") ?? "never"}");
            if (w?.LastError is not null) sb.AppendLine($"Last error: {w.LastError} (code {w.Diagnostics?.LastErrorCode ?? "-"}, socket {w.Diagnostics?.LastSocketError ?? "-"})");
            var points = c.Stations.Sum(s => s.Points); var approved = c.Stations.Sum(s => s.Approved); var withAddr = c.Stations.Sum(s => s.WithAddress);
            sb.AppendLine($"IO points: {points} total, {withAddr} with address, {approved} approved (only approved points are read).");
            // what the data says about WHICH layer is the problem
            var tcpAttempts = mine.Count(l => l.Event is "CONNECT_FAILED" or "TCP_CONNECTED");
            var codes = mine.Where(l => l.Code is not null).GroupBy(l => l.Code!).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} x{g.Count()}").ToList();
            sb.AppendLine($"Codes in range: {(codes.Count == 0 ? "none" : string.Join(", ", codes))}");
            var hints = new List<string>();
            if (mine.Any(l => l.Code == PlcDiagCodes.LiveConnectionDisabled)) hints.Add("WCS deliberately opened NO connection: Plc:AllowLiveConnection is false (no TCP attempt).");
            if (mine.Any(l => l.Code == PlcDiagCodes.ConnectionNotEnabled)) hints.Add("WCS opened NO connection: this PLC is not enabled by the Admin.");
            if (mine.Any(l => l.Code == PlcDiagCodes.ConfigurationMissing) || c.Missing.Count > 0) hints.Add("Connection parameters are missing: WCS cannot start the connection.");
            if (tcpAttempts == 0 && hints.Count == 0) hints.Add("No TCP attempt is recorded in the range (see PLC_Connection.log for the state and reason).");
            if (mine.Any(l => l.Code == PlcDiagCodes.ConnectionRefused)) hints.Add("TCP was attempted and REFUSED: the address answers but nothing listens on that port (check the port, the PLC Ethernet module and firewall).");
            if (mine.Any(l => l.Code == PlcDiagCodes.NetworkUnreachable)) hints.Add("TCP was attempted and the network is UNREACHABLE: check routing/VLAN/cable and that this host is on the PLC network.");
            if (mine.Any(l => l.Code == PlcDiagCodes.Timeout)) hints.Add("A TIMEOUT occurred (connect or MC response): check power, cabling, firewall drops and the timeout setting.");
            if (mine.Any(l => l.Code == PlcDiagCodes.EndCodeError)) hints.Add("The PLC ANSWERED with an MC end code: the link works; the request (device/range/route) was refused (see PLC_Communication.log).");
            if (mine.Any(l => l.Code == PlcDiagCodes.ProtocolError)) hints.Add("A reply was not a valid MC 3E frame: check the frame type/protocol and the route (network/PC/module/station numbers).");
            if (mine.Any(l => l.Code == PlcDiagCodes.IoPointNotApproved)) hints.Add("Some IO points are NOT read because they have no address or an invalid/overlapping one: a connected PLC can still show no data.");
            if (mine.Any(l => l.Event == "TCP_CONNECTED") && exchanges.All(x => x.PlcId != c.PlcId)) hints.Add("TCP connected but no MC exchange is stored: no approved points are polled, or the history is not recorded yet.");
            foreach (var h in hints) sb.AppendLine("- " + h);
        }
        sb.AppendLine();
        sb.AppendLine("Files: System_Info.txt, WCS_Version.txt, PLC/PLC_Connections.csv, PLC/PLC_Configuration.json, PLC/PLC_Connection.log, PLC/PLC_Runtime.log, PLC/PLC_Communication.log, PLC/PLC_Read_Write.log, PLC/PLC_Error_Summary.csv");
        return sb.ToString();
    }
}
