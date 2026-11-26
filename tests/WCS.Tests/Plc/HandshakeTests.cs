using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Application.Services;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Simulation;
using WCS.Tests.Config;
using WCS.Tests.Support;

namespace WCS.Tests.Plc;

/// <summary>A runtime stand-in that holds real StationIoHandlers (all 23 template points) and applies writes through the REAL write policy.</summary>
public sealed class FakeRuntime : IPlcRuntime
{
    private readonly Dictionary<string, StationIoHandler> _handlers = [];
    public PlcRuntimeOptions Options { get; }
    public List<(string Station, string Signal, bool Value, bool Handshake)> Writes { get; } = [];

    public FakeRuntime(PlcRuntimeOptions options, params (string Plc, string Station)[] stations)
    {
        Options = options;
        var template = PlcCatalog.Current.Template;
        foreach (var (plc, station) in stations)
            _handlers[station] = new StationIoHandler(plc, station, template.Select(t => new PointInfo(new IoPointRef(Guid.NewGuid(), plc, station, t.SignalName,
                Enum.Parse<PlcSignalDirection>(t.Direction), Enum.Parse<PlcDataType>(t.DataType), "M" + t.Seq, null, null, true, t.Group), t.Seq, IoConfigStatus.Approved)));   // dummy address: only the write policy looks at it
    }

    /// <summary>Test helper: makes a signal "live" with a value, as a poll cycle would.</summary>
    public void Set(string station, string signal, object value, PointQuality quality = PointQuality.Simulated)
    {
        var h = _handlers[station]; var p = h.Find(signal)!;
        h.Apply([new PointReading(p.Id, new PointValue(p.DataType, value), quality, null)], DateTimeOffset.UtcNow, false);
    }
    public void LiveBaseline(string station)
    {
        foreach (var s in new[] { "Station_Online" }) Set(station, s, true);
        foreach (var s in new[] { "Station_Fault", "Pickup_Request", "Delivery_Request", "Transfer_Complete", "Request_Ack", "Transfer_Permit", "AGV_Assigned" }) Set(station, s, false);
    }
    public bool Bool(string station, string signal) => (bool)(_handlers[station].Read(signal)!.Value.Value!.Value);

    public PlcRuntimeInfo Info() => new(Options.Mode.ToString(), WritePolicy.EffectiveMode(Options).ToString(), Options.AllowLiveConnection, Options.Write.Enabled, WritePolicy.WriteKeysComplete(Options), true, []);
    public bool Running => true;
    public IReadOnlyList<PlcWorkerStatus> Workers(bool admin) => [];
    public IReadOnlyList<StationSnapshot> Stations(bool admin) => _handlers.Values.Select(h => h.Snapshot(admin)).ToList();
    public StationSnapshot? Station(string stationId, bool admin) => Handler(stationId)?.Snapshot(admin);
    public StationIoHandler? Handler(string stationId) => _handlers.GetValueOrDefault(stationId);
    public PlcWorker? Worker(string plcId) => null;
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task ReloadPlcAsync(string plcId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ReadResult> ReadNowAsync(string plcId, string stationId, string signal, CancellationToken ct = default) => Task.FromResult(new ReadResult(false, "NOT_SUPPORTED", "fake runtime"));
    public Task<WriteResult> WriteAsync(string plcId, string stationId, string signal, PointValue value, string actor, CancellationToken ct = default, bool handshake = false)
    {
        var point = Handler(stationId)?.Find(signal);
        if (point is null) return Task.FromResult(new WriteResult(false, "NOT_FOUND"));
        var d = WritePolicy.Evaluate(Options, point, handshake);
        if (!d.Allowed) return Task.FromResult(new WriteResult(false, d.Code, d.Reason));
        Writes.Add((stationId, signal, (bool)value.Value, handshake));
        Set(stationId, signal, value.Value);
        return Task.FromResult(new WriteResult(true));
    }
}

public class HandshakeTests
{
    private static HandshakeCoordinator Coordinator(FakeRuntime rt, FakeEvents ev) => new(rt, Microsoft.Extensions.Options.Options.Create(rt.Options), ev, new SystemClock());

    private static HandshakeDefinition AckRule(string station = "", bool approved = true, string? reference = "PLC-ENG-HS-001") => new()
    {
        Station = station, Signal = "Request_Ack", SetWhenSignal = "Pickup_Request", SetWhenValue = true, ResetWhenSignal = "Pickup_Request", ResetWhenValue = false,
        Approved = approved, ApprovalReference = reference,
    };

