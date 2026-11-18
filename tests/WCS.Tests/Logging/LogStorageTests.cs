using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Serilog.Parsing;
using WCS.Api.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Logging;
using WCS.Application.Plc;
using WCS.Domain.Logging;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Logging;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Runtime;
using WCS.Tests.Integration;
using WCS.Tests.Support;

namespace WCS.Tests.Logging;

/// <summary>CR-007 storage, query API, sink, deployment import, retention and database monitor against a real (throw-away) PostgreSQL database.</summary>
[Collection("Database")]
public class LogStorageTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddHours(-2);

    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
        await db.SaveChangesAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private async Task SeedAsync(Harness h)
    {
        await h.Db(async db =>
        {
            db.SystemLog.AddRange(
                new SystemLogEntry(T0.AddMinutes(1), LogCategories.System, LogLevels.Information, "WCS.Api", null, "STARTUP_SUMMARY", "WCS backend starting"),
                new SystemLogEntry(T0.AddMinutes(2), LogCategories.Database, LogLevels.Warning, "WCS.Runtime.DbHealth", "postgres", "DB_LOST", "Database connection lost"),
                new SystemLogEntry(T0.AddMinutes(3), LogCategories.System, LogLevels.Error, "WCS.Runtime.SchedulerHostedService", null, null, "Scheduler tick failed", "System.InvalidOperationException: boom\n   at X.Y()", "0HNCORR1"),
                new SystemLogEntry(T0.AddMinutes(4), LogCategories.RcsAgv, LogLevels.Warning, "WCS.Application.Services.TaskOrchestrator", "AGV1", "RCS_CALL_FAILED", "RCS create call failed for R-1"),
                new SystemLogEntry(T0.AddMinutes(5), LogCategories.System, LogLevels.Critical, "WCS.Api", null, "STARTUP_FATAL", "terminated unexpectedly"),
                new SystemLogEntry(T0.AddMinutes(6), LogCategories.Deployment, LogLevels.Error, "deploy", "deploy.ps1", "DEPLOY_FAIL", "[migrations] failed (exit code 1)", null, null, null, "WIN11-01", "run1-9"),
                new SystemLogEntry(T0.AddMinutes(7), LogCategories.System, LogLevels.Warning, "WCS.Api", null, "NOTE", "cache 50% full, path a_b"));
            db.PlcCommLog.AddRange(
                new PlcCommLogEntry(T0.AddMinutes(8), "PLC-01", null, PlcLogLevel.Warning, "STATE", "PLC-01: Faulted - Mat ket noi PLC.")
                    .WithDetails("{\"ip\":\"192.168.10.101\",\"port\":5002,\"previousState\":\"Connected\",\"newState\":\"Faulted\",\"kind\":\"CONNECTION\",\"reason\":\"Mat ket noi PLC.\"}"),
                new PlcCommLogEntry(T0.AddMinutes(9), "PLC-02", "B1", PlcLogLevel.Error, "WRITE_AMBIGUOUS", "B1/Request_Ack: khong nhan duoc xac nhan"),
                new PlcCommLogEntry(T0.AddMinutes(10), "PLC-01", null, PlcLogLevel.Info, "STATE", "PLC-01: Connected"));
            db.AuditLog.AddRange(
                new AuditEntry(T0.AddMinutes(11), "admin1", "Admin", "AUTH_LOGIN", "User", null, AuditResult.Success, null, null, null, "10.0.0.5"),
                new AuditEntry(T0.AddMinutes(12), "mallory", null, "AUTH_LOGIN", "User", null, AuditResult.Failed, "Sai mat khau", null, null, "10.0.0.9"),
                new AuditEntry(T0.AddMinutes(13), "op1", "Operator", "PLC_CONFIG", "PlcConnection", "PLC-01", AuditResult.Denied, null, "{\"port\":1}", "{\"port\":2}", "10.0.0.6"));
            await db.SaveChangesAsync();
            return 0;
        });
    }

    // ------------------------------------------------------------------------------------------------------------- schema
    [Fact]
    public async Task Migration_creates_system_log_the_details_column_and_the_unified_view_over_the_three_sources()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        await SeedAsync(h);
        var rows = await h.Db(db => db.LogRows.AsNoTracking().ToListAsync());
        Assert.Equal(7 + 3 + 3, rows.Count);
        Assert.Equal(new[] { "DATABASE", "DEPLOYMENT", "PLC", "RCS_AGV", "SECURITY_AUDIT", "SYSTEM" }, rows.Select(r => r.Category).Distinct().Order().ToArray());
        Assert.All(rows.Where(r => r.Category == "PLC"), r => Assert.StartsWith("plc:", r.EntryId));
        Assert.Equal("Error", rows.Single(r => r.Event == "WRITE_AMBIGUOUS").Level);
        Assert.Equal("Information", rows.Single(r => r.Event == "AUTH_LOGIN" && r.Device == "admin1").Level);
        Assert.Equal("Warning", rows.Single(r => r.Event == "AUTH_LOGIN" && r.Device == "mallory").Level);
        Assert.Equal("Warning", rows.Single(r => r.Event == "PLC_CONFIG").Level);                       // denied action
        // existing audit and plc data are not duplicated: three source tables, three kinds of ids
        Assert.Equal(13, rows.Select(r => r.EntryId).Distinct().Count());
    }

    // ------------------------------------------------------------------------------------------------------------- search, filters, paging
    [Fact]
    public async Task Search_level_and_text_filters_are_exact_and_literal()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        await SeedAsync(h);
        await h.RunAsync(async sp =>
        {
            var s = sp.GetRequiredService<ILogQueryService>();
            LogSearchQuery Q(string? category = null, string? level = null, string? min = null, string? device = null, string? source = null, string? ev = null, string? kw = null,
                DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int size = 50) => new(from, to, category, level, min, device, source, ev, kw, page, size);

            Assert.Equal(1, (await s.SearchAsync(Q(category: "DATABASE"))).Total);
            Assert.Equal(3, (await s.SearchAsync(Q(category: "PLC"))).Total);
            Assert.Equal(3, (await s.SearchAsync(Q(category: "SECURITY_AUDIT"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(category: "DEPLOYMENT"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(category: "RCS_AGV"))).Total);
            Assert.Equal(4, (await s.SearchAsync(Q(category: "SYSTEM"))).Total);
            Assert.Equal(13, (await s.SearchAsync(Q())).Total);

            // Error: scheduler + deploy + plc WRITE_AMBIGUOUS = 3; Critical = 1; Warning: db lost, rcs, note, plc faulted, failed login, denied = 6; Information the rest
            Assert.Equal(3, (await s.SearchAsync(Q(level: "Error"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(level: "Critical"))).Total);
            Assert.Equal(6, (await s.SearchAsync(Q(level: "Warning"))).Total);
            Assert.Equal(4, (await s.SearchAsync(Q(min: "Error"))).Total);
            Assert.Equal(13, (await s.SearchAsync(Q(min: "Information"))).Total);

            Assert.Equal(2, (await s.SearchAsync(Q(device: "PLC-01"))).Total);
            Assert.Equal(2, (await s.SearchAsync(Q(device: "plc-01"))).Total);                                                    // case-insensitive
            Assert.Equal(1, (await s.SearchAsync(Q(ev: "WRITE_AMB"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(source: "SchedulerHostedService"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(kw: "tick failed"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(kw: "50%"))).Total);                                                           // % is literal, not a wildcard
            Assert.Equal(1, (await s.SearchAsync(Q(kw: "a_b"))).Total);                                                           // _ is literal
            Assert.Equal(0, (await s.SearchAsync(Q(kw: "a%b"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(kw: "mallory"))).Total);                                                       // device column of the failed login

            // time window
            Assert.Equal(2, (await s.SearchAsync(Q(from: T0.AddMinutes(1), to: T0.AddMinutes(2)))).Total);
            Assert.Equal(0, (await s.SearchAsync(Q(from: T0.AddDays(1)))).Total);

            // paging: 13 entries, page size 5 -> 5 / 5 / 3, no overlap, stable order
            var p1 = await s.SearchAsync(Q(size: 5, page: 1)); var p2 = await s.SearchAsync(Q(size: 5, page: 2)); var p3 = await s.SearchAsync(Q(size: 5, page: 3)); var p4 = await s.SearchAsync(Q(size: 5, page: 4));
            Assert.Equal(new[] { 5, 5, 3, 0 }, new[] { p1.Items.Count, p2.Items.Count, p3.Items.Count, p4.Items.Count });
            Assert.All([p1, p2, p3], p => Assert.Equal(13, p.Total));
            Assert.Equal(13, p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.Id).Distinct().Count());
            Assert.Equal(LogQueryService.MaxPageSize, (await s.SearchAsync(Q(size: 100000))).PageSize);                          // page size is capped by the backend

            // validation
            await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => s.SearchAsync(Q(category: "NOPE")));
            await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => s.SearchAsync(Q(level: "Fatal")));
            await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => s.SearchAsync(Q(from: T0.AddDays(1), to: T0)));
        });
    }

    // ------------------------------------------------------------------------------------------------------------- stats
    [Fact]
    public async Task Stats_count_levels_and_groups_list_latest_errors_and_devices_with_problems()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        await SeedAsync(h);
        await h.RunAsync(async sp =>
        {
            var st = await sp.GetRequiredService<ILogQueryService>().StatsAsync(T0, DateTimeOffset.UtcNow);
            Assert.Equal(13, st.Total);
            Assert.Equal(3, st.ByLevel["Error"]); Assert.Equal(1, st.ByLevel["Critical"]); Assert.Equal(6, st.ByLevel["Warning"]); Assert.Equal(3, st.ByLevel["Information"]);
            Assert.Equal(new[] { "SYSTEM", "PLC", "DATABASE", "DEPLOYMENT", "RCS_AGV", "SECURITY_AUDIT" }.Order().ToArray(), st.ByCategory.Keys.Order().ToArray());
            Assert.Equal(13, st.ByCategory.Values.Sum());
            Assert.Equal(3, st.ByCategory["PLC"]); Assert.Equal(4, st.ByCategory["SYSTEM"]);
            Assert.Equal(4, st.LatestErrors.Count);                                                                               // Error + Critical, newest first
            Assert.Equal("WRITE_AMBIGUOUS", st.LatestErrors[0].Event);
            Assert.Contains(st.ProblemDevices, d => d.Device == "PLC-02" && d.Errors == 1);
            Assert.Contains(st.ProblemDevices, d => d.Device == "PLC-01" && d.Warnings == 1);
            Assert.Contains("PLC-01", st.Devices);
        });
    }

    // ------------------------------------------------------------------------------------------------------------- detail + masking
    [Fact]
    public async Task Detail_returns_exception_correlation_plc_context_and_audit_diff_and_masks_secrets()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        await SeedAsync(h);
        await h.Db(async db =>
        {   // bypass the sanitizer on purpose: the read side must still mask what is in the table
            db.SystemLog.Add(new SystemLogEntry(T0.AddMinutes(20), LogCategories.System, LogLevels.Error, "X", null, "LEAK", "connect failed: Password=S3cr3tValue;Host=db",
                "Npgsql.NpgsqlException: auth failed\n Authorization: Bearer abcdef123456 cookie=wcs.auth=XYZsecret", "0HNCORR2", "{\"token\":\"tok123456\"}"));
            await db.SaveChangesAsync(); return 0;
        });
        await h.RunAsync(async sp =>
        {
            var s = sp.GetRequiredService<ILogQueryService>();
            var rows = (await s.SearchAsync(new LogSearchQuery(null, null, null, null, null, null, null, null, null, 1, 100))).Items;

            var sys = await s.DetailAsync(rows.Single(r => r.Event == null && r.Message.Contains("tick failed")).Id);
            Assert.NotNull(sys); Assert.Contains("InvalidOperationException", sys!.Exception); Assert.Equal("0HNCORR1", sys.CorrelationId); Assert.Equal("system", sys.EntryKind);

            var plc = await s.DetailAsync(rows.Single(r => r.Event == "STATE" && r.Message.Contains("Faulted")).Id);
            Assert.Equal("192.168.10.101", plc!.Ip); Assert.Equal(5002, plc.Port); Assert.Equal("Connected", plc.PreviousState); Assert.Equal("Faulted", plc.NewState); Assert.Equal("CONNECTION", plc.FailureKind);
            Assert.Equal("PLC", plc.Category); Assert.Equal("PLC-01", plc.Device);

            var aud = await s.DetailAsync(rows.Single(r => r.Event == "PLC_CONFIG").Id);
            Assert.Equal("{\"port\":1}", aud!.BeforeJson); Assert.Equal("{\"port\":2}", aud.AfterJson); Assert.Equal("Denied", aud.Result); Assert.Equal("10.0.0.6", aud.Ip); Assert.Equal("op1", aud.Actor);

            var leak = await s.DetailAsync(rows.Single(r => r.Event == "LEAK").Id);
            var all = string.Join("|", leak!.Message, leak.Exception, leak.DetailsJson);
            foreach (var secret in new[] { "S3cr3tValue", "abcdef123456", "XYZsecret", "tok123456" }) Assert.DoesNotContain(secret, all);
            Assert.Contains(LogSanitizer.Mask, all);
            Assert.DoesNotContain("S3cr3tValue", rows.Single(r => r.Event == "LEAK").Message);                                    // the list masks too

            Assert.Null(await s.DetailAsync("sys:" + Guid.NewGuid())); Assert.Null(await s.DetailAsync("nonsense")); Assert.Null(await s.DetailAsync("zzz:" + Guid.NewGuid()));
        });
    }

    // ------------------------------------------------------------------------------------------------------------- API permissions
    [Fact]
    public async Task Log_api_is_admin_only_and_returns_the_standard_envelope_with_paging()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        await SeedAsync(h);
        var id = await h.Db(db => db.SystemLog.Where(x => x.EventCode == "DB_LOST").Select(x => x.Id).SingleAsync());
        string[] urls = ["/api/logs", "/api/logs/stats", $"/api/logs/sys:{id}", "/api/logs?category=PLC&pageSize=2&page=2"];

        var anon = f.CreateClient().WithClientHeader();
        foreach (var u in urls) Assert.Equal(401, (await anon.SendJsonAsync(HttpMethod.Get, u)).Status);
        var op = await As(f, "op1");
        foreach (var u in urls) Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Get, u)).Status);

        var admin = await As(f, "admin1");
        var list = await admin.SendJsonAsync(HttpMethod.Get, "/api/logs?category=PLC&pageSize=2&page=2");
        Assert.Equal(200, list.Status);
        var data = list.Root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt64()); Assert.Equal(1, data.GetProperty("items").GetArrayLength()); Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Get, "/api/logs/stats")).Status);
        var detail = await admin.SendJsonAsync(HttpMethod.Get, $"/api/logs/sys:{id}");
        Assert.Equal("DB_LOST", detail.Root.GetProperty("data").GetProperty("event").GetString());
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Get, $"/api/logs/sys:{Guid.NewGuid()}")).Status);
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Get, "/api/logs?category=NOPE")).Status);
        Assert.Equal(405, (await admin.SendJsonAsync(HttpMethod.Delete, "/api/logs")).Status);                                // read-only: nothing can delete or write log entries
        Assert.Equal(405, (await admin.SendJsonAsync(HttpMethod.Post, "/api/logs", new { })).Status);
    }

    // ------------------------------------------------------------------------------------------------------------- sink
    private static LogEvent Ev(LogEventLevel level, string template, Exception? ex = null, params (string, object)[] props) =>
        new(DateTimeOffset.Now, level, ex, new MessageTemplateParser().Parse(template), props.Select(p => new LogEventProperty(p.Item1, new ScalarValue(p.Item2))));   // local offset, like Serilog

    [Fact]
    public async Task Sink_stores_warnings_and_tagged_events_classifies_them_masks_secrets_and_skips_plc_and_its_own_driver_events()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        using var sink = new SystemLogSink(f.Services);
        sink.Emit(Ev(LogEventLevel.Information, "ordinary information"));                                                            // not stored
        sink.Emit(Ev(LogEventLevel.Information, "WCS backend starting. {EventCode}", null, ("EventCode", "STARTUP_SUMMARY"), ("SourceContext", "Program")));
        sink.Emit(Ev(LogEventLevel.Warning, "RCS create call failed for {RequestCode}", new TimeoutException("rcs"), ("RequestCode", "R-9"), ("SourceContext", "WCS.Application.Services.TaskOrchestrator")));
        sink.Emit(Ev(LogEventLevel.Error, "Audit write failed password=Hunter2Hunter", new InvalidOperationException("x"), ("SourceContext", "WCS.Infrastructure.Auth.AuditLog")));
        sink.Emit(Ev(LogEventLevel.Error, "query failed", new Npgsql.NpgsqlException("connection refused"), ("SourceContext", "WCS.Runtime.SchedulerHostedService"), ("CorrelationId", "0HNABC")));
        sink.Emit(Ev(LogEventLevel.Fatal, "boom", new Exception("fatal"), ("EventCode", "STARTUP_FATAL")));
        sink.Emit(Ev(LogEventLevel.Warning, "PLC {PlcId} STATE", null, ("Category", "PLC"), ("PlcId", "PLC-01")));                    // lives in plc_comm_log
        sink.Emit(Ev(LogEventLevel.Error, "ef failure", null, ("SourceContext", "Microsoft.EntityFrameworkCore.Update")));          // the logger's own storage
        sink.Emit(Ev(LogEventLevel.Warning, "Database connection lost. {EventCode}", null, ("EventCode", "DB_LOST"), ("Device", "postgres")));
        sink.Emit(Ev(LogEventLevel.Error, "text with a NUL\u0000 character"));                                                        // PostgreSQL cannot store NUL: it is removed, not fatal

        Assert.True(await sink.FlushAsync(CancellationToken.None));
        var rows = await h.Db(db => db.SystemLog.AsNoTracking().OrderBy(x => x.OccurredAt).ToListAsync());
        Assert.Equal(7, rows.Count);
        Assert.Contains(rows, r => r.Message == "text with a NUL character");
        Assert.All(rows, r => Assert.Equal(TimeSpan.Zero, r.OccurredAt.Offset));
        Assert.Equal("SYSTEM", rows.Single(r => r.EventCode == "STARTUP_SUMMARY").Category);
        Assert.Equal("RCS_AGV", rows.Single(r => r.Message.StartsWith("RCS create")).Category);
        Assert.Equal("SECURITY_AUDIT", rows.Single(r => r.Message.StartsWith("Audit write")).Category);
        Assert.Equal("DATABASE", rows.Single(r => r.Message == "query failed").Category);                                            // Npgsql exception
        Assert.Equal("0HNABC", rows.Single(r => r.Message == "query failed").CorrelationId);
        Assert.Equal("Critical", rows.Single(r => r.EventCode == "STARTUP_FATAL").Level);
        Assert.Equal("DATABASE", rows.Single(r => r.EventCode == "DB_LOST").Category); Assert.Equal("postgres", rows.Single(r => r.EventCode == "DB_LOST").Device);
        Assert.DoesNotContain("Hunter2Hunter", string.Join("|", rows.Select(r => r.Message + r.Exception)));
        Assert.DoesNotContain(rows, r => r.Message.Contains("PLC") && r.Message.Contains("STATE"));
        Assert.DoesNotContain(rows, r => r.Message == "ef failure");
        Assert.Equal("R-9", JsonDocument.Parse(rows.Single(r => r.Message.StartsWith("RCS create")).DetailsJson!).RootElement.GetProperty("RequestCode").GetString());
    }

    private sealed class BrokenProvider : IServiceProvider { public object? GetService(Type t) => throw new InvalidOperationException("no database"); }

    [Fact]
    public async Task Sink_never_throws_keeps_entries_while_the_database_is_unreachable_and_stores_them_with_their_original_time_later()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var sink = new SystemLogSink(new BrokenProvider());
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        sink.Emit(new LogEvent(at, LogEventLevel.Error, new Exception("down"), new MessageTemplateParser().Parse("while the database was down"), []));
        Assert.False(await sink.FlushAsync(CancellationToken.None));          // no throw, nothing lost
        Assert.Equal(1, sink.Pending);
        sink.Dispose();

        using var later = new SystemLogSink(f.Services);
        later.Emit(new LogEvent(at, LogEventLevel.Error, null, new MessageTemplateParser().Parse("after recovery"), []));
        Assert.True(await later.FlushAsync(CancellationToken.None));
        var stored = await h.Db(db => db.SystemLog.AsNoTracking().SingleAsync(x => x.Message == "after recovery"));
        Assert.True(Math.Abs((stored.OccurredAt - at).TotalSeconds) < 1);                                                              // original time preserved

        // queue is bounded: the oldest entries are dropped and counted, the caller is never blocked
        var small = new SystemLogSink(new BrokenProvider(), capacity: 5);
        for (var i = 0; i < 20; i++) small.Emit(Ev(LogEventLevel.Error, "e" + i));
        Assert.Equal(5, small.Pending); Assert.Equal(15, small.Dropped);
        small.Dispose();
    }

    [Fact]
    public async Task A_poison_entry_is_dropped_alone_when_the_database_is_reachable_and_nothing_is_dropped_when_it_is_not()
    {
        var stored = new List<string>(); var up = true;
        var sink = new SystemLogSink(new BrokenProvider(), 100,
            store: (batch, _) => { if (batch.Any(e => e.Message == "poison")) throw new InvalidOperationException("cannot be stored"); lock (stored) stored.AddRange(batch.Select(e => e.Message)); return Task.CompletedTask; },
            databaseUp: _ => Task.FromResult(up));
        sink.Emit(Ev(LogEventLevel.Error, "first")); sink.Emit(Ev(LogEventLevel.Error, "poison")); sink.Emit(Ev(LogEventLevel.Error, "last"));
        Assert.True(await sink.FlushAsync(CancellationToken.None));
        lock (stored) Assert.Equal(new[] { "first", "last" }, stored.Order().ToArray());
        Assert.Equal(0, sink.Pending); Assert.Equal(1, sink.Dropped);

        up = false;                                                                  // database unreachable: the same failure must NOT drop anything
        sink.Emit(Ev(LogEventLevel.Error, "poison"));
        Assert.False(await sink.FlushAsync(CancellationToken.None));
        Assert.Equal(1, sink.Pending); Assert.Equal(1, sink.Dropped);
        sink.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------- deployment import
    [Fact]
    public async Task Deployment_events_are_imported_once_masked_and_searchable_and_bad_lines_are_ignored()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var dir = Path.Combine(Path.GetTempPath(), "wcs-deploylog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            DeploymentLogImporter.ResetOffsets();
            var file = Path.Combine(dir, "deployment-events-20261116.jsonl");
            string L(string id, string result, string step, string msg, int? exit = null) =>
                JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = id, ["time"] = DateTimeOffset.Now.ToString("o"), ["script"] = "deploy.ps1", ["step"] = step, ["result"] = result, ["exitCode"] = exit, ["message"] = msg, ["host"] = "WIN11-01", ["commit"] = "abc1234" });
            await File.WriteAllTextAsync(file,
                L("r1-1", "START", "deploy", "deployment started") + "\n" +
                L("r1-2", "OK", "migrate", "migrations applied") + "\n" +
                L("r1-3", "FAIL", "build", "docker build failed: Password=Sup3rSecret in output", 1) + "\n" +
                "this is not json\n" +
                L("r1-2", "OK", "migrate", "duplicate id is ignored") + "\n" +
                L("r1-4", "OK", "partial", "last line has no newline yet"));                                                        // incomplete: waits for its newline

            int Import() => h.Scoped(async sp => await sp.GetRequiredService<DeploymentLogImporter>().ImportAsync(dir)).GetAwaiter().GetResult();
            Assert.Equal(3, Import());
            Assert.Equal(0, Import());                                                                                              // idempotent
            await File.AppendAllTextAsync(file, "\n");
            Assert.Equal(1, Import());                                                                                              // the completed line is read now

            var rows = await h.Db(db => db.SystemLog.AsNoTracking().Where(x => x.Category == "DEPLOYMENT").OrderBy(x => x.ExternalId).ToListAsync());
            Assert.Equal(4, rows.Count);
            Assert.Equal("DEPLOY_FAIL", rows.Single(r => r.ExternalId == "r1-3").EventCode); Assert.Equal("Error", rows.Single(r => r.ExternalId == "r1-3").Level);
            Assert.Contains("exit code 1", rows.Single(r => r.ExternalId == "r1-3").Message);
            Assert.DoesNotContain("Sup3rSecret", string.Join("|", rows.Select(r => r.Message + r.DetailsJson)));
            Assert.All(rows, r => { Assert.Equal("deploy.ps1", r.Device); Assert.Equal("WIN11-01", r.Host); });

            // a restart (offsets forgotten) re-reads everything but stores nothing twice
            DeploymentLogImporter.ResetOffsets();
            Assert.Equal(0, Import());

            // visible through the unified search
            var found = await h.Scoped(sp => sp.GetRequiredService<ILogQueryService>().SearchAsync(new LogSearchQuery(null, null, "DEPLOYMENT", "Error", null, null, null, null, "build", 1, 10)));
            Assert.Equal(1, found.Total);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } DeploymentLogImporter.ResetOffsets(); }
    }

    [Fact]
    public async Task Deployment_import_ignores_a_missing_folder_and_unrelated_files()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        Assert.Equal(0, await h.Scoped(sp => sp.GetRequiredService<DeploymentLogImporter>().ImportAsync(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()))));
        var dir = Path.Combine(Path.GetTempPath(), "wcs-deploylog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { await File.WriteAllTextAsync(Path.Combine(dir, "notes.txt"), "x\n"); Assert.Equal(0, await h.Scoped(sp => sp.GetRequiredService<DeploymentLogImporter>().ImportAsync(dir))); }
        finally { Directory.Delete(dir, true); }
        Assert.Null(DeploymentLogImporter.Parse("{\"no\":\"id\"}")); Assert.Null(DeploymentLogImporter.Parse("[1,2]")); Assert.Null(DeploymentLogImporter.Parse("{"));
    }

    // ------------------------------------------------------------------------------------------------------------- retention
    [Fact]
    public async Task Retention_removes_only_old_system_log_rows_and_never_touches_audit_or_plc_history()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        await h.Db(async db =>
        {
            db.SystemLog.AddRange(
                new SystemLogEntry(DateTimeOffset.UtcNow.AddDays(-30), LogCategories.System, LogLevels.Error, "x", null, null, "old"),
                new SystemLogEntry(DateTimeOffset.UtcNow.AddDays(-2), LogCategories.System, LogLevels.Error, "x", null, null, "recent"));
            db.PlcCommLog.Add(new PlcCommLogEntry(DateTimeOffset.UtcNow.AddDays(-400), "PLC-01", null, PlcLogLevel.Info, "STATE", "ancient"));
            db.AuditLog.Add(new AuditEntry(DateTimeOffset.UtcNow.AddDays(-400), "a", null, "AUTH_LOGIN", null, null, AuditResult.Success, null, null, null, null));
            await db.SaveChangesAsync(); return 0;
        });

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:SystemLog:RetentionDays"] = "1" }).Build();     // below the 7-day floor
        var svc = new LogMaintenanceHostedService(f.Services.GetRequiredService<IServiceScopeFactory>(), cfg, new TestClock(), f.Services.GetRequiredService<ILogger<LogMaintenanceHostedService>>());
        await svc.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && await h.Db(db => db.SystemLog.AnyAsync(x => x.Message == "old"))) await Task.Delay(100);
        await svc.StopAsync(CancellationToken.None);

        Assert.False(await h.Db(db => db.SystemLog.AnyAsync(x => x.Message == "old")));
        Assert.True(await h.Db(db => db.SystemLog.AnyAsync(x => x.Message == "recent")));                                          // 7-day floor protects it
        Assert.Equal(1, await h.Db(db => db.PlcCommLog.CountAsync())); Assert.Equal(1, await h.Db(db => db.AuditLog.CountAsync()));
    }

    // ------------------------------------------------------------------------------------------------------------- database monitor
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f) { lock (Lines) Lines.Add((level, f(state, ex))); }
    }

    private sealed class FailingScopes : IServiceScopeFactory { public IServiceScope CreateScope() => throw new InvalidOperationException("connection refused"); }

    [Fact]
    public async Task Database_monitor_reports_a_lost_connection_once_and_a_healthy_database_silently()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;

        var down = new CaptureLogger<DbHealthMonitorHostedService>();
        var m1 = new DbHealthMonitorHostedService(new FailingScopes(), new TestClock(), down);
        await m1.StartAsync(CancellationToken.None); await Task.Delay(500); await m1.StopAsync(CancellationToken.None);
        Assert.Single(down.Lines, l => l.Text.Contains("DB_LOST"));
        Assert.Equal(LogLevel.Warning, down.Lines.Single(l => l.Text.Contains("DB_LOST")).Level);

        var up = new CaptureLogger<DbHealthMonitorHostedService>();
        var m2 = new DbHealthMonitorHostedService(f.Services.GetRequiredService<IServiceScopeFactory>(), new TestClock(), up);
        await m2.StartAsync(CancellationToken.None); await Task.Delay(1500); await m2.StopAsync(CancellationToken.None);
        Assert.Empty(up.Lines);                                                                                                       // healthy + migrated: nothing to report
    }

    // ------------------------------------------------------------------------------------------------------------- PLC event persistence with details
    [Fact]
    public async Task Plc_events_are_stored_with_ip_port_and_state_change_and_appear_in_the_unified_log()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        using (var scope = f.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<PlcCatalogSeeder>().SeedAsync();

        var events = f.Services.GetRequiredService<IPlcEvents>();
        events.Log(PlcLogLevel.Info, "PLC-01", null, "STATE", "PLC-01: Connected");
        await Task.Delay(300);
        events.Log(PlcLogLevel.Warning, "PLC-01", null, "STATE", "PLC-01: Faulted - Mat ket noi PLC.");
        events.Log(PlcLogLevel.Warning, "PLC-01", "A1", "READ_BAD_A1", "2 IO point doc loi: no response within 500 ms");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && await h.Db(db => db.PlcCommLog.CountAsync()) < 3) await Task.Delay(100);
        var logs = await h.Db(db => db.PlcCommLog.AsNoTracking().OrderBy(x => x.OccurredAt).ToListAsync());
        Assert.Equal(3, logs.Count);
        var fault = logs.Single(l => l.Message.Contains("Faulted"));
        var d = JsonDocument.Parse(fault.DetailsJson!).RootElement;
        Assert.Equal("Connected", d.GetProperty("previousState").GetString()); Assert.Equal("Faulted", d.GetProperty("newState").GetString());
        Assert.Equal("TIMEOUT", JsonDocument.Parse(logs.Single(l => l.Event == "READ_BAD_A1").DetailsJson!).RootElement.GetProperty("kind").GetString());

        var admin = await As(f, "admin1");
        var list = await admin.SendJsonAsync(HttpMethod.Get, "/api/logs?category=PLC&device=PLC-01&pageSize=10");
        Assert.Equal(3, list.Root.GetProperty("data").GetProperty("total").GetInt64());
        var id = list.Root.GetProperty("data").GetProperty("items").EnumerateArray().First(i => i.GetProperty("message").GetString()!.Contains("Faulted")).GetProperty("id").GetString();
        var detail = (await admin.SendJsonAsync(HttpMethod.Get, "/api/logs/" + id)).Root.GetProperty("data");
        Assert.Equal("Faulted", detail.GetProperty("newState").GetString()); Assert.Equal("Connected", detail.GetProperty("previousState").GetString());
    }
}
