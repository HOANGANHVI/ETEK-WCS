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
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, SimTask> _tasks = [];
    private int _counter;

    public RcsFault NextFault { get; set; }
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
            if (fault == RcsFault.RejectNextCreate) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Rejected, null, "Simulated rejection"));
            if (fault == RcsFault.TimeoutBeforeCreate) return Task.FromResult(new RcsCallResult(RcsCallOutcome.Unknown, null, "Simulated timeout (not created)"));
            if (!_tasks.TryGetValue(request.RequestCode, out var task))
                _tasks[request.RequestCode] = task = new SimTask(request.RequestCode, $"SIM-{++_counter:D5}");
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
            return Task.FromResult(_tasks.TryGetValue(requestCode, out var t) ? (t.Status, t.RcsTaskCode) : (RcsTaskStatus.NotFound, (string?)null));
        }
    }

    public Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default) =>
        Task.FromResult<RcsAgvStatus?>(new RcsAgvStatus(robotCode, true, "simulated"));

    /// <summary>Simulates the RCS finishing the cancellation (what a real callback / status poll would report).</summary>
    public void ConfirmCancel(string requestCode) { lock (_gate) { if (_tasks.TryGetValue(requestCode, out var t)) t.Status = RcsTaskStatus.Cancelled; } }
    public void MarkExecuting(string requestCode) { lock (_gate) { if (_tasks.TryGetValue(requestCode, out var t)) t.Status = RcsTaskStatus.Executing; } }

    private RcsFault Take() { var f = NextFault; NextFault = RcsFault.None; return f; }
}
