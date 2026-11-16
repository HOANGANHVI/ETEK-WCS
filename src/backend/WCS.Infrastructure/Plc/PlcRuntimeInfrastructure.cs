using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc.Mc;

namespace WCS.Infrastructure.Plc;

/// <summary>
/// The "PLC side" of the simulator: plain in-memory values keyed by PLC + Station + Signal (no device addresses exist in simulator mode,
/// so none is invented). Tests and Admins act on it through <see cref="ISimulatorControl"/>.
/// </summary>
public sealed class SimulatedPlcNetwork : ISimulatorControl
{
    private readonly ConcurrentDictionary<(string Plc, string Station, string Signal), PointValue> _values = new();
    private readonly ConcurrentDictionary<string, bool> _offline = new();
    private readonly ConcurrentDictionary<(string Plc, string Station), bool> _failing = new();

    public void SetValue(string plcId, string stationId, string signal, PointValue value) => _values[(plcId, stationId, signal)] = value;
    public void SetPlcOnline(string plcId, bool online) => _offline[plcId] = !online;
    public bool IsPlcOnline(string plcId) => !_offline.GetValueOrDefault(plcId);
    public void SetStationFault(string plcId, string stationId, bool failing) => _failing[(plcId, stationId)] = failing;
    public bool IsStationFailing(string plcId, string stationId) => _failing.GetValueOrDefault((plcId, stationId));

    /// <summary>Simulated stations are healthy by default (Station_Online = 1); every other signal starts at 0 / empty until set.</summary>
    public PointValue Get(string plcId, string stationId, string signal, PlcDataType type) =>
        _values.TryGetValue((plcId, stationId, signal), out var v) ? v
            : signal == "Station_Online" ? PointValue.OfBool(true)
            : type switch { PlcDataType.Bool => PointValue.OfBool(false), PlcDataType.Int => PointValue.OfInt(0), _ => PointValue.OfString("") };
}

/// <summary>Simulator implementation of <see cref="IPlcEndpoint"/>: same contract as the MC driver, no socket, no address.</summary>
public sealed class SimulatedPlcEndpoint(string plcId, SimulatedPlcNetwork network) : IPlcEndpoint
{
    private long _requests, _failures, _reconnects;
    private string? _lastError; private DateTimeOffset? _lastOk, _lastErr;
    public EndpointState State { get; private set; } = EndpointState.Disconnected;

    public PlcDiagnostics Diagnostics => new(_requests, _failures, 0, _reconnects, null, _lastError, 0, _lastOk, _lastErr, 0, 0);

