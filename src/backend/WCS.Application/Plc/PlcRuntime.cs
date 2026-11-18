using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>
/// CR-016: besides the effective mode, WHICH write conditions are met - so the UI can say what is missing instead of hiding the write function. Only booleans and the NAMES of the missing
/// deployment settings are returned; the engineer approval reference itself is never exposed.
/// </summary>
public sealed record PlcRuntimeInfo(string Mode, string EffectiveMode, bool AllowLiveConnection, bool WriteEnabled, bool WriteKeysComplete, bool Running, IReadOnlyList<string> Notes,
    IReadOnlyList<string>? MissingWriteKeys = null);

public interface IPlcRuntime
{
    PlcRuntimeInfo Info();
    bool Running { get; }
    IReadOnlyList<PlcWorkerStatus> Workers(bool admin);
    IReadOnlyList<StationSnapshot> Stations(bool admin);
    StationSnapshot? Station(string stationId, bool admin);
    StationIoHandler? Handler(string stationId);
    PlcWorker? Worker(string plcId);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    /// <summary>Rebuilds one PLC's worker from the database (after a configuration change). Other PLCs keep running untouched.</summary>
    Task ReloadPlcAsync(string plcId, CancellationToken ct = default);
    Task<WriteResult> WriteAsync(string plcId, string stationId, string signal, PointValue value, string actor, CancellationToken ct = default, bool handshake = false);
}

/// <summary>Controls of the in-memory simulator (act as "the PLC side"). Only meaningful in Simulator mode.</summary>
public interface ISimulatorControl
{
    void SetValue(string plcId, string stationId, string signal, PointValue value);
    void SetPlcOnline(string plcId, bool online);
    bool IsPlcOnline(string plcId);
    void SetStationFault(string plcId, string stationId, bool failing);
}

