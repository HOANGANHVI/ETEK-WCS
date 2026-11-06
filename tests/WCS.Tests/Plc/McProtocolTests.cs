using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;

namespace WCS.Tests.Plc;

/// <summary>Byte-level tests of the MC 3E binary codec (no I/O). Expected vectors are written out by hand from the frame layout.</summary>
public class McFrameTests
{
    // Route values here are TEST values (the real ones are NEEDS_CONFIGURATION): network 0, PC 0xFF, module I/O 0x03FF, station 0.
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);

    private static string Hex(byte[] b) => Convert.ToHexString(b);

    [Fact]
    public void Batch_read_of_two_D_words_has_the_documented_layout()
    {
        var f = McFrame.BuildBatchRead(Route, 0x0010, new DeviceAddress(PlcDevice.D, 100), 2, bitUnits: false);
        //      subhdr  net pc  io     stn len    timer  cmd    sub    head(3)  dev pts
        Assert.Equal("5000" + "00" + "FF" + "FF03" + "00" + "0C00" + "1000" + "0104" + "0000" + "640000" + "A8" + "0200", Hex(f));
    }

    [Fact]
    public void Batch_read_of_M_bits_uses_bit_units_and_device_code_90()
    {
        var f = McFrame.BuildBatchRead(Route, 0x0010, new DeviceAddress(PlcDevice.M, 9000), 3, bitUnits: true);
        Assert.Equal("5000" + "00" + "FF" + "FF03" + "00" + "0C00" + "1000" + "0104" + "0100" + "282300" + "90" + "0300", Hex(f));   // 9000 = 0x002328
    }

    [Fact]
    public void Batch_write_words_appends_little_endian_data_and_grows_the_length_field()
    {
        var f = McFrame.BuildBatchWriteWords(Route, 0x0010, new DeviceAddress(PlcDevice.D, 1500), [0x1234, 0xFFFF]);
        Assert.Equal("5000" + "00" + "FF" + "FF03" + "00" + "1000" + "1000" + "0114" + "0000" + "DC0500" + "A8" + "0200" + "3412" + "FFFF", Hex(f));
    }

    [Fact]
    public void Batch_write_bits_packs_one_nibble_per_point_first_point_in_the_high_nibble()
    {
        var f = McFrame.BuildBatchWriteBits(Route, 0x0010, new DeviceAddress(PlcDevice.M, 100), [true, false, true]);
        Assert.Equal("5000" + "00" + "FF" + "FF03" + "00" + "0E00" + "1000" + "0114" + "0100" + "640000" + "90" + "0300" + "1010", Hex(f));
        Assert.Equal(new byte[] { 0x11 }, McFrame.EncodeBits([true, true]));
        Assert.Equal(new byte[] { 0x01 }, McFrame.EncodeBits([false, true]));
    }

    [Fact]
    public void Requests_round_trip_through_the_parser()
    {
        var req = McFrame.ParseRequest(McFrame.BuildBatchWriteWords(Route, 7, new DeviceAddress(PlcDevice.D, 70000), [1, 2, 3]));
        Assert.Equal((McFrame.CommandBatchWrite, McFrame.SubcommandWord, 3), (req.Command, req.Subcommand, req.Points));
        Assert.Equal(new DeviceAddress(PlcDevice.D, 70000), req.Head);
        Assert.Equal(Route, req.Route); Assert.Equal(7, req.MonitoringTimer);
        Assert.Equal(new byte[] { 1, 0, 2, 0, 3, 0 }, req.Data);
    }

    [Fact]
    public void Responses_are_parsed_and_decoded()
    {
        var data = McFrame.EncodeWords([0x0001, 0xFFFE]);
        var frame = McFrame.BuildResponse(Route, 0, data);
        Assert.Equal("D000" + "00" + "FF" + "FF03" + "00" + "0600" + "0000" + "0100FEFF", Hex(frame));
        var r = McFrame.ParseResponse(frame);
        Assert.Equal((ushort)0, r.EndCode);
        Assert.Equal(new ushort[] { 1, 0xFFFE }, McFrame.DecodeWords(r.Data, 2));
        Assert.Equal(new[] { true, false, true }, McFrame.DecodeBits(new byte[] { 0x10, 0x10 }, 3));
    }

    [Fact]
    public void Malformed_frames_are_rejected()
    {
        var good = McFrame.BuildResponse(Route, 0, new byte[2]);
        Assert.Throws<McProtocolException>(() => McFrame.ParseResponse(good[..^1]));                       // truncated
        var wrongHeader = (byte[])good.Clone(); wrongHeader[0] = 0x50;
        Assert.Throws<McProtocolException>(() => McFrame.ParseResponse(wrongHeader));                      // request subheader in a response
        Assert.Throws<McProtocolException>(() => McFrame.DecodeBits(new byte[1], 3));                      // 3 points need 2 bytes
        Assert.Throws<McProtocolException>(() => McFrame.DecodeWords(new byte[3], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildBatchRead(Route, 0, new DeviceAddress(PlcDevice.D, 0), 961, false));   // > 960 words
        Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildBatchRead(Route, 0, new DeviceAddress(PlcDevice.M, 0), 0, true));
        Assert.Throws<McProtocolException>(() => McFrame.ParseRequest(new byte[10]));
    }
}

