using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;

namespace WCS.Application.Diagnostics;

public enum DiagJobState { Queued, Running, Completed, Failed, Expired }

/// <summary>Status of one system-export job as the API shows it. The ZIP path is never exposed; the file is served only through the Admin-only download endpoint.</summary>
public sealed class DiagJob
{
    public Guid Id { get; init; }
    public DiagJobState State { get; set; } = DiagJobState.Queued;
    public int Progress { get; set; }
    public string Stage { get; set; } = "Queued";
    public string RequestedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public SystemExportRequest Request { get; init; } = null!;
    public string FileName { get; set; } = "";
    public long Bytes { get; set; }
    public string? Completeness { get; set; }
    public long Events { get; set; }
    public long Errors { get; set; }
    public long Warnings { get; set; }
    public IReadOnlyList<SourceReport> Sources { get; set; } = [];
    public IReadOnlyList<string> Notes { get; set; } = [];
    public string? ErrorCode { get; set; }
    internal string? FilePath { get; set; }
    internal CancellationTokenSource? Cts { get; set; }
}

/// <summary>
/// CR-021: runs system diagnostic exports in the background (one at a time, a short queue), keeps the finished ZIP in a private temp folder for <c>RetentionHours</c> (24) and removes it afterwards.
/// A job never runs on the PLC/AGV/Task paths: it has its own database scope, a timeout, and its failure is reported on the job, not thrown into the application.
/// Job state is in memory: after a restart the list is empty and orphaned files are removed by <see cref="Sweep"/>.
/// </summary>
public sealed class DiagnosticExportJobs(IServiceScopeFactory scopes, IOptions<SystemDiagnosticOptions> options, IClock clock, ILogger<DiagnosticExportJobs> logger)
{
    private readonly ConcurrentDictionary<Guid, DiagJob> _jobs = new();
    private readonly SemaphoreSlim _one = new(1, 1);
    private static readonly Regex SafeName = new("[^A-Za-z0-9_.-]", RegexOptions.Compiled);

