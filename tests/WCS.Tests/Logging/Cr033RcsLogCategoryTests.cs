using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Serilog.Parsing;
using WCS.Api.Logging;
using WCS.Application.Diagnostics;
using WCS.Application.Logging;
using WCS.Domain.Logging;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Integration;
using WCS.Tests.Support;

namespace WCS.Tests.Logging;

/// <summary>
/// CR-033: the RCS integration events are classified RCS / AGV by their EVENT CODE (not by the name of the class that writes them, and not by the word "RCS" in a module or message);
/// what a person does (RCS Diagnostics, RCS connection settings) stays in SECURITY_AUDIT; other groups are unchanged. The query, the KPI, the paging and the export all read the one stored category.
/// </summary>
[Collection("Database")]
public class Cr033RcsLogCategoryTests(DatabaseFixture fixture)
{
    private const string Fleet = "WCS.Application.Fleet.FleetService";

    private static int _seq;   // the harness clock is fixed in the past: events are stamped inside the exported window
    private static LogEvent Ev(LogEventLevel level, string template, string source, string? code, params (string, object)[] props)
    {
        var all = new List<LogEventProperty> { new("SourceContext", new ScalarValue(source)) };
        if (code is not null) all.Add(new("EventCode", new ScalarValue(code)));
        all.AddRange(props.Select(p => new LogEventProperty(p.Item1, new ScalarValue(p.Item2))));
        return new LogEvent(T0.AddSeconds(Interlocked.Increment(ref _seq)).ToLocalTime(), level, null, new MessageTemplateParser().Parse(template), all);
    }

    // ------------------------------------------------------------------------------------------------------------- the rule (pure)
    [Theory]
    [InlineData(LogEventLevel.Information, Fleet, "RCS_FLEET_POLL_SUCCESS")]
    [InlineData(LogEventLevel.Information, Fleet, "RCS_FLEET_QUERY_RECOVERED")]
    [InlineData(LogEventLevel.Warning, Fleet, "RCS_FLEET_POLL_FAILED")]
    [InlineData(LogEventLevel.Warning, "WCS.Infrastructure.Fleet.HikFleetQueryClient", "RCS_CODE_NOT_SUCCESS")]
    [InlineData(LogEventLevel.Warning, "WCS.Application.Anything.NotNamedLikeTheIntegration", "RCS_TIMEOUT")]
    [InlineData(LogEventLevel.Error, "WCS.Infrastructure.Whatever", "RCS_CALL_EXCEPTION")]
    [InlineData(LogEventLevel.Warning, "WCS.Api", "RCS_NOT_CONNECTED")]
    [InlineData(LogEventLevel.Warning, "WCS.Runtime.AgvStatusHostedService", "AGV_STATUS_FAILED")]
    public void The_RCS_integration_event_codes_are_RCS_AGV_whichever_class_writes_them(LogEventLevel level, string source, string code)
    {
        var entry = LogClassifier.ToEntry(Ev(level, "x {EventCode}", source, code, ("EventCode", code)))!;
        Assert.Equal(LogCategories.RcsAgv, entry.Category);
        Assert.Equal(code, entry.EventCode);                                          // the event name is kept as it was
    }

    [Fact]
    public void The_word_RCS_in_a_message_a_module_or_a_logger_name_does_not_make_an_event_RCS_AGV_and_other_groups_are_unchanged()
    {
        // an application event whose text mentions the RCS stays in SYSTEM
        Assert.Equal(LogCategories.System, LogClassifier.ToEntry(Ev(LogEventLevel.Information, "WCS starting, RCS adapter is a simulator", "WCS.Api", "STARTUP_SUMMARY"))!.Category);
        Assert.Equal(LogCategories.System, LogClassifier.ToEntry(Ev(LogEventLevel.Warning, "scheduler tick failed while waiting for the RCS", "WCS.Runtime.SchedulerHostedService", null))!.Category);
        // a database event keeps DATABASE even when the text says RCS
        Assert.Equal(LogCategories.Database, LogClassifier.ToEntry(Ev(LogEventLevel.Warning, "Database lost during the RCS poll", "WCS.Runtime.DbHealth", "DB_LOST"))!.Category);
        // sign-in / audit loggers keep SECURITY_AUDIT; deployment and the explicit Category property are respected
        Assert.Equal(LogCategories.SecurityAudit, LogClassifier.ToEntry(Ev(LogEventLevel.Error, "Audit write failed", "WCS.Infrastructure.Auth.AuditLog", null))!.Category);
        Assert.Equal(LogCategories.Deployment, LogClassifier.ToEntry(Ev(LogEventLevel.Warning, "deploy step", "deploy", "NOTE", ("Category", "DEPLOYMENT")))!.Category);
        // PLC events are not stored in system_log at all (they live in plc_comm_log)
        Assert.Null(LogClassifier.ToEntry(Ev(LogEventLevel.Warning, "PLC state", "WCS.Runtime.PlcWorker", "PLC_STATE", ("Category", "PLC"))));
        // an event code that only CONTAINS RCS is not an RCS integration code
        Assert.False(LogClassifier.IsRcsAgvEventCode("TASK_RCS_NOTE")); Assert.False(LogClassifier.IsRcsAgvEventCode(null)); Assert.True(LogClassifier.IsRcsAgvEventCode("RCS_FLEET_POLL_SUCCESS"));
    }

