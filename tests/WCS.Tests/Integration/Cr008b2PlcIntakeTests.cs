using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Services;
using WCS.Domain.Common;
using WCS.Domain.Intake;
using WCS.Domain.Inventory;
using WCS.Domain.Tasks;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-008B-2: station requests are stored durably and survive RCS outages and backend restarts without being lost or duplicated.</summary>
[Collection("Database")]
public class Cr008b2PlcIntakeTests(DatabaseFixture fixture)
{
    private sealed class Link : IRcsDispatchGuard
    {
        public bool Allowed { get; set; } = true;
        public RcsDispatchStatus Check() => Allowed ? new(true, "OK", "") : new(false, "RCS_NOT_CONNECTED", "test");
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "WCS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Delegates to the real orchestrator; a request whose key is in <see cref="Failing"/> raises a TECHNICAL (non business) exception.</summary>
    private sealed class FlakyOrchestrator(ITaskOrchestrator inner, HashSet<string> failing) : ITaskOrchestrator
    {
        public Task<CreateTaskResult> CreateTaskAsync(CreateTaskCommand c, CancellationToken ct = default) =>
            failing.Contains(c.RequestKey) ? throw new InvalidOperationException("simulated technical failure") : inner.CreateTaskAsync(c, ct);
        public Task<SignalResult> HandleSignalAsync(TaskSignal s, CancellationToken ct = default) => inner.HandleSignalAsync(s, ct);
        public Task<TaskDto> CancelTaskAsync(Guid id, string r, string? u, CancellationToken ct = default) => inner.CancelTaskAsync(id, r, u, ct);
        public Task<TaskDto> UpdateTaskAsync(Guid id, string f, string s, string? e, WCS.Application.Auth.ActorInfo a, CancellationToken ct = default) => inner.UpdateTaskAsync(id, f, s, e, a, ct);
        public Task<TaskDto> DeleteTaskAsync(Guid id, string? e, WCS.Application.Auth.ActorInfo a, CancellationToken ct = default) => inner.DeleteTaskAsync(id, e, a, ct);
        public Task ProcessTaskAsync(Guid id, CancellationToken ct = default) => inner.ProcessTaskAsync(id, ct);
        public Task TryDispatchNextAsync(Guid id, CancellationToken ct = default) => inner.TryDispatchNextAsync(id, ct);
        public Task DispatchToRcsAsync(Guid id, CancellationToken ct = default) => inner.DispatchToRcsAsync(id, ct);
        public Task TryResolveDestinationAsync(Guid id, CancellationToken ct = default) => inner.TryResolveDestinationAsync(id, ct);
        public Task SendContinueAsync(Guid id, CancellationToken ct = default) => inner.SendContinueAsync(id, ct);
        public Task ReconcileCancelPendingAsync(Guid id, CancellationToken ct = default) => inner.ReconcileCancelPendingAsync(id, ct);
        public Task EnforceTimeoutAsync(Guid id, CancellationToken ct = default) => inner.EnforceTimeoutAsync(id, ct);
    }

    private static async Task<Harness> ReadyAsync(DatabaseFixture f, Action<IServiceCollection>? configure = null)
    {
        var h = await Harness.CreateAsync(f, configure);
        await h.SeedStandardAsync();
        await h.StockAsync("A1", RackKind.Full, 1);
        await h.StockAsync("A2", RackKind.Full, 1);
        return h;
    }

    private static Action<IServiceCollection> WithLink(Link link) => s => s.AddSingleton<IRcsDispatchGuard>(link);

    private static Action<IServiceCollection> WithFlaky(HashSet<string> failing) => s =>
    {
        s.AddScoped<TaskOrchestrator>();
        s.AddScoped<ITaskOrchestrator>(sp => new FlakyOrchestrator(sp.GetRequiredService<TaskOrchestrator>(), failing));
    };

    private static Task<List<StationRequestRecord>> Records(Harness h) => h.Db(db => db.StationRequests.AsNoTracking().OrderBy(r => r.FirstSeenAt).ToListAsync());

    // ------------------------------------------------------------------------------------------------ happy path
    [Fact]
    public async Task A_detected_request_is_stored_and_accepted_only_after_the_task_exists()
    {
        await using var h = await ReadyAsync(fixture);
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));

        Assert.Equal(1, await h.IntakeAsync());

