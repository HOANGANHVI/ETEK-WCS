using WCS.Application.Plc;
using WCS.Application.Services;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;

namespace WCS.Tests.Plc;

public sealed class FakeEvents : IPlcEvents
{
    public List<(PlcLogLevel Level, string? Plc, string? Station, string Event, string Message)> Logs { get; } = [];
    public List<(string Name, object Payload, bool AdminOnly)> Events { get; } = [];
    public void Log(PlcLogLevel level, string? plcId, string? stationId, string eventName, string message, IReadOnlyDictionary<string, object?>? args = null) { lock (Logs) Logs.Add((level, plcId, stationId, eventName, message)); }
    public void Publish(string name, object payload, bool adminOnly = false) { lock (Events) Events.Add((name, payload, adminOnly)); }
    public bool HasLog(string evt) { lock (Logs) return Logs.Any(l => l.Event == evt); }
}

/// <summary>Builds the real MC driver for a worker. The worker itself is the production class.</summary>
public sealed class McTestFactory : IPlcEndpointFactory
{
    public IPlcEndpoint Create(PlcRuntimeMode mode, PlcEndpointConfig config) => new McPlcEndpoint(config);
}

public class StationIoHandlerTests
{
    private static PointInfo P(string plc, string station, int seq, string signal, PlcDataType type, string? address, PlcSignalDirection dir = PlcSignalDirection.PlcToSoftware) =>
        new(new IoPointRef(Guid.NewGuid(), plc, station, signal, dir, type, address, null, null, false, "Status"), seq, address is null ? IoConfigStatus.NeedsConfiguration : IoConfigStatus.Approved);

    [Fact]
    public void A_handler_holds_only_its_own_station_and_ignores_foreign_readings()
    {
        var a1 = P("PLC-01", "A1", 1, "Safety_Ready", PlcDataType.Bool, "M1");
        var a2 = P("PLC-01", "A2", 1, "Safety_Ready", PlcDataType.Bool, "M1");
        Assert.Throws<ArgumentException>(() => new StationIoHandler("PLC-01", "A1", [a1, a2]));      // a station cannot hold another station's point
        var h1 = new StationIoHandler("PLC-01", "A1", [a1]); var h2 = new StationIoHandler("PLC-01", "A2", [a2]);

        h1.Apply([new PointReading(a1.Ref.Id, PointValue.OfBool(true), PointQuality.Good, null), new PointReading(a2.Ref.Id, PointValue.OfBool(true), PointQuality.Good, null)], DateTimeOffset.UtcNow, false);
        Assert.Equal(true, h1.Snapshot(true).Points.Single().Value);
        Assert.Null(h2.Snapshot(true).Points.Single().Value);                                         // A1's reading never reached A2
    }

    [Fact]
    public void Quality_follows_the_reading_and_the_last_value_is_kept_when_the_link_drops()
    {
        var p = P("PLC-01", "A1", 1, "Rack_Full", PlcDataType.Int, "D10");
        var n = P("PLC-01", "A1", 2, "Rack_Empty", PlcDataType.Int, null);
        var h = new StationIoHandler("PLC-01", "A1", [p, n]);
        var snap0 = h.Snapshot(true);
        Assert.Equal(("Unknown", "NotConfigured"), (snap0.Points[0].Quality, snap0.Points[1].Quality));

        var t = DateTimeOffset.UtcNow;
        var changed = h.Apply([new PointReading(p.Ref.Id, PointValue.OfInt(5), PointQuality.Good, null), new PointReading(n.Ref.Id, null, PointQuality.NotConfigured, null)], t, false);
        Assert.Equal([p.Ref.Id], changed);
        Assert.Empty(h.Apply([new PointReading(p.Ref.Id, PointValue.OfInt(5), PointQuality.Good, null)], t.AddSeconds(1), false));     // unchanged value: no event
        Assert.Equal(t.AddSeconds(1), h.Snapshot(true).Points[0].LastUpdated);                                                          // but the freshness is renewed

        h.MarkDisconnected("link lost");
        var s = h.Snapshot(true);
        Assert.Equal(("Disconnected", 5, "link lost"), (s.Points[0].Quality, (int)(s.Points[0].Value ?? 0), s.Points[0].Error));       // last value stays visible
        Assert.Equal("NotConfigured", s.Points[1].Quality);

        h.Apply([new PointReading(p.Ref.Id, null, PointQuality.Bad, "end code C056")], t.AddSeconds(2), false);
        Assert.Equal(("Bad", 5), (h.Snapshot(true).Points[0].Quality, (int)(h.Snapshot(true).Points[0].Value ?? 0)));
        Assert.Equal("Simulated", Sim(h, p).Quality);
        Assert.Equal(1, h.Snapshot(false).Good);
    }

