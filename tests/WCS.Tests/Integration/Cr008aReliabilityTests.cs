using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Infrastructure.Simulation;
using WCS.Runtime;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-008A: dispatch / scheduler reliability (AUDIT-001, 002, 003, 004, 006).</summary>
[Collection("Database")]
public class Cr008aReliabilityTests(DatabaseFixture fixture)
{
    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "WCS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>A guard a test can switch, to prove that dispatch stops when the link is declared not connected.</summary>
    private sealed class SwitchGuard : IRcsDispatchGuard
    {
        public bool Allowed { get; set; } = true;
        public RcsDispatchStatus Check() => Allowed ? new(true, "OK", "") : new(false, "RCS_NOT_CONNECTED", "test");
    }

    private static async Task<Harness> ReadyAsync(DatabaseFixture f, Action<IServiceCollection>? configure = null)
    {
        var h = await Harness.CreateAsync(f, configure);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        return h;
    }

    private static void Backoff(Harness h, int initialMs = 1000, int maxMs = 8000, int maxAttempts = 30, int maxDispatch = 5)
    {
        h.SchedulerOptions.RcsRetryInitialDelayMs = initialMs;
        h.SchedulerOptions.RcsRetryMaxDelayMs = maxMs;
        h.SchedulerOptions.RcsMaxAttempts = maxAttempts;
        h.SchedulerOptions.MaxDispatchAttempts = maxDispatch;
    }

    private static int RcsCalls(Harness h) => h.Rcs.CreateCalls + h.Rcs.ContinueCalls + h.Rcs.CancelCalls + h.Rcs.QueryCalls;


