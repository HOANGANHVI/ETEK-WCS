using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Diagnostics;
using WCS.Domain.Logging;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-021 batch A: the whole-system diagnostic export (range modes, scopes, per-source failure, secrets, manifest/summary honesty, background job API, cleanup, PLC folder unchanged).</summary>
[Collection("Database")]
public class SystemDiagnosticExportTests(DatabaseFixture fixture)
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "wcs-diag-test-" + Guid.NewGuid().ToString("N"));

    private static SystemLogEntry Sys(DateTimeOffset at, string level, string source, string code, string message, string category = "SYSTEM", string? corr = null, string? details = null, string? exception = null, string? device = null) =>
        new(at, category, level, source, device, code, message, exception, corr, details, "host1");

    private static async Task<(string Path, ExportOutcome Outcome)> Run(Harness h, SystemExportRequest req, IEnumerable<IDiagnosticEventSource>? extra = null)
    {
        await using var scope = h.Services.CreateAsyncScope();
        var ex = scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>();
        var dir = TempDir(); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pkg.zip");
        var outcome = await ex.ExportAsync(Guid.NewGuid(), req, "admin1", path, (_, _) => { }, CancellationToken.None);
        return (path, outcome);
    }

    private static string Text(string zip, string entry)
    {
        using var z = ZipFile.OpenRead(zip);
        var e = z.GetEntry(entry) ?? throw new InvalidOperationException("missing entry " + entry + "; have: " + string.Join(", ", z.Entries.Select(x => x.FullName)));
        using var r = new StreamReader(e.Open(), Encoding.UTF8); return r.ReadToEnd();
    }

    private static string[] Names(string zip) { using var z = ZipFile.OpenRead(zip); return z.Entries.Select(e => e.FullName).ToArray(); }

    private static string AllText(string zip)
    {
        using var z = ZipFile.OpenRead(zip); var sb = new StringBuilder();
        foreach (var e in z.Entries) { using var r = new StreamReader(e.Open(), Encoding.UTF8); sb.AppendLine(r.ReadToEnd()); }
        return sb.ToString();
    }

    private static IEnumerable<JsonElement> Events(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        foreach (var e in z.Entries.Where(x => x.FullName.StartsWith("Events/", StringComparison.Ordinal)))
        {
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            while (r.ReadLine() is { Length: > 0 } line) yield return JsonDocument.Parse(line).RootElement.Clone();
        }
    }

    private static JsonElement Manifest(string zip) => JsonDocument.Parse(Text(zip, "Manifest.json")).RootElement.Clone();

    private static SystemExportRequest Req(string mode, DiagScope? scope = null, DateTimeOffset? from = null, DateTimeOffset? to = null) => new(mode, from, to, scope ?? new DiagScope());

    private static async Task SeedAsync(Harness h, DateTimeOffset now)
    {
        await h.Db(async db =>
        {
            db.SystemLog.Add(Sys(now.AddMinutes(-30), "Error", "WCS.Infrastructure.Persistence.EfUnitOfWork", "DB_TX_FAILED", "transaction failed", "DATABASE", corr: "corr-1"));
            db.SystemLog.Add(Sys(now.AddHours(-5), "Warning", "WCS.Application.Services.TaskOrchestrator", "RCS_TIMEOUT", "RCS timed out", "RCS_AGV", corr: "corr-2", details: "{\"taskId\":\"task-7\",\"agv\":\"AGV1\"}"));
            db.SystemLog.Add(Sys(now.AddDays(-3), "Warning", "WCS.Api.Api.ApiPlumbing", "OLD_EVENT", "an old event"));
            db.PlcCommLog.Add(new PlcCommLogEntry(now.AddMinutes(-20), "PLC-01", "A1", PlcLogLevel.Error, "READ_FAILED", "end code 0x4031").WithDiag("END_CODE_ERROR", "MC"));
            db.PlcCommLog.Add(new PlcCommLogEntry(now.AddMinutes(-10), "PLC-02", "B1", PlcLogLevel.Info, "TCP_CONNECTED", "connected").WithDiag(null, "TCP"));
            db.AuditLog.Add(new AuditEntry(now.AddMinutes(-15), "admin1", "Admin", "AUTH_LOGIN", "User", "u1", AuditResult.Failed, "bad password", null, null, "10.0.0.5"));
            await db.SaveChangesAsync(); return 0;
        });
    }

    // ------------------------------------------------------------------------------------------------ range modes
    [Fact]
    public async Task Range_modes_1h_24h_all_and_custom_select_exactly_the_rows_in_the_range_and_the_manifest_states_the_range()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var now = DateTimeOffset.UtcNow; await SeedAsync(h, now);

        int Count(string path) => Events(path).Count();
        var (p1, o1) = await Run(h, Req("1h"));
        Assert.Equal(new[] { "DB_TX_FAILED", "READ_FAILED", "AUTH_LOGIN", "TCP_CONNECTED" }.OrderBy(x => x), Events(p1).Select(e => e.GetProperty("eventName").GetString()!).OrderBy(x => x));
        var (p24, _) = await Run(h, Req("24h"));
        Assert.Equal(5, Count(p24));                                                                  // + the 5-hour-old RCS event
        var (pAll, _) = await Run(h, Req("all"));
        Assert.Equal(6, Count(pAll));                                                                 // + the 3-day-old event: "all" = everything still stored, no 31-day cap
        var (pc, _) = await Run(h, Req("custom", from: now.AddHours(-6), to: now.AddHours(-1)));
        Assert.Equal(["RCS_TIMEOUT"], Events(pc).Select(e => e.GetProperty("eventName").GetString()!).ToArray());

        var m = Manifest(pAll);
        Assert.True(m.GetProperty("effectiveRange").GetProperty("requestedFromUtc").ValueKind == JsonValueKind.Null);          // "all": no lower bound, said so
        Assert.Contains("oldest", m.GetProperty("effectiveRange").GetProperty("note").GetString());
        Assert.Equal("all", m.GetProperty("request").GetProperty("mode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(m.GetProperty("timeZone").GetProperty("id").GetString()));                      // the time zone is stated
        Assert.Equal(6, m.GetProperty("totals").GetProperty("events").GetInt32());
        await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => Run(h, Req("custom", from: now, to: now.AddHours(-1))));
        await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => Run(h, Req("week")));
    }

    // ------------------------------------------------------------------------------------------------ scope
    [Fact]
    public async Task Whole_system_contains_every_module_and_a_module_scope_keeps_only_that_module_plus_related_rows_of_the_others()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var now = DateTimeOffset.UtcNow; await SeedAsync(h, now);
        // an Information row of another module that shares the CorrelationId of the selected Database error
        await h.Db(async db => { db.SystemLog.Add(Sys(now.AddMinutes(-29), "Information", "WCS.Application.Services.TaskScheduler", "TASK_DISPATCH", "dispatching", corr: "corr-1")); await db.SaveChangesAsync(); return 0; });

        var (all, _) = await Run(h, Req("24h"));
        var modules = Events(all).Select(e => e.GetProperty("module").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "Database", "Plc", "Auth", "Rcs", "Task" }, modules);

        var (db1, outcome) = await Run(h, Req("24h", new DiagScope { Modules = [DiagModules.Database] }));
        var evs = Events(db1).ToList();
        Assert.Contains(evs, e => e.GetProperty("eventName").GetString() == "DB_TX_FAILED" && !e.GetProperty("related").GetBoolean());
        var related = evs.Single(e => e.GetProperty("eventName").GetString() == "TASK_DISPATCH");
        Assert.True(related.GetProperty("related").GetBoolean());                                      // pulled in through corr-1, and marked as such
        Assert.DoesNotContain(evs, e => e.GetProperty("module").GetString() == "Plc");                  // PLC rows have no shared id: not linked without a basis
        Assert.Contains(outcome.Sources, s => s.Name == "plc_comm_log" && s.Status == "NotRequested");  // and the package says it did not read that source

        var (byCorr, _) = await Run(h, Req("all", new DiagScope { CorrelationId = "corr-1" }));
        Assert.Equal(["DB_TX_FAILED", "TASK_DISPATCH"], Events(byCorr).Select(e => e.GetProperty("eventName").GetString()!).OrderBy(x => x).ToArray());
        var (errs, _) = await Run(h, Req("all", new DiagScope { MinLevel = "Error" }));
        Assert.All(Events(errs).Where(e => !e.GetProperty("related").GetBoolean()), e => Assert.Contains(e.GetProperty("level").GetString(), new[] { "Error", "Critical" }));
        var (plc1, _) = await Run(h, Req("all", new DiagScope { PlcId = "PLC-01" }));
        Assert.Equal(["READ_FAILED"], Events(plc1).Select(e => e.GetProperty("eventName").GetString()!).ToArray());
    }

    // ------------------------------------------------------------------------------------------------ package content
    [Fact]
    public async Task The_package_has_every_section_with_a_truthful_manifest_and_summary_and_keeps_the_PLC_folder()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var now = DateTimeOffset.UtcNow; await SeedAsync(h, now);
        await h.Db(async db => { db.SystemLog.Add(Sys(now.AddMinutes(-5), "Error", "WCS.Application.Services.TaskOrchestrator", "CANCEL_RETRY_LIMIT", "cancel not confirmed", "RCS_AGV", exception: "System.InvalidOperationException: boom\n   at A.B()\n   at C.D()")); await db.SaveChangesAsync(); return 0; });
        var (path, outcome) = await Run(h, Req("24h"));
        var names = Names(path);
        foreach (var must in new[] { "Manifest.json", "Summary.txt", "System_Info.txt", "Health_Snapshot.json", "Configuration/config.json", "Errors/errors.jsonl", "Errors/error_summary.csv", "Timeline/timeline.csv", "Audit/audit.jsonl" })
            Assert.Contains(must, names);
        Assert.Contains(names, n => n.StartsWith("Events/events_", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("Modules/Rcs/", StringComparison.Ordinal));
        Assert.Contains("PLC/PLC_Connections.csv", names); Assert.Contains("PLC/PLC_Runtime.log", names); Assert.Contains("PLC/PLC_Error_Summary.csv", names);   // the PLC export format is unchanged

        // exception and stack trace are in the structured error file
        var err = Text(path, "Errors/errors.jsonl").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).Single(e => e.GetProperty("eventName").GetString() == "CANCEL_RETRY_LIMIT");
        Assert.Equal("System.InvalidOperationException", err.GetProperty("exceptionType").GetString()); Assert.Equal("boom", err.GetProperty("exceptionMessage").GetString());
        Assert.Contains("at A.B()", err.GetProperty("stackTrace").GetString());

        // the summary and manifest tell what was exported, per source, and the limits
        var m = Manifest(path);
        Assert.Equal("Complete", m.GetProperty("completeness").GetString());
        var srcs = m.GetProperty("sources").EnumerateArray().ToList();
        foreach (var s in new[] { "system_log", "plc_comm_log", "audit_log", "task_history", "routing_events", "inventory_transactions" }) Assert.Contains(srcs, x => x.GetProperty("name").GetString() == s);
        Assert.Equal(1, srcs.Single(x => x.GetProperty("name").GetString() == "audit_log").GetProperty("rowsExported").GetInt32());
        Assert.NotEmpty(m.GetProperty("limitations").EnumerateArray());
        Assert.Contains(m.GetProperty("files").EnumerateArray(), f => f.GetProperty("name").GetString() == "Summary.txt");
        var summary = Text(path, "Summary.txt");
        Assert.Contains("Completeness: COMPLETE", summary); Assert.Contains("CANCEL_RETRY_LIMIT", summary); Assert.Contains("Known limitations", summary); Assert.Contains("no real RCS/AGV adapter", Text(path, "System_Info.txt"));
        Assert.Equal(outcome.Events, m.GetProperty("totals").GetProperty("events").GetInt64());
    }

    // ------------------------------------------------------------------------------------------------ failure of one source
    private sealed class FailingSource : IDiagnosticEventSource
    {
        public string Name => "broken_source"; public IReadOnlyList<string> Modules => [DiagModules.Config]; public string Retention => "n/a";
        public Task<SourceStats> StatsAsync(DiagQuery q, CancellationToken ct) => throw new TimeoutException("simulated source failure");
        public Task<DateTimeOffset?> TailStartAsync(DiagQuery q, long keep, CancellationToken ct) => Task.FromResult<DateTimeOffset?>(null);
        public IAsyncEnumerable<DiagEvent> ReadAsync(DiagQuery q, DateTimeOffset? f, int p, CancellationToken ct) => throw new TimeoutException("simulated source failure");
    }

    [Fact]
    public async Task A_source_that_cannot_be_read_is_reported_as_Failed_and_never_breaks_the_package_or_claims_completeness()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var now = DateTimeOffset.UtcNow; await SeedAsync(h, now);
        await using var scope = h.Services.CreateAsyncScope();
        var real = scope.ServiceProvider.GetServices<IDiagnosticEventSource>().ToList();
        var ex = new SystemDiagnosticExporter(real.Append(new FailingSource()), scope.ServiceProvider.GetRequiredService<IDiagnosticHealthSource>(), scope.ServiceProvider.GetRequiredService<DiagnosticExportService>(),
            scope.ServiceProvider.GetRequiredService<WCS.Application.Plc.IPlcRuntime>(), scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SystemDiagnosticOptions>>(), scope.ServiceProvider.GetRequiredService<WCS.Application.Abstractions.IClock>());
        var dir = TempDir(); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "pkg.zip");
        var outcome = await ex.ExportAsync(Guid.NewGuid(), Req("24h"), "admin1", path, (_, _) => { }, CancellationToken.None);

        Assert.Equal("Partial", outcome.Completeness);
        var bad = Assert.Single(outcome.Sources, s => s.Name == "broken_source");
        Assert.Equal(("Failed", "TimeoutException"), (bad.Status, bad.Error));
        Assert.Equal(5, Events(path).Count());                                                           // everything else is still there
        var m = Manifest(path);
        Assert.Equal("Partial", m.GetProperty("completeness").GetString());
        Assert.Contains(m.GetProperty("sources").EnumerateArray(), s => s.GetProperty("name").GetString() == "broken_source" && s.GetProperty("status").GetString() == "Failed");
        var summary = Text(path, "Summary.txt");
        Assert.Contains("Completeness: PARTIAL", summary); Assert.Contains("broken_source: Failed", summary); Assert.DoesNotContain("COMPLETE (", summary);
    }

    // ------------------------------------------------------------------------------------------------ secrets and limits
    [Fact]
    public async Task No_secret_reaches_the_package_whatever_the_source_and_the_configuration_snapshot_masks_secret_keys()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var now = DateTimeOffset.UtcNow;
        await h.Db(async db =>
        {
            db.SystemLog.Add(Sys(now.AddMinutes(-3), "Error", "WCS.Infrastructure.X", "DB_FAIL", "Host=db;Password=Sup3rSecret;Database=wcs", exception: "Npgsql.NpgsqlException: connection string Password=Sup3rSecret failed\n   at X()", details: "{\"token\":\"abc123def456\",\"note\":\"Authorization: Bearer abcdef123456\"}"));
            db.AuditLog.Add(new AuditEntry(now.AddMinutes(-2), "admin1", "Admin", "USER_PASSWORD_RESET", "User", "u9", AuditResult.Success, "done", "{\"password\":\"Sup3rSecret\"}", "{\"apiKey\":\"K-9999-secret\"}", "10.0.0.1"));
            await db.SaveChangesAsync(); return 0;
        });
        var (path, _) = await Run(h, Req("24h"));
        var all = AllText(path);
        foreach (var secret in new[] { "Sup3rSecret", "abc123def456", "abcdef123456", "K-9999-secret" }) Assert.DoesNotContain(secret, all);
        Assert.Contains("***", all);
        var cfg = Text(path, "Configuration/config.json");
        Assert.DoesNotContain("ConnectionStrings", cfg); Assert.DoesNotContain("POSTGRES_PASSWORD", cfg);
    }

    [Fact]
    public async Task A_source_with_more_rows_than_the_limit_keeps_the_NEWEST_rows_and_says_how_many_were_left_out()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var now = DateTimeOffset.UtcNow;
        await h.Db(async db => { for (var i = 0; i < 30; i++) db.SystemLog.Add(Sys(now.AddMinutes(-60 + i), "Warning", "WCS.Application.X", $"E{i:D2}", "m")); await db.SaveChangesAsync(); return 0; });
        await using var scope = h.Services.CreateAsyncScope();
        var opts = new SystemDiagnosticOptions { MaxEventsPerSource = 10, PageSize = 4 };
        var ex = new SystemDiagnosticExporter(scope.ServiceProvider.GetServices<IDiagnosticEventSource>(), scope.ServiceProvider.GetRequiredService<IDiagnosticHealthSource>(), scope.ServiceProvider.GetRequiredService<DiagnosticExportService>(),
            scope.ServiceProvider.GetRequiredService<WCS.Application.Plc.IPlcRuntime>(), scope.ServiceProvider.GetRequiredService<IConfiguration>(), Microsoft.Extensions.Options.Options.Create(opts), scope.ServiceProvider.GetRequiredService<WCS.Application.Abstractions.IClock>());
        var dir = TempDir(); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "pkg.zip");
        var outcome = await ex.ExportAsync(Guid.NewGuid(), Req("24h", new DiagScope { Modules = [DiagModules.System] }), "admin1", path, (_, _) => { }, CancellationToken.None);
        var names = Events(path).Select(e => e.GetProperty("eventName").GetString()!).ToList();
        Assert.Equal(Enumerable.Range(20, 10).Select(i => $"E{i:D2}"), names);                              // newest 10, in time order, across page boundaries
        var src = outcome.Sources.Single(s => s.Name == "system_log");
        Assert.Equal(("Partial", 30L, 20L), (src.Status, src.RowsInRange, src.RowsOmittedOldest));
        Assert.Equal("Partial", outcome.Completeness);
        Assert.Contains("20 older rows", Text(path, "Summary.txt"));
    }

    // ------------------------------------------------------------------------------------------------ job API, authorization, audit, cleanup
    private async Task<(WebApplicationFactory<Program> F, HttpClient Admin, HttpClient Op)> BootApiAsync(string dir)
    {
        var f0 = new DbApiFactory(fixture);
        using (var scope = f0.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator)); await db.SaveChangesAsync();
        }
        await f0.DisposeAsync();
        var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostics:System:Directory"] = dir, ["Plc:RuntimeEnabled"] = "false" })));
        var admin = f.CreateClient().WithClientHeader(); Assert.Equal(200, await admin.LoginAsync("admin1"));
        var op = f.CreateClient().WithClientHeader(); Assert.Equal(200, await op.LoginAsync("op1"));
        return (f, admin, op);
    }

    [Fact]
    public async Task The_export_runs_as_a_background_job_only_Admins_can_start_or_download_it_and_both_steps_are_audited()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await SeedAsync(h, DateTimeOffset.UtcNow);
        var dir = TempDir();
        var (f, admin, op) = await BootApiAsync(dir);
        using var _f = f;
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Post, "/api/diagnostics/exports", new { mode = "24h" })).Status);
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, "/api/diagnostics/exports")).Status);
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Post, "/api/diagnostics/exports", new { mode = "week" })).Status);
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Post, "/api/diagnostics/exports", new { mode = "24h", modules = new[] { "NoSuchModule" } })).Status);

        var start = await admin.SendJsonAsync(HttpMethod.Post, "/api/diagnostics/exports", new { mode = "24h" });
        Assert.Equal(202, start.Status);
        var id = start.Root.GetProperty("data").GetProperty("id").GetGuid();
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Get, $"/api/diagnostics/exports/{Guid.NewGuid()}")).Status);
        JsonElement job = default; var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            job = (await admin.SendJsonAsync(HttpMethod.Get, $"/api/diagnostics/exports/{id}")).Root.GetProperty("data");
            if (job.GetProperty("state").GetString() is "Completed" or "Failed") break;
            await Task.Delay(100);
        }
        Assert.Equal("Completed", job.GetProperty("state").GetString());
        Assert.Equal(100, job.GetProperty("progress").GetInt32());
        Assert.DoesNotContain("filePath", job.GetRawText(), StringComparison.OrdinalIgnoreCase);       // the server path is never exposed
        Assert.True(job.GetProperty("sources").GetArrayLength() >= 6);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await op.GetAsync($"/api/diagnostics/exports/{id}/download")).StatusCode);
        var dl = await admin.GetAsync($"/api/diagnostics/exports/{id}/download");
        Assert.Equal(200, (int)dl.StatusCode); Assert.Equal("application/zip", dl.Content.Headers.ContentType?.MediaType); Assert.Equal("no-store", dl.Headers.CacheControl?.ToString());
        var bytes = await dl.Content.ReadAsByteArrayAsync();
        using (var z = new ZipArchive(new MemoryStream(bytes))) Assert.NotNull(z.GetEntry("Manifest.json"));
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Get, $"/api/diagnostics/exports/{Guid.NewGuid()}/download")).Status);   // an unknown job is 404, never a file

        var audit = await h.Db(db => db.AuditLog.AsNoTracking().Where(a => a.Action.StartsWith("DIAGNOSTIC_EXPORT")).ToListAsync());
        Assert.Contains(audit, a => a is { Action: "DIAGNOSTIC_EXPORT_SYSTEM", Actor: "admin1", Result: AuditResult.Success });
        Assert.Contains(audit, a => a is { Action: "DIAGNOSTIC_EXPORT_DOWNLOAD", Actor: "admin1" });
        Assert.Contains(audit, a => a is { Action: "DIAGNOSTIC_EXPORT_SYSTEM", Result: AuditResult.Failed });   // the rejected request is audited too
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Old_export_files_are_removed_by_the_cleanup_and_fresh_ones_are_kept()
    {
        var dir = TempDir(); Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "0123456789abcdef0123456789abcdef.zip"); var fresh = Path.Combine(dir, "fedcba9876543210fedcba9876543210.zip");
        File.WriteAllText(old, "x"); File.WriteAllText(fresh, "x"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-25));
        var oldWork = Path.Combine(dir, "work-aaaa"); Directory.CreateDirectory(oldWork); Directory.SetLastWriteTimeUtc(oldWork, DateTime.UtcNow.AddHours(-3));
        await using var h = await Harness.CreateAsync(fixture);
        var jobs = new DiagnosticExportJobs(h.Services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(new SystemDiagnosticOptions { Directory = dir, RetentionHours = 24 }),
            h.Services.GetRequiredService<WCS.Application.Abstractions.IClock>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<DiagnosticExportJobs>.Instance);
        Assert.Equal(1, jobs.Sweep());
        Assert.False(File.Exists(old)); Assert.True(File.Exists(fresh)); Assert.False(Directory.Exists(oldWork));
        Directory.Delete(dir, true);
    }
}
