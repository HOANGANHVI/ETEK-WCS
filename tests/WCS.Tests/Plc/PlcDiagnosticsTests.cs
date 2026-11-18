using System.Net;
using System.Net.Sockets;
using WCS.Application.Plc;
using WCS.Application.Services;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;

namespace WCS.Tests.Plc;

/// <summary>PLC Diagnostic Logging (P0): standard codes, stages and structured details. The worker/driver behaviour itself is covered by the existing suites; here only the log content is asserted.</summary>
public class PlcDiagnosticsTests
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);
    private static readonly PlcRuntimeOptions Options = new() { ReconnectInitialDelayMs = 40, ReconnectMaxDelayMs = 160 };

    private static string S(IReadOnlyDictionary<string, object?>? a, string key) => a is not null && a.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    private static MelsecMcMockServer Server(int port = 0) { var s = new MelsecMcMockServer { ExpectedRoute = Route }; s.Start(port); return s; }

    private static PlcEndpointConfig Config(int port, int poll = 25, int timeout = 250) =>
        new("PLC-01", "127.0.0.1", port, "McProtocol3EBinaryTcp", 0, 0xFF, 0x03FF, 0, 0x0010, timeout, poll);

    private static IoPointRef R(string sig, PlcDataType t, string? a, bool pollable, PlcSignalDirection dir = PlcSignalDirection.PlcToSoftware) =>
        new(Guid.NewGuid(), "PLC-01", "A1", sig, dir, t, a, null, null, false, "Status", Pollable: pollable);

    private static List<StationIoHandler> OneStation(params PointInfo[] points) => [new StationIoHandler("PLC-01", "A1", points)];

    private static PointInfo Approved(string sig, string address, PlcDataType t = PlcDataType.Bool) => new(R(sig, t, address, true), 1, IoConfigStatus.Approved);

    private sealed class Factory(Func<IPlcEndpoint> make) : IPlcEndpointFactory { public IPlcEndpoint Create(PlcRuntimeMode mode, PlcEndpointConfig config) => make(); }

    private sealed class ThrowingEndpoint(Exception onConnect) : IPlcEndpoint
    {
        public EndpointState State => EndpointState.Disconnected;
        public PlcDiagnostics Diagnostics => new(0, 0, 0, 0, null, null, null, null, null, 0, 0);
        public Task ConnectAsync(CancellationToken ct) => throw onConnect;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<PointReading>> ReadStationAsync(IReadOnlyList<IoPointRef> points, CancellationToken ct) => throw new InvalidOperationException("not connected");
        public Task WritePointAsync(IoPointRef point, PointValue value, CancellationToken ct) => throw new InvalidOperationException("not connected");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static PlcWorker NewWorker(FakeEvents ev, PlcEndpointConfig cfg, List<StationIoHandler> stations, IPlcEndpointFactory? factory = null, bool enabled = true, string? why = null) =>
        new("PLC-01", "PLC-01", PlcRuntimeMode.RealReadOnly, Options, cfg, enabled, why, stations, factory ?? new McTestFactory(), ev, new SystemClock());

    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(15); }
        return cond();
    }

    // ------------------------------------------------------------------ classification (pure)

    [Theory]
    [InlineData(SocketError.ConnectionRefused, PlcDiagCodes.ConnectionRefused)]
    [InlineData(SocketError.NetworkUnreachable, PlcDiagCodes.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable, PlcDiagCodes.NetworkUnreachable)]
    [InlineData(SocketError.TimedOut, PlcDiagCodes.Timeout)]
    [InlineData(SocketError.ConnectionReset, PlcDiagCodes.ConnectionLost)]
    [InlineData(SocketError.ConnectionAborted, PlcDiagCodes.ConnectionLost)]
    [InlineData(SocketError.AccessDenied, PlcDiagCodes.ConnectFailed)]
    public void Socket_errors_map_to_standard_codes_with_the_socket_error_name(SocketError error, string code)
    {
        var r = PlcDiagClassifier.Classify(new SocketException((int)error), PlcDiagStages.Tcp);
        Assert.Equal(code, r.Code);
        Assert.Equal(error.ToString(), r.SocketError);
        Assert.Equal(PlcDiagStages.Tcp, r.Stage);
        Assert.False(string.IsNullOrEmpty(r.Explanation));
    }

    [Fact]
    public void Inner_exceptions_are_unwrapped_and_reported()
    {
        var ex = new IOException("Unable to read", new SocketException((int)SocketError.ConnectionReset));
        var r = PlcDiagClassifier.Classify(ex, PlcDiagStages.Tcp);
        Assert.Equal(PlcDiagCodes.ConnectionLost, r.Code);
        Assert.Contains("SocketException", r.Inner);
        Assert.Equal(PlcDiagCodes.Timeout, PlcDiagClassifier.Classify(new TimeoutException("no response within 300 ms"), PlcDiagStages.Mc).Code);
        Assert.Equal(PlcDiagCodes.ConnectionLost, PlcDiagClassifier.Classify(new IOException("connection closed by the PLC"), PlcDiagStages.Mc).Code);
    }

    [Fact]
    public void MC_exceptions_map_to_end_code_and_protocol_errors_at_the_MC_stage()
    {
        var end = PlcDiagClassifier.Classify(new McEndCodeException(0x4031), PlcDiagStages.Read);
        Assert.Equal((PlcDiagCodes.EndCodeError, PlcDiagStages.Mc), (end.Code, end.Stage));
        var proto = PlcDiagClassifier.Classify(new McProtocolException("bad subheader"), PlcDiagStages.Read);
        Assert.Equal((PlcDiagCodes.ProtocolError, PlcDiagStages.Mc), (proto.Code, proto.Stage));
    }

    [Fact]
    public void Events_without_explicit_codes_get_one_inferred_from_their_name()
    {
        Assert.Equal((PlcDiagCodes.LiveConnectionDisabled, PlcDiagStages.Config), PlcDiag.Infer("DISABLED", "[LIVE_CONNECTION_DISABLED] x"));
        Assert.Equal((PlcDiagCodes.ConnectionNotEnabled, PlcDiagStages.Config), PlcDiag.Infer("DISABLED", "[CONNECTION_NOT_ENABLED] x"));
        Assert.Equal(PlcDiagCodes.ConfigurationMissing, PlcDiag.Infer("STATE", "PLC-01: NeedsConfiguration - [PLC_PARAMS_MISSING] port").Code);
        Assert.Equal(PlcDiagCodes.WriteBlocked, PlcDiag.Infer("WRITE_DENIED", "blocked").Code);
        Assert.Equal(PlcDiagCodes.ConnectionRefused, PlcDiag.Infer("CONNECT_FAILED", "Connection failed: No connection could be made because the target machine actively refused it.").Code);
        Assert.Equal(PlcDiagCodes.IoPointNotApproved, PlcDiag.Infer("IO_NOT_APPROVED", "x").Code);
    }

    // ------------------------------------------------------------------ worker: why nothing is connected

    [Fact]
    public async Task A_disabled_PLC_logs_the_reason_code_and_that_no_TCP_connection_was_attempted()
    {
        await using var server = Server();
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(server.Port), OneStation(Approved("Pickup_Request", "M100")), enabled: false, why: "[LIVE_CONNECTION_DISABLED] Chưa cho phép kết nối thật (Plc:AllowLiveConnection).");
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("DISABLED")));
        var a = ev.ArgsOf("DISABLED");
        Assert.Equal(PlcDiagCodes.LiveConnectionDisabled, S(a, PlcDiag.CodeKey));
        Assert.Equal(PlcDiagStages.Config, S(a, PlcDiag.StageKey));
        Assert.Equal("False", S(a, "tcpAttempted"));
        Assert.False(string.IsNullOrEmpty(S(a, "explanation")));
        Assert.Equal(0, server.ConnectionCount);

        var ev2 = new FakeEvents();
        await using var w2 = NewWorker(ev2, Config(server.Port), OneStation(Approved("Pickup_Request", "M100")), enabled: false, why: "[CONNECTION_NOT_ENABLED] Admin chưa bật kết nối PLC này.");
        w2.Start();
        Assert.True(await WaitFor(() => ev2.HasLog("DISABLED")));
        Assert.Equal(PlcDiagCodes.ConnectionNotEnabled, S(ev2.ArgsOf("DISABLED"), PlcDiag.CodeKey));
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task Missing_configuration_is_logged_with_the_missing_parameters_and_no_connection_is_attempted()
    {
        await using var server = Server();
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(server.Port) with { NetworkNumber = null, PcNumber = null }, OneStation(Approved("Pickup_Request", "M100")));
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("CONFIG_MISSING")));
        var a = ev.ArgsOf("CONFIG_MISSING");
        Assert.Equal(PlcDiagCodes.ConfigurationMissing, S(a, PlcDiag.CodeKey));
        Assert.Contains("networkNumber", S(a, "missing"));
        Assert.Contains("pcNumber", S(a, "missing"));
        Assert.Equal(0, server.ConnectionCount);
    }

    // ------------------------------------------------------------------ worker: TCP

    [Fact]
    public async Task TCP_connection_refused_is_classified_with_socket_error_duration_and_retry_count()
    {
        int port; { await using var s = Server(); port = s.Port; }      // a port that is closed again
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(port, timeout: 5000), OneStation(Approved("Pickup_Request", "M100")));   // Windows retries a refused SYN for ~2 s: a short timeout would report a timeout instead
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("CONNECT_FAILED"), 8000));
        var a = ev.ArgsOf("CONNECT_FAILED");
        Assert.Equal(PlcDiagCodes.ConnectionRefused, S(a, PlcDiag.CodeKey));
        Assert.Equal("ConnectionRefused", S(a, "socketError"));
        Assert.Equal("True", S(a, "tcpAttempted"));
        Assert.Equal("127.0.0.1", S(a, "ip"));
        Assert.Equal(port.ToString(), S(a, "port"));
        Assert.True(long.Parse(S(a, "durationMs")) >= 0);
        Assert.True(int.Parse(S(a, "retry")) >= 1);
        Assert.Equal(PlcDiagStages.Tcp, S(a, PlcDiag.StageKey));
    }

    [Theory]
    [InlineData(SocketError.NetworkUnreachable, PlcDiagCodes.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable, PlcDiagCodes.NetworkUnreachable)]
    [InlineData(SocketError.TimedOut, PlcDiagCodes.Timeout)]
    public async Task Unreachable_and_timed_out_connects_are_classified(SocketError error, string code)
    {
        var ev = new FakeEvents();
        var factory = new Factory(() => new ThrowingEndpoint(new SocketException((int)error)));
        await using var w = NewWorker(ev, Config(5000), OneStation(Approved("Pickup_Request", "M100")), factory);
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("CONNECT_FAILED")));
        Assert.Equal(code, S(ev.ArgsOf("CONNECT_FAILED"), PlcDiag.CodeKey));
        Assert.Equal(error.ToString(), S(ev.ArgsOf("CONNECT_FAILED"), "socketError"));
    }

    [Fact]
    public async Task A_connect_timeout_without_a_socket_error_is_still_PLC_TIMEOUT()
    {
        var ev = new FakeEvents();
        var factory = new Factory(() => new ThrowingEndpoint(new TimeoutException("connect to 10.0.0.1:5000 timed out")));
        await using var w = NewWorker(ev, Config(5000), OneStation(Approved("Pickup_Request", "M100")), factory);
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("CONNECT_FAILED")));
        Assert.Equal(PlcDiagCodes.Timeout, S(ev.ArgsOf("CONNECT_FAILED"), PlcDiag.CodeKey));
    }

    [Fact]
    public async Task Connection_loss_and_reconnect_log_the_cause_the_retries_and_the_downtime()
    {
        var server = Server(); var port = server.Port;
        server.SetBit(100, true);
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(port), OneStation(Approved("Pickup_Request", "M100")));
        w.Start();
        Assert.True(await WaitFor(() => w.Status(true).State == "Connected"));
        Assert.True(ev.HasLog("TCP_CONNECTED"));
        Assert.Equal("False", S(ev.ArgsOf("TCP_CONNECTED"), "recovered"));                    // the first connect is not a recovery
        Assert.True(long.Parse(S(ev.ArgsOf("TCP_CONNECTED"), "durationMs")) >= 0);

        server.Stop();
        Assert.True(await WaitFor(() => w.Status(true).State == "Faulted"));
        Assert.True(await WaitFor(() => ev.HasLog("LINK_LOST") || ev.HasLog("CONNECT_FAILED")));
        var lost = ev.ArgsOf("LINK_LOST") ?? ev.ArgsOf("CONNECT_FAILED");
        Assert.Contains(S(lost, PlcDiag.CodeKey), new[] { PlcDiagCodes.ConnectionLost, PlcDiagCodes.ConnectionRefused, PlcDiagCodes.Timeout, PlcDiagCodes.ReconnectFailed });
        Assert.True(await WaitFor(() => ev.HasLog("CONNECT_FAILED")));
        Assert.Equal("True", S(ev.ArgsOf("CONNECT_FAILED"), "reconnect"));                     // an attempt after a lost connection says so

        await using var back = Server(port);                                                    // the PLC comes back
        Assert.True(await WaitFor(() => w.Status(true).State == "Connected"));
        var rec = ev.ArgsOf("TCP_CONNECTED");
        Assert.Equal("True", S(rec, "recovered"));
        Assert.Equal(PlcDiagStages.Recovery, S(rec, PlcDiag.StageKey));
        Assert.True(int.Parse(S(rec, "retries")) >= 1);
        Assert.True(int.Parse(S(rec, "downtimeSeconds")) >= 0);
    }

    // ------------------------------------------------------------------ worker: MC and reads

    [Fact]
    public async Task An_MC_end_code_error_is_reported_as_PLC_END_CODE_ERROR_with_the_code()
    {
        await using var server = Server();
        server.SetBit(100, true); server.ForceEndCode(0x4031, 1000);
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(server.Port), OneStation(Approved("Pickup_Request", "M100")));
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("READ_BAD_A1")));
        var a = ev.ArgsOf("READ_BAD_A1");
        Assert.Equal(PlcDiagCodes.EndCodeError, S(a, PlcDiag.CodeKey));
        Assert.Equal("0x4031", S(a, "endCode"));
        Assert.Equal(PlcDiagStages.Mc, S(a, PlcDiag.StageKey));
        Assert.Equal("Connected", w.Status(true).State);                                        // the link itself is healthy
    }

    [Fact]
    public async Task An_MC_response_timeout_is_reported_as_PLC_TIMEOUT_at_the_read_stage()
    {
        await using var server = Server();
        server.SetBit(100, true); server.ResponseDelay = TimeSpan.FromSeconds(3);   // a silent PLC
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(server.Port, timeout: 120), OneStation(Approved("Pickup_Request", "M100")));
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("READ_BAD_A1")));
        Assert.Equal(PlcDiagCodes.Timeout, S(ev.ArgsOf("READ_BAD_A1"), PlcDiag.CodeKey));
    }

    [Fact]
    public async Task A_PLC_that_closes_the_connection_is_PLC_CONNECTION_LOST()
    {
        await using var server = Server();
        server.DropNextRequests(10);
        await using var c = new McTcpClient(new McClientSettings("127.0.0.1", server.Port, Route, 0x0010, TimeSpan.FromMilliseconds(300), MaxReadRetries: 0));
        await c.ConnectAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 100), 1, CancellationToken.None));
        Assert.Equal(PlcDiagCodes.ConnectionLost, c.Diagnostics.LastErrorCode);
    }

    [Fact]
    public async Task The_driver_records_the_standard_code_and_socket_error_of_its_last_failure()
    {
        await using var server = Server();
        server.ResponseDelay = TimeSpan.FromSeconds(3);
        await using var c = new McTcpClient(new McClientSettings("127.0.0.1", server.Port, Route, 0x0010, TimeSpan.FromMilliseconds(150), MaxReadRetries: 0));
        await c.ConnectAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 100), 1, CancellationToken.None));
        Assert.Equal(PlcDiagCodes.Timeout, c.Diagnostics.LastErrorCode);
        Assert.Equal("TIMEOUT", c.Diagnostics.LastFailedExchange!.Outcome);
    }

    [Fact]
    public async Task A_generic_read_failure_is_PLC_READ_FAILED()
    {
        var ev = new FakeEvents();
        var factory = new Factory(() => new FailingReadEndpoint());
        await using var w = NewWorker(ev, Config(5000), OneStation(Approved("Pickup_Request", "M100")), factory);
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("STATION_ERROR_A1")));
        Assert.Equal(PlcDiagCodes.ReadFailed, S(ev.ArgsOf("STATION_ERROR_A1"), PlcDiag.CodeKey));
        Assert.Equal("InvalidOperationException", S(ev.ArgsOf("STATION_ERROR_A1"), "exception"));
    }

    private sealed class FailingReadEndpoint : IPlcEndpoint
    {
        public EndpointState State => EndpointState.Connected;
        public PlcDiagnostics Diagnostics => new(0, 0, 0, 0, null, null, null, null, null, 0, 0);
        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<PointReading>> ReadStationAsync(IReadOnlyList<IoPointRef> points, CancellationToken ct) => throw new InvalidOperationException("decoder exploded");
        public Task WritePointAsync(IoPointRef point, PointValue value, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Unapproved_points_are_reported_once_with_the_reason_and_cause_no_read_traffic()
    {
        await using var server = Server();
        server.SetBit(100, true);
        var ev = new FakeEvents();
        var points = OneStation(
            Approved("Pickup_Request", "M100"),
            new PointInfo(R("Rack_Full", PlcDataType.Int, null, false), 2, IoConfigStatus.NeedsConfiguration),             // no address
            new PointInfo(R("Rack_Empty", PlcDataType.Int, "D1502", false), 3, IoConfigStatus.Configured));                // address entered but invalid for polling (CR-012: approval is not needed to read)
        await using var w = NewWorker(ev, Config(server.Port), points);
        w.Start();
        Assert.True(await WaitFor(() => ev.HasLog("IO_NOT_APPROVED")));
        var a = ev.ArgsOf("IO_NOT_APPROVED");
        Assert.Equal(PlcDiagCodes.IoPointNotApproved, S(a, PlcDiag.CodeKey));
        Assert.Equal("2", S(a, "count")); Assert.Equal("1", S(a, "noAddress")); Assert.Equal("1", S(a, "notApproved"));
        Assert.Contains("Rack_Full", S(a, "sample"));
        await Task.Delay(400);                                                                // many polling cycles later
        Assert.Equal(1, ev.Logs.Count(l => l.Event == "IO_NOT_APPROVED"));                    // not once per cycle
        Assert.All(server.ReceivedFrames.Select(x => McFrame.ParseRequest(x)), r => Assert.Equal(McFrame.CommandBatchRead, r.Command));
    }

    [Fact]
    public async Task Logging_does_not_change_what_the_worker_sends_or_how_often()
    {
        await using var server = Server();
        server.SetBit(100, true);
        var ev = new FakeEvents();
        await using var w = NewWorker(ev, Config(server.Port, poll: 40), OneStation(Approved("Pickup_Request", "M100")));
        w.Start();
        Assert.True(await WaitFor(() => w.Status(true).Cycles >= 5));
        Assert.All(server.ReceivedFrames.Select(x => McFrame.ParseRequest(x)), r => Assert.Equal(McFrame.CommandBatchRead, r.Command));   // reads only
        Assert.True(ev.Logs.Count < 10, $"expected few log lines in steady state, got {ev.Logs.Count}");                                   // no per-cycle logging
    }
}
