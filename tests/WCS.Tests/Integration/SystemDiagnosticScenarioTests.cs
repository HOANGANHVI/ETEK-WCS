using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using WCS.Api.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Diagnostics;
using WCS.Application.Plc;
using WCS.Domain.Common;
using WCS.Domain.Plc;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Support;
using Xunit.Abstractions;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-021 batch D: the end-to-end scenario and the size/time/memory behaviour. The scenario runs the real pipeline (Serilog + enricher + the persisting sink -> system_log), the real task/scheduler/inventory code
/// against the RCS SIMULATOR and the MC MOCK server for the PLC; it proves that a failed task can be reconstructed from the package alone. It says nothing about a real RCS, AGV or PLC (none is used).
/// </summary>
[Collection("Database")]
public class SystemDiagnosticScenarioTests(DatabaseFixture fixture, ITestOutputHelper output)
{
    private sealed class Holder : IServiceProvider { public IServiceProvider? Inner; public object? GetService(Type t) => Inner?.GetService(t); }

    private static string TempDir() { var d = Path.Combine(Path.GetTempPath(), "wcs-scn-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    private static List<JsonElement> Lines(string zip, string prefix)
    {
        var all = new List<JsonElement>();
        using var z = ZipFile.OpenRead(zip);
        foreach (var e in z.Entries.Where(x => x.FullName.StartsWith(prefix, StringComparison.Ordinal) && x.FullName.EndsWith(".jsonl", StringComparison.Ordinal)))
        {
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            while (r.ReadLine() is { Length: > 0 } line) all.Add(JsonDocument.Parse(line).RootElement.Clone());
        }
        return all;
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Text(string zip, string entry)
    {
        using var z = ZipFile.OpenRead(zip); using var r = new StreamReader(z.GetEntry(entry)!.Open(), Encoding.UTF8); return r.ReadToEnd();
    }

    [Fact]
    public async Task A_failed_task_can_be_reconstructed_across_modules_from_the_exported_package_alone()
    {
        var holder = new Holder();
        var sink = new SystemLogSink(holder);
        var serilog = new LoggerConfiguration().MinimumLevel.Verbose().Enrich.With<DiagnosticContextEnricher>().WriteTo.Sink(sink).CreateLogger();
        await using var plc = new MelsecMcMockServer { ExpectedRoute = new McRoute(0, 255, 1023, 0) }; plc.Start();
        await using var h = await Harness.CreateAsync(fixture, s =>
        {
            s.AddLogging(b => b.AddSerilog(serilog, dispose: false));
            s.Configure<PlcRuntimeOptions>(o => { o.Mode = PlcRuntimeMode.RealReadOnly; o.AllowLiveConnection = true; o.ReconnectInitialDelayMs = 50; o.ReconnectMaxDelayMs = 200; });
        });
        holder.Inner = h.Services;
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);

        // ---- the PLC side: the MC mock serves PLC-01; one confirmed address is polled; then the mock starts answering with an error end code
        string plcErrorWindowStart;
        await h.Scoped(async sp =>
        {
            await sp.GetRequiredService<PlcCatalogSeeder>().SeedAsync();
            var cfg = sp.GetRequiredService<PlcConfigService>();
            await cfg.UpdateConnectionAsync("PLC-01", new UpdatePlcConnectionCommand("PLC-01", "127.0.0.1", plc.Port, "McProtocol3EBinaryTcp", 0, 255, 1023, 0, 16, 300, 50, true), sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlcRuntimeOptions>>().Value, "test");
            var ack = (await cfg.ListPointsAsync("PLC-01", "A1", true)).Single(p => p.SignalName == "Heartbeat_SW").Id;
            await cfg.SetAddressAsync(ack, new SetPointAddressCommand("M103", null, null));
            return 0;
        });
        await h.Services.GetRequiredService<IPlcRuntime>().StartAsync();
        await Wait(async () => (await h.Db(db => db.PlcCommLog.CountAsync(l => l.Event == "TCP_CONNECTED"))) > 0);
        plcErrorWindowStart = DateTimeOffset.UtcNow.ToString("O");
        plc.ForceEndCode(0x4031, 100000);
        await Wait(async () => (await h.Db(db => db.PlcCommLog.CountAsync(l => l.Level == PlcLogLevel.Warning || l.Level == PlcLogLevel.Error))) > 0);
        plc.ForceEndCode(0, 0);

        // ---- the business side, under the correlation id of the creating request
        Guid taskId; const string corr = "http-scenario-0001";
        using (DiagnosticContext.Begin(corr, agvId: null))
            taskId = (await h.CreateTaskAsync("BP01", "A1")).Task.Id;
        await h.AdvanceUntilAsync(taskId, TaskState.Executing);
        h.Rcs.NextFault = RcsFault.RejectNextContinue;                                               // the RCS refuses the continue command: the task fails
        for (var i = 0; i < 40 && (await h.GetTaskAsync(taskId)).State != TaskState.Failed; i++)
        {
            var step = await h.AdvanceAsync(taskId);
            if (step.Signal is null) await h.TickAsync();
        }
        var final = await h.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Failed, final.State);
        Assert.Equal(corr, final.CorrelationId);

        // an exception with a stack trace, written with the task's frame (as any unexpected failure inside a task step would be)
        using (DiagnosticContext.Begin(corr, taskId: taskId.ToString("D")))
        {
            var lg = h.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WCS.Application.Services.TaskOrchestrator");
            try { throw new InvalidOperationException("post-commit step failed (Password=Sup3rSecret)"); }
            catch (Exception ex) { lg.LogError(ex, "Post-commit action failed for task {TaskId}. {EventCode}", taskId, "POST_COMMIT_FAILED"); }
        }
        await sink.FlushAsync(CancellationToken.None);

        // ---- export the whole system, then read ONLY the package
        var dir = TempDir(); var zip = Path.Combine(dir, "pkg.zip");
        await using (var scope = h.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>().ExportAsync(Guid.NewGuid(), new SystemExportRequest("custom", h.Clock.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddHours(1), new DiagScope()), "admin1", zip, (_, _) => { }, CancellationToken.None);
        }

        var events = Lines(zip, "Events/");
        var chain = events.Where(e => Str(e, "correlationId") == corr).ToList();
        // order within the logging pipeline (one clock). The harness stamps task history with its TestClock, so the order BETWEEN sources is only meaningful on a real clock: here the log events carry the story.
        var story = chain.Where(e => Str(e, "source") == "system_log").ToList();
        string[] names = story.Select(e => Str(e, "eventName")!).ToArray();
        output.WriteLine("story (" + story.Count + " of " + chain.Count + " events with this id): " + string.Join(" -> ", names));

        // 1) every module of the task's life shares the id
        var sources = chain.Select(e => Str(e, "source")).ToHashSet();
        Assert.Superset(new HashSet<string?> { "system_log", "task_history", "inventory_transactions" }, sources);
        // 2) the order of the story is recoverable from the package: created -> AGV decision -> RCS create accepted -> RCS continue refused -> task finished Failed
        int At(string n, int from = 0) { var i = Array.IndexOf(names, n, from); Assert.True(i >= 0, "missing event " + n + " in: " + string.Join(", ", names)); return i; }
        var created = At("TASK_CREATED"); var decision = At("TASK_DISPATCH_DECISION", created); var rcsCreate = At("RCS_CALL", decision);
        var rcsRefused = At("RCS_CALL_FAILED", rcsCreate); var finished = At("TASK_FINISHED", rcsRefused);
        Assert.True(created < decision && decision < rcsCreate && rcsCreate < rcsRefused && rcsRefused < finished);
        var refused = story[rcsRefused]; Assert.Equal("Rcs", Str(refused, "module")); Assert.Equal(taskId.ToString("D"), Str(refused, "taskId")); Assert.Contains("Simulated rejection", Str(refused, "message"));
        var fin = story[finished]; Assert.Equal("Task", Str(fin, "module")); Assert.Contains("Failed", Str(fin, "message"));
        // 3) the exception and its stack trace, with module, task and correlation
        var err = Lines(zip, "Errors/").Single(e => Str(e, "eventName") == "POST_COMMIT_FAILED");
        Assert.Equal(("System.InvalidOperationException", taskId.ToString("D"), corr), (Str(err, "exceptionType"), Str(err, "taskId"), Str(err, "correlationId")));
        Assert.Contains("at WCS.Tests.Integration.SystemDiagnosticScenarioTests", Str(err, "stackTrace"));
        // 4) the PLC communication error is in the package as a PLC event with its code and stage, and is NOT claimed to belong to the task (no basis for the link)
        var plcErr = events.Where(e => Str(e, "module") == "Plc" && Str(e, "level") is "Warning" or "Error").ToList();
        Assert.NotEmpty(plcErr); Assert.All(plcErr, e => Assert.Null(Str(e, "correlationId")));
        Assert.Contains(plcErr, e => (Str(e, "message") ?? "").Contains("4031") || (Str(e, "errorCode") ?? "").Length > 0);
        Assert.Contains("PLC/PLC_Communication.log", ZipFile.OpenRead(zip).Entries.Select(x => x.FullName));
        // 5) no secret anywhere, and the Summary points at the task that went wrong
        using (var z = ZipFile.OpenRead(zip))
            foreach (var en in z.Entries) { using var r = new StreamReader(en.Open(), Encoding.UTF8); Assert.DoesNotContain("Sup3rSecret", await r.ReadToEndAsync()); }
        Assert.Contains(taskId.ToString("D"), Text(zip, "Summary.txt"));
        // 6) the manifest is truthful: every source read, nothing silently missing
        var m = JsonDocument.Parse(Text(zip, "Manifest.json")).RootElement;
        Assert.DoesNotContain(m.GetProperty("sources").EnumerateArray(), s => s.GetProperty("status").GetString() == "Failed");
        // 7) a CorrelationId-scoped export returns exactly this story and no other task
        var scopedZip = Path.Combine(dir, "scoped.zip");
        await using (var scope = h.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>().ExportAsync(Guid.NewGuid(), new SystemExportRequest("custom", h.Clock.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddHours(1), new DiagScope { CorrelationId = corr }), "admin1", scopedZip, (_, _) => { }, CancellationToken.None);
        Assert.All(Lines(scopedZip, "Events/"), e => Assert.Equal(corr, Str(e, "correlationId")));
        Assert.True(Lines(scopedZip, "Events/").Count >= created);

        try { Directory.Delete(dir, true); } catch (IOException) { }
        await h.Services.GetRequiredService<IPlcRuntime>().StopAsync();
        _ = plcErrorWindowStart;
    }

    private static async Task Wait(Func<Task<bool>> cond, int ms = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (await cond()) return; await Task.Delay(50); }
        Assert.True(await cond(), "condition not reached in time");
    }

    // ------------------------------------------------------------------------------------------------ volume, time, memory
    [Fact]
    public async Task A_large_export_streams_in_flat_memory_within_the_limits_and_says_what_it_left_out()
    {
        await using var h = await Harness.CreateAsync(fixture);
        const int system = 300_000, plcRows = 200_000;
        await h.Db(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(@"INSERT INTO system_log (""Id"",""OccurredAt"",""Category"",""Level"",""Source"",""Device"",""EventCode"",""Message"",""Exception"",""CorrelationId"",""DetailsJson"",""Host"",""ExternalId"",""MessageTemplate"")
                SELECT gen_random_uuid(), now() - (g || ' seconds')::interval, 'SYSTEM', CASE WHEN g % 50 = 0 THEN 'Error' ELSE 'Warning' END, 'WCS.Application.Services.TaskOrchestrator', NULL, 'LOAD_' || (g % 20),
                       'synthetic load event number ' || g || ' with some realistic padding text to give the row a typical size', NULL, 'corr-' || (g % 1000), '{{""taskId"":""t-' || (g % 500) || '""}}', 'h', NULL, 'synthetic'
                FROM generate_series(1, {0}) g", system);
            await db.Database.ExecuteSqlRawAsync(@"INSERT INTO plc_comm_log (""Id"",""OccurredAt"",""PlcId"",""StationId"",""Level"",""Event"",""Message"",""DetailsJson"",""DiagCode"",""Stage"")
                SELECT gen_random_uuid(), now() - (g || ' seconds')::interval, 'PLC-0' || (1 + g % 4), 'A1', 1 + (g % 3), 'READ_BAD', 'synthetic PLC event ' || g, NULL, 'END_CODE_ERROR', 'MC' FROM generate_series(1, {0}) g", plcRows);
            return 0;
        });

        long peak = 0; using var stop = new CancellationTokenSource();
        var proc = Process.GetCurrentProcess();
        GC.Collect(); GC.WaitForPendingFinalizers(); var baseline = GC.GetTotalMemory(true);
        var sampler = Task.Run(async () => { while (!stop.IsCancellationRequested) { peak = Math.Max(peak, GC.GetTotalMemory(false)); try { await Task.Delay(25, stop.Token); } catch (OperationCanceledException) { } } });
        var dir = TempDir(); var zip = Path.Combine(dir, "big.zip");
        var sw = Stopwatch.StartNew(); ExportOutcome outcome;
        await using (var scope = h.Services.CreateAsyncScope())
            outcome = await scope.ServiceProvider.GetRequiredService<SystemDiagnosticExporter>().ExportAsync(Guid.NewGuid(), new SystemExportRequest("all", null, null, new DiagScope()), "admin1", zip, (_, _) => { }, CancellationToken.None);
        sw.Stop(); stop.Cancel(); await sampler;
        var growthMb = (peak - baseline) / 1024.0 / 1024.0; var zipMb = new FileInfo(zip).Length / 1024.0 / 1024.0;
        output.WriteLine($"events={outcome.Events:N0} zip={zipMb:F1} MB time={sw.Elapsed.TotalSeconds:F1} s managed-heap growth={growthMb:F0} MB completeness={outcome.Completeness}");

        Assert.True(outcome.Events >= system + plcRows - 10, $"events {outcome.Events}");                // nothing left out below the limit
        Assert.Equal("Complete", outcome.Completeness);
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(3), $"export took {sw.Elapsed}");
        Assert.True(growthMb < 400, $"managed heap grew {growthMb:F0} MB: the export is not streaming");  // 500k events would need > 1 GB if held in memory
        Assert.True(zipMb < 400);

        // the same volume with a per-source limit: the NEWEST rows are kept, the omission is stated in the manifest
        await using var scope2 = h.Services.CreateAsyncScope();
        var opts = Microsoft.Extensions.Options.Options.Create(new SystemDiagnosticOptions { MaxEventsPerSource = 50_000 });
        var limited = new SystemDiagnosticExporter(scope2.ServiceProvider.GetServices<IDiagnosticEventSource>(), scope2.ServiceProvider.GetRequiredService<IDiagnosticHealthSource>(), scope2.ServiceProvider.GetRequiredService<DiagnosticExportService>(),
            scope2.ServiceProvider.GetRequiredService<IPlcRuntime>(), scope2.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), opts, scope2.ServiceProvider.GetRequiredService<IClock>());
        var zip2 = Path.Combine(dir, "limited.zip");
        var o2 = await limited.ExportAsync(Guid.NewGuid(), new SystemExportRequest("all", null, null, new DiagScope()), "admin1", zip2, (_, _) => { }, CancellationToken.None);
        Assert.Equal("Partial", o2.Completeness);
        var sl = o2.Sources.Single(s => s.Name == "system_log");
        Assert.Equal(("Partial", 50_000L, (long)(system - 50_000)), (sl.Status, sl.RowsExported, sl.RowsOmittedOldest));
        Assert.Contains($"{system - 50_000} older rows", Text(zip2, "Summary.txt"));
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