/// <summary>
/// Supervisor of the 4 PLC workers. Builds them from the database, applies the three-mode safety model (SIMULATOR / REAL_READ_ONLY /
/// REAL_READ_WRITE) and exposes snapshots. A real connection is opened only when BOTH the deployment guard and the Admin switch allow it.
/// </summary>
public sealed class PlcRuntime(IServiceScopeFactory scopes, IOptions<PlcRuntimeOptions> options, IPlcEndpointFactory factory, IPlcEvents events, IClock clock,
    ILogger<PlcRuntime> logger) : IPlcRuntime
{
    private readonly ConcurrentDictionary<string, PlcWorker> _workers = new();
    private readonly SemaphoreSlim _sync = new(1, 1);
    private volatile bool _running;
    private PlcRuntimeOptions Options => options.Value;

    public bool Running => _running;

    public PlcRuntimeInfo Info()
    {
        var o = Options; var notes = new List<string>();
        var effective = WritePolicy.EffectiveMode(o);
        if (o.Mode != PlcRuntimeMode.Simulator && !o.AllowLiveConnection) notes.Add("[LIVE_CONNECTION_DISABLED] Plc:AllowLiveConnection = false: không mở socket tới PLC thật.");
        if (o.Mode == PlcRuntimeMode.RealReadWrite && effective != PlcRuntimeMode.RealReadWrite)
            notes.Add("[WRITE_KEYS_MISSING] REAL_READ_WRITE đã được yêu cầu nhưng thiếu khóa ghi (AllowLiveConnection, Write.Enabled): hệ thống chạy REAL_READ_ONLY.");
        if (o.Mode == PlcRuntimeMode.Simulator) notes.Add("[SIMULATOR_MODE] Chế độ mô phỏng: không có kết nối nào tới PLC vật lý.");
        return new PlcRuntimeInfo(o.Mode.ToString(), effective.ToString(), o.AllowLiveConnection, o.Write.Enabled, WritePolicy.WriteKeysComplete(o), _running, notes, WritePolicy.MissingWriteKeys(o));
    }

    public IReadOnlyList<PlcWorkerStatus> Workers(bool admin) => _workers.Values.OrderBy(w => w.PlcId).Select(w => w.Status(admin)).ToList();
    public IReadOnlyList<StationSnapshot> Stations(bool admin) => _workers.Values.OrderBy(w => w.PlcId).SelectMany(w => w.Stations).Select(s => s.Snapshot(admin)).ToList();
    public StationSnapshot? Station(string stationId, bool admin) => Handler(stationId)?.Snapshot(admin);
    public StationIoHandler? Handler(string stationId) => _workers.Values.SelectMany(w => w.Stations).FirstOrDefault(s => s.StationId == stationId);
    public PlcWorker? Worker(string plcId) => _workers.GetValueOrDefault(plcId);

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct);
        try
        {
            if (_running) return;
            foreach (var w in await BuildAsync(null, ct)) { _workers[w.PlcId] = w; w.Start(); }
            _running = true;
            logger.LogInformation("PLC runtime started: {Count} workers, mode {Mode}", _workers.Count, Options.Mode);
            events.Log(PlcLogLevel.Info, null, null, "RUNTIME_START", $"PLC runtime started in mode {Info().EffectiveMode} with {_workers.Count} PLC workers. Requested mode {Options.Mode}; write settings missing: {(WritePolicy.MissingWriteKeys(Options) is { Count: > 0 } m ? string.Join(", ", m) : "none")}.",
                WCS.Application.Logging.LogArgs.Of(("mode", Info().EffectiveMode), ("workers", _workers.Count), ("requestedMode", Options.Mode.ToString()), ("missingWriteKeys", string.Join(",", WritePolicy.MissingWriteKeys(Options)))));
        }
        finally { _sync.Release(); }
    }

    public async Task StopAsync()
    {
        await _sync.WaitAsync();
        try
        {
            _running = false;
            foreach (var w in _workers.Values) await w.DisposeAsync();
            _workers.Clear();
        }
        finally { _sync.Release(); }
    }

    public async Task ReloadPlcAsync(string plcId, CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct);
        try
        {
            if (!_running) return;
            if (_workers.TryRemove(plcId, out var old)) await old.DisposeAsync();
            foreach (var w in await BuildAsync(plcId, ct)) { _workers[w.PlcId] = w; w.Start(); }
            events.Log(PlcLogLevel.Info, plcId, null, "RELOAD", $"{plcId}: configuration reloaded.", WCS.Application.Logging.LogArgs.Of(("plc", plcId)));
        }
        finally { _sync.Release(); }
    }

    private async Task<List<PlcWorker>> BuildAsync(string? onlyPlc, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlcConfigRepository>();
        var o = Options; var mode = WritePolicy.EffectiveMode(o);
        var stations = await repo.ListStationsAsync(ct);
        var points = await repo.ListPointsAsync(onlyPlc, null, ct);
        var result = new List<PlcWorker>();
        foreach (var c in (await repo.ListConnectionsAsync(ct)).Where(c => onlyPlc is null || c.Id == onlyPlc))
        {
            bool enabled; string? why = null;
            if (mode == PlcRuntimeMode.Simulator) enabled = true;
            else if (!o.AllowLiveConnection) { enabled = false; why = "[LIVE_CONNECTION_DISABLED] Chưa cho phép kết nối thật (Plc:AllowLiveConnection)."; }
            else if (!c.Enabled) { enabled = false; why = "[CONNECTION_NOT_ENABLED] Admin chưa bật kết nối PLC này."; }
            else enabled = true;

            var pollable = PlcPollablePolicy.Pollable(points.Where(p => p.PlcId == c.Id).ToList(), new AddressBases(c.XyAddressBase, c.WAddressBase));   // CR-012: valid address = readable
            var handlers = stations.Where(s => s.PlcId == c.Id && s.Enabled).OrderBy(s => s.Order).Select(s => new StationIoHandler(c.Id, s.Id,
                points.Where(p => p.StationId == s.Id).Select(p => new PointInfo(
                    new IoPointRef(p.Id, p.PlcId, p.StationId, p.SignalName, p.Direction, p.DataType, p.HasConfirmedAddress ? p.ActualAddress : null, p.WordLength, p.Encoding, p.WriteAllowed, p.Group, pollable.Contains(p.Id)),
                    p.Seq, p.Status)))).ToList();
            var cfg = new PlcEndpointConfig(c.Id, c.IpAddress, c.Port, c.Protocol, c.NetworkNumber, c.PcNumber, c.ModuleIoNumber, c.PlcStationNumber, c.MonitoringTimer, c.TimeoutMs, c.PollingIntervalMs,
                c.ConnectTimeoutMs, c.ReadTimeoutMs, c.WriteTimeoutMs, c.XyAddressBase, c.WAddressBase);
            result.Add(new PlcWorker(c.Id, c.Name, mode, o, cfg, enabled, why, handlers, factory, events, clock));
            LogConfigSnapshot(c, cfg, mode, enabled, why, handlers);
        }
        return result;
    }

    /// <summary>One structured line per PLC at start and at every reload: what is configured and why a connection will (not) be attempted. Contains no secret.</summary>
    private void LogConfigSnapshot(WCS.Domain.Plc.PlcConnection c, PlcEndpointConfig cfg, PlcRuntimeMode mode, bool enabled, string? why, IReadOnlyList<StationIoHandler> handlers)
    {
        try
        {
            var o = Options;
            var missing = c.MissingParameters();
            var (whyCode, _) = PlcDiag.Infer("DISABLED", why ?? "");
            var frame = (c.Protocol ?? "").Contains("3E", StringComparison.OrdinalIgnoreCase) ? "MC 3E" : (c.Protocol ?? "").Contains("4E", StringComparison.OrdinalIgnoreCase) ? "MC 4E" : null;
            var encoding = (c.Protocol ?? "").Contains("Binary", StringComparison.OrdinalIgnoreCase) ? "Binary" : (c.Protocol ?? "").Contains("Ascii", StringComparison.OrdinalIgnoreCase) ? "ASCII" : null;
            var points = handlers.SelectMany(h => h.Points).ToList();
            var willConnect = enabled && mode != PlcRuntimeMode.Simulator && missing.Count == 0;
            var reason = mode == PlcRuntimeMode.Simulator ? "simulator mode: no physical connection"
                : !o.AllowLiveConnection ? PlcDiagCodes.LiveConnectionDisabled : !c.Enabled ? PlcDiagCodes.ConnectionNotEnabled
                : missing.Count > 0 ? PlcDiagCodes.ConfigurationMissing : "connection will be attempted";
            events.Log(PlcLogLevel.Info, c.Id, null, "CONFIG_SNAPSHOT",
                $"{c.Id} ({c.Name}) {cfg.IpAddress}:{cfg.Port?.ToString() ?? "?"} protocol {c.Protocol ?? "?"}; adminEnabled={c.Enabled}; mode={mode}; allowLive={o.AllowLiveConnection}; {reason}.",
                PlcDiag.Args(missing.Count > 0 && whyCode is null && mode != PlcRuntimeMode.Simulator ? PlcDiagCodes.ConfigurationMissing : whyCode, PlcDiagStages.Config,
                    ("plcName", c.Name), ("ip", cfg.IpAddress), ("port", cfg.Port), ("protocol", c.Protocol), ("frame", frame), ("encoding", encoding),
                    ("networkNo", cfg.NetworkNumber), ("pcNo", cfg.PcNumber), ("moduleIoNo", cfg.ModuleIoNumber), ("stationNo", cfg.PlcStationNumber),
                    ("monitoringTimer", cfg.MonitoringTimer), ("timeoutMs", cfg.TimeoutMs), ("connectTimeoutMs", cfg.ConnectTimeoutMs), ("readTimeoutMs", cfg.ReadTimeoutMs),
                    ("writeTimeoutMs", cfg.WriteTimeoutMs), ("pollingIntervalMs", cfg.PollingIntervalMs), ("adminEnabled", c.Enabled), ("workerEnabled", enabled),
                    ("mode", Options.Mode.ToString()), ("effectiveMode", mode.ToString()), ("allowLiveConnection", o.AllowLiveConnection),
                    ("writeEnabled", o.Write.Enabled), ("writeKeysComplete", WritePolicy.WriteKeysComplete(o)), ("willAttemptConnection", willConnect), ("reason", reason),
                    ("missing", missing.Count == 0 ? null : string.Join(",", missing)), ("stations", handlers.Count), ("points", points.Count),
                    ("pollable", points.Count(p => p.Pollable)), ("hasAddress", points.Count(p => !string.IsNullOrWhiteSpace(p.Address)))));
        }
        catch (Exception) { /* diagnostics must never disturb start-up */ }
    }

    public async Task<WriteResult> WriteAsync(string plcId, string stationId, string signal, PointValue value, string actor, CancellationToken ct = default, bool handshake = false)
    {
        var worker = Worker(plcId);
        var handler = worker?.Stations.FirstOrDefault(s => s.StationId == stationId);
        var point = handler?.Find(signal);
        if (worker is null || handler is null || point is null) return new WriteResult(false, "NOT_FOUND", "Không tìm thấy IO point.");
        var decision = WritePolicy.Evaluate(Options, point, handshake);
        if (!decision.Allowed)
        {
            events.Log(PlcLogLevel.Warning, plcId, stationId, "WRITE_DENIED", $"{actor}: {stationId}/{signal} blocked ({decision.Code}).", PlcDiag.Args(PlcDiagCodes.WriteBlocked, PlcDiagStages.Write, ("actor", actor), ("station", stationId), ("signal", signal), ("code", decision.Code), ("blockedBy", decision.Code),
                ("mode", WritePolicy.EffectiveMode(Options).ToString()), ("address", point.Address)));
            return new WriteResult(false, decision.Code, decision.Reason);
        }
        // CR-014: the value is checked against the signal (type, 16-bit INT range, ASCII STRING length) before anything is queued
        var shape = WriteValueValidator.Check(point.DataType, point.WordLength, point.Encoding, value);
        if (!shape.Ok) return new WriteResult(false, shape.Code, shape.Message);
        return await worker.EnqueueWriteAsync(point, value).WaitAsync(ct);
    }
}
