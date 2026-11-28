using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Fleet;
using WCS.Application.RcsMaps;
using WCS.Application.RealDispatch;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Rcs;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;

namespace WCS.Infrastructure.Simulation;

/// <summary>CR-050: <c>Rcs:Simulator:Execution</c>. Off by default: without it the simulated RCS only accepts commands, as before.</summary>
public sealed class AmrSimulationOptions
{
    public const string SectionName = "Rcs:Simulator:Execution";
    public bool Enabled { get; set; }
    public double SpeedMultiplier { get; set; } = 1;
    public int TickMs { get; set; } = 250;
}

/// <summary>Faults an Admin can arm. Each is used up by the next thing it applies to.</summary>
public enum AmrFault { RejectDispatch = 1, DispatchTimeout = 2, MoveFailure = 3, PickFailure = 4, DropFailure = 5, ExecutionStall = 6, ResponseDelay = 7, DuplicateResponse = 8, ExitStall = 9 }

public enum AmrRunPhase { Queued = 1, ToSource = 2, Picking = 3, WaitingForTrigger = 4, ToDestination = 5, Dropping = 6, Leaving = 7, Completed = 8, Failed = 9, Cancelled = 10 }

public sealed record AmrRobotView(string RobotCode, double X, double Y, double Battery, string State, string? RequestCode, double Dir = 90);
/// <summary>CR-069: one run as the map monitor shows it. Positions in millimetres; <paramref name="Path"/> = the segment being driven, <paramref name="NextPath"/> = the one planned after the pick.</summary>
public sealed record AmrLiveRun(string RequestCode, string Kind, Guid? OrderId, string Order, string? ProcessCode, int? LegSequence, string? StepKey, string? RobotCode, string? RackId, string? RackKind, bool Carrying,
    string? Pick, string? Drop, string Phase, string Action, bool Stalled, string? Failure, double Progress, IReadOnlyList<double[]> Path, double Travelled, IReadOnlyList<double[]> NextPath,
    DateTimeOffset? CreatedAt, DateTimeOffset? PickedAt, DateTimeOffset? DroppedAt);
public sealed record AmrLiveView(bool Active, bool Paused, double SpeedMultiplier, bool Connected, string? MapId, string? MapCode, IReadOnlyList<AmrRobotView> Robots, IReadOnlyList<AmrLiveRun> Runs)
{
    public static AmrLiveView Inactive { get; } = new(false, false, 1, false, null, null, [], []);
}
public sealed record AmrRunView(string RequestCode, string Kind, string ParentTask, string? RobotCode, string? Source, string? Destination, string Phase, bool Stalled, string? Failure, IReadOnlyList<string> Events);
public sealed record AmrSimulationView(bool Enabled, double SpeedMultiplier, bool Connected, double ResponseDelaySeconds, IReadOnlyDictionary<string, int> ArmedFaults,
    IReadOnlyList<AmrRobotView> Robots, IReadOnlyList<AmrRunView> Runs, bool Paused = false, string? MapId = null);

/// <summary>
/// CR-050: the part of the AMRs behind the simulated RCS. It receives work only through the dispatch interfaces WCS already uses, and reports back only through the entry points a real system's
/// events use (leg pick / drop, AMR left the port, task signals). It never touches inventory, reservations, order states or ports itself, writes no PLC signal and opens no network connection.
/// It acts only on legs of orders, and on Simulator tasks, created after it was enabled. State is in memory: a restart forgets it.
/// CR-069: with an imported RCS map the AMRs drive along its connections between the points of the locations (no point or no route = the run fails, nothing moves across the map);
/// without a map positions and distances stay synthetic.
/// </summary>
public sealed class AmrExecutionSimulator
{
    private const double SpeedMmPerSecond = 1000, PortRadius = 1500, HandlingSeconds = 2, RetrySeconds = 2, Spacing = 5000;
    private const double MapPortRadius = 700, OnPoint = 300;   // CR-069: on a map a station is left beyond 0.7 m; an AMR within 0.3 m of a point stands on it
    private const int MapReloadSteps = 12;

    private sealed class Robot { public required string Code; public double X, Y; public double Battery = 100; public double Dir = 90; public Run? Run; }

    private sealed class Run
    {
        public required string RequestCode; public required bool IsTask;
        public Guid EntityId; public string Parent = ""; public string? RequestedRobot; public Robot? Robot;
        public string? Source, Destination; public AmrRunPhase Phase = AmrRunPhase.Queued, FailedIn;
        public bool Picked, Dropped, SourceExitPending, DestinationExitPending, Stalled;
        public double Handling, Wait, HalfTravelled, TravelTotal;
        public string? Failure, LastRefusal;
        public readonly List<string> Events = [];
        // CR-069: the route being driven, and what the monitor shows about the work
        public List<(double X, double Y)>? Path; public double Along; public List<(double X, double Y)> Next = [];
        public Guid? OrderId; public string? ProcessCode, StepKey, RackCode, RackKind; public int? LegSequence; public DateTimeOffset? CreatedAt, PickedAt, DroppedAt;
    }

    private readonly IServiceScopeFactory _scopes;
    private readonly RcsSimulator _rcs;
    private readonly IClock _clock;
    private readonly bool _realDispatch;
    private readonly ILogger<AmrExecutionSimulator> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _step = new(1, 1);
    private readonly Dictionary<string, Run> _runs = [];
    private readonly Dictionary<string, Robot> _robots = [];
    private readonly Dictionary<AmrFault, int> _faults = [];
    private readonly Dictionary<string, (double X, double Y)> _places = new(StringComparer.OrdinalIgnoreCase);
    private double _delaySeconds = 10;
    private DateTimeOffset _enabledAt;
    private bool _connected = true;

    public bool Enabled { get; private set; }
    public double SpeedMultiplier { get; private set; } = 1;
    public bool Paused { get; private set; }
    private SimMapGraph? _map;
    private int _mapAge;