    public string Directory => options.Value.Directory is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "wcs-diagnostics");

    public DiagJob Start(SystemExportRequest req, string actor)
    {
        var o = options.Value; var now = clock.UtcNow;
        SystemDiagnosticExporter.ResolveRange(req, now);                         // validates the mode/range now, not in the background
        foreach (var m in req.Scope.Modules) if (!DiagModules.IsValid(m)) throw AppException.Validation("DIAGNOSTIC_MODULE_INVALID", $"Unknown module '{m}'.");
        if (req.Scope.MinLevel is not null && !DiagLevels.IsValid(req.Scope.MinLevel)) throw AppException.Validation("DIAGNOSTIC_LEVEL_INVALID", "minLevel must be Information, Warning, Error or Critical.");
        var scope = req.Scope with { Modules = req.Scope.Modules.Select(DiagModules.Canonical).Distinct().ToList() };
        req = req with { Scope = scope };
        if (_jobs.Values.Count(j => j.State is DiagJobState.Queued or DiagJobState.Running) >= 1 + Math.Max(0, o.MaxQueuedJobs))
            throw AppException.Rejected("DIAGNOSTIC_EXPORT_BUSY", "Too many diagnostic exports are already running or waiting. Try again when one has finished.");

        var id = Guid.NewGuid();
        var label = scope.IsWholeSystem ? "ALL" : SafeName.Replace(string.Join("-", scope.Modules.Count > 0 ? scope.Modules : [scope.PlcId ?? scope.TaskId ?? scope.CorrelationId ?? "filtered"]), "_");
        var job = new DiagJob { Id = id, RequestedBy = actor, CreatedAt = now, Request = req, FileName = $"WCS_SystemDiagnostic_{label}_{now:yyyyMMdd_HHmmss}.zip", Cts = new CancellationTokenSource() };
        _jobs[id] = job;
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    public DiagJob? Get(Guid id) => _jobs.TryGetValue(id, out var j) ? j : null;
    public IReadOnlyList<DiagJob> List() => _jobs.Values.OrderByDescending(j => j.CreatedAt).Take(20).ToList();

    /// <summary>The finished file, or null when the job is unknown, not completed or its file is gone (expired).</summary>
    public (string Path, string FileName)? OpenDownload(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var j) || j.State != DiagJobState.Completed || j.FilePath is null || !File.Exists(j.FilePath)) return null;
        return (j.FilePath, j.FileName);
    }

    private async Task RunAsync(DiagJob job)
    {
        var o = options.Value;
        await _one.WaitAsync();
        try
        {
            job.State = DiagJobState.Running; job.StartedAt = clock.UtcNow; job.Stage = "Starting";
            var dir = Directory; EnsureDirectory(dir);
            var path = Path.Combine(dir, job.Id.ToString("N") + ".zip");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(job.Cts!.Token); timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, o.JobTimeoutMinutes)));
            await using var scope = scopes.CreateAsyncScope();
            var exporter = scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>();
            var outcome = await exporter.ExportAsync(job.Id, job.Request, job.RequestedBy, path, (p, s) => { job.Progress = p; job.Stage = s; }, timeout.Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            job.FilePath = outcome.FilePath; job.Bytes = outcome.Bytes; job.Completeness = outcome.Completeness; job.Sources = outcome.Sources; job.Events = outcome.Events;
            job.Errors = outcome.Errors; job.Warnings = outcome.Warnings; job.Notes = outcome.Notes;
            job.FinishedAt = clock.UtcNow; job.ExpiresAt = job.FinishedAt.Value.AddHours(Math.Max(1, o.RetentionHours)); job.Progress = 100; job.Stage = "Done"; job.State = DiagJobState.Completed;
            logger.LogInformation("Diagnostic export {JobId} finished: {Events} events, {Bytes} bytes, {Completeness}. {EventCode}", job.Id, job.Events, job.Bytes, job.Completeness, "DIAG_EXPORT_DONE");
        }
        catch (Exception ex)
        {
            job.State = DiagJobState.Failed; job.FinishedAt = clock.UtcNow; job.Stage = "Failed";
            job.ErrorCode = ex is OperationCanceledException ? "DIAGNOSTIC_EXPORT_TIMEOUT" : ex is AppException a ? a.Code : "DIAGNOSTIC_EXPORT_FAILED";
            logger.LogError(ex, "Diagnostic export {JobId} failed ({Code}). {EventCode}", job.Id, job.ErrorCode, "DIAG_EXPORT_FAILED");
            try { var p = Path.Combine(Directory, job.Id.ToString("N") + ".zip"); if (File.Exists(p)) File.Delete(p); if (File.Exists(p + ".part")) File.Delete(p + ".part"); } catch (Exception) { /* cleaned by the sweep */ }
        }
        finally { _one.Release(); }
    }

    private static void EnsureDirectory(string dir)
    {
        if (System.IO.Directory.Exists(dir)) return;
        if (OperatingSystem.IsWindows()) System.IO.Directory.CreateDirectory(dir);
        else System.IO.Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);   // private to the service account
    }

    /// <summary>Removes finished files older than the retention, expired job records, and orphaned files/work folders (e.g. after a restart). Returns the number of files removed.</summary>
    public int Sweep()
    {
        var o = options.Value; var now = clock.UtcNow; var removed = 0;
        foreach (var j in _jobs.Values.Where(j => j.ExpiresAt is { } e && e <= now && j.State == DiagJobState.Completed))
        {
            try { if (j.FilePath is not null && File.Exists(j.FilePath)) { File.Delete(j.FilePath); removed++; } } catch (Exception) { continue; }
            j.State = DiagJobState.Expired; j.FilePath = null;
        }
        foreach (var j in _jobs.Values.Where(j => j.State is DiagJobState.Expired or DiagJobState.Failed && (j.FinishedAt ?? now) < now.AddHours(-Math.Max(1, o.RetentionHours) * 2))) _jobs.TryRemove(j.Id, out _);
        var dir = Directory;
        if (!System.IO.Directory.Exists(dir)) return removed;
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, o.RetentionHours));
        var live = _jobs.Values.Where(j => j.State is DiagJobState.Running or DiagJobState.Queued).Select(j => j.Id.ToString("N")).ToHashSet();
        try
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*.zip*"))
                if (File.GetLastWriteTimeUtc(f) < cutoff && !live.Contains(Path.GetFileNameWithoutExtension(f).Split('.')[0])) { File.Delete(f); removed++; }
            foreach (var w in System.IO.Directory.EnumerateDirectories(dir, "work-*"))
                if (Directory_LastWrite(w) < DateTime.UtcNow.AddHours(-1) && !live.Contains(Path.GetFileName(w)[5..])) System.IO.Directory.Delete(w, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "Diagnostic export cleanup could not remove everything (will retry). {EventCode}", "DIAG_EXPORT_CLEANUP"); }
        return removed;
    }

    private static DateTime Directory_LastWrite(string d) => System.IO.Directory.GetLastWriteTimeUtc(d);
}
