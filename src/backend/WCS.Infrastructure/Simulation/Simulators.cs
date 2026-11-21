using System.Collections.Concurrent;
using WCS.Application.Abstractions;
using WCS.Domain.Common;

namespace WCS.Infrastructure.Simulation;

// -------------------------------------------------------------------------------------------------------------
// In-memory simulators. They exist so the business flows can be exercised without devices. They are NOT a model of
// the Mitsubishi MC protocol nor of the HIK RCS-2000 API: no vendor endpoint or address is assumed anywhere here.
// -------------------------------------------------------------------------------------------------------------

public sealed class PlcSimulator : IPlcAdapter
{
    private readonly ConcurrentDictionary<string, int> _signals = new();
    private PlcConnectionStatus _status = PlcConnectionStatus.Disconnected;

    public Task ConnectAsync(CancellationToken ct = default) { _status = PlcConnectionStatus.Connected; return Task.CompletedTask; }
    public Task ReconnectAsync(CancellationToken ct = default) => ConnectAsync(ct);
    public PlcConnectionStatus GetConnectionStatus() => _status;

    public Task<IReadOnlyList<PlcSignalValue>> ReadSignalsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlcSignalValue>>(keys.Select(k => new PlcSignalValue(k, _signals.GetValueOrDefault(k))).ToList());

    public Task WriteSignalsAsync(IReadOnlyCollection<PlcSignalValue> values, CancellationToken ct = default)
    {
        foreach (var v in values) _signals[v.Key] = v.Value;
        return Task.CompletedTask;
    }
}

/// <summary>Station handshake simulator. Tests/operators enqueue requests; permission requests are recorded.</summary>
public sealed class StationSignalSimulator : IStationSignalService
{
    private readonly ConcurrentQueue<StationRequest> _requests = new();
    private readonly ConcurrentQueue<StationRequest> _acknowledged = new();
    private readonly ConcurrentQueue<(string Station, TransferDirection Direction, Guid TaskId)> _permissionRequests = new();

    public void EnqueueRequest(StationRequest request) => _requests.Enqueue(request);
    public IReadOnlyCollection<StationRequest> Acknowledged => _acknowledged.ToArray();
    public IReadOnlyCollection<(string Station, TransferDirection Direction, Guid TaskId)> PermissionRequests => _permissionRequests.ToArray();

    public Task<IReadOnlyList<StationRequest>> PollRequestsAsync(CancellationToken ct = default)
    {
        var list = new List<StationRequest>();
        while (_requests.TryDequeue(out var r)) list.Add(r);
        return Task.FromResult<IReadOnlyList<StationRequest>>(list);
    }

    public Task AcknowledgeRequestAsync(StationRequest request, CancellationToken ct = default)
    {
        _acknowledged.Enqueue(request);
        return Task.CompletedTask;
    }

    public Task RequestTransferPermissionAsync(string stationCode, TransferDirection direction, Guid taskId, CancellationToken ct = default)
    {
        _permissionRequests.Enqueue((stationCode, direction, taskId));
        return Task.CompletedTask;
    }
}

public enum RcsFault { None, RejectNextCreate, TimeoutAfterCreate, TimeoutBeforeCreate, RejectNextContinue, TimeoutNextContinue }