    public Task ConnectAsync(CancellationToken ct)
    {
        if (!network.IsPlcOnline(plcId)) { Fail("simulated PLC is offline"); State = EndpointState.Faulted; throw new IOException("simulated PLC is offline"); }
        if (State == EndpointState.Faulted) _reconnects++;
        State = EndpointState.Connected;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync() { State = EndpointState.Disconnected; return Task.CompletedTask; }

    private void Fail(string error) { _failures++; _lastError = error; _lastErr = DateTimeOffset.UtcNow; }

    public Task<IReadOnlyList<PointReading>> ReadStationAsync(IReadOnlyList<IoPointRef> points, CancellationToken ct)
    {
        _requests++;
        if (!network.IsPlcOnline(plcId)) { Fail("simulated PLC is offline"); State = EndpointState.Faulted; throw new IOException("simulated PLC is offline"); }
        _lastOk = DateTimeOffset.UtcNow;
        var station = points.Count > 0 ? points[0].StationId : "";
        if (network.IsStationFailing(plcId, station))
            return Task.FromResult<IReadOnlyList<PointReading>>(points.Select(p => new PointReading(p.Id, null, PointQuality.Bad, "simulated station read failure")).ToList());
        return Task.FromResult<IReadOnlyList<PointReading>>(points.Select(p => new PointReading(p.Id, network.Get(plcId, p.StationId, p.SignalName, p.DataType), PointQuality.Good, null)).ToList());
    }

    public Task WritePointAsync(IoPointRef point, PointValue value, CancellationToken ct)
    {
        _requests++;
        if (!network.IsPlcOnline(plcId)) { Fail("simulated PLC is offline"); State = EndpointState.Faulted; throw new IOException("simulated PLC is offline"); }
        if (point.Direction != PlcSignalDirection.SoftwareToPlc) throw new InvalidOperationException("only Software -> PLC points can be written");
        network.SetValue(plcId, point.StationId, point.SignalName, value);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class PlcEndpointFactory(SimulatedPlcNetwork network) : IPlcEndpointFactory
{
    public IPlcEndpoint Create(PlcRuntimeMode mode, PlcEndpointConfig config) =>
        mode == PlcRuntimeMode.Simulator ? new SimulatedPlcEndpoint(config.PlcId, network) : new McPlcEndpoint(config);
}

/// <summary>
/// Collects log lines and realtime events from the workers on a channel and delivers them off the worker threads: logs are persisted
/// (batched) and pushed to Admins, events go through the application notifier (SignalR in the API).
/// </summary>
public sealed class PlcEventDispatcher : IPlcEvents, IDisposable
{
    private abstract record Item;
    private sealed record LogItem(PlcCommLogEntry Entry) : Item;
    private sealed record EventItem(string Name, object Payload, bool AdminOnly) : Item;

    private readonly Channel<Item> _channel;
    // CR-007: connection facts for log context (refreshed off the worker threads), last known state per PLC, and drop accounting
    private readonly ConcurrentDictionary<string, (string? Ip, int? Port)> _connections = new();
    private readonly ConcurrentDictionary<string, string> _lastState = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _faultedSince = new();
    private long _dropped, _reportedDropped;
    private DateTimeOffset _connectionsLoadedAt = DateTimeOffset.MinValue, _lastDropReport = DateTimeOffset.MinValue;
    private int _loadingConnections;
    private readonly IServiceProvider _sp;
    private readonly IClock _clock;
    private readonly ILogger<PlcEventDispatcher> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public PlcEventDispatcher(IServiceProvider sp, IClock clock, ILogger<PlcEventDispatcher> logger)
    {
        _sp = sp; _clock = clock; _logger = logger;
        _channel = Channel.CreateBounded<Item>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref _dropped));
        _loop = Task.Run(RunAsync);
    }

    public void Log(PlcLogLevel level, string? plcId, string? stationId, string eventName, string message)
    {
        // CR-007: every PLC event is ALSO written to the application log (stdout), so it is visible in "docker compose logs" and survives a database outage.
        // The sources already rate-limit these events (state changes, LogLimited), so this adds only a few lines per minute and never one per polling cycle.
        PlcEventDetails? details = null;
        try { details = BuildDetails(plcId, eventName, message); WriteToApplicationLog(level, plcId, stationId, eventName, message, details); }
        catch (Exception) { /* logging must never disturb the PLC worker */ }

        var entry = new PlcCommLogEntry(_clock.UtcNow, plcId, stationId, level, eventName, message);
        if (details is { IsEmpty: false }) entry.WithDetails(details.ToJson());
        _channel.Writer.TryWrite(new LogItem(entry));
    }

    private PlcEventDetails BuildDetails(string? plcId, string eventName, string message)
    {
        string? previous = null; double? faultedFor = null;
        if (eventName == "STATE" && plcId is not null)
        {
            _lastState.TryGetValue(plcId, out previous);
            var probe = PlcEventEnricher.Build(plcId, eventName, message, null, null, previous, null);
            if (probe.NewState is { } now)
            {
                if (now == "Faulted") _faultedSince.TryAdd(plcId, _clock.UtcNow);
                else if (now == "Connected" && _faultedSince.TryRemove(plcId, out var since)) faultedFor = (_clock.UtcNow - since).TotalSeconds;
                else if (now != "Connecting") _faultedSince.TryRemove(plcId, out _);
                _lastState[plcId] = now;
            }
        }
        (string? Ip, int? Port) conn = default;
        if (plcId is not null) { EnsureConnectionsLoaded(); _connections.TryGetValue(plcId, out conn); }
        return PlcEventEnricher.Build(plcId, eventName, message, conn.Ip, conn.Port, previous, faultedFor);
    }

    private void WriteToApplicationLog(PlcLogLevel level, string? plcId, string? stationId, string eventName, string message, PlcEventDetails d)
    {
        var lvl = level switch { PlcLogLevel.Error => LogLevel.Error, PlcLogLevel.Warning => LogLevel.Warning, _ => LogLevel.Information };
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["Category"] = "PLC", ["Device"] = plcId, ["PlcId"] = plcId, ["Station"] = stationId, ["EventCode"] = eventName,
            ["PlcIp"] = d.Ip, ["PlcPort"] = d.Port, ["PreviousState"] = d.PreviousState, ["NewState"] = d.NewState, ["Kind"] = d.Kind, ["EndCode"] = d.EndCode,
        }))
            _logger.Log(lvl, "PLC {PlcId}{StationSuffix} {EventCode}: {Message}{Context}", plcId ?? "-", stationId is null ? "" : "/" + stationId, eventName, message, d.Context());
    }

    /// <summary>Loads IP/port of the 4 connections at most every 5 minutes, in the background (never on a worker thread, never fatal).</summary>
    private void EnsureConnectionsLoaded()
    {
        if (_clock.UtcNow - _connectionsLoadedAt < TimeSpan.FromMinutes(5) || Interlocked.CompareExchange(ref _loadingConnections, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _sp.CreateAsyncScope();
                foreach (var c in await scope.ServiceProvider.GetRequiredService<IPlcConfigRepository>().ListConnectionsAsync())
                    _connections[c.Id] = (c.IpAddress, c.Port);
                _connectionsLoadedAt = _clock.UtcNow;
            }
            catch (Exception) { _connectionsLoadedAt = _clock.UtcNow.AddMinutes(-4); /* try again in a minute */ }
            finally { Interlocked.Exchange(ref _loadingConnections, 0); }
        });
    }

    public void Publish(string name, object payload, bool adminOnly = false) => _channel.Writer.TryWrite(new EventItem(name, payload, adminOnly));

    private async Task RunAsync()
    {
        var lastPurge = DateTimeOffset.MinValue;
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                var batch = new List<Item>();
                while (batch.Count < 100 && reader.TryRead(out var i)) batch.Add(i);
                try
                {
                    var logs = batch.OfType<LogItem>().Select(l => l.Entry).ToList();
                    var events = batch.OfType<EventItem>().Select(e => new WcsEvent(e.Name, e.Payload, e.AdminOnly)).ToList();
                    events.AddRange(logs.Select(l => new WcsEvent("PlcLog", new { l.OccurredAt, l.PlcId, l.StationId, level = l.Level.ToString(), l.Event, l.Message }, AdminOnly: true)));
                    if (logs.Count > 0) await PersistAsync(logs);
                    if (events.Count > 0) await _sp.GetRequiredService<IWcsNotifier>().PublishAsync(events, _cts.Token);
                    if (_clock.UtcNow - lastPurge > TimeSpan.FromHours(6)) { lastPurge = _clock.UtcNow; await PurgeAsync(); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // the entries were already written to the application log by Log(); only the database copy / realtime push of this batch is lost
                    _logger.LogWarning(ex, "PLC event delivery failed: {Lost} PLC log entries of this batch were not stored in the database (they remain in the application log). {EventCode}", batch.OfType<LogItem>().Count(), "PLC_LOG_PERSIST_FAILED");
                }
                ReportDrops();
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
    }

    /// <summary>At most one warning per minute when the bounded queue had to drop events (a slow database, an event storm).</summary>
    private void ReportDrops()
    {
        var dropped = Interlocked.Read(ref _dropped);
        if (dropped == _reportedDropped || _clock.UtcNow - _lastDropReport < TimeSpan.FromMinutes(1)) return;
        var newly = dropped - _reportedDropped; _reportedDropped = dropped; _lastDropReport = _clock.UtcNow;
        _logger.LogWarning("PLC event queue overflow: {Dropped} events were dropped (total {Total}). The database copy is incomplete. {EventCode}", newly, dropped, "PLC_LOG_QUEUE_OVERFLOW");
    }

    private async Task PersistAsync(List<PlcCommLogEntry> logs)
    {
        await using var scope = _sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlcConfigRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.ExecuteAsync(_ => { foreach (var l in logs) repo.AddLog(l); return Task.FromResult(0); });
    }

    private async Task PurgeAsync()
    {
        var days = _sp.GetRequiredService<IOptions<PlcRuntimeOptions>>().Value.LogRetentionDays;
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPlcConfigRepository>().PurgeLogsAsync(_clock.UtcNow.AddDays(-Math.Max(1, days)));
    }

    public void Dispose() { _channel.Writer.TryComplete(); _cts.Cancel(); try { _loop.Wait(1000); } catch { /* stopping */ } _cts.Dispose(); }
}