/// <summary>The real driver against a real TCP socket (mock MC server): protocol, end codes, timeouts, retry, reconnect, serialization.</summary>
public class McTcpClientTests
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);   // TEST values

    private static McClientSettings Settings(int port, int timeoutMs = 500, int retries = 1) =>
        new("127.0.0.1", port, Route, 0x0010, TimeSpan.FromMilliseconds(timeoutMs), retries, TimeSpan.FromMilliseconds(10));

    private static MelsecMcMockServer StartServer()
    {
        var s = new MelsecMcMockServer { ExpectedRoute = Route };
        s.Start();
        return s;
    }

    [Fact]
    public async Task Reads_and_writes_M_bits_and_D_words_over_TCP()
    {
        await using var server = StartServer();
        server.SetBit(9001, true); server.SetBit(9003, true); server.SetWord(1510, 0xFFFF); server.SetWord(1511, 42);
        await using var c = new McTcpClient(Settings(server.Port));
        await c.ConnectAsync(CancellationToken.None);

        Assert.Equal(new[] { false, true, false, true }, await c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 9000), 4, default));
        Assert.Equal(new ushort[] { 0xFFFF, 42 }, await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 1510), 2, default));
        await c.WriteBitsAsync(new DeviceAddress(PlcDevice.M, 9100), [true, true, false], default);
        await c.WriteWordsAsync(new DeviceAddress(PlcDevice.D, 1600), [7, 8, 9], default);
        Assert.True(server.GetBit(9100) && server.GetBit(9101) && !server.GetBit(9102));
        Assert.Equal(new ushort[] { 7, 8, 9 }, new[] { server.GetWord(1600), server.GetWord(1601), server.GetWord(1602) });
        Assert.Equal(EndpointState.Connected, c.State);
        Assert.Equal(4, c.Diagnostics.Requests); Assert.Equal(0, c.Diagnostics.Failures);
        Assert.True(c.Diagnostics.BytesSent > 0 && c.Diagnostics.BytesReceived > 0 && c.Diagnostics.LastSuccessAt is not null);
    }

    [Fact]
    public async Task The_frames_on_the_wire_are_exactly_the_encoded_requests()
    {
        await using var server = StartServer();
        await using var c = new McTcpClient(Settings(server.Port));
        await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 100), 2, default);
        Assert.Equal(McFrame.BuildBatchRead(Route, 0x0010, new DeviceAddress(PlcDevice.D, 100), 2, false), Assert.Single(server.ReceivedFrames));
    }

    [Fact]
    public async Task A_PLC_end_code_is_reported_without_losing_the_connection()
    {
        await using var server = StartServer();
        server.MaxDeviceNumber = 2000;
        await using var c = new McTcpClient(Settings(server.Port));
        var ex = await Assert.ThrowsAsync<McEndCodeException>(() => c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 5000), 1, default));
        Assert.Equal(MelsecMcMockServer.EndCodeDeviceOutOfRange, ex.EndCode);
        Assert.Equal(EndpointState.Connected, c.State);                                   // the link is fine, the address is not
        Assert.Equal(0xC056, c.Diagnostics.LastEndCode);
        Assert.Equal(new ushort[] { 0 }, await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 10), 1, default));   // still usable

        server.ForceEndCode(0xC059, 1);
        await Assert.ThrowsAsync<McEndCodeException>(() => c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 0), 1, default));
    }

    [Fact]
    public async Task A_silent_PLC_causes_a_timeout_and_the_client_recovers_when_the_PLC_answers_again()
    {
        await using var server = StartServer();
        server.Mute = true;
        await using var c = new McTcpClient(Settings(server.Port, timeoutMs: 150, retries: 0));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 1), 1, default));
        Assert.True(sw.ElapsedMilliseconds < 1500);
        Assert.Equal(EndpointState.Faulted, c.State);
        Assert.Equal(1, c.Diagnostics.Timeouts);

        server.Mute = false;
        server.SetWord(1, 77);
        Assert.Equal(new ushort[] { 77 }, await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 1), 1, default));     // reconnects by itself
        Assert.Equal(EndpointState.Connected, c.State);
    }

    [Fact]
    public async Task A_dropped_connection_is_retried_for_reads_but_never_for_writes()
    {
        await using var server = StartServer();
        await using var c = new McTcpClient(Settings(server.Port, retries: 1));
        server.SetWord(5, 55);
        server.DropNextRequests(1);
        Assert.Equal(new ushort[] { 55 }, await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 5), 1, default));    // retried on a fresh connection
        Assert.Equal(1, c.Diagnostics.Reconnects);
        Assert.Equal(2, server.ConnectionCount);

        server.DropNextRequests(1);
        await Assert.ThrowsAnyAsync<Exception>(() => c.WriteWordsAsync(new DeviceAddress(PlcDevice.D, 6), [1], default));   // an unknown write outcome is not repeated
        Assert.Equal(EndpointState.Faulted, c.State);
        Assert.Equal(1, server.RequestCount - 2);     // exactly one request for the failed write (2 earlier reads' frames excluded)
    }

    [Fact]
    public async Task Reconnect_works_after_the_PLC_was_powered_off_and_on()
    {
        var server = StartServer();
        var port = server.Port;
        await using var c = new McTcpClient(Settings(port, timeoutMs: 200, retries: 0));
        await c.ConnectAsync(default);
        server.Stop();                                                       // PLC off
        await Assert.ThrowsAnyAsync<Exception>(() => c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 1), 1, default));
        await Assert.ThrowsAnyAsync<Exception>(() => c.ConnectAsync(default));       // connection refused while it is off
        Assert.NotEqual(EndpointState.Connected, c.State);

        await using var back = new MelsecMcMockServer { ExpectedRoute = Route };      // PLC on again, same address
        back.Start(port); back.SetWord(1, 5);
        await c.ConnectAsync(default);
        Assert.Equal(new ushort[] { 5 }, await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 1), 1, default));
        await server.DisposeAsync();
    }

    [Fact]
    public async Task A_PLC_that_does_not_know_our_route_stays_silent_and_is_reported_as_a_timeout()
    {
        await using var server = new MelsecMcMockServer { ExpectedRoute = new McRoute(1, 2, 0x03FF, 0) };
        server.Start();
        await using var c = new McTcpClient(Settings(server.Port, timeoutMs: 150, retries: 0));
        await Assert.ThrowsAsync<TimeoutException>(() => c.ReadBitsAsync(new DeviceAddress(PlcDevice.M, 0), 1, default));
    }

    [Fact]
    public async Task Concurrent_callers_never_overlap_on_the_socket()
    {
        await using var server = StartServer();
        for (var i = 0; i < 100; i++) server.SetWord(2000 + i, (ushort)i);
        await using var c = new McTcpClient(Settings(server.Port, timeoutMs: 2000));
        await c.ConnectAsync(default);

        var tasks = Enumerable.Range(0, 80).Select(i => Task.Run(async () =>
        {
            if (i % 5 == 0) { await c.WriteWordsAsync(new DeviceAddress(PlcDevice.D, 3000 + i), [(ushort)i], default); return -1; }
            var w = await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 2000 + i), 1, default);
            Assert.Equal((ushort)i, w[0]);
            return i;
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(0, server.PipelinedRequests);               // never two requests in flight on one connection
        Assert.Equal(80, server.RequestCount);
        Assert.Equal(1, server.ConnectionCount);
        for (var i = 0; i < 80; i += 5) Assert.Equal((ushort)i, server.GetWord(3000 + i));
    }

    [Fact]
    public async Task Large_reads_are_split_into_frames_within_the_protocol_limits()
    {
        await using var server = StartServer();
        server.SetWord(2999, 9);
        await using var c = new McTcpClient(Settings(server.Port, timeoutMs: 2000));
        var words = await c.ReadWordsAsync(new DeviceAddress(PlcDevice.D, 1000), 2000, default);     // 2000 > 960 -> 3 requests
        Assert.Equal(2000, words.Length); Assert.Equal(3, server.RequestCount);
        Assert.Equal((ushort)9, words[1999]);   // D2999 is the last word read
    }
}