    private static Task<TaskDto> CancelAsync(Harness h, Guid id) =>
        h.Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().CancelTaskAsync(id, "cr008a test", "tester"));

    /// <summary>A task accepted by the simulated RCS and then cancelled by an operator: it waits in CANCEL_PENDING.</summary>
    private static async Task<(TransportTask Task, string RequestCode)> CancelPendingAsync(Harness h)
    {
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Accepted", t.State);
        var pending = await CancelAsync(h, t.Id);
        Assert.Equal("CancelPending", pending.State);
        return (await h.GetTaskAsync(t.Id), pending.RcsRequestCode);
    }

    // ============================================================================================ AUDIT-001
    [Fact]
    public async Task Production_with_only_the_simulator_refuses_the_task_and_never_calls_rcs()
    {
        await using var h = await ReadyAsync(fixture, s => s.AddSingleton<IHostEnvironment>(new FakeEnv("Production")));

        var ex = await Assert.ThrowsAsync<AppException>(() => h.CreateTaskAsync("BP01", "A1"));
        Assert.Equal("RCS_NOT_CONNECTED", ex.Code);
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Empty(await h.ActiveReservationsAsync());                       // nothing reserved
        Assert.Equal(0, RcsCalls(h));
        Assert.Equal(0, h.Rcs.CreatedTaskCount);
        Assert.False(h.Services.GetRequiredService<IRcsDispatchGuard>().Check().Allowed);
    }

    [Fact]
    public async Task Production_explicit_opt_in_allows_the_supervised_simulator_test()
    {
        var guard = new RcsDispatchGuard(new RcsSimulator(), new FakeEnv("Production"), allowSimulatorInProduction: true);
        Assert.True(guard.Check().Allowed);
        Assert.False(new RcsDispatchGuard(new RcsSimulator(), new FakeEnv("Production"), false).Check().Allowed);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public async Task Non_production_environments_keep_the_simulator_behaviour(string environment)
    {
        await using var h = await ReadyAsync(fixture, s => s.AddSingleton<IHostEnvironment>(new FakeEnv(environment)));
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Accepted", t.State);
        Assert.Equal(1, h.Rcs.CreatedTaskCount);
        await h.AdvanceUntilAsync(t.Id, TaskState.Completed);
    }

    [Fact]
    public async Task Without_any_host_environment_the_default_still_dispatches()
    {
        await using var h = await ReadyAsync(fixture);
        Assert.Equal("Accepted", (await h.CreateTaskAsync("BP01", "A1")).Task.State);
    }

    [Fact]
    public async Task Dispatch_of_an_existing_task_stops_when_the_link_is_declared_not_connected()
    {
        var guard = new SwitchGuard();
        await using var h = await ReadyAsync(fixture, s => s.AddSingleton<IRcsDispatchGuard>(guard));
        Backoff(h);
        h.Rcs.Unreachable = true;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;               // outcome unknown -> Dispatching
        Assert.Equal("Dispatching", t.State);
        var before = RcsCalls(h);

        guard.Allowed = false;
        for (var i = 0; i < 5; i++) { h.Clock.Advance(TimeSpan.FromSeconds(30)); await h.TickAsync(); }

        Assert.Equal(before, RcsCalls(h));                                     // no call at all while not connected
        Assert.Equal(TaskState.Dispatching, (await h.GetTaskAsync(t.Id)).State);
    }

    [Fact]
    public async Task Production_shows_the_agv_as_unknown_not_as_the_simulated_online()
    {
        await using var h = await ReadyAsync(fixture, s => s.AddSingleton<IHostEnvironment>(new FakeEnv("Production")));
        await h.RunAsync(sp => sp.GetRequiredService<AgvStatusService>().RefreshAsync());
        var agv = await h.AgvAsync("AGV1");                                    // seeded WITH a robot code ("R-FULL")
        Assert.Equal(AgvConnectionStatus.Unknown, agv.ConnectionStatus);
        Assert.Equal("RCS_NOT_CONNECTED", agv.StatusDetail);
        Assert.Equal(0, h.Rcs.QueryCalls);

        await using var dev = await ReadyAsync(fixture);
        await dev.RunAsync(sp => sp.GetRequiredService<AgvStatusService>().RefreshAsync());
        Assert.Equal(AgvConnectionStatus.Online, (await dev.AgvAsync("AGV1")).ConnectionStatus);   // Development: unchanged
    }

    // ============================================================================================ AUDIT-002
    private sealed class FailingStations(bool failPoll) : IStationSignalService
    {
        public int Polls, Acks;
        public List<string> Acked { get; } = [];
        public Task<IReadOnlyList<StationRequest>> PollRequestsAsync(CancellationToken ct = default)
        {
            Polls++;
            if (failPoll) throw new InvalidOperationException("PLC intake exploded");
            return Task.FromResult<IReadOnlyList<StationRequest>>([new("A1", "BP01", "k1"), new("A1", "BP01", "boom"), new("A2", "BP01", "k3")]);
        }
        public Task AcknowledgeRequestAsync(StationRequest request, CancellationToken ct = default) { Acks++; Acked.Add(request.RequestKey); return Task.CompletedTask; }
        public Task RequestTransferPermissionAsync(string stationCode, TransferDirection direction, Guid taskId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DirectUnitOfWork : IUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => work(ct);
    }

    private sealed class ThrowingRequestRepository : IStationRequestRepository
    {
        public Task<WCS.Domain.Intake.StationRequestRecord?> GetByKeyAsync(string requestKey, CancellationToken ct = default) => Task.FromResult<WCS.Domain.Intake.StationRequestRecord?>(null);
        public Task<IReadOnlyList<WCS.Domain.Intake.StationRequestRecord>> ListPendingAsync(int take, CancellationToken ct = default) => throw new InvalidOperationException("intake store exploded");
        public void Add(WCS.Domain.Intake.StationRequestRecord record) { }
    }

    private sealed class RecordingScheduler : ITaskScheduler
    {
        public int Runs;
        public Task RunOnceAsync(CancellationToken ct = default) { Interlocked.Increment(ref Runs); return Task.CompletedTask; }
    }

    private sealed class StubOrchestrator : ITaskOrchestrator
    {
        public List<string> Created { get; } = [];
        public Task<CreateTaskResult> CreateTaskAsync(CreateTaskCommand c, CancellationToken ct = default)
        {
            if (c.RequestKey == "boom") throw new DataConflictException("persistent conflict");
            Created.Add(c.RequestKey);
            return Task.FromResult<CreateTaskResult>(null!);
        }
        public Task<SignalResult> HandleSignalAsync(TaskSignal s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskDto> CancelTaskAsync(Guid id, string r, string? u, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskDto> UpdateTaskAsync(Guid id, string f, string s, string? e, WCS.Application.Auth.ActorInfo a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TaskDto> DeleteTaskAsync(Guid id, string? e, WCS.Application.Auth.ActorInfo a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ProcessTaskAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task TryDispatchNextAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DispatchToRcsAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task TryResolveDestinationAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendContinueAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReconcileCancelPendingAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnforceTimeoutAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Scheduler_keeps_running_while_the_station_intake_fails_every_tick()
    {
        var stations = new FailingStations(failPoll: true);
        var scheduler = new RecordingScheduler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IStationSignalService>(stations);
        services.AddSingleton<ITaskOrchestrator>(new StubOrchestrator());
        services.AddScoped<StationRequestProcessor>();
        services.AddSingleton<IRcsDispatchGuard>(new SwitchGuard());
        services.AddSingleton<IMaintenanceGate>(new OpenMaintenanceGate());   // CR-026: the processor now asks the Maintenance gate (always open here)
        services.AddSingleton<IClock>(new TestClock());
        services.AddSingleton<IUnitOfWork, DirectUnitOfWork>();
        services.AddSingleton<IStationRequestRepository, ThrowingRequestRepository>();   // CR-008B-2: the processor itself now fails -> the host must still run the scheduler
        services.AddSingleton<ITaskScheduler>(scheduler);
        services.Configure<SchedulerOptions>(o => o.TickIntervalMs = 100);
        await using var provider = services.BuildServiceProvider();

        var hosted = new SchedulerHostedService(provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new SchedulerOptions { TickIntervalMs = 100 }), NullLogger<SchedulerHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref scheduler.Runs) < 3 && DateTime.UtcNow < deadline) await Task.Delay(50);
        await hosted.StopAsync(CancellationToken.None);

        Assert.True(stations.Polls >= 3, "the intake really failed on every tick");
        Assert.True(scheduler.Runs >= 3, $"scheduler passes ran despite the intake failure (runs={scheduler.Runs})");
    }

    // ============================================================================================ AUDIT-003
    [Fact]
    public async Task Dispatch_retries_are_spaced_by_the_backoff_not_made_on_every_tick()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 8000, maxDispatch: 10);
        h.Rcs.Unreachable = true;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Dispatching", t.State);
        Assert.Equal(1, h.Rcs.CreateCalls);

        for (var i = 0; i < 25; i++) await h.TickAsync();                      // 25 ticks, clock not advanced
        Assert.Equal(1, RcsCalls(h));                                           // not a single extra call

        h.Clock.Advance(TimeSpan.FromMilliseconds(1000));                       // first backoff step elapsed
        await h.TickAsync(); await h.TickAsync();
        Assert.Equal(2, RcsCalls(h));                                           // exactly one more (a status query - never a blind re-create)
        Assert.Equal(1, h.Rcs.CreateCalls);
        Assert.Equal(0, h.Rcs.CreatedTaskCount);

        h.Clock.Advance(TimeSpan.FromMilliseconds(1999));                       // second step needs 2 s: not yet
        await h.TickAsync();
        Assert.Equal(2, RcsCalls(h));
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await h.TickAsync();
        Assert.Equal(3, RcsCalls(h));
    }

    [Fact]
    public async Task Timeout_after_creation_never_creates_a_second_rcs_task_even_with_backoff()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h);
        h.Rcs.NextFault = RcsFault.TimeoutAfterCreate;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        Assert.Equal("Dispatching", t.State);

        await h.TickAsync();                                                    // too early: nothing happens
        Assert.Equal(TaskState.Dispatching, (await h.GetTaskAsync(t.Id)).State);

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await h.TickAsync();
        var recovered = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Accepted, recovered.State);                      // mapped by the QUERY
        Assert.NotNull(recovered.RcsTaskCode);
        Assert.Equal(1, h.Rcs.CreatedTaskCount);
        Assert.Equal(1, h.Rcs.CreateCalls);
    }

    [Fact]
    public async Task Dispatch_budget_is_bounded_and_ends_uncertain_without_releasing_resources()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 4000, maxDispatch: 3);
        h.Rcs.Unreachable = true;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;

        for (var i = 0; i < 12; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        var failed = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Contains("RCS_DISPATCH_UNCERTAIN", failed.StateReason);
        Assert.True(failed.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());                     // the rack stays reserved until an operator reconciles
        Assert.True(RcsCalls(h) <= 3, $"at most MaxDispatchAttempts RCS calls, was {RcsCalls(h)}");
        Assert.Equal(0, h.Rcs.CreatedTaskCount);
    }

    [Fact]
    public async Task Repeated_identical_dispatch_outcomes_do_not_flood_the_history()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 1000, maxDispatch: 50);
        h.Rcs.Unreachable = true;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        for (var i = 0; i < 10; i++) { h.Clock.Advance(TimeSpan.FromSeconds(2)); await h.TickAsync(); }

        var task = await h.GetTaskAsync(t.Id);
        Assert.True(task.DispatchAttempts >= 8);
        Assert.True(task.History.Count(x => x.EventName is "RCS_DISPATCH_UNKNOWN" or "RCS_QUERY_UNKNOWN") <= 2,
            "identical outcomes are recorded once, not on every attempt");
    }

    // ============================================================================================ AUDIT-004
    private static async Task<TransportTask> ContinuingAsync(Harness h)
    {
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;
        await h.AdvanceUntilAsync(t.Id, TaskState.Executing);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionRequested);
        await h.SignalAsync(t.Id, SignalKind.PlcPermissionGranted);
        h.Rcs.NextFault = RcsFault.TimeoutNextContinue;
        await h.SignalAsync(t.Id, SignalKind.PickConfirmed);
        var c = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Continuing, c.State);
        return c;
    }

    [Fact]
    public async Task Continue_retry_is_spaced_bounded_and_then_hands_over_to_an_operator()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 4000, maxAttempts: 4);
        var t = await ContinuingAsync(h);
        h.Rcs.Unreachable = true;
        var before = RcsCalls(h);

        for (var i = 0; i < 20; i++) await h.TickAsync();                       // no time passes: no calls
        Assert.Equal(before, RcsCalls(h));

        for (var i = 0; i < 40; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Continuing, task.State);                         // never failed / completed by the retry loop
        Assert.True(task.RequiresReconciliation);
        Assert.Single(task.History, x => x.EventName == "RCS_CONTINUE_RETRY_EXHAUSTED");
        Assert.True(h.Rcs.ContinueCalls <= 4, $"continue calls are bounded by RcsMaxAttempts, was {h.Rcs.ContinueCalls}");
        Assert.True(task.History.Count(x => x.EventName == "RCS_CONTINUE_UNKNOWN") <= 2);

        var after = RcsCalls(h);
        for (var i = 0; i < 5; i++) { h.Clock.Advance(TimeSpan.FromMinutes(5)); await h.TickAsync(); }
        Assert.Equal(after, RcsCalls(h));                                       // automatic retry has stopped
    }

    [Fact]
    public async Task Continue_recovers_when_rcs_comes_back_within_the_limit()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 4000, maxAttempts: 10);
        var t = await ContinuingAsync(h);
        h.Rcs.Unreachable = true;
        h.Clock.Advance(TimeSpan.FromSeconds(1)); await h.TickAsync();
        Assert.Equal(TaskState.Continuing, (await h.GetTaskAsync(t.Id)).State);

        h.Rcs.Unreachable = false;
        h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync();
        Assert.Equal(TaskState.Executing, (await h.GetTaskAsync(t.Id)).State);
    }

    [Fact]
    public async Task Cancel_follow_up_is_spaced_and_bounded_and_the_limit_fails_the_task_for_reconciliation()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 4000, maxAttempts: 4);
        var (t, _) = await CancelPendingAsync(h);
        h.Rcs.Unreachable = true;
        var before = RcsCalls(h);

        for (var i = 0; i < 20; i++) await h.TickAsync();
        Assert.Equal(before, RcsCalls(h));                                      // 20 ticks without time passing: no call to RCS

        for (var i = 0; i < 40; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }
        var task = await h.GetTaskAsync(t.Id);
        // CR-008B-3 (decision D-1): the limit is final - Failed + reconciliation, nothing released
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal(WCS.Application.Services.TaskOrchestrator.RetryLimitReason, task.StateReason);
        Assert.True(task.RequiresReconciliation);
        Assert.Single(task.History, x => x.EventName == "RCS_CANCEL_RETRY_EXHAUSTED");
        Assert.Single(task.History, x => x.EventName == "RCS_QUERY_UNKNOWN");   // identical result recorded once
        Assert.True(h.Rcs.QueryCalls <= 4);
        Assert.NotEmpty(await h.ActiveReservationsAsync());

        var after = RcsCalls(h);
        h.Rcs.Unreachable = false;
        for (var i = 0; i < 5; i++) { h.Clock.Advance(TimeSpan.FromMinutes(5)); await h.TickAsync(); }
        Assert.Equal(after, RcsCalls(h));                                       // stopped for good: no Cancel / query any more
    }

    // ============================================================================================ AUDIT-006
    [Fact]
    public async Task Cancel_pending_with_rcs_completed_ends_for_reconciliation_and_keeps_the_rack()
    {
        await using var h = await ReadyAsync(fixture);
        var (t, code) = await CancelPendingAsync(h);
        h.Rcs.SetStatus(code, RcsTaskStatus.Completed);
        await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("CANCEL_RACE_RCS_COMPLETED", task.StateReason);
        Assert.True(task.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());                     // rack / slot NOT released
        Assert.Equal(0, await h.ActiveTaskCountAsync("AGV1"));                  // the AGV is no longer driven by RCS: free
        Assert.Single(await h.RacksAtAsync("A1"));                              // the rack is still where WCS last knew it
    }

    [Fact]
    public async Task Cancel_pending_with_rcs_interrupted_ends_for_reconciliation_and_keeps_the_rack()
    {
        await using var h = await ReadyAsync(fixture);
        var (t, code) = await CancelPendingAsync(h);
        h.Rcs.SetStatus(code, RcsTaskStatus.Interrupted);
        await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("CANCEL_RACE_RCS_INTERRUPTED", task.StateReason);
        Assert.True(task.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
    }

    [Fact]
    public async Task Cancel_pending_with_rcs_unknown_changes_nothing_and_releases_nothing()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h);
        var (t, code) = await CancelPendingAsync(h);
        h.Rcs.SetStatus(code, RcsTaskStatus.Unknown);
        for (var i = 0; i < 4; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.CancelPending, task.State);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
        Assert.Single(task.History, x => x.EventName == "RCS_QUERY_UNKNOWN");
    }

    [Fact]
    public async Task Cancel_pending_with_notfound_after_rcs_accepted_is_an_anomaly_not_a_cancellation()
    {
        await using var h = await ReadyAsync(fixture);
        var (t, code) = await CancelPendingAsync(h);
        Assert.NotNull(t.RcsTaskCode);
        h.Rcs.Forget(code);
        await h.TickAsync();

        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, task.State);                             // NOT Cancelled
        Assert.Equal("RCS_NOTFOUND_AFTER_ACCEPT", task.StateReason);
        Assert.True(task.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
    }

    [Fact]
    public async Task Cancel_pending_with_notfound_is_never_turned_into_cancelled_even_when_rcs_never_accepted()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 4000, maxAttempts: 5, maxDispatch: 10);
        h.Rcs.Unreachable = true;
        var t = (await h.CreateTaskAsync("BP01", "A1")).Task;                   // dispatch outcome unknown: no RCS task code
        Assert.Equal("Dispatching", t.State);
        await CancelAsync(h, t.Id);
        Assert.Equal(TaskState.CancelPending, (await h.GetTaskAsync(t.Id)).State);
        h.Rcs.Unreachable = false;                                              // RCS answers again and has no record of the task

        for (var i = 0; i < 3; i++) { h.Clock.Advance(TimeSpan.FromSeconds(5)); await h.TickAsync(); }
        var waiting = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.CancelPending, waiting.State);                   // NotFound (even repeated) is not a cancellation (decision D-9 not approved)
        Assert.Single(waiting.History, x => x.EventName == "RCS_CANCEL_NOTFOUND_NOT_CONFIRMED");

        for (var i = 0; i < 10; i++) { h.Clock.Advance(TimeSpan.FromMinutes(2)); await h.TickAsync(); }
        var final = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Failed, final.State);                            // only the retry limit ends it, as Failed (D-1), never as Cancelled
        Assert.True(final.RequiresReconciliation);
        Assert.NotEmpty(await h.ActiveReservationsAsync());
        Assert.DoesNotContain(final.History, x => x.ToState == TaskState.Cancelled);
    }

    [Fact]
    public async Task Cancel_pending_with_executing_resends_the_cancel_with_backoff_and_cancelled_confirms()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h, initialMs: 1000, maxMs: 4000);
        var (t, code) = await CancelPendingAsync(h);
        Assert.Equal(1, h.Rcs.CancelCalls);

        h.Rcs.MarkExecuting(code);                                              // the cancel did not take effect
        await h.TickAsync();                                                    // too early (backoff)
        Assert.Equal(1, h.Rcs.CancelCalls);
        h.Clock.Advance(TimeSpan.FromSeconds(2)); await h.TickAsync();
        Assert.Equal(2, h.Rcs.CancelCalls);                                     // asked again, once
        await h.TickAsync();
        Assert.Equal(2, h.Rcs.CancelCalls);

        h.Rcs.ConfirmCancel(code);
        h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync();
        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.Cancelled, task.State);
        Assert.True(task.RequiresReconciliation);                               // existing rule: held until reconciled
        Assert.Single(task.History, x => x.EventName == "RCS_CANCEL_REQUESTED");
    }

    [Fact]
    public async Task Cancel_pending_with_cancelling_waits_without_calling_cancel_again()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h);
        var (t, _) = await CancelPendingAsync(h);                               // the simulator now reports Cancelling
        for (var i = 0; i < 3; i++) { h.Clock.Advance(TimeSpan.FromSeconds(10)); await h.TickAsync(); }

        Assert.Equal(1, h.Rcs.CancelCalls);
        var task = await h.GetTaskAsync(t.Id);
        Assert.Equal(TaskState.CancelPending, task.State);
        Assert.Single(task.History, x => x.EventName == "RCS_CANCEL_IN_PROGRESS");
    }

    // ============================================================================================ PER_AGV
    [Fact]
    public async Task Per_agv_is_still_enforced_and_both_agvs_run_concurrently_with_backoff_active()
    {
        await using var h = await ReadyAsync(fixture);
        Backoff(h);
        await h.StockAsync("A2", RackKind.Full, 1);
        await h.StockAsync("B1", RackKind.Empty, 1);

        var full1 = (await h.CreateTaskAsync("BP01", "A1")).Task;               // AGV1
        var empty = (await h.CreateTaskAsync("BP02", "A1")).Task;               // AGV2, in parallel
        var full2 = (await h.CreateTaskAsync("BP01", "A2")).Task;               // AGV1 busy -> queued

        Assert.Equal("Accepted", full1.State);
        Assert.Equal("Accepted", empty.State);
        Assert.Equal("Queued", full2.State);
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV2"));

        for (var i = 0; i < 5; i++) await h.TickAsync();
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));                  // the queued task never becomes a second active one
        Assert.Equal("Queued", (await h.GetTaskAsync(full2.Id)).State.ToString());
    }
}
