using System.Threading.Channels;
using WCS.Application.Abstractions;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>Fire-and-forget sink for diagnostics (persisted + pushed to Admin) and realtime events. Never blocks a PLC worker.</summary>
public interface IPlcEvents
{
    /// <param name="args">Parameters of the event (stored in the details JSON as <c>args</c>) so the UI can phrase the event in the user's language; <paramref name="message"/> stays the raw original text.</param>
    void Log(PlcLogLevel level, string? plcId, string? stationId, string eventName, string message, IReadOnlyDictionary<string, object?>? args = null);
    void Publish(string name, object payload, bool adminOnly = false);
}

/// <param name="IsAmbiguous">The request may have reached the PLC but no confirmation arrived (CR-003). The write was NOT retried; verify the value before acting.</param>
public sealed record WriteResult(bool Success, string? Code = null, string? Message = null, bool IsAmbiguous = false);

public sealed record PlcWorkerStatus(string PlcId, string Name, string State, string Mode, string? Reason, IReadOnlyList<string> MissingParameters, DateTimeOffset? LastPollAt,
    DateTimeOffset? LastSuccessAt, long Cycles, long Reconnects, int ConsecutiveFailures, int Stations, int TotalPoints, int ConfiguredPoints, int GoodPoints,
    string? IpAddress, string? LastError, PlcDiagnostics? Diagnostics);

/// <summary>
/// One worker per physical PLC. It is the ONLY user of its connection, so reads, writes and reconnects never overlap on the socket.
/// Each cycle: apply queued writes, read every station independently (a failing station never stops the others), publish changes.
/// A broken link is retried with back-off without affecting the other PLCs' workers.
/// </summary>
public sealed class PlcWorker : IAsyncDisposable
{
    private sealed record WriteRequest(IoPointRef Point, PointValue Value, TaskCompletionSource<WriteResult> Done);

    private readonly string _plcId, _name;
    private readonly PlcRuntimeMode _mode;
    private readonly PlcRuntimeOptions _options;
    private readonly PlcEndpointConfig _config;
    private readonly bool _enabled;
    private readonly string? _disabledReason;
    private readonly IReadOnlyList<StationIoHandler> _stations;
    private readonly IPlcEndpointFactory _factory;
    private readonly IPlcEvents _events;
    private readonly IClock _clock;
    private readonly Channel<WriteRequest> _writes = Channel.CreateUnbounded<WriteRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IPlcEndpoint? _endpoint;

    private EndpointState _state = EndpointState.Disconnected;
    private string? _reason, _lastError;
    private IReadOnlyList<string> _missing = [];
    private DateTimeOffset? _lastPoll, _lastSuccess;
    private long _cycles, _reconnects;
    private int _failures;
    private bool _everConnected;
    private DateTimeOffset? _lostAt;                                  // diagnostics: when the current outage began
    private readonly Dictionary<string, int> _notApprovedLogged = [];   // diagnostics: last logged count of unpolled points per station (log on change only)

    public PlcWorker(string plcId, string name, PlcRuntimeMode mode, PlcRuntimeOptions options, PlcEndpointConfig config, bool enabled, string? disabledReason,
        IReadOnlyList<StationIoHandler> stations, IPlcEndpointFactory factory, IPlcEvents events, IClock clock)
    {
        _plcId = plcId; _name = name; _mode = mode; _options = options; _config = config; _enabled = enabled; _disabledReason = disabledReason;
        _stations = stations; _factory = factory; _events = events; _clock = clock;
        if (stations.Any(s => s.PlcId != plcId)) throw new ArgumentException("a PLC worker only serves the stations of its own PLC");
        if (!enabled) { _state = EndpointState.Disabled; _reason = disabledReason; }
    }