    private static IoPointState Sim(StationIoHandler h, PointInfo p)
    {
        h.Apply([new PointReading(p.Ref.Id, PointValue.OfInt(1), PointQuality.Good, null)], DateTimeOffset.UtcNow, simulated: true);
        return h.Snapshot(true).Points[0];
    }

    [Fact]
    public void Operator_snapshots_never_contain_addresses_or_error_text()
    {
        var p = P("PLC-01", "A1", 1, "Rack_Full", PlcDataType.Int, "D10");
        var h = new StationIoHandler("PLC-01", "A1", [p]);
        h.Apply([new PointReading(p.Ref.Id, null, PointQuality.Bad, "192.168.10.101 refused")], DateTimeOffset.UtcNow, false);
        var op = h.Snapshot(admin: false).Points.Single();
        Assert.Null(op.Address); Assert.Null(op.Error);
        var adm = h.Snapshot(admin: true).Points.Single();
        Assert.Equal("D10", adm.Address); Assert.Contains("refused", adm.Error);
    }
}

/// <summary>The production worker against real MC mock servers: independence of 4 PLCs / 16 stations, isolation, reconnect, write queue.</summary>
public class PlcWorkerTests
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);   // TEST values
    private static readonly PlcRuntimeOptions Options = new() { ReconnectInitialDelayMs = 40, ReconnectMaxDelayMs = 160 };
    private static readonly string[] StationIds = ["S1", "S2", "S3", "S4"];

    private static MelsecMcMockServer Server(int port = 0) { var s = new MelsecMcMockServer { ExpectedRoute = Route }; s.Start(port); return s; }

    private static PlcEndpointConfig Config(string plc, int port, int poll = 25, int timeout = 250) =>
        new(plc, "127.0.0.1", port, "McProtocol3EBinaryTcp", 0, 0xFF, 0x03FF, 0, 0x0010, timeout, poll);

    /// <summary>Four stations per PLC. Each station owns its own address block (stationIndex * 20); the SAME blocks are reused on every PLC.</summary>
    private static List<StationIoHandler> Stations(string plc)
    {
        var list = new List<StationIoHandler>();
        for (var i = 0; i < 4; i++)
        {
            var st = StationIds[i]; var m = 100 + i * 20; var d = 1500 + i * 20;
            IoPointRef R(string sig, PlcDataType t, string? a, PlcSignalDirection dir = PlcSignalDirection.PlcToSoftware) =>
                new(Guid.NewGuid(), plc, st, sig, dir, t, a, null, null, true, "Status");
            list.Add(new StationIoHandler(plc, st,
            [
                new PointInfo(R("Pickup_Request", PlcDataType.Bool, $"M{m}"), 1, IoConfigStatus.Approved),
                new PointInfo(R("Rack_Full", PlcDataType.Int, $"D{d}"), 2, IoConfigStatus.Approved),
                new PointInfo(R("Request_Ack", PlcDataType.Bool, $"M{m + 1}", PlcSignalDirection.SoftwareToPlc), 3, IoConfigStatus.Approved),
                new PointInfo(R("Error_Code", PlcDataType.Int, null, PlcSignalDirection.SoftwareToPlc), 4, IoConfigStatus.NeedsConfiguration),     // no address -> never touched
            ]));
        }
        return list;
    }

    private static PlcWorker NewWorker(string plc, int port, FakeEvents ev, List<StationIoHandler>? stations = null, bool enabled = true, PlcEndpointConfig? cfg = null) =>
        new(plc, plc, PlcRuntimeMode.RealReadOnly, Options, cfg ?? Config(plc, port), enabled, enabled ? null : "off", stations ?? Stations(plc), new McTestFactory(), ev, new SystemClock());

    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(15); }
        return cond();
    }

    private static object? Val(PlcWorker w, string station, string signal) => w.Stations.Single(s => s.StationId == station).Snapshot(true).Points.Single(p => p.SignalName == signal).Value;
    private static string Q(PlcWorker w, string station, string signal) => w.Stations.Single(s => s.StationId == station).Snapshot(true).Points.Single(p => p.SignalName == signal).Quality;

    [Fact]
    public async Task Four_PLCs_run_as_four_independent_workers_each_with_its_own_address_namespace()
    {
        var servers = Enumerable.Range(0, 4).Select(_ => Server()).ToList();
        var ev = new FakeEvents();
        var workers = servers.Select((s, i) => NewWorker($"PLC-0{i + 1}", s.Port, ev)).ToList();
        try
        {
            // The SAME addresses (M100, D1500, ...) hold DIFFERENT values on each physical PLC.
            for (var p = 0; p < 4; p++)
                for (var st = 0; st < 4; st++) { servers[p].SetBit(100 + st * 20, (p + st) % 2 == 0); servers[p].SetWord(1500 + st * 20, (ushort)(1000 * (p + 1) + st)); }
            workers.ForEach(w => w.Start());
            Assert.True(await WaitFor(() => workers.All(w => w.Status(true).State == "Connected" && w.Status(true).GoodPoints >= 12)));

            for (var p = 0; p < 4; p++)
                for (var st = 0; st < 4; st++)
                {
                    Assert.Equal((p + st) % 2 == 0, Val(workers[p], StationIds[st], "Pickup_Request"));
                    Assert.Equal(1000 * (p + 1) + st, Val(workers[p], StationIds[st], "Rack_Full"));
                }
            // 4 PLCs = 4 connections (not 16): each server saw exactly one TCP connection serving its four stations.
            Assert.All(servers, s => Assert.Equal(1, s.ConnectionCount));
            Assert.All(workers, w => { var s = w.Status(true); Assert.Equal((4, 16, 12), (s.Stations, s.TotalPoints, s.GoodPoints)); });
            Assert.Equal(16, workers.Sum(w => w.Stations.Count));          // 4 PLCs x 4 stations
        }
        finally { foreach (var w in workers) await w.DisposeAsync(); foreach (var s in servers) await s.DisposeAsync(); }
    }

    [Fact]
    public async Task Points_without_a_confirmed_address_are_never_polled()
    {
        await using var server = Server();
        await using var w = NewWorker("PLC-01", server.Port, new FakeEvents());
        w.Start();
        Assert.True(await WaitFor(() => w.Status(true).Cycles >= 3));
        Assert.Equal("NotConfigured", Q(w, "S1", "Error_Code"));
        // every frame concerns M/D addresses of the three configured points of a station; nothing was ever requested for Error_Code
        var heads = server.ReceivedFrames.Select(f => McFrame.ParseRequest(f).Head.ToString()).Distinct().ToList();
        Assert.Equal(8, heads.Count);      // per station: one bit read (M pair) + one word read; Error_Code has no address so nothing else
        Assert.DoesNotContain("D1580", heads);
    }

    [Fact]
    public async Task One_PLC_going_offline_does_not_disturb_the_other_three_and_it_reconnects_by_itself()
    {
        var servers = Enumerable.Range(0, 4).Select(_ => Server()).ToList();
        var ev = new FakeEvents();
        var workers = servers.Select((s, i) => NewWorker($"PLC-0{i + 1}", s.Port, ev)).ToList();
        try
        {
            servers.ForEach(s => s.SetWord(1500, 1));
            workers.ForEach(w => w.Start());
            Assert.True(await WaitFor(() => workers.All(w => w.Status(true).State == "Connected")));
            var port1 = servers[0].Port;

            servers[0].Stop();                                                                           // PLC-01 loses power
            Assert.True(await WaitFor(() => workers[0].Status(true).State == "Faulted"));
            Assert.True(await WaitFor(() => Q(workers[0], "S1", "Rack_Full") == "Disconnected"));
            Assert.Equal(1, Val(workers[0], "S1", "Rack_Full"));                                          // last value stays visible

            // the other three keep polling at full speed: a new value is picked up while PLC-01 is down
            for (var p = 1; p < 4; p++) servers[p].SetWord(1500, (ushort)(50 + p));
            Assert.True(await WaitFor(() => Enumerable.Range(1, 3).All(p => Equals(Val(workers[p], "S1", "Rack_Full"), 50 + p))));
            Assert.All(workers.Skip(1), w => { Assert.Equal("Connected", w.Status(true).State); Assert.Equal(0, w.Status(true).ConsecutiveFailures); });
            Assert.True(workers[0].Status(true).ConsecutiveFailures >= 1);

            await using var back = Server(port1);                                                          // power returns on the same address
            back.SetWord(1500, 99);
            Assert.True(await WaitFor(() => workers[0].Status(true).State == "Connected" && Equals(Val(workers[0], "S1", "Rack_Full"), 99), 8000));
            Assert.True(workers[0].Status(true).Reconnects >= 1);
            Assert.Equal("Good", Q(workers[0], "S1", "Rack_Full"));
            Assert.True(ev.HasLog("STATE"));
            Assert.Contains(ev.Events, e => e.Name == "PlcStatusChanged");
        }
        finally { foreach (var w in workers) await w.DisposeAsync(); foreach (var s in servers) await s.DisposeAsync(); }
    }

    [Fact]
    public async Task A_failing_station_does_not_stop_the_other_stations_of_the_same_PLC()
    {
        await using var server = Server();
        server.MaxDeviceNumber = 1519;                                          // PLC end code for D1520+ (station S2..S4 blocks), M is fine
        var stations = Stations("PLC-01");
        await using var w = NewWorker("PLC-01", server.Port, new FakeEvents(), stations);
        server.SetWord(1500, 7); server.SetBit(100, true);
        w.Start();
        Assert.True(await WaitFor(() => Q(w, "S1", "Rack_Full") == "Good"));
        Assert.True(await WaitFor(() => Q(w, "S2", "Rack_Full") == "Bad"));
        Assert.Equal("Good", Q(w, "S2", "Pickup_Request"));                     // the M block of the same station is fine
        Assert.Equal(7, Val(w, "S1", "Rack_Full")); Assert.Equal(true, Val(w, "S1", "Pickup_Request"));
        Assert.Equal("Connected", w.Status(true).State);                        // a PLC error code is not a lost link
        Assert.Contains("C056", w.Stations[1].Snapshot(true).Points.First(p => p.SignalName == "Rack_Full").Error);
    }

    [Fact]
    public async Task Queued_writes_are_executed_in_order_and_never_overlap_with_polling()
    {
        await using var server = Server();
        await using var w = NewWorker("PLC-01", server.Port, new FakeEvents());
        w.Start();
        Assert.True(await WaitFor(() => w.Status(true).State == "Connected"));
        var ack = w.Stations[0].Find("Request_Ack")!;

        var results = new List<Task<WriteResult>>();
        for (var i = 0; i < 30; i++) results.Add(w.EnqueueWriteAsync(ack, PointValue.OfBool(i % 2 == 0)));     // 30 writes from the same thread, queued in order
        var done = await Task.WhenAll(results);
        Assert.All(done, r => Assert.True(r.Success, r.Message));
        Assert.False(server.GetBit(101));                                       // i = 29 is odd -> false: the LAST queued write won
        // and a burst from many threads at once
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => w.EnqueueWriteAsync(ack, PointValue.OfBool(true)))));
        Assert.All(concurrent, r => Assert.True(r.Success));
        Assert.Equal(0, server.PipelinedRequests);                              // reads, writes and polls never shared the socket at the same time
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task A_write_is_refused_immediately_when_the_PLC_is_not_connected_instead_of_waiting_in_the_queue()
    {
        var server = Server(); var port = server.Port; await server.DisposeAsync();      // nothing is listening
        await using var w = NewWorker("PLC-01", port, new FakeEvents());
        w.Start();
        Assert.True(await WaitFor(() => w.Status(true).State == "Faulted"));
        var r = await w.EnqueueWriteAsync(w.Stations[0].Find("Request_Ack")!, PointValue.OfBool(true)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(r.Success); Assert.Equal("NOT_CONNECTED", r.Code);
    }

    [Fact]
    public async Task A_disabled_worker_opens_no_connection_and_an_incomplete_configuration_reports_NEEDS_CONFIGURATION()
    {
        await using var server = Server();
        await using var off = NewWorker("PLC-01", server.Port, new FakeEvents(), enabled: false);
        off.Start();
        await Task.Delay(300);
        Assert.Equal("Disabled", off.Status(true).State);
        Assert.Equal(0, server.ConnectionCount);                                // never connected

        await using var incomplete = NewWorker("PLC-02", server.Port, new FakeEvents(), cfg: Config("PLC-02", server.Port) with { NetworkNumber = null, PcNumber = null });
        incomplete.Start();
        Assert.True(await WaitFor(() => incomplete.Status(true).State == "NeedsConfiguration"));
        Assert.Contains("networkNumber", incomplete.Status(true).MissingParameters);
        Assert.Equal(0, server.ConnectionCount);
        Assert.Equal("Disconnected", Q(incomplete, "S1", "Rack_Full"));
    }

    [Fact]
    public async Task Stopping_a_worker_completes_pending_writes_and_releases_the_socket()
    {
        await using var server = Server();
        var w = NewWorker("PLC-01", server.Port, new FakeEvents());
        w.Start();
        Assert.True(await WaitFor(() => w.Status(true).State == "Connected"));
        await w.DisposeAsync();
        var r = await w.EnqueueWriteAsync(w.Stations[0].Find("Request_Ack")!, PointValue.OfBool(true));
        Assert.False(r.Success);
        Assert.True(await WaitFor(() => server.ConnectionCount == 1));
    }
}