    [Fact]
    public void The_observed_phase_is_a_technical_readout_and_needs_live_data()
    {
        var rt = new FakeRuntime(new PlcRuntimeOptions(), ("PLC-01", "A1"));
        Assert.Equal(HandshakePhase.Unknown, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);          // nothing read yet
        rt.LiveBaseline("A1");
        Assert.Equal(HandshakePhase.Idle, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Pickup_Request", true);   Assert.Equal(HandshakePhase.PickupRequested, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Delivery_Request", true); Assert.Equal(HandshakePhase.ConflictingRequests, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Pickup_Request", false);  Assert.Equal(HandshakePhase.DeliveryRequested, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Delivery_Request", false); rt.Set("A1", "Transfer_Complete", true); Assert.Equal(HandshakePhase.TransferComplete, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Station_Fault", true);    Assert.Equal(HandshakePhase.Fault, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Station_Fault", false); rt.Set("A1", "Station_Online", false); Assert.Equal(HandshakePhase.Offline, HandshakeCoordinator.Observe(rt.Handler("A1")!).Phase);
        rt.Set("A1", "Station_Online", true); rt.Set("A1", "Transfer_Complete", false);
        rt.Set("A1", "Pickup_Request", true, PointQuality.Bad);                                                 // stale/bad data is never interpreted
        var (phase, reason) = HandshakeCoordinator.Observe(rt.Handler("A1")!);
        Assert.Equal(HandshakePhase.Unknown, phase); Assert.Contains("Pickup_Request", reason);
    }

    [Fact]
    public async Task Without_approved_rules_the_framework_only_observes_and_every_station_stays_NEEDS_CONFIRMATION()
    {
        var rt = new FakeRuntime(new PlcRuntimeOptions(), ("PLC-01", "A1"), ("PLC-01", "A2"), ("PLC-02", "B1"));
        var ev = new FakeEvents(); var hs = Coordinator(rt, ev);
        foreach (var s in new[] { "A1", "A2", "B1" }) rt.LiveBaseline(s);
        rt.Set("A1", "Pickup_Request", true); rt.Set("B1", "Delivery_Request", true);

        Assert.Equal(0, await hs.StepAsync());
        Assert.Empty(rt.Writes);                                                // nothing is ever written without an approved rule
        var states = hs.States().ToDictionary(s => s.StationId);
        Assert.Equal(3, states.Count);
        Assert.All(states.Values, s => { Assert.Equal("NEEDS_CONFIRMATION", s.Approval); Assert.Equal(0, s.ApprovedRules); Assert.Equal(23, s.SignalsAwaitingConfirmation); });
        Assert.Equal(("PickupRequested", "Idle", "DeliveryRequested"), (states["A1"].Phase, states["A2"].Phase, states["B1"].Phase));   // independent per station
        Assert.Contains(ev.Events, e => e.Name == "HandshakePhaseChanged");
    }

    [Fact]
    public async Task Rules_that_are_not_approved_or_have_no_approval_reference_are_ignored()
    {
        var o = new PlcRuntimeOptions { Handshake = { Definitions = [AckRule(approved: false), AckRule(reference: null), AckRule(reference: " ")] } };
        var rt = new FakeRuntime(o, ("PLC-01", "A1")); rt.LiveBaseline("A1"); rt.Set("A1", "Pickup_Request", true);
        var hs = Coordinator(rt, new FakeEvents());
        Assert.Equal(0, await hs.StepAsync());
        Assert.Empty(rt.Writes);
        Assert.Equal("NEEDS_CONFIRMATION", hs.State("PLC-01", "A1").Approval);
    }

    [Fact]
    public async Task An_approved_rule_sets_and_clears_its_bit_per_station_and_does_not_repeat_itself()
    {
        var o = new PlcRuntimeOptions { Handshake = { Definitions = [AckRule()] } };
        var rt = new FakeRuntime(o, ("PLC-01", "A1"), ("PLC-01", "A2")); var ev = new FakeEvents();
        rt.LiveBaseline("A1"); rt.LiveBaseline("A2");
        var hs = Coordinator(rt, ev);

        Assert.Equal(0, await hs.StepAsync());                                  // no request yet -> no ack
        rt.Set("A1", "Pickup_Request", true);
        Assert.Equal(1, await hs.StepAsync());
        Assert.True(rt.Bool("A1", "Request_Ack")); Assert.False(rt.Bool("A2", "Request_Ack"));      // only the requesting station
        Assert.Equal(0, await hs.StepAsync());                                  // already set: no repeated write
        Assert.Equal("Request_Ack", Assert.Single(hs.State("PLC-01", "A1").Active).Signal);
        Assert.Equal("PARTIALLY_APPROVED", hs.State("PLC-01", "A1").Approval);

        rt.Set("A1", "Pickup_Request", false);                                  // the reset condition
        Assert.Equal(1, await hs.StepAsync());
        Assert.False(rt.Bool("A1", "Request_Ack")); Assert.Empty(hs.State("PLC-01", "A1").Active);
        Assert.Equal(new[] { ("A1", "Request_Ack", true, true), ("A1", "Request_Ack", false, true) }, rt.Writes.ToArray());
        Assert.True(ev.HasLog("HANDSHAKE"));
    }