    public string PlcId => _plcId;
    public IReadOnlyList<StationIoHandler> Stations => _stations;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _wake.Release();
        if (_loop is not null) { try { await _loop; } catch (OperationCanceledException) { /* stopping */ } }
        _writes.Writer.TryComplete();
        while (_writes.Reader.TryRead(out var w)) w.Done.TrySetResult(new WriteResult(false, "STOPPED", "Kết nối PLC đã dừng."));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_endpoint is not null) await _endpoint.DisposeAsync();
    }

    // -------------------------------------------------------------------------------------------------------------------- status
    public PlcWorkerStatus Status(bool admin)
    {
        lock (_gate)
        {
            var snaps = _stations.Select(s => s.Snapshot(false)).ToList();
            var diag = admin ? _endpoint?.Diagnostics : null;
            return new PlcWorkerStatus(_plcId, _name, _state.ToString(), _mode.ToString(), _reason, _missing, _lastPoll, _lastSuccess, _cycles, _reconnects, _failures,
                _stations.Count, snaps.Sum(s => s.Total), snaps.Sum(s => s.Configured), snaps.Sum(s => s.Good), admin ? _config.IpAddress : null, admin ? _lastError : null, diag);
        }
    }

    private void SetState(EndpointState state, string? reason = null, string? error = null)
    {
        bool changed;
        lock (_gate) { changed = _state != state; _state = state; _reason = reason; if (error is not null) _lastError = error; }
        if (changed)
        {
            _events.Publish("PlcStatusChanged", new { plcId = _plcId, state = state.ToString(), mode = _mode.ToString(), reason });
            _events.Log(state is EndpointState.Faulted ? PlcLogLevel.Warning : PlcLogLevel.Info, _plcId, null, "STATE", $"{_plcId}: {state}{(reason is null ? "" : " - " + reason)}",
                WCS.Application.Logging.LogArgs.Of(("plc", _plcId), ("state", state.ToString()), ("reason", reason)));
        }
    }

    // -------------------------------------------------------------------------------------------------------------------- writes
    /// <summary>Queues a write. The worker executes writes in arrival order between read cycles; the policy check is done by the caller.</summary>
    public Task<WriteResult> EnqueueWriteAsync(IoPointRef point, PointValue value)
    {
        var tcs = new TaskCompletionSource<WriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_writes.Writer.TryWrite(new WriteRequest(point, value, tcs))) return Task.FromResult(new WriteResult(false, "STOPPED", "Kết nối PLC đã dừng."));
        _wake.Release();
        return tcs.Task;
    }

    private async Task DrainWritesAsync(CancellationToken ct)
    {
        while (_writes.Reader.TryRead(out var w))
        {
            if (_endpoint is null || _endpoint.State != EndpointState.Connected) { w.Done.TrySetResult(new WriteResult(false, "NOT_CONNECTED", "PLC chưa kết nối; lệnh ghi không được xếp hàng chờ.")); continue; }
            try
            {
                await _endpoint.WritePointAsync(w.Point, w.Value, ct);
                _events.Log(PlcLogLevel.Info, _plcId, w.Point.StationId, "WRITE", $"{w.Point.StationId}/{w.Point.SignalName} = {w.Value}",
                    WCS.Application.Logging.LogArgs.Of(("station", w.Point.StationId), ("signal", w.Point.SignalName), ("value", w.Value.ToString())));
                w.Done.TrySetResult(new WriteResult(true));
            }
            catch (PlcWriteAmbiguousException ex)
            {
                // Never retried: the PLC may already have executed it. The link was closed by the driver; the main loop reconnects.
                _events.Log(PlcLogLevel.Error, _plcId, w.Point.StationId, "WRITE_AMBIGUOUS", $"{w.Point.StationId}/{w.Point.SignalName}: {ex.Message} Not retried automatically; read the value back to confirm.",
                    PlcDiag.From(PlcDiagClassifier.Classify(ex, PlcDiagStages.Write, PlcDiagCodes.WriteFailed), ("station", w.Point.StationId), ("signal", w.Point.SignalName), ("error", ex.Message)));
                w.Done.TrySetResult(new WriteResult(false, "WRITE_AMBIGUOUS", ex.Message, IsAmbiguous: true));
            }
            catch (OperationCanceledException) { w.Done.TrySetResult(new WriteResult(false, "CANCELLED", "Đã hủy.")); throw; }
            catch (Exception ex)
            {
                _events.Log(PlcLogLevel.Error, _plcId, w.Point.StationId, "WRITE_FAILED", $"{w.Point.StationId}/{w.Point.SignalName}: {ex.Message}",
                    PlcDiag.From(PlcDiagClassifier.Classify(ex, PlcDiagStages.Write, PlcDiagCodes.WriteFailed), ("station", w.Point.StationId), ("signal", w.Point.SignalName), ("error", ex.Message)));
                w.Done.TrySetResult(new WriteResult(false, "WRITE_FAILED", ex.Message));
            }
        }
    }

    // -------------------------------------------------------------------------------------------------------------------- main loop
    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = _options.ReconnectInitialDelayMs;
        try
        {
            if (!_enabled)
            {
                _events.Log(PlcLogLevel.Info, _plcId, null, "DISABLED", _disabledReason ?? "disabled", DisabledArgs());
                MarkAllDisconnected(null);
                while (!ct.IsCancellationRequested) { FailPendingWrites("DISABLED", _disabledReason ?? "Kết nối PLC đang tắt."); await _wake.WaitAsync(TimeSpan.FromMilliseconds(500), ct); }
                return;
            }
            while (!ct.IsCancellationRequested)
            {
                if (_endpoint is null)
                {
                    try { _endpoint = _factory.Create(_mode, _config); }
                    catch (NeedsConfigurationException nc)
                    {
                        lock (_gate) _missing = nc.Missing;
                        SetState(EndpointState.NeedsConfiguration, "[PLC_PARAMS_MISSING] " + string.Join(", ", nc.Missing));
                        MarkAllDisconnected(null);
                        LogLimited("CONFIG_MISSING", PlcLogLevel.Warning, null, $"Connection parameters missing: {string.Join(", ", nc.Missing)}. No connection is attempted.",
                            PlcDiag.Args(PlcDiagCodes.ConfigurationMissing, PlcDiagStages.Config, ("missing", string.Join(",", nc.Missing)), ("ip", _config.IpAddress), ("port", _config.Port)));
                        FailPendingWrites("NEEDS_CONFIGURATION", nc.Message);
                        await _wake.WaitAsync(TimeSpan.FromSeconds(1), ct);
                        continue;
                    }
                }
                if (_endpoint.State != EndpointState.Connected)
                {
                    if (_state != EndpointState.Faulted) SetState(EndpointState.Connecting);   // while retrying a dead link stay Faulted (no flapping, no log flood)
                    var connectSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        await _endpoint.ConnectAsync(ct);
                        var wasRecovery = _everConnected || _failures > 0; var priorFailures = _failures; var lostAt = _lostAt;
                        if (_everConnected) lock (_gate) _reconnects++;
                        _everConnected = true; backoff = _options.ReconnectInitialDelayMs;
                        lock (_gate) _failures = 0;
                        _lostAt = null; _notApprovedLogged.Clear();
                        SetState(EndpointState.Connected);
                        LogTcpConnected(connectSw.ElapsedMilliseconds, priorFailures, lostAt, wasRecovery);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        lock (_gate) _failures++;
                        _lostAt ??= _clock.UtcNow;
                        SetState(EndpointState.Faulted, "[PLC_CONNECT_FAILED] Không kết nối được tới PLC.", ex.Message);
                        MarkAllDisconnected(ex.Message);
                        FailPendingWrites("NOT_CONNECTED", "PLC chưa kết nối.");
                        LogLimited("CONNECT_FAILED", PlcLogLevel.Warning, null, $"Connection failed: {ex.Message}. Retrying in {backoff} ms.",
                            ConnectFailedArgs(ex, connectSw.ElapsedMilliseconds, backoff));
                        await _wake.WaitAsync(TimeSpan.FromMilliseconds(backoff), ct);
                        backoff = Math.Min(backoff * 2, _options.ReconnectMaxDelayMs);
                        continue;
                    }
                }

                await DrainWritesAsync(ct);
                var linkLost = false;
                foreach (var station in _stations)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var readings = await _endpoint.ReadStationAsync(station.Points, ct);
                        var changed = station.Apply(readings, _clock.UtcNow, _mode == PlcRuntimeMode.Simulator);
                        if (changed.Count > 0) _events.Publish("StationIoChanged", station.Snapshot(false, changed));
                        if (readings.Any(r => r.Quality is PointQuality.Good or PointQuality.Simulated)) lock (_gate) _lastSuccess = _clock.UtcNow;
                        var bad = readings.Count(r => r.Quality == PointQuality.Bad);
                        if (bad > 0) LogLimited("READ_BAD_" + station.StationId, PlcLogLevel.Warning, station.StationId, $"{bad} IO point(s) failed to read: {readings.First(r => r.Quality == PointQuality.Bad).Error}",
                            ReadBadArgs(readings, bad));
                        if (_mode != PlcRuntimeMode.Simulator) LogNotApprovedOnChange(station, readings);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        // A failure inside one station must not stop the others.
                        station.Apply(station.Points.Select(p => new PointReading(p.Id, null, (p.Address is null || !p.Pollable) && _mode != PlcRuntimeMode.Simulator ? PointQuality.NotConfigured : PointQuality.Bad, ex.Message)).ToList(), _clock.UtcNow, false);
                        LogLimited("STATION_ERROR_" + station.StationId, PlcLogLevel.Error, station.StationId, $"Station processing error: {ex.Message}",
                            PlcDiag.From(PlcDiagClassifier.Classify(ex, PlcDiagStages.Read, PlcDiagCodes.ReadFailed), ("error", ex.Message), ("exception", ex.GetType().Name)));
                    }
                    if (_endpoint.State != EndpointState.Connected) { linkLost = true; break; }
                    await DrainWritesAsync(ct);
                }
                lock (_gate) { _cycles++; _lastPoll = _clock.UtcNow; }

                if (linkLost)
                {
                    lock (_gate) _failures++;
                    var err = _endpoint.Diagnostics.LastError ?? "link lost";
                    var lostDiag = _endpoint.Diagnostics;
                    _lostAt ??= _clock.UtcNow;
                    SetState(EndpointState.Faulted, "[PLC_LINK_LOST] Mất kết nối PLC.", err);
                    MarkAllDisconnected(err);
                    await _endpoint.DisconnectAsync();
                    LogLimited("LINK_LOST", PlcLogLevel.Warning, null, $"Connection lost: {err}. Reconnecting in {backoff} ms.", LinkLostArgs(lostDiag, err, backoff));
                    await _wake.WaitAsync(TimeSpan.FromMilliseconds(backoff), ct);
                    backoff = Math.Min(backoff * 2, _options.ReconnectMaxDelayMs);
                    continue;
                }
                var interval = _config.PollingIntervalMs ?? _options.SimulatorPollingIntervalMs;
                await _wake.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(10, interval)), ct);   // woken early by a queued write
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
        catch (Exception ex)
        {
            SetState(EndpointState.Faulted, "[PLC_WORKER_CRASHED] Worker dừng do lỗi không mong đợi.", ex.Message);
            _events.Log(PlcLogLevel.Error, _plcId, null, "WORKER_CRASH", ex.ToString(), PlcDiag.Args(PlcDiagCodes.WorkerCrashed, PlcDiagStages.State, ("exception", ex.GetType().Name)));
        }
        finally { FailPendingWrites("STOPPED", "Kết nối PLC đã dừng."); }
    }

    private void FailPendingWrites(string code, string message)
    {
        while (_writes.Reader.TryRead(out var w)) w.Done.TrySetResult(new WriteResult(false, code, message));
    }

    private void MarkAllDisconnected(string? reason)
    {
        foreach (var s in _stations)
        {
            var changed = s.MarkDisconnected(reason);
            if (changed.Count > 0) _events.Publish("StationIoChanged", s.Snapshot(false, changed));
        }
    }

    // -------------------------------------------------------------------------------------------------------------------- diagnostics (logging only; none of this changes behaviour)
    private IReadOnlyDictionary<string, object?> DisabledArgs()
    {
        var (code, stage) = PlcDiag.Infer("DISABLED", _disabledReason ?? "");
        return PlcDiag.Args(code, stage, ("reason", _disabledReason), ("ip", _config.IpAddress), ("port", _config.Port), ("mode", _mode.ToString()),
            ("allowLiveConnection", _options.AllowLiveConnection), ("tcpAttempted", false));
    }

    private IReadOnlyDictionary<string, object?> ConnectFailedArgs(Exception ex, long durationMs, int backoffMs)
    {
        var reconnect = _everConnected;
        var r = PlcDiagClassifier.Classify(ex, reconnect ? PlcDiagStages.Retry : PlcDiagStages.Tcp, reconnect ? PlcDiagCodes.ReconnectFailed : PlcDiagCodes.ConnectFailed);
        return PlcDiag.From(r, ("error", ex.Message), ("exception", ex.GetType().Name), ("ip", _config.IpAddress), ("port", _config.Port), ("durationMs", durationMs),
            ("retry", _failures), ("backoffMs", backoffMs), ("reconnect", reconnect), ("tcpAttempted", true),
            ("connectTimeoutMs", _config.ConnectTimeoutMs ?? _config.TimeoutMs),
            ("downtimeSeconds", _lostAt is { } l ? (int)(_clock.UtcNow - l).TotalSeconds : null));
    }

    private IReadOnlyDictionary<string, object?> LinkLostArgs(PlcDiagnostics d, string err, int backoffMs)
    {
        var code = d.LastErrorCode ?? PlcDiagClassifier.ClassifyText(err, PlcDiagCodes.ConnectionLost);
        return PlcDiag.Args(code, PlcDiagStages.Tcp, ("error", err), ("socketError", d.LastSocketError), ("backoffMs", backoffMs), ("retry", _failures),
            ("lastEndCode", d.LastEndCode), ("lastLatencyMs", d.LastLatencyMs), ("ip", _config.IpAddress), ("port", _config.Port));
    }

    private void LogTcpConnected(long durationMs, int priorFailures, DateTimeOffset? lostAt, bool recovery)
    {
        try
        {
            var downtime = lostAt is { } l ? (int?)(int)(_clock.UtcNow - l).TotalSeconds : null;
            _events.Log(PlcLogLevel.Info, _plcId, null, "TCP_CONNECTED", $"TCP connected to {_config.IpAddress}:{_config.Port} in {durationMs} ms" + (recovery ? $" after {priorFailures} failed attempt(s)" : ""),
                PlcDiag.Args(null, recovery ? PlcDiagStages.Recovery : PlcDiagStages.Tcp, ("ip", _config.IpAddress), ("port", _config.Port), ("durationMs", durationMs),
                    ("retries", priorFailures), ("downtimeSeconds", downtime), ("recovered", recovery), ("reconnects", _reconnects), ("mode", _mode.ToString())));
        }
        catch (Exception) { /* diagnostics must never disturb the worker */ }
    }

    private IReadOnlyDictionary<string, object?> ReadBadArgs(IReadOnlyList<PointReading> readings, int bad)
    {
        var first = readings.First(r => r.Quality == PointQuality.Bad).Error;
        var code = PlcDiagClassifier.ClassifyText(first, PlcDiagCodes.ReadFailed)!;
        var m = System.Text.RegularExpressions.Regex.Match(first ?? "", "end code (0x[0-9A-Fa-f]+)");
        return PlcDiag.Args(code, code is PlcDiagCodes.EndCodeError or PlcDiagCodes.ProtocolError ? PlcDiagStages.Mc : PlcDiagStages.Read, ("count", bad), ("error", first), ("endCode", m.Success ? m.Groups[1].Value : null));
    }

    /// <summary>Says WHY an IO point generates no read traffic. Logged when the count changes (and once after every (re)connect), never once per cycle.</summary>
    private void LogNotApprovedOnChange(StationIoHandler station, IReadOnlyList<PointReading> readings)
    {
        try
        {
            var notConfigured = 0;
            foreach (var r in readings) if (r.Quality == PointQuality.NotConfigured) notConfigured++;
            if (_notApprovedLogged.TryGetValue(station.StationId, out var last) ? last == notConfigured : notConfigured == 0) { _notApprovedLogged[station.StationId] = notConfigured; return; }
            _notApprovedLogged[station.StationId] = notConfigured;
            if (notConfigured == 0) return;
            var ids = readings.Where(r => r.Quality == PointQuality.NotConfigured).Select(r => r.PointId).ToHashSet();
            var points = station.Points.Where(p => ids.Contains(p.Id)).ToList();
            var noAddress = points.Count(p => string.IsNullOrWhiteSpace(p.Address));
            var notApproved = points.Count - noAddress;
            _events.Log(PlcLogLevel.Info, _plcId, station.StationId, "IO_NOT_APPROVED",
                $"{notConfigured}/{station.Points.Count} IO point(s) are not polled ({noAddress} without an address, {notApproved} with an address that is invalid or overlaps another point): no read request is sent for them.",
                PlcDiag.Args(PlcDiagCodes.IoPointNotApproved, PlcDiagStages.Read, ("count", notConfigured), ("total", station.Points.Count), ("noAddress", noAddress),
                    ("notApproved", notApproved), ("sample", string.Join(",", points.Take(5).Select(p => p.SignalName)))));
        }
        catch (Exception) { /* diagnostics must never disturb the worker */ }
    }

    /// <summary>Same message at most once per minute per key, so a dead PLC does not flood the log.</summary>
    private void LogLimited(string key, PlcLogLevel level, string? stationId, string message, IReadOnlyDictionary<string, object?>? args = null)
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            if (_lastLogged.TryGetValue(key, out var t) && now - t < TimeSpan.FromSeconds(60)) return;
            _lastLogged[key] = now;
        }
        _events.Log(level, _plcId, stationId, key, message, args);
    }
}
