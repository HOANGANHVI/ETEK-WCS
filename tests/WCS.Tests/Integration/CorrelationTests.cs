using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using WCS.Api.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Diagnostics;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-021 batch B: the CorrelationId of a task / order is set once, survives every worker and request that touches it, links events of several modules in the export, and old rows without one still work.</summary>
[Collection("Database")]
public class CorrelationTests(DatabaseFixture fixture)
{
    /// <summary>Wraps the RCS simulator and records which diagnostic frame was active at each call (what a log line written at that moment would carry).</summary>
    private sealed class RecordingRcs(RcsSimulator inner) : IRcsClient
    {
        public List<(string Op, DiagFrame? Frame)> Calls { get; } = [];
        private void Rec(string op) { lock (Calls) Calls.Add((op, DiagnosticContext.Current)); }
        public Task<RcsCallResult> CreateTaskAsync(RcsCreateTaskRequest r, CancellationToken ct = default) { Rec("create"); return inner.CreateTaskAsync(r, ct); }
        public Task<RcsCallResult> ContinueTaskAsync(string requestCode, string? rcsTaskCode, string dest, CancellationToken ct = default) { Rec("continue"); return inner.ContinueTaskAsync(requestCode, rcsTaskCode, dest, ct); }
        public Task<RcsCallResult> CancelTaskAsync(string requestCode, string? rcsTaskCode, CancellationToken ct = default) { Rec("cancel"); return inner.CancelTaskAsync(requestCode, rcsTaskCode, ct); }
        public Task<(RcsTaskStatus Status, string? RcsTaskCode)> QueryTaskStatusAsync(string requestCode, CancellationToken ct = default) { Rec("query"); return inner.QueryTaskStatusAsync(requestCode, ct); }
        public Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default) => inner.QueryAgvStatusAsync(robotCode, ct);
    }

    private async Task<(Harness H, RecordingRcs Rcs)> ReadyAsync()
    {
        RecordingRcs? rec = null;
        var h = await Harness.CreateAsync(fixture, s => s.AddSingleton<IRcsClient>(sp => rec = new RecordingRcs(sp.GetRequiredService<RcsSimulator>())));
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        _ = h.Services.GetRequiredService<IRcsClient>();
        return (h, rec!);
    }

    private static async Task<string> ExportAsync(Harness h, SystemExportRequest req)
    {
        await using var scope = h.Services.CreateAsyncScope();
        var dir = Path.Combine(Path.GetTempPath(), "wcs-corr-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pkg.zip");
        await scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>().ExportAsync(Guid.NewGuid(), req, "admin1", path, (_, _) => { }, CancellationToken.None);
        return path;
    }

    private static List<JsonElement> Events(string zip)
    {
        var all = new List<JsonElement>();
        using var z = ZipFile.OpenRead(zip);
        foreach (var e in z.Entries.Where(x => x.FullName.StartsWith("Events/", StringComparison.Ordinal)))
        {
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            while (r.ReadLine() is { Length: > 0 } line) all.Add(JsonDocument.Parse(line).RootElement.Clone());
        }
        return all;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ------------------------------------------------------------------------------------------------ the id is set once and survives every worker
    [Fact]
    public async Task A_task_keeps_the_CorrelationId_of_the_request_that_created_it_and_every_later_step_runs_under_it_without_any_request()
    {
        var (h, rcs) = await ReadyAsync();
        await using var _h = h;
        Guid taskId; string corr;
        using (DiagnosticContext.Begin("http-corr-0001"))
        {
            var created = (await h.CreateTaskAsync("BP01", "A1")).Task;
            taskId = created.Id;
            corr = (await h.GetTaskAsync(taskId)).CorrelationId!;
        }
        Assert.Equal("http-corr-0001", corr);                                                         // the id of the creating request
        Assert.Null(DiagnosticContext.Current);                                                       // the frame did not leak out of the using block

        // everything after this runs with NO ambient frame (scheduler ticks, simulated signals): each step must re-enter the TASK's own id
        await h.AdvanceUntilAsync(taskId, TaskState.Completed);
        var calls = rcs.Calls.ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, c => { Assert.Equal("http-corr-0001", c.Frame?.CorrelationId); Assert.Equal(taskId.ToString("D"), c.Frame?.TaskId); });
        Assert.Contains(calls, c => c.Op == "create");

        // a second task created outside any request gets its own, new, opaque id
        await h.StockAsync("A2", RackKind.Full, 1);
        var other = (await h.CreateTaskAsync("BP01", "A2")).Task;
        var otherCorr = (await h.GetTaskAsync(other.Id)).CorrelationId;
        Assert.Matches("^[0-9a-f]{32}$", otherCorr!); Assert.NotEqual(corr, otherCorr);
        Assert.Null(DiagnosticContext.Current);
    }

    [Fact]
    public async Task The_correlation_never_changes_once_set()
    {
        var (h, _) = await ReadyAsync();
        await using var _h = h;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var corr = (await h.GetTaskAsync(t.Id)).CorrelationId;
        await h.Db(async db => { var x = await db.Tasks.SingleAsync(k => k.Id == t.Id); x.SetCorrelation("something-else-123"); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(corr, (await h.GetTaskAsync(t.Id)).CorrelationId);
    }

    [Fact]
    public async Task An_external_correlation_id_is_accepted_only_when_it_is_short_and_made_of_harmless_characters()
    {
        Assert.Equal("abc-12345.XY_9", DiagnosticContext.Accept("abc-12345.XY_9"));
        Assert.Null(DiagnosticContext.Accept("short"));
        Assert.Null(DiagnosticContext.Accept("has space inside"));
        Assert.Null(DiagnosticContext.Accept("<script>alert(1)</script>"));
        Assert.Null(DiagnosticContext.Accept(new string('a', 65)));
        Assert.Null(DiagnosticContext.Accept(null));
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------------------------------------ the export links the modules through it
    [Fact]
    public async Task Filtering_the_export_by_CorrelationId_returns_the_events_of_every_module_of_that_task_and_a_module_scope_pulls_the_others_in_as_related()
    {
        var (h, _) = await ReadyAsync();
        await using var _h = h;
        Guid taskId;
        using (DiagnosticContext.Begin("http-corr-0002")) taskId = (await h.CreateTaskAsync("BP01", "A1")).Task.Id;
        await h.AdvanceUntilAsync(taskId, TaskState.Completed);
        // a different task that must NOT appear
        await h.StockAsync("A2", RackKind.Full, 1);
        var other = (await h.CreateTaskAsync("BP01", "A2")).Task.Id;
        await h.Db(async db => { db.SystemLog.Add(new WCS.Domain.Logging.SystemLogEntry(DateTimeOffset.UtcNow.AddMinutes(-1), "RCS_AGV", "Warning", "WCS.Application.Services.TaskOrchestrator", "AGV1", "RCS_TIMEOUT", "RCS timed out", null, "http-corr-0002", $"{{\"TaskId\":\"{taskId:D}\"}}", "host")); await db.SaveChangesAsync(); return 0; });

        var byCorr = Events(await ExportAsync(h, new SystemExportRequest("24h", null, null, new DiagScope { CorrelationId = "http-corr-0002" })));
        Assert.NotEmpty(byCorr);
        Assert.All(byCorr, e => Assert.Equal("http-corr-0002", Str(e, "correlationId")));
        var sources = byCorr.Select(e => Str(e, "source")).ToHashSet();
        Assert.Contains("task_history", sources); Assert.Contains("inventory_transactions", sources); Assert.Contains("system_log", sources);   // three modules, one id
        Assert.DoesNotContain(byCorr, e => Str(e, "taskId") == other.ToString("D"));
        Assert.Contains(byCorr, e => Str(e, "eventName") == "TASK_CREATED");

        // module scope: only the Task rows are selected; the inventory and RCS rows come in as related, marked as such
        var byModule = Events(await ExportAsync(h, new SystemExportRequest("24h", null, null, new DiagScope { Modules = [DiagModules.Task] })));
        var inv = byModule.Where(e => Str(e, "module") == "Inventory").ToList();
        Assert.NotEmpty(inv); Assert.All(inv, e => Assert.True(e.GetProperty("related").GetBoolean()));
        Assert.All(byModule.Where(e => Str(e, "module") == "Task" && Str(e, "source") == "task_history"), e => Assert.False(e.GetProperty("related").GetBoolean()));

        var byTask = Events(await ExportAsync(h, new SystemExportRequest("all", null, null, new DiagScope { TaskId = taskId.ToString("D") })));
        Assert.Contains(byTask, e => Str(e, "source") == "task_history"); Assert.Contains(byTask, e => Str(e, "source") == "inventory_transactions");
    }

    // ------------------------------------------------------------------------------------------------ old data and the migration
    [Fact]
    public async Task The_migration_is_additive_and_rows_without_a_CorrelationId_still_export_and_can_still_be_found_by_TaskId()
    {
        var (h, _) = await ReadyAsync();
        await using var _h = h;
        var (tCol, oCol) = await h.Db(async db => (
            await db.Database.SqlQueryRaw<string>("SELECT is_nullable AS \"Value\" FROM information_schema.columns WHERE table_name='transport_tasks' AND column_name='CorrelationId'").SingleAsync(),
            await db.Database.SqlQueryRaw<string>("SELECT is_nullable AS \"Value\" FROM information_schema.columns WHERE table_name='transport_orders' AND column_name='CorrelationId'").SingleAsync()));
        Assert.Equal(("YES", "YES"), (tCol, oCol));                                                    // nullable: nothing was rewritten

        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.Db(async db => { await db.Database.ExecuteSqlRawAsync("UPDATE transport_tasks SET \"CorrelationId\" = NULL"); return 0; });   // a task created before CR-021
        var all = Events(await ExportAsync(h, new SystemExportRequest("24h", null, null, new DiagScope())));
        var hist = all.Where(e => Str(e, "source") == "task_history").ToList();
        Assert.NotEmpty(hist); Assert.All(hist, e => Assert.Null(Str(e, "correlationId")));            // honest: no id, no invented link
        Assert.Empty(Events(await ExportAsync(h, new SystemExportRequest("24h", null, null, new DiagScope { CorrelationId = "http-corr-0002" }))));
        Assert.NotEmpty(Events(await ExportAsync(h, new SystemExportRequest("24h", null, null, new DiagScope { TaskId = t.Id.ToString("D") }))));   // the TaskId link still works
    }

    [Fact]
    public async Task An_order_gets_a_CorrelationId_at_creation_and_its_routing_events_carry_it_in_the_export()
    {
        await using var kit = await RoutingKit.CreateAsync(fixture);
        var (_, ver) = await kit.CreateProcessAsync("T-CORR", RequestType.PickupRequest, RackKind.Full, ["A1", "A2"], RoutingKit.MultiLeg("B1", "F1"));
        await kit.PublishAsync(ver);
        await kit.H.StockAsync("A1", RackKind.Full, 1);
        WCS.Application.Routing.StartOrderResult start;
        using (DiagnosticContext.Begin("http-order-0001")) start = await kit.StartAsync("k-corr", "A1", RequestType.PickupRequest, RackKind.Full);
        var order = await kit.H.Db(db => db.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == start.Order.Id));
        Assert.Equal("http-order-0001", order.CorrelationId);
        var ev = Events(await ExportAsync(kit.H, new SystemExportRequest("24h", null, null, new DiagScope { CorrelationId = "http-order-0001" })));
        Assert.Contains(ev, e => Str(e, "source") == "routing_events" && Str(e, "eventName") == "ORDER_CREATED");
    }

    // ------------------------------------------------------------------------------------------------ logging pipeline and HTTP
    private static List<LogEvent> Capture(Action<Serilog.ILogger> log)
    {
        var events = new List<LogEvent>();
        using var logger = new LoggerConfiguration().Enrich.With<DiagnosticContextEnricher>().WriteTo.Sink(new DelegatingSink(events.Add)).CreateLogger();
        log(logger);
        return events;
    }

    private sealed class DelegatingSink(Action<LogEvent> write) : ILogEventSink { public void Emit(LogEvent e) => write(e); }

    private static string? Prop(LogEvent e, string n) => e.Properties.TryGetValue(n, out var v) && v is ScalarValue { Value: { } o } ? o.ToString() : null;

    [Fact]
    public void The_log_enricher_copies_the_frame_into_every_event_never_overwrites_an_explicit_property_and_adds_nothing_for_a_null_id()
    {
        var plain = Capture(l => l.Information("no frame"));
        Assert.DoesNotContain("CorrelationId", plain[0].Properties.Keys);

        List<LogEvent> inFrame;
        using (DiagnosticContext.Begin("corr-enrich-001", taskId: "task-1", plcId: "PLC-02"))
            inFrame = Capture(l => { l.Warning("framed"); l.Warning("explicit {TaskId}", "explicit-task"); });
        Assert.Equal(("corr-enrich-001", "task-1", "PLC-02"), (Prop(inFrame[0], "CorrelationId"), Prop(inFrame[0], "TaskId"), Prop(inFrame[0], "PlcId")));
        Assert.DoesNotContain("AgvId", inFrame[0].Properties.Keys);
        Assert.Equal("explicit-task", Prop(inFrame[1], "TaskId"));                                        // the call's own property wins
        Assert.Equal("corr-enrich-001", Prop(inFrame[1], "CorrelationId"));
    }

    [Fact]
    public void A_persisted_system_log_entry_carries_the_correlation_and_the_ids_of_the_frame()
    {
        List<LogEvent> evs;
        using (DiagnosticContext.Begin("corr-persist-01", taskId: "11111111-2222-3333-4444-555555555555", agvId: "AGV1"))
            evs = Capture(l => l.Warning("RCS create call failed {EventCode}", "RCS_CREATE_FAILED"));
        var entry = LogClassifier.ToEntry(evs.Single())!;
        Assert.Equal("corr-persist-01", entry.CorrelationId);
        Assert.Contains("11111111-2222-3333-4444-555555555555", entry.DetailsJson);
        Assert.Contains("AGV1", entry.DetailsJson);
        Assert.Equal("RCS_CREATE_FAILED", entry.EventCode);
    }

    [Fact]
    public async Task The_HTTP_pipeline_keeps_a_valid_X_Correlation_Id_from_the_caller_and_ignores_an_unsafe_one()
    {
        await using var h = await Harness.CreateAsync(fixture);
        using var f = new DbApiFactory(fixture);
        var c = f.CreateClient();
        var ok = new HttpRequestMessage(HttpMethod.Get, "/healthz"); ok.Headers.Add("X-Correlation-Id", "caller-id-0001");
        Assert.Equal("caller-id-0001", (await c.SendAsync(ok)).Headers.GetValues("X-Correlation-Id").Single());
        var bad = new HttpRequestMessage(HttpMethod.Get, "/healthz"); bad.Headers.Add("X-Correlation-Id", "x y");
        var back = (await c.SendAsync(bad)).Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("x y", back); Assert.False(string.IsNullOrWhiteSpace(back));
        var none = (await c.GetAsync("/healthz")).Headers.GetValues("X-Correlation-Id").Single();
        Assert.False(string.IsNullOrWhiteSpace(none));                                                    // every response still names its id
    }
}