    [Fact]
    public async Task A_rule_never_acts_on_data_that_is_not_live_and_never_drives_an_inbound_signal()
    {
        var o = new PlcRuntimeOptions { Handshake = { Definitions = [AckRule(), new HandshakeDefinition { Signal = "Pickup_Request", SetWhenSignal = "Station_Online", Approved = true, ApprovalReference = "X" }] } };
        var rt = new FakeRuntime(o, ("PLC-01", "A1")); var hs = Coordinator(rt, new FakeEvents());
        rt.LiveBaseline("A1");
        rt.Set("A1", "Pickup_Request", true, PointQuality.Disconnected);       // PLC link down: last value only
        Assert.Equal(0, await hs.StepAsync());
        Assert.Empty(rt.Writes);
        Assert.Contains("Pickup_Request", hs.State("PLC-01", "A1").LastError);   // the rule that targets a PLC -> Software bit was rejected as invalid
    }

    [Fact]
    public async Task Writes_still_obey_the_mode_the_allow_list_and_the_blocked_signals()
    {
        HandshakeDefinition Permit() => new() { Signal = "Transfer_Permit", SetWhenSignal = "Pickup_Request", Approved = true, ApprovalReference = "X" };

        // READ_ONLY: refused
        var ro = new FakeRuntime(new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadOnly, AllowLiveConnection = true, Handshake = { Definitions = [AckRule()] } }, ("PLC-01", "A1"));
        ro.LiveBaseline("A1"); ro.Set("A1", "Pickup_Request", true);
        var hsRo = Coordinator(ro, new FakeEvents());
        Assert.Equal(0, await hsRo.StepAsync());
        Assert.StartsWith("READ_ONLY", hsRo.State("PLC-01", "A1").LastError);

        // READ_WRITE with every key: a normal command bit may be written, a motion permit may NOT (not even by an approved rule)
        var rwOptions = new PlcRuntimeOptions
        {
            Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true, Write = { Enabled = true },
            Handshake = { Definitions = [AckRule(), Permit()] },
        };
        var rw = new FakeRuntime(rwOptions, ("PLC-01", "A1")); rw.LiveBaseline("A1"); rw.Set("A1", "Pickup_Request", true);
        rwOptions.Declare(rw.Handler("A1")!.Find("Request_Ack")!, rw.Handler("A1")!.Find("Transfer_Permit")!);   // CR-044: declared; the motion permit stays blocked even so
        var hsRw = Coordinator(rw, new FakeEvents());
        Assert.Equal(1, await hsRw.StepAsync());
        Assert.Equal("Request_Ack", Assert.Single(rw.Writes).Signal);
        Assert.False(rw.Bool("A1", "Transfer_Permit"));
        Assert.StartsWith("WRITE_BLOCKED_SAFETY", hsRw.State("PLC-01", "A1").LastError);

        // the simulator may exercise the whole chain, so the framework can be tested end to end
        var sim = new FakeRuntime(new PlcRuntimeOptions { Handshake = { Definitions = [Permit()] } }, ("PLC-01", "A1")); sim.LiveBaseline("A1"); sim.Set("A1", "Pickup_Request", true);
        Assert.Equal(1, await Coordinator(sim, new FakeEvents()).StepAsync());
        Assert.True(sim.Bool("A1", "Transfer_Permit"));
        // ... but never the Safety group
        Assert.False(WritePolicy.Evaluate(new PlcRuntimeOptions(), new IoPointRef(Guid.NewGuid(), "P", "S", "Anything", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, null, null, null, true, "Safety / Interlock"), forHandshake: true).Allowed);
    }

    [Fact]
    public async Task A_bit_that_stays_set_too_long_is_reported_but_not_acted_on()
    {
        var rule = AckRule(); rule.TimeoutMs = 20; rule.ResetWhenSignal = "";
        var rt = new FakeRuntime(new PlcRuntimeOptions { Handshake = { Definitions = [rule] } }, ("PLC-01", "A1")); var ev = new FakeEvents();
        rt.LiveBaseline("A1"); rt.Set("A1", "Pickup_Request", true);
        var hs = Coordinator(rt, ev);
        await hs.StepAsync();                                                  // sets Request_Ack
        await Task.Delay(60);
        await hs.StepAsync(); await hs.StepAsync();
        Assert.True(ev.HasLog("HANDSHAKE_TIMEOUT"));
        lock (ev.Logs) Assert.Single(ev.Logs, l => l.Event == "HANDSHAKE_TIMEOUT");     // logged once, no retry policy is invented
        Assert.True(rt.Bool("A1", "Request_Ack"));                                    // and the bit was not touched
        Assert.Single(rt.Writes);
    }
}

