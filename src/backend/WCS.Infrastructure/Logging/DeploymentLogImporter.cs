using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WCS.Application.Logging;
using WCS.Domain.Logging;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Logging;

/// <summary>
/// CR-007: brings deployment events into the searchable log WITHOUT opening any network endpoint. The PowerShell deployment scripts append one JSON object per line to
/// <c>deployment-events-YYYYMMDD.jsonl</c> in the log folder; the backend container mounts that folder read-only and this importer reads it.
/// Import is idempotent (unique <c>ExternalId</c> = event id), so restarts and re-reads never duplicate; every text is masked again before it is stored.
/// Events written while the stack was down are picked up when the backend starts.
/// </summary>
public sealed class DeploymentLogImporter(WcsDbContext db, ILogger<DeploymentLogImporter> logger)
{
    private const int MaxLineChars = 8000, MaxEventsPerRun = 2000, MaxBytesPerRun = 4 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, long> Offsets = new();

    /// <summary>Reads new complete lines of every deployment-events-*.jsonl file. Returns how many new entries were stored.</summary>
    public async Task<int> ImportAsync(string directory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;
        var stored = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "deployment-events-*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (stored >= MaxEventsPerRun) break;
            try { stored += await ImportFileAsync(file, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Deployment log file {File} could not be imported. {EventCode}", Path.GetFileName(file), "DEPLOY_IMPORT_FAILED"); }
        }
        return stored;
    }

    private async Task<int> ImportFileAsync(string file, CancellationToken ct)
    {
        var start = Offsets.GetValueOrDefault(file, 0L);
        byte[] bytes;
        await using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (fs.Length < start) start = 0;                       // file was replaced
            if (fs.Length == start) return 0;
            fs.Seek(start, SeekOrigin.Begin);
            bytes = new byte[Math.Min(fs.Length - start, MaxBytesPerRun)];
            var read = 0;
            while (read < bytes.Length) { var n = await fs.ReadAsync(bytes.AsMemory(read), ct); if (n == 0) break; read += n; }
            if (read < bytes.Length) Array.Resize(ref bytes, read);
        }

        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0) return 0;                              // no complete line yet
        var text = Encoding.UTF8.GetString(bytes, 0, lastNewline + 1);
        if (start == 0 && text.Length > 0 && text[0] == '﻿') text = text[1..];

        var entries = new List<SystemLogEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.Length > MaxLineChars) continue;
            var e = Parse(line);
            if (e is not null) entries.Add(e);
        }

        var inserted = 0;
        if (entries.Count > 0)
        {
            var ids = entries.Select(e => e.ExternalId!).Distinct().ToList();
            var known = (await db.SystemLog.AsNoTracking().Where(x => x.ExternalId != null && ids.Contains(x.ExternalId)).Select(x => x.ExternalId!).ToListAsync(ct)).ToHashSet();
            var fresh = entries.Where(e => known.Add(e.ExternalId!)).ToList();
            db.SystemLog.AddRange(fresh);
            await db.SaveChangesAsync(ct);
            inserted = fresh.Count;
        }
        Offsets[file] = start + lastNewline + 1;                    // advance only after the entries are stored
        return inserted;
    }

    /// <summary>One JSON line -> entry; malformed lines are skipped, never fatal. Public for unit tests.</summary>
    public static SystemLogEntry? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var id = S("id"); if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return null;
            var script = LogSanitizer.Redact(S("script"), 60) ?? "deploy";
            var step = LogSanitizer.Redact(S("step"), 120) ?? "";
            var result = (S("result") ?? "INFO").ToUpperInvariant();
            if (result.Length > 20) result = result[..20];
            var level = S("level") is { } l && LogLevels.IsValid(l) ? l : result switch { "FAIL" => LogLevels.Error, "WARN" => LogLevels.Warning, _ => LogLevels.Information };
            int? exit = r.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.TryGetInt32(out var n) ? n : null;
            var at = DateTimeOffset.TryParse(S("time"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.UtcNow;
            var host = LogSanitizer.Redact(S("host"), 100);
            var message = LogSanitizer.Redact(S("message"), 1500) ?? "";
            var text = ((step.Length > 0 ? $"[{step}] " : "") + message).Trim() + (exit is { } x ? $" (exit code {x})" : "");
            var details = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["script"] = script, ["step"] = step, ["result"] = result, ["exitCode"] = exit, ["host"] = host, ["commit"] = LogSanitizer.Redact(S("commit"), 40), ["runId"] = LogSanitizer.Redact(S("runId"), 64),
            });
            return new SystemLogEntry(at, LogCategories.Deployment, level, "deploy", script, "DEPLOY_" + result, LogSanitizer.Redact(text, 2000)!, null, null, details, host, id);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Forgets read positions (tests).</summary>
    public static void ResetOffsets() => Offsets.Clear();
}