    public AmrExecutionSimulator(IServiceScopeFactory scopes, RcsSimulator rcs, IClock clock, IOptions<AmrSimulationOptions> options, IOptions<RealDispatchOptions> real, ILogger<AmrExecutionSimulator> logger)
    {
        _scopes = scopes; _rcs = rcs; _clock = clock; _realDispatch = real.Value.Enabled; _logger = logger;
        _rcs.CreateInterceptor = InterceptTaskCreate;
        if (options.Value.Enabled && !_realDispatch) Configure(true, options.Value.SpeedMultiplier);
    }

    // ------------------------------------------------------------------ control (Admin)
    public void Configure(bool enabled, double speedMultiplier)
    {
        if (enabled && _realDispatch) throw AppException.Rejected("AMR_SIMULATION_REAL_DISPATCH", "The AMR simulation cannot run while the real RCS dispatch is enabled.");
        if (speedMultiplier is < 0.1 or > 100) throw AppException.Validation("AMR_SIMULATION_SPEED", "The speed is between 0.1 and 100.");
        lock (_gate)
        {
            SpeedMultiplier = speedMultiplier;
            if (enabled == Enabled) return;
            Enabled = enabled;
            if (enabled)
            {
                _enabledAt = _clock.UtcNow.AddMilliseconds(-1);   // the database keeps microseconds: a task created in the same instant counts as created after
                foreach (var a in _rcs.Fleet) _robots.TryAdd(a.RobotCode, new Robot { Code = a.RobotCode, X = Number(a.PosX), Y = Number(a.PosY), Battery = Number(a.Battery, 100) });
            }
            else
            {   // switching off forgets the runs: nothing in WCS is changed; legs and tasks stay as they are and can be handled by hand
                _runs.Clear(); _faults.Clear(); foreach (var r in _robots.Values) r.Run = null; _connected = true; _rcs.Unreachable = false; Paused = false; _mapAge = 0;
            }
            Publish();
        }
    }

    public void Arm(AmrFault fault, int count, double? delaySeconds)
    {
        RequireEnabled();
        if (!Enum.IsDefined(fault) || count is < 1 or > 20) throw AppException.Validation("AMR_SIMULATION_FAULT", "Unknown fault or count outside 1..20.");
        lock (_gate) { _faults[fault] = count; if (delaySeconds is { } d) _delaySeconds = Math.Clamp(d, 0.1, 600); }
    }

    /// <summary>CR-069: while paused nothing moves and nothing is reported to WCS; resuming goes on from the same place.</summary>
    public void SetPaused(bool paused) { RequireEnabled(); lock (_gate) Paused = paused; }

    /// <summary>CR-069: the state the map monitor draws.</summary>
    public AmrLiveView Live()
    {
        lock (_gate)
        {
            if (!Enabled) return AmrLiveView.Inactive;
            static double[][] Line(IEnumerable<(double X, double Y)>? p) => p?.Select(x => new[] { Math.Round(x.X), Math.Round(x.Y) }).ToArray() ?? [];
            var open = _runs.Values.Where(r => r.Phase is not (AmrRunPhase.Completed or AmrRunPhase.Cancelled)).ToList();
            var done = _runs.Values.Where(r => r.Phase is AmrRunPhase.Completed or AmrRunPhase.Cancelled).Reverse().Take(10);
            return new(true, Paused, SpeedMultiplier, _connected, _map?.MapId, _map?.MapCode, Robots(),
                open.Concat(done).Select(r => new AmrLiveRun(r.RequestCode, r.IsTask ? "Task" : "Leg", r.OrderId, r.Parent, r.ProcessCode, r.LegSequence, r.StepKey, r.Robot?.Code ?? r.RequestedRobot, r.RackCode, r.RackKind,
                    r.Picked && !r.Dropped, r.Source, r.Destination, r.Phase.ToString(), ActionOf(r), r.Stalled, r.Failure, ProgressOf(r), Line(r.Path), Math.Round(r.Along), Line(r.Next),
                    r.CreatedAt, r.PickedAt, r.DroppedAt)).ToList());
        }
    }

    private static string ActionOf(Run r) => r.Phase switch
    {
        AmrRunPhase.Queued => "Queued", AmrRunPhase.ToSource => "MovingToPick", AmrRunPhase.Picking => "Picking", AmrRunPhase.WaitingForTrigger => "Waiting", AmrRunPhase.ToDestination => "MovingToDrop",
        AmrRunPhase.Dropping => "Dropping", AmrRunPhase.Leaving => "Leaving", AmrRunPhase.Completed => "Completed", AmrRunPhase.Failed => "Failed", _ => "Cancelled",
    };

    /// <summary>0..100: driving to the pick 0-40, picking 40-50, driving to the drop 50-90, dropping 90-100.</summary>
    private static double ProgressOf(Run r)
    {
        var way = r.Path is { Count: > 0 } p ? Math.Clamp(r.Along / Math.Max(1, SimMapGraph.Length(p)), 0, 1) : 0;
        var phase = r.Phase == AmrRunPhase.Failed ? r.FailedIn : r.Phase;
        return Math.Round(phase switch
        {
            AmrRunPhase.ToSource => 40 * way, AmrRunPhase.Picking => 40 + 10 * Math.Min(1, r.Handling / HandlingSeconds), AmrRunPhase.WaitingForTrigger => 50,
            AmrRunPhase.ToDestination => 50 + 40 * way, AmrRunPhase.Dropping => 90 + 10 * Math.Min(1, r.Handling / HandlingSeconds), AmrRunPhase.Leaving or AmrRunPhase.Completed => 100, _ => r.Dropped ? 100 : 0,
        }, 1);
    }