public class PlcStationSignalServiceTests
{
    private static (FakeRuntime Rt, PlcStationSignalService Svc, FakeEvents Ev) Build(bool enabled, params (string Station, string Signal, string Flow)[] map)
    {
        var o = new PlcRuntimeOptions { Integration = { Enabled = enabled, StationFlows = map.Select(m => new StationFlowMapping { Station = m.Station, Signal = m.Signal, Flow = m.Flow }).ToList() } };
        var rt = new FakeRuntime(o, ("PLC-01", "A1"), ("PLC-01", "A2"), ("PLC-01", "A3"));
        var ev = new FakeEvents();
        foreach (var s in new[] { "A1", "A2", "A3" }) rt.LiveBaseline(s);
        return (rt, new PlcStationSignalService(rt, Microsoft.Extensions.Options.Options.Create(o), ev), ev);
    }

    [Fact]
    public async Task By_default_no_station_creates_a_task_even_when_a_request_bit_is_high()
    {
        var (rt, svc, _) = Build(enabled: false);
        rt.Set("A1", "Pickup_Request", true); rt.Set("A1", "Request_ID", "REQ-1");
        Assert.Empty(await svc.PollRequestsAsync());

        var (rt2, svc2, _) = Build(enabled: true);                                   // enabled but no approved mapping: still monitor-only
        rt2.Set("A1", "Pickup_Request", true); rt2.Set("A1", "Request_ID", "REQ-1");
        Assert.Empty(await svc2.PollRequestsAsync());
    }

    [Fact]
    public async Task An_approved_mapping_turns_a_rising_edge_into_one_idempotent_request()
    {
        var (rt, svc, ev) = Build(true, ("A1", "Pickup_Request", "BP01"));
        rt.Set("A1", "Request_ID", "REQ-7"); rt.Set("A1", "Pickup_Request", true);
        var first = Assert.Single(await svc.PollRequestsAsync());
        Assert.Equal(new StationRequest("A1", "BP01", "PLC-01:A1:REQ-7"), first);
        Assert.Empty(await svc.PollRequestsAsync());                                   // still high: no second request
        rt.Set("A1", "Pickup_Request", false); Assert.Empty(await svc.PollRequestsAsync());
        rt.Set("A1", "Request_ID", "REQ-8"); rt.Set("A1", "Pickup_Request", true);
        Assert.Equal("PLC-01:A1:REQ-8", Assert.Single(await svc.PollRequestsAsync()).RequestKey);
        Assert.True(ev.HasLog("REQUEST"));
    }

    [Fact]
    public async Task Unmapped_stations_and_unreadable_request_ids_never_create_requests()
    {
        var (rt, svc, ev) = Build(true, ("A1", "Pickup_Request", "BP01"));
        rt.Set("A2", "Pickup_Request", true); rt.Set("A2", "Request_ID", "REQ-1");     // A2 has no approved mapping
        rt.Set("A3", "Delivery_Request", true);
        Assert.Empty(await svc.PollRequestsAsync());

        rt.Set("A1", "Pickup_Request", true);                                           // mapped, but Request_ID is not readable (address/encoding unconfirmed)
        Assert.Empty(await svc.PollRequestsAsync());
        Assert.True(ev.HasLog("REQUEST_ID_MISSING"));
        rt.Set("A1", "Request_ID", "REQ-9");                                            // as soon as the ID becomes readable the request is not lost
        Assert.Equal("PLC-01:A1:REQ-9", Assert.Single(await svc.PollRequestsAsync()).RequestKey);

        rt.Set("A1", "Pickup_Request", false); rt.Set("A1", "Pickup_Request", true, PointQuality.Disconnected);    // data that is not live is never an edge
        Assert.Empty(await svc.PollRequestsAsync());
    }

    [Fact]
    public async Task Acknowledge_and_transfer_permit_never_write_anything()
    {
        var (rt, svc, ev) = Build(true, ("A1", "Pickup_Request", "BP01"));
        await svc.AcknowledgeRequestAsync(new StationRequest("A1", "BP01", "k"));
        await svc.RequestTransferPermissionAsync("A1", TransferDirection.Unload, Guid.NewGuid());
        Assert.Empty(rt.Writes);
        Assert.True(ev.HasLog("TRANSFER_PERMIT_NOT_APPROVED"));
    }

    [Fact]
    public void The_PLC_backed_service_replaces_the_simulator_only_when_integration_is_explicitly_enabled()
    {
        using var off = new ConfigApiFactory();
        using var on = new ConfigApiFactory().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Plc:Integration:Enabled"] = "true" })));
        Assert.IsType<StationSignalSimulator>(off.Services.GetRequiredService<IStationSignalService>());
        Assert.IsType<PlcStationSignalService>(on.Services.GetRequiredService<IStationSignalService>());
    }
}