        var task = Assert.Single(await h.Db(db => db.Tasks.AsNoTracking().ToListAsync()));
        var rec = Assert.Single(await Records(h));
        Assert.Equal(StationRequestStatus.Accepted, rec.Status);
        Assert.Equal(task.Id, rec.TaskId);                                        // accepted = linked to the committed task
        Assert.Equal("PLC-01:A1:R1", task.RequestKey);
        Assert.Single(h.Stations.Acknowledged);
    }

    // ------------------------------------------------------------------------------------------------ RCS unavailable / reconnect
    [Fact]
    public async Task While_rcs_is_not_connected_the_request_is_kept_without_using_an_attempt_and_is_processed_when_it_reconnects()
    {
        var link = new Link { Allowed = false };
        await using var h = await ReadyAsync(fixture, WithLink(link));
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));

        Assert.Equal(0, await h.IntakeAsync());
        for (var i = 0; i < 5; i++) { h.Clock.Advance(TimeSpan.FromMinutes(5)); Assert.Equal(0, await h.IntakeAsync()); }

        var waiting = Assert.Single(await Records(h));
        Assert.Equal(StationRequestStatus.Pending, waiting.Status);
        Assert.Equal(0, waiting.Attempts);                                        // "not connected" is a state, not an attempt
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Empty(h.Stations.Acknowledged);                                    // not accepted: not acknowledged
        Assert.Equal(0, h.Rcs.CreateCalls + h.Rcs.QueryCalls);

        link.Allowed = true;                                                      // RCS is connected now
        Assert.Equal(1, await h.IntakeAsync());
        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Equal(StationRequestStatus.Accepted, Assert.Single(await Records(h)).Status);
    }

    [Fact]
    public async Task Production_with_only_the_simulator_keeps_the_station_request_instead_of_losing_it()
    {
        await using var h = await ReadyAsync(fixture, s => s.AddSingleton<IHostEnvironment>(new FakeEnv("Production")));
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));

        await h.IntakeAsync(); await h.IntakeAsync();

        Assert.Equal(StationRequestStatus.Pending, Assert.Single(await Records(h)).Status);
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Equal(0, h.Rcs.CreateCalls);                                       // the Production guard still protects the simulator
        Assert.Empty(await h.ActiveReservationsAsync());
    }

    // ------------------------------------------------------------------------------------------------ restart
    [Fact]
    public async Task A_pending_request_survives_a_backend_restart_and_is_processed_afterwards()
    {
        var link = new Link { Allowed = false };
        await using var h = await ReadyAsync(fixture, WithLink(link));
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));
        await h.IntakeAsync();
        Assert.Equal(StationRequestStatus.Pending, Assert.Single(await Records(h)).Status);

        await h.RestartAsync();                                                   // all in-memory state is gone, including the signal queue
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
        link.Allowed = true;
        Assert.Equal(1, await h.IntakeAsync());                                   // nothing is re-enqueued: the request comes from the database

        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Equal(StationRequestStatus.Accepted, Assert.Single(await Records(h)).Status);
    }

    [Fact]
    public async Task An_accepted_request_is_not_processed_again_after_a_restart_even_if_the_bit_is_seen_again()
    {
        await using var h = await ReadyAsync(fixture);
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));
        await h.IntakeAsync();
        await h.RestartAsync();

        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));   // the PLC bit is still high: a new rising edge after the restart
        await h.IntakeAsync();

        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Single(await Records(h));
    }

    // ------------------------------------------------------------------------------------------------ idempotency / concurrency
    [Fact]
    public async Task The_same_request_id_seen_many_times_creates_one_record_and_one_task()
    {
        await using var h = await ReadyAsync(fixture);
        for (var i = 0; i < 3; i++) h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R7"));
        await h.IntakeAsync();
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R7"));
        await h.IntakeAsync();

        Assert.Single(await Records(h));
        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
    }

    [Fact]
    public async Task Two_stations_requesting_at_the_same_time_each_get_their_own_task()
    {
        await using var h = await ReadyAsync(fixture);
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));
        h.Stations.EnqueueRequest(new StationRequest("A2", "BP01", "PLC-01:A2:R1"));

        Assert.Equal(2, await h.IntakeAsync());

        var tasks = await h.Db(db => db.Tasks.AsNoTracking().ToListAsync());
        Assert.Equal(2, tasks.Count);
        Assert.Equal(2, tasks.Select(t => t.RequestLocationId).Distinct().Count());
        Assert.Equal(1, await h.ActiveTaskCountAsync("AGV1"));                    // PER_AGV: the second one is queued, not a second active task
        Assert.All(await Records(h), r => Assert.Equal(StationRequestStatus.Accepted, r.Status));
    }

    [Fact]
    public async Task A_crash_between_task_creation_and_the_accepted_mark_only_repeats_an_idempotent_call()
    {
        await using var h = await ReadyAsync(fixture);
        await h.CreateTaskAsync("BP01", "A1", "PLC-01:A1:R1");                    // the task was committed ...
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:R1"));   // ... but the request record never got its Accepted mark

        await h.IntakeAsync();

        Assert.Single(await h.Db(db => db.Tasks.ToListAsync()));
        Assert.Equal(StationRequestStatus.Accepted, Assert.Single(await Records(h)).Status);
        Assert.Equal(1, h.Rcs.CreatedTaskCount);
    }

    // ------------------------------------------------------------------------------------------------ failures are isolated
    [Fact]
    public async Task A_technical_failure_of_one_request_keeps_it_pending_and_does_not_stop_the_others()
    {
        var failing = new HashSet<string> { "PLC-01:A2:BOOM" };
        await using var h = await ReadyAsync(fixture, WithFlaky(failing));
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:OK1"));
        h.Stations.EnqueueRequest(new StationRequest("A2", "BP01", "PLC-01:A2:BOOM"));
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:OK2"));

        Assert.Equal(2, await h.IntakeAsync());

        var recs = await Records(h);
        Assert.Equal(2, recs.Count(r => r.Status == StationRequestStatus.Accepted));
        var boom = Assert.Single(recs, r => r.RequestKey == "PLC-01:A2:BOOM");
        Assert.Equal((StationRequestStatus.Pending, 1), (boom.Status, boom.Attempts));
        Assert.Contains("simulated technical failure", boom.LastError);
        Assert.Equal(2, await h.Db(db => db.Tasks.CountAsync()));
        Assert.Equal(2, h.Stations.Acknowledged.Count);                           // the failed one is not acknowledged

        await h.IntakeAsync();                                                    // backoff: no new attempt without time passing
        Assert.Equal(1, Assert.Single(await Records(h), r => r.RequestKey == "PLC-01:A2:BOOM").Attempts);

        failing.Clear();                                                          // the cause is gone
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await h.IntakeAsync());
        Assert.Equal(StationRequestStatus.Accepted, Assert.Single(await Records(h), r => r.RequestKey == "PLC-01:A2:BOOM").Status);
        Assert.Equal(3, await h.Db(db => db.Tasks.CountAsync()));
    }

    [Fact]
    public async Task A_request_that_keeps_failing_stops_after_the_retry_limit_and_waits_for_an_operator()
    {
        var failing = new HashSet<string> { "PLC-01:A1:BOOM" };
        await using var h = await ReadyAsync(fixture, WithFlaky(failing));
        h.SchedulerOptions.RcsRetryInitialDelayMs = 1000;
        h.SchedulerOptions.RcsRetryMaxDelayMs = 2000;
        h.SchedulerOptions.RcsMaxAttempts = 3;
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP01", "PLC-01:A1:BOOM"));

        for (var i = 0; i < 8; i++) { await h.IntakeAsync(); h.Clock.Advance(TimeSpan.FromMinutes(1)); }

        var rec = Assert.Single(await Records(h));
        Assert.Equal(StationRequestStatus.Failed, rec.Status);
        Assert.Equal(3, rec.Attempts);
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
    }

    [Fact]
    public async Task A_permanent_business_rejection_is_recorded_acknowledged_and_never_retried()
    {
        await using var h = await ReadyAsync(fixture);
        h.Stations.EnqueueRequest(new StationRequest("A1", "BP04", "PLC-01:A1:R9"));         // BP-04 is REVIEW

        Assert.Equal(0, await h.IntakeAsync());
        await h.IntakeAsync();

        var rec = Assert.Single(await Records(h));
        Assert.Equal(StationRequestStatus.Rejected, rec.Status);
        Assert.Contains("FLOW_IN_REVIEW", rec.LastError);
        Assert.Single(h.Stations.Acknowledged);                                   // acknowledged once, then left alone
        Assert.Empty(await h.Db(db => db.Tasks.ToListAsync()));
    }

    // ------------------------------------------------------------------------------------------------ retry policy numbers used by the documentation
    [Fact]
    public void Retry_policy_gives_the_timings_quoted_in_the_documentation()
    {
        var o = new SchedulerOptions();                                           // defaults: 2 s, 60 s, 5, 30
        Assert.Equal([2, 4, 8, 16, 32, 60, 60], Enumerable.Range(1, 7).Select(n => (int)RcsRetryPolicy.DelayAfter(n, o).TotalSeconds));
        Assert.Equal(60, RcsRetryPolicy.DelayAfter(30, o).TotalSeconds);

        double t = 0; var attemptTimes = new List<double> { 0 };
        for (var n = 1; n < o.MaxDispatchAttempts; n++) { t += RcsRetryPolicy.DelayAfter(n, o).TotalSeconds; attemptTimes.Add(t); }
        Assert.Equal([0d, 2, 6, 14, 30], attemptTimes);
        Assert.Equal(62, t + RcsRetryPolicy.DelayAfter(o.MaxDispatchAttempts, o).TotalSeconds);          // dispatch declared uncertain at 62 s

        double cc = 0;
        for (var n = 1; n < o.RcsMaxAttempts; n++) cc += RcsRetryPolicy.DelayAfter(n, o).TotalSeconds;
        Assert.Equal(1502, cc);                                                   // 30th continue/cancel attempt at 1 502 s (about 25 minutes)

        Assert.Equal(TimeSpan.Zero, RcsRetryPolicy.DelayAfter(5, new SchedulerOptions { RcsRetryInitialDelayMs = 0 }));
    }
}