    [Fact]
    public void The_module_of_an_export_row_is_derived_independently_of_the_category()
    {
        Assert.Equal(DiagModules.Rcs, DiagModules.ForSystemLog(LogCategories.RcsAgv, Fleet, "RCS_FLEET_POLL_SUCCESS"));
        Assert.Equal(DiagModules.Rcs, DiagModules.ForSystemLog(LogCategories.RcsAgv, Fleet, "RCS_CODE_NOT_SUCCESS"));
    }

    // ------------------------------------------------------------------------------------------------------------- stored, queried, counted, paged and exported
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddHours(-1);

    [Fact]
    public async Task Stored_RCS_events_are_found_by_the_category_filter_counted_once_in_the_KPI_paged_consistently_and_exported_with_that_category_while_audit_rows_stay_in_SECURITY_AUDIT()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture);
        // the real sink path: producer -> classifier -> system_log
        using (var sink = new SystemLogSink(f.Services))
        {
            sink.Emit(Ev(LogEventLevel.Information, "poll ok {EventCode}", Fleet, "RCS_FLEET_POLL_SUCCESS", ("EventCode", "RCS_FLEET_POLL_SUCCESS")));
            sink.Emit(Ev(LogEventLevel.Information, "works again {EventCode}", Fleet, "RCS_FLEET_QUERY_RECOVERED", ("EventCode", "RCS_FLEET_QUERY_RECOVERED")));
            sink.Emit(Ev(LogEventLevel.Warning, "not success {EventCode}", "WCS.Infrastructure.Fleet.HikFleetQueryClient", "RCS_CODE_NOT_SUCCESS", ("EventCode", "RCS_CODE_NOT_SUCCESS")));
            sink.Emit(Ev(LogEventLevel.Information, "WCS backend starting. {EventCode}", "WCS.Api", "STARTUP_SUMMARY", ("EventCode", "STARTUP_SUMMARY")));
            Assert.True(await sink.FlushAsync(CancellationToken.None));
        }
        await h.Db(async db =>
        {
            db.AuditLog.AddRange(
                new AuditEntry(T0.AddMinutes(1), "admin1", "Admin", "RCS_DIAG_EXECUTE", "RcsApi", "queryAgvStatus", AuditResult.Success, null, null, null, "10.0.0.5"),
                new AuditEntry(T0.AddMinutes(2), "admin1", "Admin", "CONFIG_RCS_UPDATE", "ConnectionSetting", "Rcs", AuditResult.Success, null, null, null, "10.0.0.5"));
            await db.SaveChangesAsync(); return 0;
        });

        await h.RunAsync(async sp =>
        {
            var s = sp.GetRequiredService<ILogQueryService>();
            LogSearchQuery Q(string? category = null, string? source = null, int page = 1, int size = 50) => new(null, null, category, null, null, null, source, null, null, page, size);

            // the category filter finds the RCS events, and ONLY them; the audit rows are in SECURITY_AUDIT
            var rcs = await s.SearchAsync(Q(category: "RCS_AGV"));
            Assert.Equal(3, rcs.Total);
            Assert.Equal(new[] { "RCS_FLEET_POLL_SUCCESS", "RCS_FLEET_QUERY_RECOVERED", "RCS_CODE_NOT_SUCCESS" }.Order(), rcs.Items.Select(i => i.Event!).Order());
            Assert.All(rcs.Items, i => Assert.Equal("RCS_AGV", i.Category));
            var audit = await s.SearchAsync(Q(category: "SECURITY_AUDIT"));
            Assert.Equal(2, audit.Total); Assert.Contains(audit.Items, i => i.Event == "RCS_DIAG_EXECUTE"); Assert.Contains(audit.Items, i => i.Event == "CONFIG_RCS_UPDATE");
            Assert.Equal(1, (await s.SearchAsync(Q(category: "SYSTEM"))).Total);

            // the source filter works on its own and together with the category
            Assert.Equal(2, (await s.SearchAsync(Q(source: "FleetService"))).Total);
            Assert.Equal(1, (await s.SearchAsync(Q(category: "RCS_AGV", source: "HikFleetQueryClient"))).Total);

            // KPI: every entry in exactly one group (no double count), RCS / AGV = 3
            var st = await s.StatsAsync(T0.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.Equal(3, st.ByCategory["RCS_AGV"]); Assert.Equal(2, st.ByCategory["SECURITY_AUDIT"]); Assert.Equal(1, st.ByCategory["SYSTEM"]); Assert.Equal(0, st.ByCategory["PLC"]);
            Assert.Equal(st.Total, st.ByCategory.Values.Sum()); Assert.Equal(6, st.Total);

            // paging: the total is the same on every page and the pages do not overlap
            var p1 = await s.SearchAsync(Q(category: "RCS_AGV", size: 2, page: 1)); var p2 = await s.SearchAsync(Q(category: "RCS_AGV", size: 2, page: 2));
            Assert.Equal(3, p1.Total); Assert.Equal(3, p2.Total); Assert.Equal(2, p1.Items.Count); Assert.Single(p2.Items);
            Assert.Equal(3, p1.Items.Concat(p2.Items).Select(i => i.Id).Distinct().Count());
        });

        // the export keeps the stored category; the module is Rcs for the RCS events and the audit rows stay SECURITY_AUDIT
        string path;
        await using (var scope = h.Services.CreateAsyncScope())
        {
            var ex = scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>();
            var dir = Path.Combine(Path.GetTempPath(), "wcs-cr033-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); path = Path.Combine(dir, "pkg.zip");
            await ex.ExportAsync(Guid.NewGuid(), new SystemExportRequest("all", null, null, new DiagScope()), "admin1", path, (_, _) => { }, CancellationToken.None);
        }
        var events = new List<JsonElement>();
        using (var z = ZipFile.OpenRead(path))
            foreach (var e in z.Entries.Where(x => x.FullName.StartsWith("Events/", StringComparison.Ordinal)))
            {
                using var r = new StreamReader(e.Open(), Encoding.UTF8);
                while (r.ReadLine() is { Length: > 0 } line) events.Add(JsonDocument.Parse(line).RootElement.Clone());
            }
        JsonElement One(string name) => events.SingleOrDefault(x => x.GetProperty("eventName").GetString() == name) is { ValueKind: not JsonValueKind.Undefined } v ? v : throw new Xunit.Sdk.XunitException("missing " + name + "; have: " + string.Join(",", events.Select(x => x.GetProperty("eventName").GetString() + "/" + x.GetProperty("source").GetString())));
        foreach (var n in new[] { "RCS_FLEET_POLL_SUCCESS", "RCS_FLEET_QUERY_RECOVERED", "RCS_CODE_NOT_SUCCESS" })
        {
            Assert.Equal("RCS_AGV", One(n).GetProperty("category").GetString());
            Assert.Equal("Rcs", One(n).GetProperty("module").GetString());
            Assert.Equal("system_log", One(n).GetProperty("source").GetString());
        }
        Assert.Equal("FleetService", One("RCS_FLEET_POLL_SUCCESS").GetProperty("component").GetString());
        Assert.Equal("SECURITY_AUDIT", One("RCS_DIAG_EXECUTE").GetProperty("category").GetString());
        Assert.Equal("SECURITY_AUDIT", One("CONFIG_RCS_UPDATE").GetProperty("category").GetString());
        Assert.Equal("SYSTEM", One("STARTUP_SUMMARY").GetProperty("category").GetString());
    }
}
