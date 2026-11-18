using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Logging;
using WCS.Application.Plc;

namespace WCS.Application.Diagnostics;

/// <summary>mode: 1h | 24h | all | custom. "all" = from the oldest row each source still stores (its retention is the real limit and is written to the Manifest).</summary>
public sealed record SystemExportRequest(string Mode, DateTimeOffset? From, DateTimeOffset? To, DiagScope Scope);

public sealed class SourceReport
{
    public string Name { get; set; } = "";
    public IReadOnlyList<string> Modules { get; set; } = [];
    /// <summary>OK | Partial (cut by a limit) | Failed (could not be read) | NotRequested (outside the selected scope).</summary>
    public string Status { get; set; } = "OK";
    public long RowsInRange { get; set; }
    public long RowsExported { get; set; }
    public long RowsOmittedOldest { get; set; }
    public DateTimeOffset? OldestStored { get; set; }
    public DateTimeOffset? NewestInRange { get; set; }
    public string Retention { get; set; } = "";
    public string? Error { get; set; }
    public string? Note { get; set; }
}

public sealed record ExportOutcome(string FilePath, long Bytes, string Completeness, IReadOnlyList<SourceReport> Sources, long Events, long Errors, long Warnings, IReadOnlyList<string> Notes);

/// <summary>
/// CR-021: builds the whole-system diagnostic ZIP. It READS only. Rows are streamed from each source in pages into small temporary files and then merged by time into the ZIP, so memory
/// stays flat whatever the range; a source that cannot be read is reported (Manifest and Summary) and the package is still produced; limits are applied at the OLD end and reported, never silently.
/// </summary>
public sealed class SystemDiagnosticExporter(IEnumerable<IDiagnosticEventSource> sources, IDiagnosticHealthSource health, DiagnosticExportService plcExport, IPlcRuntime runtime,
    IConfiguration config, IOptions<SystemDiagnosticOptions> options, IClock clock)
{
    private static readonly string[] ConfigSections = ["Plc", "Integration", "Scheduler", "Logging", "Diagnostics", "Auth", "Deployment", "DeploymentLogs", "Routing", "Monitoring", "Bootstrap", "Rcs"];
    private static readonly Regex SecretKey = new(@"(?i)(password|passwd|pwd|secret|token|api[_-]?key|private[_-]?key|credential|connection[_-]?string|cookie)", RegexOptions.Compiled);
    private static readonly string[] ProcessSources = ["task_history", "routing_events", "inventory_transactions", "audit_log"];

    private sealed class Acc
    {
        public readonly Dictionary<string, int[]> ModuleLevel = [];                        // module -> [info, warn, error, critical]
        public readonly Dictionary<(string Module, string Key), ErrGroup> ErrorGroups = [];
        public readonly Dictionary<string, int> TaskIssues = [];
        public readonly HashSet<string> CorrelationIds = [], TaskIds = [];
        public long Total, Errors, Warnings, TimelineQualifying;
        public DateTimeOffset? First, Last;
        public readonly Queue<DiagEvent> RecentErrors = new();
    }
    private sealed class ErrGroup { public int Count; public DateTimeOffset First, Last; public string Level = ""; public string? Sample; public string? Code; }
    private sealed record TempFile(string Source, string Path, long Rows);
    private sealed class Budget { public long Left; public bool Hit; }

    public async Task<ExportOutcome> ExportAsync(Guid packageId, SystemExportRequest req, string requestedBy, string outPath, Action<int, string> progress, CancellationToken ct)
    {
        var o = options.Value; var now = clock.UtcNow;
        var (from, to) = ResolveRange(req, now);
        var scope = req.Scope;
        var work = Path.Combine(Path.GetDirectoryName(outPath)!, "work-" + packageId.ToString("N"));
        Directory.CreateDirectory(work);
        var notes = new List<string>(); var reports = new List<SourceReport>(); var temps = new List<TempFile>(); var acc = new Acc();
        try
        {
            // ---------------------------------------------------------------- 1. collect (stream each source to a temp file)
            var wanted = sources.Where(s => scope.Modules.Count == 0 || s.Modules.Any(m => scope.Modules.Contains(m, StringComparer.OrdinalIgnoreCase))).ToList();
            var skipped = sources.Except(wanted).ToList();
            var q = new DiagQuery(from, to, scope);
            var i = 0;
            foreach (var s in wanted)
            {
                progress(5 + 55 * i++ / Math.Max(1, wanted.Count), $"Reading {s.Name}");
                var (rep, tf) = await CollectAsync(s, q, related: false, scope, work, acc, o, ct);
                reports.Add(rep); if (tf is not null) temps.Add(tf);
            }
            foreach (var s in skipped) reports.Add(new SourceReport { Name = s.Name, Modules = s.Modules, Status = "NotRequested", Retention = s.Retention, Note = "Outside the selected modules (rows sharing a CorrelationId/TaskId with a selected row are still pulled in, see the related pass)." });

            // related pass: other modules' rows that share a CorrelationId or TaskId with what was selected
            var narrowed = scope.Modules.Count > 0 || scope.PlcId is not null || scope.Device is not null || scope.MinLevel is not null;
            if (narrowed && (acc.CorrelationIds.Count > 0 || acc.TaskIds.Count > 0))
            {
                var rq = q with { RelatedCorrelationIds = acc.CorrelationIds.Take(o.MaxRelatedIds).ToHashSet(), RelatedTaskIds = acc.TaskIds.Take(o.MaxRelatedIds).ToHashSet() };
                var n = 0;
                foreach (var s in sources)
                {
                    progress(60 + 5 * n++ / Math.Max(1, sources.Count()), $"Related events from {s.Name}");
                    var (rep, tf) = await CollectAsync(s, rq, related: true, scope, work, acc, o, ct);
                    if (rep.RowsExported > 0 || rep.Status == "Failed") { rep.Name += " (related)"; reports.Add(rep); }
                    if (tf is not null) temps.Add(tf);
                }
                notes.Add($"Related pass: rows of other modules sharing one of {acc.CorrelationIds.Count} CorrelationId(s) / {acc.TaskIds.Count} TaskId(s) with the selected rows were added and are marked related=true.");
            }

            // ---------------------------------------------------------------- 2. write the ZIP
            var tmpZip = outPath + ".part"; var bud = new Budget { Left = o.MaxUncompressedBytes };
            var files = new List<(string Name, long Rows)>();
            await using (var fs = new FileStream(tmpZip, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                void AddText(string name, string text, bool sanitize = true)
                {
                    var clean = sanitize ? SanitizeText(text) : text;
                    var bytes = Encoding.UTF8.GetBytes(clean);
                    if (bytes.Length > bud.Left) { bud.Hit = true; bytes = Encoding.UTF8.GetBytes(clean[..(int)Math.Max(0, Math.Min(clean.Length, bud.Left))] + "\n[TRUNCATED: size limit reached]\n"); }
                    bud.Left = Math.Max(0, bud.Left - bytes.Length);
                    using var w = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); w.Write(bytes);
                    files.Add((name, 0));
                }

                progress(66, "System information");
                AddText("System_Info.txt", SystemInfo(now, requestedBy));
                AddText("Health_Snapshot.json", JsonSerializer.Serialize(await HealthAsync(ct), DiagJson.Full));
                AddText("Configuration/config.json", JsonSerializer.Serialize(ConfigSnapshot(), DiagJson.Full));

                // the merged, time-ordered files (each pass reads the sorted temp files again; disk, not memory)
                var skipTimeline = Math.Max(0, acc.TimelineQualifying - o.MaxTimelineRows);
                long tlSeen = 0;
                progress(70, "Events");
                var ev = await MergeAsync(zip, temps, _ => true, r => $"Events/events_{r.Day}.jsonl", (r, w) => w.WriteLine(r.Json), bud, ct);
                foreach (var f in ev) files.Add(f);
                progress(78, "Errors");
                var er = await MergeAsync(zip, temps, r => r.Rank >= 3 || r.Json.Contains("\"exceptionType\"", StringComparison.Ordinal), _ => "Errors/errors.jsonl", (r, w) => w.WriteLine(r.Json), bud, ct);
                foreach (var f in er) files.Add(f);
                AddText("Errors/error_summary.csv", ErrorSummaryCsv(acc));
                progress(84, "Timeline");
                var skipLocal = skipTimeline; var seen = tlSeen;
                var tl = await MergeAsync(zip, temps, r => (r.Rank >= 2 || ProcessSources.Contains(r.Source)) && (skipLocal > 0 ? (seen++ >= skipLocal) : true), _ => "Timeline/timeline.csv",
                    (r, w) => w.WriteLine(TimelineRow(r.Event)), bud, ct, header: TimelineHeader);
                foreach (var f in tl) files.Add(f);
                if (skipTimeline > 0) notes.Add($"Timeline.csv holds the newest {o.MaxTimelineRows} of {acc.TimelineQualifying} qualifying events (older ones are in Events/).");
                progress(88, "Audit");
                var au = await MergeAsync(zip, temps, r => r.Source == "audit_log", _ => "Audit/audit.jsonl", (r, w) => w.WriteLine(r.Json), bud, ct);
                foreach (var f in au) files.Add(f);
                progress(92, "Modules");
                var md = await MergeAsync(zip, temps, _ => true, r => $"Modules/{r.Module}/{r.Module}_{r.Day}.log", (r, w) => w.WriteLine(ModuleLine(r.Event)), bud, ct, orderByEntry: true);
                foreach (var f in md) files.Add(f);

                // PLC folder: the existing PLC export, unchanged in format, copied in
                var wantPlc = (scope.Modules.Count == 0 || scope.Modules.Contains(DiagModules.Plc, StringComparer.OrdinalIgnoreCase)) && scope.TaskId is null && scope.CorrelationId is null;
                var plcRep = new SourceReport { Name = "plc_package (PLC/ folder)", Modules = [DiagModules.Plc], Retention = "plc_comm_log 30 days (Plc:LogRetentionDays); MC exchange history: newest 300 per PLC (OK reads sampled 1 per 5 s)" };
                if (!wantPlc) { plcRep.Status = "NotRequested"; plcRep.Note = "Not part of the selected scope."; }
                else
                {
                    try
                    {
                        progress(95, "PLC package");
                        var plcTo = to ?? now; var plcFrom = from is { } f0 && plcTo - f0 <= TimeSpan.FromDays(o.PlcFolderMaxDays) ? f0 : plcTo.AddDays(-o.PlcFolderMaxDays);
                        var pkg = await plcExport.ExportPlcAsync(new DiagnosticRequest(scope.PlcId, plcFrom, plcTo), ct);
                        using var inZip = new ZipArchive(new MemoryStream(pkg.Content), ZipArchiveMode.Read);
                        foreach (var e in inZip.Entries)
                        {
                            using var rd = new StreamReader(e.Open(), Encoding.UTF8);
                            AddText(e.FullName.StartsWith("PLC/", StringComparison.Ordinal) ? e.FullName : "PLC/package/" + e.FullName, await rd.ReadToEndAsync(ct), sanitize: false);   // already sanitised by the PLC export
                        }
                        // the PLC export keeps its own 31-day cap; it only counts as a LOSS when stored PLC events older than that cap exist in the requested range
                        var plcOldest = reports.FirstOrDefault(r => r.Name == "plc_comm_log")?.OldestStored;
                        var cut = (from is null || from < plcFrom) && plcOldest is { } oldest && oldest < plcFrom;
                        plcRep.RowsExported = pkg.Files.Count;
                        plcRep.Note = $"PLC export ({pkg.Files.Count} files) for {plcFrom:O} .. {plcTo:O}" + (cut ? $" - cut to the PLC export's {o.PlcFolderMaxDays}-day cap; older PLC events are in Events/ (module Plc) but not in this folder" : "");
                        foreach (var w in pkg.Warnings) notes.Add("PLC package: " + w);
                        if (cut) plcRep.Status = "Partial";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { plcRep.Status = "Failed"; plcRep.Error = ex.GetType().Name + (ex is AppException ae ? ": " + ae.Code : ""); }
                }
                reports.Add(plcRep);

                // ---------------------------------------------------------------- 3. Summary and Manifest (written last: they describe what was really written)
                if (bud.Hit) notes.Add($"The uncompressed size limit ({o.MaxUncompressedBytes / 1024 / 1024} MB) was reached: later files are cut or missing. See files marked TRUNCATED.");
                var completeness = reports.Any(r => r.Status is "Failed") || bud.Hit ? "Partial" : reports.Any(r => r.Status == "Partial") ? "Partial" : "Complete";
                progress(98, "Summary");
                AddText("Summary.txt", Summary(req, from, to, now, requestedBy, acc, reports, notes, completeness, files));
                AddText("Manifest.json", JsonSerializer.Serialize(Manifest(packageId, req, from, to, now, requestedBy, acc, reports, notes, completeness, files), DiagJson.Full));
            }
            File.Move(tmpZip, outPath, overwrite: true);
            var comp = reports.Any(r => r.Status == "Failed") || reports.Any(r => r.Status == "Partial") || notes.Any(n => n.Contains("limit")) ? "Partial" : "Complete";
            return new ExportOutcome(outPath, new FileInfo(outPath).Length, comp, reports, acc.Total, acc.Errors, acc.Warnings, notes);
        }
        finally { try { Directory.Delete(work, true); } catch (Exception) { /* temp only */ } }
    }

    // ------------------------------------------------------------------------------------------------ range
    public static (DateTimeOffset? From, DateTimeOffset? To) ResolveRange(SystemExportRequest req, DateTimeOffset now) => (req.Mode ?? "").ToLowerInvariant() switch
    {
        "1h" => (now.AddHours(-1), now),
        "24h" => (now.AddHours(-24), now),
        "all" => ((DateTimeOffset?)null, now),
        "custom" when req.From is { } f && req.To is { } t && t > f => (f, t),
        _ => throw AppException.Validation("DIAGNOSTIC_RANGE_INVALID", "mode must be 1h, 24h, all, or custom with from < to."),
    };

    // ------------------------------------------------------------------------------------------------ collect
    private async Task<(SourceReport, TempFile?)> CollectAsync(IDiagnosticEventSource s, DiagQuery q, bool related, DiagScope scope, string work, Acc acc, SystemDiagnosticOptions o, CancellationToken ct)
    {
        var rep = new SourceReport { Name = s.Name, Modules = s.Modules, Retention = s.Retention };
        var path = Path.Combine(work, $"{(related ? "r" : "p")}_{s.Name}.tsv");
        long rows = 0;
        try
        {
            var stats = await s.StatsAsync(q, ct);
            rep.RowsInRange = stats.RowsInRange; rep.OldestStored = stats.OldestStored; rep.NewestInRange = stats.NewestInRange;
            DateTimeOffset? start = null;
            if (stats.RowsInRange > o.MaxEventsPerSource)
            {
                start = await s.TailStartAsync(q, o.MaxEventsPerSource, ct);
                rep.Status = "Partial"; rep.RowsOmittedOldest = stats.RowsInRange - o.MaxEventsPerSource;
                rep.Note = $"Only the newest {o.MaxEventsPerSource} rows are exported (limit Diagnostics:System:MaxEventsPerSource); {rep.RowsOmittedOldest} older rows in the range were left out.";
            }
            await using var w = new StreamWriter(path, false, new UTF8Encoding(false));
            await foreach (var e0 in s.ReadAsync(q, start, o.PageSize, ct))
            {
                var e = e0 with { Related = related };
                if (related ? scope.Modules.Count > 0 && scope.Modules.Contains(e.Module, StringComparer.OrdinalIgnoreCase) : !Matches(e, scope)) continue;   // related pass keeps only the modules NOT already exported
                var rank = DiagLevels.Rank(e.Level);
                var json = JsonSerializer.Serialize(Clean(e), DiagJson.Compact);
                await w.WriteAsync($"{e.TimestampUtc.UtcTicks}\t{s.Name}\t{e.Module}\t{rank}\t{json}\n");
                rows++; Count(acc, e, rank, s.Name);
            }
            rep.RowsExported = rows;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rep.Status = "Failed"; rep.Error = ex.GetType().Name; rep.Note = "This source could not be read; the package contains no rows from it (or only the rows read before the failure).";
            rep.RowsExported = rows;
        }
        return (rep, rows > 0 ? new TempFile(s.Name, path, rows) : null);
    }

    private static bool Matches(DiagEvent e, DiagScope s) =>
        (s.Modules.Count == 0 || s.Modules.Contains(e.Module, StringComparer.OrdinalIgnoreCase))
        && (s.MinLevel is null || DiagLevels.Rank(e.Level) >= DiagLevels.Rank(s.MinLevel))
        && (s.PlcId is null || string.Equals(e.PlcId, s.PlcId, StringComparison.OrdinalIgnoreCase))
        && (s.Device is null || string.Equals(e.PlcId ?? e.AgvId ?? e.StationId, s.Device, StringComparison.OrdinalIgnoreCase))
        && (s.CorrelationId is null || string.Equals(e.CorrelationId, s.CorrelationId, StringComparison.Ordinal))
        && (s.TaskId is null || string.Equals(e.TaskId, s.TaskId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Last line of defence: every free-text field is masked before it is written (the stored text may predate a rule).</summary>
    private static DiagEvent Clean(DiagEvent e) => e with
    {
        Message = LogSanitizer.Redact(e.Message, 4000), ExceptionMessage = LogSanitizer.Redact(e.ExceptionMessage, 4000), StackTrace = LogSanitizer.Redact(e.StackTrace, 8000),
        Context = LogSanitizer.Redact(e.Context, 8000), Actor = LogSanitizer.Redact(e.Actor, 200),
    };

    private static void Count(Acc a, DiagEvent e, int rank, string source)
    {
        a.Total++; a.First = a.First is null || e.TimestampUtc < a.First ? e.TimestampUtc : a.First; a.Last = a.Last is null || e.TimestampUtc > a.Last ? e.TimestampUtc : a.Last;
        var arr = a.ModuleLevel.TryGetValue(e.Module, out var x) ? x : a.ModuleLevel[e.Module] = new int[4]; arr[Math.Clamp(rank, 1, 4) - 1]++;
        if (rank >= 3) a.Errors++; else if (rank == 2) a.Warnings++;
        if (rank >= 2 || ProcessSources.Contains(source)) a.TimelineQualifying++;
        if (e.CorrelationId is not null && a.CorrelationIds.Count < 5000) a.CorrelationIds.Add(e.CorrelationId);
        if (e.TaskId is not null && a.TaskIds.Count < 5000) a.TaskIds.Add(e.TaskId);
        if (rank >= 2 && e.TaskId is not null) a.TaskIssues[e.TaskId] = a.TaskIssues.GetValueOrDefault(e.TaskId) + 1;
        if (rank >= 2)
        {
            var key = (e.Module, e.ErrorCode ?? e.EventName);
            if (!a.ErrorGroups.TryGetValue(key, out var g)) a.ErrorGroups[key] = g = new ErrGroup { First = e.TimestampUtc, Level = e.Level, Code = e.ErrorCode };
            g.Count++; g.Last = e.TimestampUtc; g.Level = DiagLevels.Rank(e.Level) > DiagLevels.Rank(g.Level) ? e.Level : g.Level; g.Sample = e.Message ?? e.ExceptionMessage;
        }
        if (rank >= 3) { a.RecentErrors.Enqueue(e); while (a.RecentErrors.Count > 40) a.RecentErrors.Dequeue(); }
    }

    // ------------------------------------------------------------------------------------------------ merge
    private sealed record Rec(long Ticks, string Source, string Module, int Rank, string Json)
    {
        public string Day => new DateTimeOffset(Ticks, TimeSpan.Zero).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        private DiagEvent? _ev;
        public DiagEvent Event => _ev ??= JsonSerializer.Deserialize<DiagEvent>(Json, DiagJson.Compact)!;
    }

    private static Rec? Parse(string? line)
    {
        if (line is null) return null;
        var p = line.Split('\t', 5);
        return p.Length == 5 ? new Rec(long.Parse(p[0], CultureInfo.InvariantCulture), p[1], p[2], int.Parse(p[3], CultureInfo.InvariantCulture), p[4]) : null;
    }

    /// <summary>K-way merge of the sorted temp files into one or more ZIP entries (a new entry whenever <paramref name="entryName"/> changes). ZIP entries are written one at a time.</summary>
    private async Task<List<(string, long)>> MergeAsync(ZipArchive zip, List<TempFile> temps, Func<Rec, bool> filter, Func<Rec, string> entryName, Action<Rec, StreamWriter> write,
        Budget bud, CancellationToken ct, string? header = null, bool orderByEntry = false)
    {
        var made = new List<(string, long)>();
        var readers = temps.Select(t => new StreamReader(t.Path, Encoding.UTF8)).ToList();
        var heads = new Rec?[readers.Count];
        try
        {
            for (var k = 0; k < readers.Count; k++) heads[k] = Parse(await readers[k].ReadLineAsync(ct));
            if (orderByEntry) return await MergeByEntryAsync(zip, temps, filter, entryName, write, bud, ct);
            StreamWriter? w = null; string? cur = null; long rows = 0;
            try
            {
                while (true)
                {
                    var best = -1;
                    for (var k = 0; k < heads.Length; k++) if (heads[k] is not null && (best < 0 || heads[k]!.Ticks < heads[best]!.Ticks)) best = k;
                    if (best < 0) break;
                    var r = heads[best]!; heads[best] = Parse(await readers[best].ReadLineAsync(ct));
                    if (!filter(r)) continue;
                    if (bud.Left <= 0) { bud.Hit = true; break; }
                    var name = entryName(r);
                    if (name != cur)
                    {
                        if (w is not null) { await w.FlushAsync(ct); await w.DisposeAsync(); made.Add((cur!, rows)); }
                        w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); cur = name; rows = 0;
                        if (header is not null) w.WriteLine(header);
                    }
                    write(r, w!); rows++; bud.Left -= r.Json.Length + 1;
                }
            }
            finally { if (w is not null) { await w.FlushAsync(ct); await w.DisposeAsync(); made.Add((cur!, rows)); } }
        }
        finally { foreach (var r in readers) r.Dispose(); }
        return made;
    }

    /// <summary>Module/day files: one pass per distinct entry name so only one ZIP entry is ever open (the time order inside each file is kept).</summary>
    private async Task<List<(string, long)>> MergeByEntryAsync(ZipArchive zip, List<TempFile> temps, Func<Rec, bool> filter, Func<Rec, string> entryName, Action<Rec, StreamWriter> write,
        Budget bud, CancellationToken ct)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in temps)
            foreach (var line in File.ReadLines(t.Path)) { var r = Parse(line); if (r is not null && filter(r)) names.Add(entryName(r)); }
        var made = new List<(string, long)>();
        foreach (var name in names)
        {
            if (bud.Left <= 0) { bud.Hit = true; break; }
            long rows = 0;
            await using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            // within one entry the rows of all sources are merged by time
            var readers = temps.Select(t => new StreamReader(t.Path, Encoding.UTF8)).ToList();
            try
            {
                var heads = new Rec?[readers.Count];
                for (var k = 0; k < readers.Count; k++) heads[k] = NextMatch(readers[k], r => entryName(r) == name && filter(r));
                while (true)
                {
                    var best = -1;
                    for (var k = 0; k < heads.Length; k++) if (heads[k] is not null && (best < 0 || heads[k]!.Ticks < heads[best]!.Ticks)) best = k;
                    if (best < 0) break;
                    var r = heads[best]!; heads[best] = NextMatch(readers[best], x => entryName(x) == name && filter(x));
                    write(r, w); rows++; bud.Left -= r.Json.Length + 1;
                }
            }
            finally { foreach (var r in readers) r.Dispose(); }
            made.Add((name, rows));
        }
        return made;
    }

    private static Rec? NextMatch(StreamReader rd, Func<Rec, bool> pred)
    {
        while (rd.ReadLine() is { } line) { var r = Parse(line); if (r is not null && pred(r)) return r; }
        return null;
    }

    // ------------------------------------------------------------------------------------------------ text/CSV renderings
    private const string TimelineHeader = "TimestampUtc,Level,Module,EventName,Source,CorrelationId,TaskId,AgvId,PlcId,StationId,RackId,Actor,Operation,Result,DurationMs,ErrorCode,Related,Message";

    private static string Csv(string? v) { if (v is null) return ""; return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v; }
    private static string Flat(string? s) => (s ?? "").Replace("\r", " ").Replace("\n", " ");

    private static string TimelineRow(DiagEvent e) => string.Join(',', new[]
    {
        e.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), e.Level, e.Module, e.EventName, e.Source, e.CorrelationId, e.TaskId, e.AgvId, e.PlcId, e.StationId, e.RackId, e.Actor,
        e.Operation, e.Result, e.DurationMs?.ToString(CultureInfo.InvariantCulture), e.ErrorCode, e.Related ? "true" : "", Flat(e.Message),
    }.Select(Csv));

    private static string ModuleLine(DiagEvent e)
    {
        var sb = new StringBuilder();
        sb.Append(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append(" [").Append(e.Level).Append("] ").Append(e.EventName);
        void Id(string n, string? v) { if (v is not null) sb.Append(' ').Append(n).Append('=').Append(v); }
        Id("corr", e.CorrelationId); Id("task", e.TaskId); Id("agv", e.AgvId); Id("plc", e.PlcId); Id("station", e.StationId); Id("rack", e.RackId); Id("actor", e.Actor);
        Id("op", e.Operation); Id("result", e.Result); Id("ms", e.DurationMs?.ToString(CultureInfo.InvariantCulture)); Id("code", e.ErrorCode);
        if (e.Related) sb.Append(" (related)");
        sb.Append(": ").Append(Flat(e.Message));
        if (e.ExceptionType is not null) sb.Append(" | ").Append(e.ExceptionType).Append(": ").Append(Flat(e.ExceptionMessage));
        if (!string.IsNullOrWhiteSpace(e.Context)) sb.Append(" | ").Append(Flat(e.Context));
        return sb.ToString();
    }

    private static string ErrorSummaryCsv(Acc a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Module,CodeOrEvent,HighestLevel,Count,FirstUtc,LastUtc,SampleMessage");
        foreach (var (k, g) in a.ErrorGroups.OrderByDescending(x => DiagLevels.Rank(x.Value.Level)).ThenByDescending(x => x.Value.Count))
            sb.AppendLine(string.Join(',', new[] { k.Module, k.Key, g.Level, g.Count.ToString(CultureInfo.InvariantCulture), g.First.ToString("O"), g.Last.ToString("O"), Flat(g.Sample) }.Select(Csv)));
        return sb.ToString();
    }

    private static string SanitizeText(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n')) sb.Append(LogSanitizer.Redact(line.TrimEnd('\r'), 20000) ?? "").Append('\n');
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------ snapshots
    private string SystemInfo(DateTimeOffset now, string requestedBy)
    {
        var p = System.Diagnostics.Process.GetCurrentProcess(); var tz = TimeZoneInfo.Local;
        var sb = new StringBuilder();
        sb.AppendLine("WCS SYSTEM DIAGNOSTIC PACKAGE - system information");
        sb.AppendLine($"Generated (UTC): {now:O}");
        sb.AppendLine($"Server local time: {DateTimeOffset.Now:O}");
        sb.AppendLine($"Server time zone: {tz.Id} ({tz.DisplayName}); UTC offset {tz.GetUtcOffset(DateTimeOffset.Now)}. All timestamps in this package are UTC unless stated.");
        sb.AppendLine($"Requested by: {requestedBy}");
        sb.AppendLine($"WCS build label (WCS_BUILD, the deployed git commit): {Environment.GetEnvironmentVariable("WCS_BUILD") ?? "(not set)"}");
        sb.AppendLine($"Assembly version: {typeof(SystemDiagnosticExporter).Assembly.GetName().Version}");
        sb.AppendLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(not set)"}");
        sb.AppendLine($"Machine name: {Environment.MachineName}; OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Process started (UTC): {p.StartTime.ToUniversalTime():O}; uptime {(now - p.StartTime.ToUniversalTime()):d\\.hh\\:mm\\:ss}; processors {Environment.ProcessorCount}; working set {p.WorkingSet64 / 1024 / 1024} MB");
        sb.AppendLine($"PLC mode: {config["Plc:Mode"] ?? "Simulator"} (live connection allowed: {config.GetValue("Plc:AllowLiveConnection", false)}); RCS/AGV integration mode: {config["Integration:Mode"] ?? "Simulator"} (no real RCS/AGV adapter exists yet).");
        sb.AppendLine("Docker container status and restart counts are NOT included (the backend cannot see them).");
        return sb.ToString();
    }

    private async Task<Dictionary<string, object?>> HealthAsync(CancellationToken ct)
    {
        var d = new Dictionary<string, object?>();
        try { d["plcRuntime"] = runtime.Info(); d["plcWorkers"] = runtime.Workers(true).Select(w => new { w.PlcId, w.State, w.Mode, w.Reason, w.ConsecutiveFailures, w.Reconnects, w.Cycles, w.LastPollAt, w.LastSuccessAt, w.LastError, w.TotalPoints, w.ConfiguredPoints, w.GoodPoints }).ToList(); }
        catch (Exception ex) { d["plcRuntime"] = new { error = ex.GetType().Name }; }
        try { foreach (var kv in await health.SnapshotAsync(ct)) d[kv.Key] = kv.Value; }
        catch (Exception ex) when (ex is not OperationCanceledException) { d["database"] = new { reachable = false, error = ex.GetType().Name }; }
        d["capturedUtc"] = clock.UtcNow;
        return d;
    }

    private Dictionary<string, object?> ConfigSnapshot()
    {
        var d = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        void Walk(IConfigurationSection s)
        {
            var kids = s.GetChildren().ToList();
            if (kids.Count == 0) { d[s.Path] = SecretKey.IsMatch(s.Key) ? LogSanitizer.Mask : LogSanitizer.Redact(s.Value, 500); return; }
            foreach (var k in kids) Walk(k);
        }
        foreach (var name in ConfigSections) Walk(config.GetSection(name));
        return new Dictionary<string, object?>
        {
            ["note"] = "Only the WCS sections listed are included; values whose key looks secret (password, token, key, connection string, cookie...) are masked as ***; connection strings and environment variables are not included.",
            ["sections"] = ConfigSections, ["values"] = d,
        };
    }

    // ------------------------------------------------------------------------------------------------ summary / manifest
    private object Manifest(Guid id, SystemExportRequest req, DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now, string by, Acc a, List<SourceReport> sources, List<string> notes, string completeness, List<(string Name, long Rows)> files)
    {
        var tz = TimeZoneInfo.Local;
        return new
        {
            schemaVersion = 1, packageId = id, kind = "WCS_SYSTEM_DIAGNOSTIC", generatedUtc = now, timeZone = new { id = tz.Id, utcOffset = tz.GetUtcOffset(now).ToString() }, requestedBy = by,
            applicationVersion = Environment.GetEnvironmentVariable("WCS_BUILD") ?? typeof(SystemDiagnosticExporter).Assembly.GetName().Version?.ToString(),
            completeness,
            request = new { mode = req.Mode, from = req.From, to = req.To, scope = req.Scope },
            effectiveRange = new { requestedFromUtc = from, requestedToUtc = to, firstEventUtc = a.First, lastEventUtc = a.Last, note = from is null ? "Mode 'all': from the oldest row each source still stores; see sources[].oldestStored and retention." : null },
            totals = new { events = a.Total, errors = a.Errors, warnings = a.Warnings, filesWritten = files.Count },
            byModule = a.ModuleLevel.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => new { information = k.Value[0], warning = k.Value[1], error = k.Value[2], critical = k.Value[3] }),
            sources = sources.Select(s => new { s.Name, s.Modules, s.Status, s.RowsInRange, s.RowsExported, s.RowsOmittedOldest, oldestStored = s.OldestStored, newestInRange = s.NewestInRange, s.Retention, s.Error, s.Note }),
            files = files.Select(f => new { name = f.Name, rows = f.Rows > 0 ? f.Rows : (long?)null }),
            notes, dataOrigin = new { plcMode = config["Plc:Mode"] ?? "Simulator", rcsAdapter = "Simulator (no real RCS/AGV adapter exists)", simulatedDataPossible = (config["Plc:Mode"] ?? "Simulator") == "Simulator" },
            sanitization = "Free text and JSON context pass through LogSanitizer; no passwords, tokens, cookies, private keys or connection strings are written.",
            limitations = Limitations(),
        };
    }

    private static string[] Limitations() =>
    [
        "RCS/AGV: only a simulator exists; real request/response, RCS error codes, battery and position are not available (they are recorded only where the simulator provides them).",
        "CorrelationId links events created after CR-021 batch B; older rows can only be linked through TaskId / OrderId / TransactionId where the source stores them.",
        "PLC MC exchange history keeps only the newest 300 exchanges per PLC and samples successful reads; it cannot give a complete frame history.",
        "Each source is limited by its own retention (see sources[].retention); 'all' means everything still stored, not everything that ever happened.",
        "Information-level events that were never stored (before CR-021 batch C) cannot be recovered.",
    ];

    private string Summary(SystemExportRequest req, DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now, string by, Acc a, List<SourceReport> sources, List<string> notes, string completeness, List<(string Name, long Rows)> files)
    {
        var tz = TimeZoneInfo.Local; var sb = new StringBuilder();
        sb.AppendLine("WCS SYSTEM DIAGNOSTIC PACKAGE - SUMMARY");
        sb.AppendLine($"Generated (UTC): {now:O} (server time zone {tz.Id}, UTC offset {tz.GetUtcOffset(now)}); requested by {by}");
        sb.AppendLine($"Completeness: {completeness.ToUpperInvariant()}" + (completeness == "Complete" ? " (every requested source was read in full within the limits)" : " - SEE THE PROBLEMS SECTION BELOW: some data is missing or cut"));
        sb.AppendLine($"Mode: {req.Mode}; requested range (UTC): {(from is null ? "(oldest stored)" : from.Value.ToString("O"))} .. {to:O}");
        sb.AppendLine($"Actual events: {a.Total}" + (a.First is null ? "" : $" between {a.First:O} and {a.Last:O}") + $"; errors {a.Errors}; warnings {a.Warnings}");
        sb.AppendLine("Scope: " + (req.Scope.IsWholeSystem ? "whole system" : $"modules=[{string.Join(",", req.Scope.Modules)}] minLevel={req.Scope.MinLevel ?? "-"} plc={req.Scope.PlcId ?? "-"} device={req.Scope.Device ?? "-"} correlationId={req.Scope.CorrelationId ?? "-"} taskId={req.Scope.TaskId ?? "-"}"));
        sb.AppendLine();
        sb.AppendLine("== Problems with this package (read first) ==");
        var problems = sources.Where(s => s.Status is "Failed" or "Partial").ToList();
        if (problems.Count == 0 && notes.Count == 0) sb.AppendLine("None: every requested source was read completely.");
        foreach (var s in problems) sb.AppendLine($"- {s.Name}: {s.Status}" + (s.Error is null ? "" : $" ({s.Error})") + (s.Note is null ? "" : " - " + s.Note));
        foreach (var n in notes) sb.AppendLine("- " + n);
        sb.AppendLine();
        sb.AppendLine("== Sources ==");
        foreach (var s in sources) sb.AppendLine($"- {s.Name} [{string.Join("/", s.Modules)}] {s.Status}: exported {s.RowsExported} of {s.RowsInRange} in range; oldest stored {(s.OldestStored?.ToString("O") ?? "n/a")}; retention: {s.Retention}");
        sb.AppendLine();
        sb.AppendLine("== Events per module (information / warning / error / critical) ==");
        foreach (var (m, v) in a.ModuleLevel.OrderBy(k => k.Key)) sb.AppendLine($"- {m}: {v[0]} / {v[1]} / {v[2]} / {v[3]}");
        sb.AppendLine();
        sb.AppendLine("== Most frequent problems (warning and above) ==");
        foreach (var (k, g) in a.ErrorGroups.OrderByDescending(x => DiagLevels.Rank(x.Value.Level)).ThenByDescending(x => x.Value.Count).Take(15))
            sb.AppendLine($"- [{g.Level}] {k.Module} {k.Key} x{g.Count} ({g.First:O} .. {g.Last:O}): {Flat(g.Sample)}");
        if (a.ErrorGroups.Count == 0) sb.AppendLine("- none in the selected range");
        sb.AppendLine();
        sb.AppendLine("== Most recent errors ==");
        foreach (var e in a.RecentErrors.Reverse().Take(10)) sb.AppendLine($"- {e.TimestampUtc:O} {e.Module} {e.EventName} corr={e.CorrelationId ?? "-"} task={e.TaskId ?? "-"}: {Flat(e.Message ?? e.ExceptionMessage)}");
        if (a.RecentErrors.Count == 0) sb.AppendLine("- none");
        sb.AppendLine();
        sb.AppendLine("== Tasks with warnings or errors ==");
        foreach (var (t, n) in a.TaskIssues.OrderByDescending(x => x.Value).Take(15)) sb.AppendLine($"- task {t}: {n} warning/error event(s) - filter Events/ or Timeline/ by taskId");
        if (a.TaskIssues.Count == 0) sb.AppendLine("- none");
        sb.AppendLine();
        sb.AppendLine("== How to read the package ==");
        sb.AppendLine("Manifest.json: what was requested, what each source delivered, limits. Events/events_<day>.jsonl: every normalised event in time order (one JSON per line). Errors/: warnings are in Timeline, errors with stack traces here.");
        sb.AppendLine("Timeline/timeline.csv: warnings, errors and every task/routing/inventory/audit event. Modules/<Module>/: readable per-module logs. PLC/: the PLC diagnostic export in its own format. Audit/: who did what. Health_Snapshot.json and Configuration/config.json: state and (masked) settings at export time.");
        sb.AppendLine("To follow one transaction: search Events/ for its corr (CorrelationId) or task (TaskId) value; rows from other modules with the same value belong to it. A null id means the source does not record it, not that it is unrelated.");
        sb.AppendLine();
        sb.AppendLine("== Known limitations ==");
        foreach (var l in Limitations()) sb.AppendLine("- " + l);
        return sb.ToString();
    }
}