    private List<AmrRobotView> Robots() => _robots.Values.OrderBy(r => r.Code, StringComparer.Ordinal)
        .Select(r => new AmrRobotView(r.Code, Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Battery, 1), r.Run?.Phase.ToString() ?? "Idle", r.Run?.RequestCode, r.Dir)).ToList();

    public void ClearFaults() { lock (_gate) { _faults.Clear(); foreach (var r in _runs.Values) r.Stalled = false; } }

    public void SetConnected(bool connected) { RequireEnabled(); lock (_gate) { _connected = connected; _rcs.Unreachable = !connected; } }

    /// <summary>The operator's "the AMR has been recovered": a failed or stalled run goes on from the step it stopped in. A task that WCS already ended is not resumed.</summary>
    public async Task RecoverAsync(string requestCode, CancellationToken ct = default)
    {
        RequireEnabled();
        await _step.WaitAsync(ct);
        try
        {
            var run = Find(requestCode) ?? throw AppException.NotFound("AMR_RUN_NOT_FOUND", "The simulator has no run of this request.");
            if (run.Stalled) { run.Stalled = false; Note(run, "stall cleared"); return; }
            if (run is { Phase: AmrRunPhase.WaitingForTrigger, IsTask: false, Destination: null })
            {   // a destination trigger lost with the connection is not repeated by WCS: the operator makes the simulator ask for it again
                await using var scope = _scopes.CreateAsyncScope();
                var leg = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteAsync(t => scope.ServiceProvider.GetRequiredService<IRoutingRepository>().GetLegAsync(run.EntityId, t), ct);
                if (leg?.DestinationLocationCode is not { } again) throw AppException.Rejected("AMR_RUN_NO_DESTINATION", "WCS has not chosen a destination for the leg yet.");
                run.Destination = again; Note(run, $"trigger repeated on request: destination {again}"); return;
            }
            if (run.Phase != AmrRunPhase.Failed) throw AppException.Rejected("AMR_RUN_NOT_FAILED", "Only a failed or stalled run is recovered.");
            if (run.IsTask) throw AppException.Rejected("AMR_TASK_ENDED", "WCS ended this task when the AMR reported the interruption; reconcile it on the task screen.");
            run.Phase = run.FailedIn; run.Failure = null; run.Handling = 0; if (run.Path is null) run.Along = 0; _rcs.SetStatus(requestCode, RcsTaskStatus.Executing);
            Note(run, "recovered");
            await using var recording = _scopes.CreateAsyncScope();
            await RecordAsync(recording.ServiceProvider, "Recovered", run, new { phase = run.Phase.ToString() }, "Applied", ct);
        }
        finally { _step.Release(); }
    }

    public AmrSimulationView View()
    {
        lock (_gate)
            return new(Enabled, SpeedMultiplier, _connected, _delaySeconds, _faults.ToDictionary(f => f.Key.ToString(), f => f.Value), Robots(),
                _runs.Values.Reverse().Take(50).Select(r => new AmrRunView(r.RequestCode, r.IsTask ? "Task" : "Leg", r.Parent, r.Robot?.Code ?? r.RequestedRobot, r.Source, r.Destination, r.Phase.ToString(), r.Stalled, r.Failure, r.Events.ToList())).ToList(),
                Paused, _map?.MapId);
    }

    // ------------------------------------------------------------------ the RCS side (called by the simulated dispatchers)
    /// <summary>A leg was sent. NULL = the simulation does not take it (it is off, or the order was created before it was switched on): the caller answers as it always did.</summary>
    public LegDispatchResult? Dispatch(LegDispatchRequest request)
    {
        lock (_gate)
        {
            if (!Enabled || request.OrderCreatedAt < _enabledAt) return null;   // earlier orders stay as they were, even when their leg leaves now
            if (!_connected) return new(LegDispatchOutcome.Unknown, null, "Simulated RCS connection lost");
            if (_runs.TryGetValue(request.RequestCode, out var known)) return new(LegDispatchOutcome.Accepted, TaskCode(request), null);   // the same request code never starts a second run
            if (Take(AmrFault.RejectDispatch)) return new(LegDispatchOutcome.Rejected, null, "Simulated RCS rejection");
            if (Take(AmrFault.DispatchTimeout)) return new(LegDispatchOutcome.Unknown, null, "Simulated RCS timeout (the leg was not accepted)");
            var code = TaskCode(request);
            var run = new Run { RequestCode = request.RequestCode, IsTask = false, EntityId = request.LegId, RequestedRobot = request.RobotCode, Source = request.SourceLocationCode, Destination = request.DestinationLocationCode };
            _runs[request.RequestCode] = run;
            Note(run, "accepted");
            return new(LegDispatchOutcome.Accepted, code, null);
        }
    }

    private string TaskCode(LegDispatchRequest r) => r.RobotCode is null ? _rcs.CreateLegTask(r.RequestCode) : $"SIM-{r.RequestCode}";

    /// <summary>The destination trigger. NULL = not a run of the simulator.</summary>
    public LegDispatchResult? Continue(string requestCode, string? taskCode, string destination)
    {
        lock (_gate)
        {
            if (!Enabled || !_runs.TryGetValue(requestCode, out var run)) return null;
            if (!_connected) { Note(run, $"trigger for {destination} lost (connection)"); return new(LegDispatchOutcome.Unknown, null, "Simulated RCS connection lost"); }
            run.Destination = destination; Note(run, $"trigger: destination {destination}");
            return new(LegDispatchOutcome.Accepted, taskCode ?? $"SIM-{requestCode}", null);
        }
    }

    /// <summary>The status of a leg of the simulator. NULL = not a run of the simulator.</summary>
    public RcsTaskInfo? Query(string requestCode)
    {
        lock (_gate)
        {
            if (!Enabled || !_runs.TryGetValue(requestCode, out var run) || run.IsTask) return null;
            if (!_connected) return new(RcsTaskStatus.Unknown, null, null);
            var status = run.Phase switch { AmrRunPhase.Completed or AmrRunPhase.Leaving => RcsTaskStatus.Completed, AmrRunPhase.Failed => RcsTaskStatus.Interrupted, AmrRunPhase.Cancelled => RcsTaskStatus.Cancelled, AmrRunPhase.Queued => RcsTaskStatus.Created, _ => RcsTaskStatus.Executing };
            return new(status, $"SIM-{requestCode}", run.Robot?.Code);
        }
    }

    private RcsCallResult? InterceptTaskCreate()
    {
        lock (_gate)
        {
            if (!Enabled) return null;
            if (Take(AmrFault.RejectDispatch)) return new(RcsCallOutcome.Rejected, null, "Simulated RCS rejection");
            if (Take(AmrFault.DispatchTimeout)) return new(RcsCallOutcome.Unknown, null, "Simulated RCS timeout (not created)");
            return null;
        }
    }

    // ------------------------------------------------------------------ execution
    /// <summary>Moves the simulation forward by <paramref name="elapsed"/> real time (times the speed). The hosted loop calls it; tests call it directly.</summary>
    public async Task StepAsync(TimeSpan elapsed, CancellationToken ct = default)
    {
        if (!Enabled || Paused) return;
        await _step.WaitAsync(ct);
        try
        {
            if (Paused) return;
            var seconds = elapsed.TotalSeconds * SpeedMultiplier;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await LoadMapAsync(scope.ServiceProvider, ct);
                await DiscoverTasksAsync(scope.ServiceProvider, ct);
                if (_map is null) await LoadPlacesAsync(scope.ServiceProvider, ct);
            }
            List<Run> open;
            lock (_gate) open = _runs.Values.Where(r => r.Phase is not (AmrRunPhase.Completed or AmrRunPhase.Cancelled)).ToList();
            foreach (var run in open)
            {
                if (!Enabled) return;
                try { await using var scope = _scopes.CreateAsyncScope(); await AdvanceAsync(run, seconds, scope.ServiceProvider, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "AMR simulation step of {RequestCode} failed; it is tried again. {EventCode}", run.RequestCode, "AMR_SIM_STEP_FAILED"); run.Wait = RetrySeconds; }
            }
            lock (_gate) Publish();
        }
        finally { _step.Release(); }
    }

    private async Task DiscoverTasksAsync(IServiceProvider sp, CancellationToken ct)
    {
        var tasks = sp.GetRequiredService<ITransportTaskRepository>();
        var locations = sp.GetRequiredService<ILocationRepository>();
        foreach (var t in await tasks.ListByStatesAsync(TaskStateMachine.AgvOccupyingStates, 200, ct))
        {
            if (t.ExecutionMode != ExecutionMode.Simulator || t.CreatedAt < _enabledAt || Find(t.RcsRequestCode) is not null || _rcs.StatusOf(t.RcsRequestCode) is null) continue;
            var run = new Run { RequestCode = t.RcsRequestCode, IsTask = true, EntityId = t.Id, Parent = t.RequestKey, RequestedRobot = _rcs.RobotOf(t.RcsRequestCode),
                Source = t.SourceLocationId is { } s ? (await locations.GetAsync(s, ct))?.Code : null, Destination = t.DestinationLocationId is { } d ? (await locations.GetAsync(d, ct))?.Code : null };
            lock (_gate) _runs[run.RequestCode] = run;
            Note(run, "accepted");
        }
    }

    private async Task LoadPlacesAsync(IServiceProvider sp, CancellationToken ct)
    {
        lock (_gate) if (_places.Count > 0 && _runs.Values.All(r => (r.Source is null || _places.ContainsKey(r.Source)) && (r.Destination is null || _places.ContainsKey(r.Destination)))) return;
        var codes = (await sp.GetRequiredService<ILocationRepository>().ListAsync(ct)).Select(l => l.Code).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        lock (_gate) for (var i = 0; i < codes.Count; i++) _places.TryAdd(codes[i], ((i % 8 + 1) * Spacing, (i / 8 + 1) * Spacing));
    }

    private (double X, double Y) Place(string? code) =>
        _map is { } m ? (m.PointOf(code) is { } id ? m.Position(id) : (0, 0)) : code is not null && _places.TryGetValue(code, out var p) ? p : (0, 0);

    /// <summary>CR-069: reads the imported map (again after a few steps, so a new import or link is followed) and places AMRs that stand on no point of it.</summary>
    private async Task LoadMapAsync(IServiceProvider sp, CancellationToken ct)
    {
        if (_mapAge++ % MapReloadSteps != 0) return;
        var view = await sp.GetRequiredService<RcsMapService>().GetAsync(ct);
        var graph = view is null ? null : new SimMapGraph(view);
        lock (_gate)
        {
            if (graph?.Key == _map?.Key) return;
            _map = graph;
            if (graph is null) return;
            var taken = new HashSet<long>();
            foreach (var r in _robots.Values.Where(r => graph.Nearest((r.X, r.Y)).Distance <= OnPoint)) taken.Add(graph.Nearest((r.X, r.Y)).Id);
            foreach (var r in _robots.Values.Where(r => r.Run is null).OrderBy(r => r.Code, StringComparer.Ordinal)) Park(graph, r, taken);
        }
    }

    /// <summary>A simulated AMR that is on no point of the map starts at a free parking place of the map.</summary>
    private static void Park(SimMapGraph map, Robot r, HashSet<long> taken)
    {
        if (map.Nearest((r.X, r.Y)).Distance <= OnPoint) return;
        var home = map.Homes.FirstOrDefault(id => !taken.Contains(id), map.Homes[0]);
        taken.Add(home); (r.X, r.Y) = map.Position(home);
    }

    private async Task AdvanceAsync(Run run, double seconds, IServiceProvider sp, CancellationToken ct)
    {
        if (!await StillOpenAsync(run, sp, ct)) return;
        if (run.Phase == AmrRunPhase.Failed || run.Stalled) return;
        if (run.Wait > 0) { run.Wait -= seconds; if (run.Wait > 0) return; }
        if (run.Phase == AmrRunPhase.Queued)
        {
            if (!Assign(run)) return;
            run.Phase = AmrRunPhase.ToSource; run.TravelTotal = 0; run.Path = null; run.Along = 0;
            if (_map is { } m) lock (_gate) { Park(m, run.Robot!, []); run.Next = NextRoute(m, run); } Note(run, $"{run.Robot!.Code} started");
            await RecordAsync(sp, "Started", run, new { source = run.Source }, "Applied", ct);   // CR-051: monitoring shows the work as being executed by this AMR
            if (run.IsTask) await TaskSignalAsync(run, sp, ct);
            return;
        }
        if (run.Phase is AmrRunPhase.ToSource or AmrRunPhase.Picking or AmrRunPhase.ToDestination or AmrRunPhase.Dropping && Take(AmrFault.ExecutionStall)) { run.Stalled = true; Note(run, "stalled (no response)"); return; }

        switch (run.Phase)
        {
            case AmrRunPhase.ToSource:
                if (await TravelAsync(run, run.Source, seconds, sp, ct)) { run.Phase = AmrRunPhase.Picking; run.Handling = 0; Note(run, $"at {run.Source}: pick trigger"); }
                break;
            case AmrRunPhase.Picking:
                if ((run.Handling += seconds) < HandlingSeconds) break;
                if (Take(AmrFault.PickFailure)) { await FailAsync(run, "PICK_FAILED", sp, ct); break; }
                if (!await ReportAsync(run, drop: false, sp, ct)) break;
                run.Picked = true; run.SourceExitPending = !run.IsTask; run.TravelTotal = 0; run.Path = null; run.Along = 0; run.Next = [];
                run.Phase = run.Destination is null ? AmrRunPhase.WaitingForTrigger : AmrRunPhase.ToDestination;
                break;
            case AmrRunPhase.WaitingForTrigger:
                if (run.IsTask) { run.Destination ??= await TaskDestinationAsync(run, sp, ct); }
                // the AMR waits outside the port it picked at, so that port is released
                if (run.SourceExitPending)
                {
                    if (_map is not null) { if (!await LeaveOnMapAsync(run, run.Source!, seconds, sp, ct)) break; }
                    else { var src = Place(run.Source); Move(run.Robot!, (src.X, src.Y + PortRadius * 2), seconds); }
                    await ExitIfOutsideAsync(run, run.Source!, sp, ct);
                }
                if (run.Destination is not null) { run.Phase = AmrRunPhase.ToDestination; run.TravelTotal = 0; run.Path = null; run.Along = 0; }
                break;
            case AmrRunPhase.ToDestination:
                var arrived = await TravelAsync(run, run.Destination, seconds, sp, ct);
                if (run.Phase == AmrRunPhase.Failed) break;
                if (run.SourceExitPending) await ExitIfOutsideAsync(run, run.Source!, sp, ct);
                if (arrived) { run.Phase = AmrRunPhase.Dropping; run.Handling = 0; Note(run, $"at {run.Destination}: drop trigger"); }
                break;
            case AmrRunPhase.Dropping:
                if ((run.Handling += seconds) < HandlingSeconds) break;
                if (Take(AmrFault.DropFailure)) { await FailAsync(run, "DROP_FAILED", sp, ct); break; }
                if (!await ReportAsync(run, drop: true, sp, ct)) break;
                run.Dropped = true; run.DestinationExitPending = !run.IsTask; run.Phase = AmrRunPhase.Leaving; run.Path = null; run.Along = 0;
                break;
            case AmrRunPhase.Leaving:
                // the AMR stays in the destination port after the drop: nothing releases the port until it really leaves
                if (Take(AmrFault.ExitStall)) { run.Stalled = true; Note(run, $"stalled in the port of {run.Destination} (not leaving)"); break; }
                if (_map is not null) { if (!await LeaveOnMapAsync(run, run.Destination!, seconds, sp, ct)) break; }
                else { var dest = Place(run.Destination); Move(run.Robot!, (dest.X, dest.Y - PortRadius * 2), seconds); }
                if (run.SourceExitPending) await ExitIfOutsideAsync(run, run.Source!, sp, ct);
                if (run.DestinationExitPending) await ExitIfOutsideAsync(run, run.Destination!, sp, ct);
                if (!run.DestinationExitPending && !run.SourceExitPending) { Finish(run, AmrRunPhase.Completed, "completed"); _rcs.SetStatus(run.RequestCode, RcsTaskStatus.Completed); }
                break;
        }
    }

    private static List<(double X, double Y)> NextRoute(SimMapGraph m, Run run) =>
        m.PointOf(run.Source) is { } a && m.PointOf(run.Destination) is { } b ? m.Route(a, b) ?? [] : [];

    /// <summary>Drives <paramref name="seconds"/> along the route of the run; true = at its end.</summary>
    private bool Drive(Run run, double seconds)
    {
        lock (_gate)
        {
            var r = run.Robot!; var path = run.Path!; var total = SimMapGraph.Length(path);
            var before = run.Along; run.Along = Math.Min(total, run.Along + SpeedMmPerSecond * seconds);
            r.Battery = Math.Max(0, r.Battery - (run.Along - before) / 100_000);
            var left = run.Along;
            for (var i = 1; i < path.Count; i++)
            {
                var a = path[i - 1]; var b = path[i]; var d = SimMapGraph.Distance(a, b);
                if (d <= 0) continue;
                if (left <= d || i == path.Count - 1)
                {
                    var f = Math.Clamp(left / d, 0, 1); (r.X, r.Y) = (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f); r.Dir = _map!.DirectionOf(b.X - a.X, b.Y - a.Y);
                    break;
                }
                left -= d;
            }
            if (total <= 0 && path.Count > 0) (r.X, r.Y) = path[^1];
            return run.Along >= total;
        }
    }

    private async Task<bool> TravelOnMapAsync(Run run, SimMapGraph m, string? location, double seconds, IServiceProvider sp, CancellationToken ct)
    {
        if (run.Path is null)
        {
            if (m.PointOf(location) is not { } to) { await FailAsync(run, $"NO_MAP_POINT: {location ?? "?"} has no point on map {m.MapId}", sp, ct); return false; }
            var at = m.Nearest((run.Robot!.X, run.Robot.Y));
            if (m.Route(at.Id, to) is not { } route) { await FailAsync(run, $"NO_MAP_PATH: map {m.MapId} has no route from point {at.Id} to {location} (point {to})", sp, ct); return false; }
            if (at.Distance > 1) route.Insert(0, (run.Robot.X, run.Robot.Y));   // stopped between two points: first back to the point
            run.Path = route; run.Along = 0;
            Note(run, $"route to {location}: {route.Count} positions, {Math.Round(SimMapGraph.Length(route))} mm");
        }
        var half = SimMapGraph.Length(run.Path) / 2;
        var arrived = Drive(run, seconds);
        if (run.Along >= half && Take(AmrFault.MoveFailure)) { await FailAsync(run, "MOVE_FAILED", sp, ct); return false; }
        return arrived;
    }

    /// <summary>Drives out of a station along the map; false = it cannot (the run has failed).</summary>
    private async Task<bool> LeaveOnMapAsync(Run run, string location, double seconds, IServiceProvider sp, CancellationToken ct)
    {
        var m = _map!;
        if (run.Path is null)
        {
            if (m.PointOf(location) is not { } from || m.Leave(from, MapPortRadius) is not { } route) { await FailAsync(run, $"NO_MAP_PATH: the AMR cannot leave {location} on map {m.MapId}", sp, ct); return false; }
            run.Path = route; run.Along = 0;
        }
        Drive(run, seconds);
        return true;
    }

    /// <summary>WCS decides whether the work still exists: a cancelled or finished leg or task stops the run; anything that is not a Simulator record is left alone.</summary>
    private async Task<bool> StillOpenAsync(Run run, IServiceProvider sp, CancellationToken ct)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var (open, parent, dest) = await uow.ExecuteAsync<(bool, string, string?)>(async t =>
        {
            if (run.IsTask)
            {
                var task = await sp.GetRequiredService<ITransportTaskRepository>().GetAsync(run.EntityId, t);
                if (task is not null)
                {
                    run.RackKind = task.RackKind.ToString(); run.CreatedAt = task.CreatedAt; run.PickedAt = task.PickConfirmedAt; run.DroppedAt = task.DropConfirmedAt; run.ProcessCode = task.Flow.ToString();
                    if (run.RackCode is null && task.RackId is { } rid && await sp.GetRequiredService<IRackRepository>().GetAsync(rid, t) is { } rack) run.RackCode = rack.ExternalRackId ?? rid.ToString()[..8];
                }
                var alive = task is not null && task.ExecutionMode == ExecutionMode.Simulator && (task.State is not (TaskState.Completed or TaskState.Failed or TaskState.Cancelled) || run.Dropped || run.Phase == AmrRunPhase.Failed
                    || (run.Phase == AmrRunPhase.Dropping && task.DropConfirmedAt is not null));
                return (alive, run.Parent, run.Destination);
            }
            var routing = sp.GetRequiredService<IRoutingRepository>();
            var leg = await routing.GetLegAsync(run.EntityId, t);
            var order = leg is null ? null : await routing.GetOrderAsync(leg.OrderId, t);
            var ok = leg is not null && order is not null && order.ExecutionMode == ExecutionMode.Simulator && (run.Dropped || (!leg.IsTerminal && !order.IsTerminal));
            if (leg is not null && order is not null)
            {
                run.OrderId = order.Id; run.ProcessCode = order.ProcessCode; run.LegSequence = leg.Sequence; run.StepKey = leg.StepKey; run.RackKind = leg.RackKind.ToString();
                run.CreatedAt = leg.CreatedAt; run.PickedAt = leg.PickConfirmedAt; run.DroppedAt = leg.DropConfirmedAt;
                if (run.RackCode is null && leg.RackId is { } rid && await sp.GetRequiredService<IRackRepository>().GetAsync(rid, t) is { } rack) run.RackCode = rack.ExternalRackId ?? rid.ToString()[..8];
            }
            return (ok, order?.RequestKey ?? run.Parent, leg?.DestinationLocationCode ?? run.Destination);
        }, ct);
        if (!open) { Finish(run, AmrRunPhase.Cancelled, "the work is closed in WCS"); return false; }
        run.Parent = parent;
        // the destination of a running AMR comes from the trigger only (a lost one is repeated on request, see RecoverAsync)
        if (!run.IsTask && run.Destination is null && run.Phase == AmrRunPhase.Queued) run.Destination = dest;
        return true;
    }

    private bool Assign(Run run)
    {
        lock (_gate)
        {
            if (!_connected) return false;
            Robot? robot;
            if (run.RequestedRobot is { } code)
            {
                if (!_robots.TryGetValue(code, out robot)) _robots[code] = robot = new Robot { Code = code };
                if (robot.Run is not null) return false;
            }
            else robot = _robots.Values.Where(r => r.Run is null).OrderBy(r => r.Code, StringComparer.Ordinal).FirstOrDefault();
            if (robot is null) return false;
            robot.Run = run; run.Robot = robot; _rcs.SetStatus(run.RequestCode, RcsTaskStatus.Executing);
            if (!run.IsTask) _rcs.ReassignRobot(run.RequestCode, robot.Code);
            return true;
        }
    }

    private static bool Move(Robot r, (double X, double Y) to, double seconds)
    {
        var dx = to.X - r.X; var dy = to.Y - r.Y; var distance = Math.Sqrt(dx * dx + dy * dy); var step = SpeedMmPerSecond * seconds;
        var moved = Math.Min(step, distance);
        r.Battery = Math.Max(0, r.Battery - moved / 100_000);
        if (distance <= step) { r.X = to.X; r.Y = to.Y; return true; }
        r.X += dx / distance * step; r.Y += dy / distance * step; return false;
    }

    private async Task<bool> TravelAsync(Run run, string? location, double seconds, IServiceProvider sp, CancellationToken ct)
    {
        if (_map is { } m) return await TravelOnMapAsync(run, m, location, seconds, sp, ct);
        var to = Place(location);
        var r = run.Robot!;
        if (run.TravelTotal == 0) { run.TravelTotal = Math.Max(1, Math.Sqrt(Math.Pow(to.X - r.X, 2) + Math.Pow(to.Y - r.Y, 2))); run.HalfTravelled = run.TravelTotal / 2; }
        var arrived = Move(r, to, seconds);
        var left = Math.Sqrt(Math.Pow(to.X - r.X, 2) + Math.Pow(to.Y - r.Y, 2));
        if (left <= run.HalfTravelled && Take(AmrFault.MoveFailure)) { await FailAsync(run, "MOVE_FAILED", sp, ct); return false; }
        return arrived;
    }

    /// <summary>The AMR is farther from the port than its radius: tell WCS through the same service an operator confirmation uses. A refusal (hand-over not recorded yet) is tried again.</summary>
    private async Task ExitIfOutsideAsync(Run run, string location, IServiceProvider sp, CancellationToken ct)
    {
        var p = Place(location); var r = run.Robot!;
        if (Math.Sqrt(Math.Pow(p.X - r.X, 2) + Math.Pow(p.Y - r.Y, 2)) <= (_map is null ? PortRadius : MapPortRadius) || !_connected) return;
        try
        {
            var released = await sp.GetRequiredService<StationTransactionService>().ConfirmAgvExitedForLegAsync(run.EntityId, location, ct);
            if (location == run.Source) run.SourceExitPending = false;
            if (location == run.Destination && run.Dropped) run.DestinationExitPending = false;
            if (released > 0) { Note(run, $"left the port of {location}"); await RecordAsync(sp, "AMR left port", run, new { location }, "Applied", ct); }
        }
        catch (AppException e) when (e.Code is "HANDOVER_NOT_COMPLETED" or "TRANSACTION_NEEDS_RECONCILIATION") { Note(run, $"port of {location} not released: {e.Code}"); run.Wait = RetrySeconds; }
    }

    /// <summary>Reports a pick or a drop to WCS. FALSE = not delivered yet (delay, lost connection, refused): the AMR stays at the station and tries again.</summary>
    private async Task<bool> ReportAsync(Run run, bool drop, IServiceProvider sp, CancellationToken ct)
    {
        if (!_connected) return false;
        if (Take(AmrFault.ResponseDelay)) { run.Wait = _delaySeconds; Note(run, $"response delayed {_delaySeconds.ToString(CultureInfo.InvariantCulture)} s"); return false; }
        var name = drop ? "DropConfirmed" : "PickConfirmed";
        if (run.IsTask) return await TaskHandlingAsync(run, drop, sp, ct);
        var engine = sp.GetRequiredService<IRouteEngine>();
        Task<EventResult> Send() => drop ? engine.ConfirmDropAsync(run.EntityId, ConfirmationSource.Simulator, "amr-simulator", null, ct) : engine.ConfirmPickAsync(run.EntityId, ConfirmationSource.Simulator, "amr-simulator", null, ct);
        var result = await Send();
        var refused = result.Outcome == EventOutcome.Rejected ? $"{name}: {result.Message}" : null;
        if (refused is null || refused != run.LastRefusal) await RecordAsync(sp, name, run, new { location = drop ? run.Destination : run.Source, message = result.Message }, result.Outcome.ToString(), ct);   // a repeated refusal is recorded once
        if (refused is not null) { if (refused != run.LastRefusal) Note(run, $"{name} refused: {result.Message}"); run.LastRefusal = refused; run.Wait = RetrySeconds; return false; }
        run.LastRefusal = null;
        Note(run, $"{name}: {result.Outcome}");
        if (Take(AmrFault.DuplicateResponse)) { var again = await Send(); Note(run, $"{name} sent again: {again.Outcome}"); await RecordAsync(sp, name, run, new { repeated = true }, again.Outcome.ToString(), ct); }
        return true;
    }

    // ------------------------------------------------------------------ task engine: the existing simulation driver produces the same signals as the manual "advance"
    private async Task<SimulationStep?> TaskSignalAsync(Run run, IServiceProvider sp, CancellationToken ct)
    {
        try { var s = await sp.GetRequiredService<SimulationDriver>().AdvanceAsync(run.EntityId, ct); if (s.Signal is not null) Note(run, $"{s.Signal}: {s.Task.State}"); return s; }
        catch (AppException e) { Note(run, $"task signal refused: {e.Code}"); return null; }
    }

    private async Task<bool> TaskHandlingAsync(Run run, bool drop, IServiceProvider sp, CancellationToken ct)
    {
        var task = await sp.GetRequiredService<IUnitOfWork>().ExecuteAsync(t => sp.GetRequiredService<ITransportTaskRepository>().GetAsync(run.EntityId, t), ct);
        if (task is null) return false;
        if (drop ? task.DropConfirmedAt is not null : task.PickConfirmedAt is not null) { if (!drop) run.Destination ??= await TaskDestinationAsync(run, sp, ct); return true; }
        if (task.State is not (TaskState.Executing or TaskState.WaitingForPlc or TaskState.Accepted) || (drop && task.CurrentLeg != TaskLeg.Drop)) { run.Wait = RetrySeconds / 4; return false; }
        await TaskSignalAsync(run, sp, ct);   // AgvStarted / PLC permission / pick / drop, one at a time, as the driver decides
        return false;                          // looked at again on the next step, until WCS shows the confirmation
    }

    private async Task<string?> TaskDestinationAsync(Run run, IServiceProvider sp, CancellationToken ct) =>
        await sp.GetRequiredService<IUnitOfWork>().ExecuteAsync<string?>(async t =>
        {
            var task = await sp.GetRequiredService<ITransportTaskRepository>().GetAsync(run.EntityId, t);
            return task is { CurrentLeg: TaskLeg.Drop, DestinationLocationId: { } d, State: not (TaskState.Continuing or TaskState.WaitingForDestination) } ? (await sp.GetRequiredService<ILocationRepository>().GetAsync(d, t))?.Code : null;
        }, ct);

    private async Task FailAsync(Run run, string reason, IServiceProvider sp, CancellationToken ct)
    {
        run.FailedIn = run.Phase; run.Phase = AmrRunPhase.Failed; run.Failure = reason; Note(run, $"failed: {reason}");
        _rcs.SetStatus(run.RequestCode, RcsTaskStatus.Interrupted);
        if (run.IsTask)
        {   // the approved path of an interrupted task (CR-008B-4): rack unknown, locations to reconcile, AGV on hold
            var r = await sp.GetRequiredService<ITaskOrchestrator>().HandleSignalAsync(new TaskSignal(SignalKind.RcsInterrupted, run.EntityId, EventKey: $"amr-sim:{run.RequestCode}:{reason}", Source: ConfirmationSource.Simulator, User: "amr-simulator"), ct);
            await RecordAsync(sp, "RcsInterrupted", run, new { reason }, r.Outcome.ToString(), ct);
            if (run.Robot is { } robot) robot.Run = null;
        }
        else
        {   // no approved rule says what a failed leg does to the order: it is recorded and shown, nothing else changes
            await sp.GetRequiredService<IRouteEngine>().ReportLegProblemAsync(run.EntityId, "LEG_RCS_INTERRUPTED", $"{reason} ({run.Robot?.Code})", ct);
            await RecordAsync(sp, "LegInterrupted", run, new { reason }, "Recorded", ct);
        }
    }

    private Task RecordAsync(IServiceProvider sp, string name, Run run, object payload, string result, CancellationToken ct) =>
        sp.GetRequiredService<IRcsMessageRecorder>().RecordAsync(new RcsMessageEntry(ExecutionMode.Simulator, RcsMessageDirection.In, $"Simulated AGV Event: {name}", "SIMULATOR (in-process, no network)", run.RequestCode,
            Payload: System.Text.Json.JsonSerializer.Serialize(new { taskCode = run.RequestCode, robotCode = run.Robot?.Code, data = payload }), Result: result), ct);

    // ------------------------------------------------------------------ helpers
    private void Finish(Run run, AmrRunPhase phase, string note) { run.Phase = phase; Note(run, note); lock (_gate) if (run.Robot is { } r && r.Run == run) r.Run = null; }

    private Run? Find(string requestCode) { lock (_gate) return _runs.GetValueOrDefault(requestCode); }

    private void Note(Run run, string text) { lock (_gate) { run.Events.Add($"{_clock.UtcNow:HH:mm:ss.fff} {text}"); if (run.Events.Count > 60) run.Events.RemoveAt(0); } }

    private bool Take(AmrFault f) { lock (_gate) { if (!_faults.TryGetValue(f, out var n) || n <= 0) return false; if (n == 1) _faults.Remove(f); else _faults[f] = n - 1; return true; } }

    private void RequireEnabled() { if (!Enabled) throw AppException.Rejected("AMR_SIMULATION_OFF", "Switch the AMR simulation on first."); }

    /// <summary>The simulated fleet as the RCS reports it (fleet monitoring reads it through its normal query). Status codes of queryAgvStatus: 2 working, 3 task abnormal, 4 idle, 6 lifting.</summary>
    private void Publish()
    {
        if (_robots.Count == 0) return;
        static string N(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
        _rcs.SetFleet(_robots.Values.OrderBy(r => r.Code, StringComparer.Ordinal).Select(r =>
        {
            var phase = r.Run?.Phase;
            var status = r.Run is { Stalled: true } || phase == AmrRunPhase.Failed ? "3" : phase is AmrRunPhase.Picking or AmrRunPhase.Dropping ? "6" : phase is null or AmrRunPhase.Queued ? "4" : "2";
            var speed = phase is AmrRunPhase.ToSource or AmrRunPhase.ToDestination or AmrRunPhase.Leaving && r.Run is { Stalled: false } ? N(SpeedMmPerSecond) : "0";
            var carrying = r.Run is { Picked: true, Dropped: false } c ? c.RackCode : null;
            return new RcsFleetAmr(r.Code, r.Dir.ToString(CultureInfo.InvariantCulture), null, N(r.Battery), N(r.X), N(r.Y), _map?.MapCode ?? "SIM-MAP", speed, status, "0", "0", carrying, null, []);
        }));
    }

    private static double Number(string? s, double fallback = 0) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

/// <summary>The clock of the AMR simulation. It does nothing while the simulation is off.</summary>
public sealed class AmrSimulationHostedService(AmrExecutionSimulator simulator, IOptions<AmrSimulationOptions> options, ILogger<AmrSimulationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = TimeSpan.FromMilliseconds(Math.Clamp(options.Value.TickMs, 50, 5000));
        using var timer = new PeriodicTimer(tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await simulator.StepAsync(tick, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "AMR simulation step failed. {EventCode}", "AMR_SIM_STEP_FAILED"); }
        }
    }
}
