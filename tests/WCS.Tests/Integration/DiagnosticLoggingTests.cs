using Microsoft.Extensions.Configuration;
using WCS.Application;
using WCS.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog.Events;
using WCS.Api.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Diagnostics;
using WCS.Domain.Common;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Simulation;
using WCS.Runtime;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-021 batch C: the diagnostic events that were missing - RCS calls, task decisions, station requests, AGV status changes, inventory, database, API, SignalR, application life - with their ids, and the guards they must not weaken.</summary>
[Collection("Database")]
public class DiagnosticLoggingTests(DatabaseFixture fixture)
{
    public sealed record Entry(string Category, LogLevel Level, string Message, string? EventCode, IReadOnlyDictionary<string, object?> Props, DiagFrame? Frame);

    /// <summary>Records every log call together with the diagnostic frame that was active when it was written (what the Serilog enricher would copy onto it).</summary>
    public sealed class Capture : ILoggerProvider
    {
        public List<Entry> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Cap(this, categoryName);
        public void Dispose() { }
        public IEnumerable<Entry> With(string code) { lock (Entries) return Entries.Where(e => e.EventCode == code).ToList(); }
        private sealed class Cap(Capture owner, string cat) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt)
            {
                var props = new Dictionary<string, object?>();
                if (state is IEnumerable<KeyValuePair<string, object?>> kv) foreach (var p in kv) props[p.Key] = p.Value;
                lock (owner.Entries) owner.Entries.Add(new Entry(cat, level, fmt(state, ex), props.GetValueOrDefault("EventCode") as string, props, DiagnosticContext.Current));
            }
        }
    }

    private async Task<(Harness H, Capture Log)> ReadyAsync(Action<IServiceCollection>? more = null)
    {
        var cap = new Capture();
        var h = await Harness.CreateAsync(fixture, s => { s.AddSingleton<ILoggerProvider>(cap); more?.Invoke(s); });
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        return (h, cap);
    }

    // ------------------------------------------------------------------------------------------------ RCS
    [Fact]
    public async Task Every_RCS_call_is_logged_with_operation_request_code_outcome_duration_and_adapter_under_the_tasks_ids()
    {
        var (h, log) = await ReadyAsync();
        await using var _h = h;
        var task = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var corr = (await h.GetTaskAsync(task.Id)).CorrelationId;
        var create = Assert.Single(log.With("RCS_CALL"), e => (string?)e.Props["Operation"] == "CREATE");
        Assert.Equal(LogLevel.Information, create.Level);
        Assert.EndsWith("LoggingRcsClient", create.Category);
        Assert.Equal("RcsSimulator", create.Props["Adapter"]);                                                 // a simulated answer is never mistaken for a real device
        Assert.StartsWith("WCS-", (string)create.Props["RequestCode"]!); Assert.True((long)create.Props["DurationMs"]! >= 0);
        Assert.Equal((corr, task.Id.ToString("D")), (create.Frame?.CorrelationId, create.Frame?.TaskId));   // the AGV is named in the task's creation and decision events

        // a refusal and a timeout are warnings with the outcome, and the task's own flow is untouched
        var (h2, log2) = await ReadyAsync();
        await using var _h2 = h2;
        h2.Rcs.NextFault = RcsFault.RejectNextCreate;
        var rejected = (await h2.CreateTaskAsync("BP01", "A1")).Task;
        var fail = Assert.Single(log2.With("RCS_CALL_FAILED"));
        Assert.Equal(LogLevel.Warning, fail.Level); Assert.Equal("Rejected", fail.Props["Outcome"]?.ToString()); Assert.Contains("Simulated rejection", fail.Message);
        Assert.Equal(rejected.Id.ToString("D"), fail.Frame?.TaskId);
    }

    [Fact]
    public async Task The_Production_guard_still_refuses_the_simulator_when_it_is_wrapped_by_the_logging_decorator()
    {
        var (h, _) = await ReadyAsync(s => s.AddSingleton<IHostEnvironment>(new FakeProdEnv()));
        await using var _h = h;
        Assert.IsType<LoggingRcsClient>(h.Services.GetRequiredService<IRcsClient>());
        Assert.IsType<RcsSimulator>(IRcsClientDecorator.Unwrap(h.Services.GetRequiredService<IRcsClient>()));
        var guard = h.Services.GetRequiredService<IRcsDispatchGuard>().Check();
        Assert.False(guard.Allowed); Assert.Equal("RCS_NOT_CONNECTED", guard.Code);
        var ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => h.CreateTaskAsync("BP01", "A1"));
        Assert.Equal("RCS_NOT_CONNECTED", ex.Code);
        Assert.Equal(0, h.Rcs.CreatedTaskCount);
    }

    private sealed class FakeProdEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production"; public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "."; public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    // ------------------------------------------------------------------------------------------------ task decisions
    [Fact]
    public async Task The_task_life_leaves_creation_decision_RCS_and_finish_events_that_share_one_correlation_and_one_task_id()
    {
        var (h, log) = await ReadyAsync();
        await using var _h = h;
        var task = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(task.Id, TaskState.Completed);
        var corr = (await h.GetTaskAsync(task.Id)).CorrelationId;
        var id = task.Id.ToString("D");

        var created = Assert.Single(log.With("TASK_CREATED"));
        Assert.Contains("flow", created.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((corr, id, "AGV1", "A1"), (created.Frame?.CorrelationId, created.Frame?.TaskId, created.Frame?.AgvId, created.Frame?.StationId));
        var decision = Assert.Single(log.With("TASK_DISPATCH_DECISION"));
        Assert.Contains("AGV1", decision.Message); Assert.Equal(id, decision.Frame?.TaskId);
        var finished = Assert.Single(log.With("TASK_FINISHED"));
        Assert.Equal(LogLevel.Information, finished.Level); Assert.Equal("Completed", finished.Props["State"]?.ToString());
        Assert.All(new[] { created, decision, finished }.Concat(log.With("RCS_CALL")), e => Assert.Equal(corr, e.Frame?.CorrelationId));   // ONE id across the creation, the decision, every RCS call and the end
    }

    [Fact]
    public async Task A_rejected_creation_a_busy_AGV_and_a_failed_task_say_why_without_changing_the_outcome()
    {
        var (h, log) = await ReadyAsync();
        await using var _h = h;
        var ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => h.CreateTaskAsync("BP99", "A1"));      // not an approved flow
        var rej = Assert.Single(log.With("TASK_CREATE_REJECTED"));
        Assert.Equal(LogLevel.Warning, rej.Level); Assert.Equal(ex.Code, rej.Props["Code"]); Assert.Equal("BP99", rej.Props["Flow"]);

        // two requests for the same AGV: the second waits (PER_AGV) and the reason is logged ONCE, not on every scheduler tick
        await h.StockAsync("A2", RackKind.Full, 1);
        var first = (await h.CreateTaskAsync("BP01", "A1")).Task;
        var second = (await h.CreateTaskAsync("BP01", "A2")).Task;
        await h.TickAsync(); await h.TickAsync(); await h.TickAsync();
        var wait = log.With("TASK_DISPATCH_WAIT").ToList();
        Assert.Single(wait);
        Assert.Equal("AGV_BUSY_PER_AGV", wait[0].Props["Reason"]); Assert.Equal(second.Id.ToString("D"), wait[0].Frame?.TaskId);
        Assert.Equal("Queued", (await h.GetTaskAsync(second.Id)).State.ToString());                                             // behaviour unchanged

        await h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(second.Id, "no longer needed", "operator1"));   // still queued: cancelled at once
        var req = Assert.Single(log.With("TASK_CANCEL_REQUESTED"));
        Assert.Equal("operator1", req.Props["User"]); Assert.Equal("no longer needed", req.Props["Reason"]); Assert.Equal(second.Id.ToString("D"), req.Frame?.TaskId);
        var cancelled = Assert.Single(log.With("TASK_FINISHED"), e => e.Props["State"]?.ToString() == "Cancelled");
        Assert.Contains("no longer needed", cancelled.Message); Assert.Equal(second.Id.ToString("D"), cancelled.Frame?.TaskId);
        Assert.Equal(first.Id, (await h.GetTaskAsync(first.Id)).Id);
    }

    // ------------------------------------------------------------------------------------------------ station requests, AGV, inventory
    [Fact]
    public async Task A_PLC_station_request_gets_its_own_correlation_that_the_task_it_creates_keeps()
    {
        var (h, log) = await ReadyAsync();
        await using var _h = h;
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));
        await h.IntakeAsync();
        var acc = Assert.Single(log.With("STATION_REQUEST_ACCEPTED"));
        var taskId = Guid.Parse(acc.Props["TaskId"]!.ToString()!);
        var t = await h.GetTaskAsync(taskId);
        Assert.False(string.IsNullOrEmpty(t.CorrelationId));
        Assert.Equal(("A1", t.CorrelationId), (acc.Frame?.StationId, log.With("TASK_CREATED").Single().Frame?.CorrelationId));   // the PLC request and the task share the id; the station is the frame's station
    }

    [Fact]
    public async Task An_AGV_status_change_is_one_event_and_a_steady_status_is_not_logged_again()
    {
        var (h, log) = await ReadyAsync();
        await using var _h = h;
        await h.RunAsync(sp => sp.GetRequiredService<WCS.Application.Services.AgvStatusService>().RefreshAsync());
        var first = log.With("AGV_STATUS_CHANGED").Count();
        await h.RunAsync(sp => sp.GetRequiredService<WCS.Application.Services.AgvStatusService>().RefreshAsync());
        Assert.Equal(first, log.With("AGV_STATUS_CHANGED").Count());                                                              // no second line for an unchanged status
    }

    // ------------------------------------------------------------------------------------------------ database
    [Fact]
    public void Database_errors_are_classified_by_their_PostgreSQL_code_and_never_by_their_detail_text()
    {
        static Exception Pg(string state) => new PostgresException("text with a secret value", "ERROR", "ERROR", state);
        Assert.Equal("DB_DEADLOCK", DbDiagnosticsInterceptor.Classify(Pg("40P01")).Code);
        Assert.Equal("DB_CONSTRAINT_VIOLATION", DbDiagnosticsInterceptor.Classify(Pg("23505")).Code);
        Assert.Equal("DB_TIMEOUT", DbDiagnosticsInterceptor.Classify(Pg("57014")).Code);
        Assert.Equal("DB_CONNECTION_FAILURE", DbDiagnosticsInterceptor.Classify(Pg("08006")).Code);
        Assert.Equal("DB_SERIALIZATION_FAILURE", DbDiagnosticsInterceptor.Classify(Pg("40001")).Code);
        Assert.Equal("DB_COMMAND_FAILED", DbDiagnosticsInterceptor.Classify(Pg("42P01")).Code);
        Assert.Equal("DB_TIMEOUT", DbDiagnosticsInterceptor.Classify(new InvalidOperationException("x", new TimeoutException())).Code);
        Assert.DoesNotContain("secret", DbDiagnosticsInterceptor.Classify(Pg("23505")).Reason);
    }

    [Fact]
    public async Task A_slow_command_and_a_failed_command_are_logged_with_the_command_text_but_never_with_parameter_values()
    {
        var cap = new Capture();
        using var lf = LoggerFactory.Create(b => b.AddProvider(cap));
        var opts = new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(new DbDiagnosticsInterceptor(lf.CreateLogger<DbDiagnosticsInterceptor>(), 20)).Options;
        await using var db = new WcsDbContext(opts);
        var secret = "Sup3rSecretValue";
        await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.1)");
        await db.Database.ExecuteSqlRawAsync("SELECT {0}::text, pg_sleep(0.1)", secret);
        var slow = cap.With("DB_SLOW_QUERY").ToList();
        Assert.True(slow.Count >= 2);
        Assert.All(slow, e => { Assert.Equal(LogLevel.Warning, e.Level); Assert.True((long)e.Props["DurationMs"]! >= 20); Assert.DoesNotContain(secret, e.Message); });
        Assert.Contains(slow, e => e.Message.Contains("pg_sleep"));

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("SELECT 1 FROM table_that_does_not_exist_" + secret.Length));
        var failed = Assert.Single(cap.With("DB_COMMAND_FAILED"));
        Assert.Equal(LogLevel.Error, failed.Level); Assert.Equal("42P01", failed.Props["SqlState"]);
    }

    [Fact]
    public void An_event_code_decides_the_module_before_the_message_text_does()
    {
        // a task event whose text mentions RCS is still a TASK event (the old text-based category would have called it RCS)
        Assert.Equal("Task", DiagModules.ForSystemLog("RCS_AGV", "WCS.Application.Services.TaskOrchestrator", "TASK_FINISHED"));
        Assert.Equal("Rcs", DiagModules.ForSystemLog("RCS_AGV", "WCS.Infrastructure.Simulation.LoggingRcsClient", "RCS_CALL_FAILED"));
        Assert.Equal("Station", DiagModules.ForSystemLog("SYSTEM", "WCS.Application.Services.StationRequestProcessor", "STATION_REQUEST_REJECTED"));
        Assert.Equal("Inventory", DiagModules.ForSystemLog("SYSTEM", "WCS.Application.Services.InventoryService", "INVENTORY_UNCERTAIN"));
        Assert.Equal("Database", DiagModules.ForSystemLog("DATABASE", "WCS.Infrastructure.Persistence.DbDiagnosticsInterceptor", "DB_DEADLOCK"));
        Assert.Equal("Api", DiagModules.ForSystemLog("SYSTEM", "Serilog.AspNetCore.RequestLoggingMiddleware", "API_ACCESS_DENIED"));
        Assert.Equal("System", DiagModules.ForSystemLog("SYSTEM", "WCS.Runtime.ApplicationLifecycleHostedService", "APP_STARTED"));
        Assert.Equal("Database", DiagModules.ForSystemLog("DATABASE", "WCS.Runtime.DbHealthMonitorHostedService", "DB_LOST"));      // codes without a CR-021 prefix fall back to the category as before
        Assert.Null(DiagModules.ForEventCode("SOMETHING_ELSE"));
    }

    // ------------------------------------------------------------------------------------------------ API, SignalR, application life, configuration
    [Fact]
    public void Requests_are_stored_only_when_slow_denied_or_failed_and_carry_a_stable_event_code()
    {
        static HttpContext Ctx(string path, int status) { var c = new DefaultHttpContext(); c.Request.Path = path; c.Response.StatusCode = status; c.Items[LogSetup.RequestStartKey] = System.Diagnostics.Stopwatch.GetTimestamp(); return c; }
        LogSetup.SlowRequestMs = 1000;
        Assert.Equal(LogEventLevel.Warning, LogSetup.RequestLevel(Ctx("/api/tasks", 200), 1000, null));        // slow
        Assert.Equal(LogEventLevel.Information, LogSetup.RequestLevel(Ctx("/api/tasks", 200), 999, null));
        Assert.Equal(LogEventLevel.Information, LogSetup.RequestLevel(Ctx("/api/tasks", 404), 5, null));       // business answers are not stored
        Assert.Equal(LogEventLevel.Warning, LogSetup.RequestLevel(Ctx("/api/plc/points", 403), 5, null));
        Assert.Equal(LogEventLevel.Debug, LogSetup.RequestLevel(Ctx("/healthz", 200), 5000, null));            // a probe is never stored, however slow
        Assert.Equal(LogEventLevel.Information, LogSetup.RequestLevel(Ctx("/hubs/wcs", 101), 887_254, null)); // a SignalR connection that stays open for hours is not a slow request
    }

    [Fact]
    public async Task The_SignalR_hub_logs_connect_and_a_disconnect_with_an_error_as_a_warning()
    {
        var cap = new Capture(); using var lf = LoggerFactory.Create(b => b.AddProvider(cap));
        var hub = new WCS.Api.Api.WcsHub(lf.CreateLogger<WCS.Api.Api.WcsHub>()) { Context = new FakeHubContext() };
        hub.Groups = new NoGroups();
        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(null);
        await hub.OnDisconnectedAsync(new IOException("socket closed"));
        Assert.Contains("admin1", Assert.Single(cap.With("SIGNALR_CONNECTED")).Message);
        Assert.Equal(LogLevel.Information, Assert.Single(cap.With("SIGNALR_DISCONNECTED")).Level);
        var err = Assert.Single(cap.With("SIGNALR_DISCONNECTED_ERROR"));
        Assert.Equal(LogLevel.Warning, err.Level);
    }

    private sealed class FakeHubContext : Microsoft.AspNetCore.SignalR.HubCallerContext
    {
        public override string ConnectionId => "c1"; public override string? UserIdentifier => "u";
        public override System.Security.Claims.ClaimsPrincipal? User { get; } = new(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin1"), new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")], "test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class Life : IHostApplicationLifetime
    {
        public CancellationTokenSource Started = new(), Stopping = new(), Stopped = new();
        CancellationToken IHostApplicationLifetime.ApplicationStarted => Started.Token;
        CancellationToken IHostApplicationLifetime.ApplicationStopping => Stopping.Token;
        CancellationToken IHostApplicationLifetime.ApplicationStopped => Stopped.Token;
        public void StopApplication() { }
    }

    private sealed class NoGroups : Microsoft.AspNetCore.SignalR.IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task The_real_runtime_registrations_pass_container_validation_so_no_background_worker_can_stop_the_application_from_starting()
    {
        // regression: a hosted service that asked for every IHostedService in its constructor was a circular dependency. Development validates the container at start-up, so the whole backend failed to start;
        // the test factory removes the workers, so only the REAL registrations (AddWcsRuntime) reveal it.
        await using var h = await Harness.CreateAsync(fixture);
        var config = new ConfigurationBuilder().AddConfiguration(fixture.Configuration)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:WcsDatabase"] = fixture.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddWcsApplication(config); services.AddWcsInfrastructure(config); services.AddWcsRuntime(config);
        services.AddSingleton<IHostEnvironment>(new FakeProdEnv { EnvironmentName = "Development" });
        services.AddSingleton<IHostApplicationLifetime>(new Life());
        services.AddSingleton<IClock>(h.Clock);
        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });   // throws on a circular or missing dependency
        var workers = sp.GetServices<IHostedService>().Select(w => w.GetType().Name).ToList();
        Assert.Contains("ApplicationLifecycleHostedService", workers);
        Assert.Contains("SchedulerHostedService", workers);
    }

    private sealed class W1 : IHostedService { public Task StartAsync(CancellationToken c) => Task.CompletedTask; public Task StopAsync(CancellationToken c) => Task.CompletedTask; }

    [Fact]
    public async Task Application_start_and_stop_are_logged_with_the_list_of_background_workers()
    {
        var cap = new Capture(); using var lf = LoggerFactory.Create(b => b.AddProvider(cap)); var life = new Life();
        using var sp = new ServiceCollection().AddSingleton<IHostedService>(new W1()).AddSingleton<IHostedService>(new W1()).BuildServiceProvider();
        var svc = new ApplicationLifecycleHostedService(sp, life, lf.CreateLogger<ApplicationLifecycleHostedService>());
        await svc.StartAsync(CancellationToken.None);
        Assert.Empty(cap.With("APP_STARTED"));
        life.Started.Cancel(); life.Stopping.Cancel(); life.Stopped.Cancel();
        var started = Assert.Single(cap.With("APP_STARTED"));
        Assert.Contains("W1", started.Message); Assert.Single(cap.With("APP_STOPPING")); Assert.Single(cap.With("APP_STOPPED"));
    }
}