/// <summary>RCS simulator: idempotent by request code, supports fault injection to exercise timeout/unknown-result handling.</summary>
public sealed class RcsSimulator : IRcsClient
{
    private sealed class SimTask(string requestCode, string rcsTaskCode)
    {
        public string RequestCode { get; } = requestCode;
        public string RcsTaskCode { get; } = rcsTaskCode;
        public RcsTaskStatus Status { get; set; } = RcsTaskStatus.Created;
        /// <summary>CR-028: the AMR the simulated RCS gave the task (null = none yet). Legacy: the robot code WCS sent.</summary>
        public string? AgvCode { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, SimTask> _tasks = [];
    private int _counter;

    public RcsFault NextFault { get; set; }
    /// <summary>CR-008A: simulates an RCS outage - create/continue/cancel return an unknown outcome and queries are inconclusive until reset.</summary>
    public bool Unreachable { get; set; }
    public int CreateCalls { get; private set; }
    public int ContinueCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public int QueryCalls { get; private set; }
    public int CreatedTaskCount { get { lock (_gate) return _tasks.Count; } }

    public RcsTaskStatus? StatusOf(string requestCode) { lock (_gate) return _tasks.TryGetValue(requestCode, out var t) ? t.Status : null; }

    public Task<RcsCallResult> CreateTaskAsync(RcsCreateTaskRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            CreateCalls++;
            var fault = Take();
            if (Unreachable) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated RCS outage"));
            if (fault == RcsFault.RejectNextCreate) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Rejected, null, "Simulated rejection"));
            if (fault == RcsFault.TimeoutBeforeCreate) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated timeout (not created)"));
            if (!_tasks.TryGetValue(request.RequestCode, out var task))
            {
                _tasks[request.RequestCode] = task = new SimTask(request.RequestCode, $"SIM-{++_counter:D5}");
                // CR-028: a request WITHOUT a robot code is what RcsManaged dispatch sends: the simulated RCS chooses the AMR itself (repeating the same request code never creates or re-assigns a second task)
                task.AgvCode = request.RobotCode ?? (AutoAssign ? ChooseRobot() : null);
            }
            return Task.FromResult(fault == RcsFault.TimeoutAfterCreate
                ? new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated timeout (task WAS created)")
                : new RcsCallResult(RcsCallOutcome.Accepted, task.RcsTaskCode, null));
        }
    }

    public Task<RcsCallResult> ContinueTaskAsync(string requestCode, string? rcsTaskCode, string destinationLocationCode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ContinueCalls++;
            var fault = Take();
            if (Unreachable) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated RCS outage"));
            if (fault == RcsFault.RejectNextContinue) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Rejected, null, "Simulated rejection"));
            if (!_tasks.TryGetValue(requestCode, out var t)) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Rejected, null, "Unknown task"));
            if (fault == RcsFault.TimeoutNextContinue)
                return Task.FromResult(new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated timeout (not applied)"));
            t.Status = RcsTaskStatus.Executing;
            return Task.FromResult(new RcsCallResult(RcsCallOutcome.Accepted, t.RcsTaskCode, null));
        }
    }

    public Task<RcsCallResult> CancelTaskAsync(string requestCode, string? rcsTaskCode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            CancelCalls++;
            if (Unreachable) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated RCS outage"));
            if (!_tasks.TryGetValue(requestCode, out var t)) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Rejected, null, "Unknown task"));
            if (t.Status is RcsTaskStatus.Completed or RcsTaskStatus.Cancelled) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Rejected, null, "Already finished"));
            t.Status = RcsTaskStatus.Cancelling;
            return Task.FromResult(new RcsCallResult(RcsCallOutcome.Accepted, t.RcsTaskCode, null));
        }
    }

    public Task<(RcsTaskStatus Status, string? RcsTaskCode)> QueryTaskStatusAsync(string requestCode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            QueryCalls++;
            if (Unreachable) return Task.FromResult((RcsTaskStatus.Unknown, (string?)null));
            return Task.FromResult(_tasks.TryGetValue(requestCode, out var t) ? (t.Status, t.RcsTaskCode) : (RcsTaskStatus.NotFound, (string?)null));
        }
    }

    public Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default) =>
        Task.FromResult<RcsAgvStatus?>(new RcsAgvStatus(robotCode, true, "simulated"));

    // ------------------------------------------------------------------ CR-028: the simulated RCS chooses the AMR (queryTaskStatus agvCode, 5.2.15)
    /// <summary>True (default): a task created without a robot code gets the least-loaded free AMR of the simulated fleet at once. False: tasks stay unassigned until <see cref="AssignPending"/>.</summary>
    public bool AutoAssign { get; set; } = true;
    /// <summary>Makes the task status query inconclusive (as an outage of that API would).</summary>
    public bool TaskQueryFailure { get; set; }

    private static bool Finished(RcsTaskStatus s) => s is RcsTaskStatus.Completed or RcsTaskStatus.Cancelled;

    /// <summary>The least-loaded AMR of the simulated fleet that is not excluded; null when the fleet is empty. Ties: the smaller robot code.</summary>
    private string? ChooseRobot() =>
        _fleet.Where(a => a.ExclType != "1").Select(a => a.RobotCode)
            .OrderBy(code => _tasks.Values.Count(t => t.AgvCode == code && !Finished(t.Status))).ThenBy(code => code, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Assigns every task that has no AMR yet (used after the fleet was filled or AutoAssign was off).</summary>
    public void AssignPending() { lock (_gate) foreach (var t in _tasks.Values.Where(t => t.AgvCode is null && !Finished(t.Status))) t.AgvCode = ChooseRobot(); }

    /// <summary>The AMR the simulated RCS gave the task (null = none).</summary>
    public string? RobotOf(string requestCode) { lock (_gate) return _tasks.TryGetValue(requestCode, out var t) ? t.AgvCode : null; }

    /// <summary>Forces another AMR for a task (a robot change by the RCS).</summary>
    public void ReassignRobot(string requestCode, string? robotCode) { lock (_gate) if (_tasks.TryGetValue(requestCode, out var t)) t.AgvCode = robotCode; }

    public int TaskCount { get { lock (_gate) return _tasks.Count; } }

    public Task<RcsTaskInfo> QueryTaskAsync(string requestCode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            QueryCalls++;
            if (Unreachable || TaskQueryFailure) return Task.FromResult(new RcsTaskInfo(RcsTaskStatus.Unknown, null, null));
            return Task.FromResult(_tasks.TryGetValue(requestCode, out var t) ? new RcsTaskInfo(t.Status, t.RcsTaskCode, t.AgvCode) : new RcsTaskInfo(RcsTaskStatus.NotFound, null, null));
        }
    }

    // ------------------------------------------------------------------ CR-027: the simulated AMR fleet (empty unless a test / Development fills it; never derived from the agvs table)
    private List<WCS.Application.Fleet.RcsFleetAmr> _fleet = [];
    /// <summary>How the next fleet queries fail: null = they succeed.</summary>
    public WCS.Application.Fleet.RcsFleetResult? FleetFailure { get; set; }
    public int FleetQueryCalls { get; private set; }

    public void SetFleet(IEnumerable<WCS.Application.Fleet.RcsFleetAmr> amrs) { lock (_gate) _fleet = [.. amrs]; }

    /// <summary>N simulated AMRs with codes SIM-001..SIM-N and plausible, deterministic values (idle, charged, spread on a line). 0 = an empty fleet.</summary>
    public void SetFleetSize(int n) => SetFleet(Enumerable.Range(1, Math.Max(0, n)).Select(i => new WCS.Application.Fleet.RcsFleetAmr($"SIM-{i:D3}", "90", null, (100 - i % 50).ToString(),
        (i * 1000).ToString(), "2000", "SIM-MAP", "0", "4", "0", "0", null, null, [])));

    public Task<WCS.Application.Fleet.RcsFleetResult> QueryFleetAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            FleetQueryCalls++;
            if (Unreachable) return Task.FromResult(WCS.Application.Fleet.RcsFleetResult.Fail(WCS.Application.Fleet.FleetErrorCodes.Unreachable, "Simulated RCS outage"));
            if (FleetFailure is { } f) return Task.FromResult(f);
            return Task.FromResult(WCS.Application.Fleet.RcsFleetResult.Ok([.. _fleet]));
        }
    }

    /// <summary>Simulates the RCS finishing the cancellation (what a real callback / status poll would report).</summary>
    public void ConfirmCancel(string requestCode) { lock (_gate) { if (_tasks.TryGetValue(requestCode, out var t)) t.Status = RcsTaskStatus.Cancelled; } }
    /// <summary>CR-008A: force the status RCS reports for a task (Completed, Interrupted, ...), as a real status poll would.</summary>
    public void SetStatus(string requestCode, RcsTaskStatus status) { lock (_gate) { if (_tasks.TryGetValue(requestCode, out var t)) t.Status = status; } }
    /// <summary>CR-008A: RCS forgets the task (query then answers NotFound).</summary>
    public void Forget(string requestCode) { lock (_gate) _tasks.Remove(requestCode); }
    public void MarkExecuting(string requestCode) { lock (_gate) { if (_tasks.TryGetValue(requestCode, out var t)) t.Status = RcsTaskStatus.Executing; } }

    private RcsFault Take() { var f = NextFault; NextFault = RcsFault.None; return f; }
}
