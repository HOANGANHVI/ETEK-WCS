using System.Net.Sockets;
using WCS.Application.Plc;
using WCS.Application.Services;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc.Mc;
using WCS.Infrastructure.Simulation;

namespace WCS.Tests.Plc;

/// <summary>CR-003: device addressing (radix), strict response validation, ambiguous writes, per-operation timeouts, diagnostics and approved-only polling.</summary>
public class McAddressTests
{
    private static int N(string text, AddressBases? b) => DeviceAddress.Parse(text, b).Address!.Value.Number;

    [Theory]
    [InlineData("X0", 0)] [InlineData("X7", 7)] [InlineData("X10", 8)] [InlineData("X17", 15)] [InlineData("X20", 16)]
    [InlineData("Y0", 0)] [InlineData("Y10", 8)] [InlineData("Y20", 16)] [InlineData("y020", 16)]
    public void X_and_Y_in_octal_follow_the_reference_documentation_table(string text, int expected) =>
        Assert.Equal(expected, N(text, new AddressBases(Xy: 8)));

    [Theory]
    [InlineData("X10", 16)] [InlineData("X20", 32)] [InlineData("XFF", 255)] [InlineData("Y1F", 31)] [InlineData("x7ff", 2047)]
    public void X_and_Y_in_hexadecimal_as_written_by_GX_Works_for_MELSEC_Q(string text, int expected) =>
        Assert.Equal(expected, N(text, new AddressBases(Xy: 16)));

    [Fact]
    public void The_same_text_means_different_devices_in_octal_and_hexadecimal_which_is_why_the_base_is_never_defaulted()
    {
        Assert.NotEqual(N("X10", new AddressBases(Xy: 8)), N("X10", new AddressBases(Xy: 16)));
        foreach (var text in new[] { "X10", "Y20", "W100" })
        {
            var r = DeviceAddress.Parse(text, null);
            Assert.Null(r.Address); Assert.Equal(AddressProblem.BaseUnconfirmed, r.Problem); Assert.Contains("NEEDS_CONFIRMATION", r.Message);
        }
        Assert.Equal(AddressProblem.BaseUnconfirmed, DeviceAddress.Parse("X10", new AddressBases(Xy: 10)).Problem);       // 10 is not a valid X/Y base
        Assert.Equal(AddressProblem.BaseUnconfirmed, DeviceAddress.Parse("W10", new AddressBases(Xy: 8)).Problem);        // the W base is separate
    }

    [Theory]
    [InlineData("X8")] [InlineData("X19")] [InlineData("XA")] [InlineData("Y9")]
    public void Octal_addresses_reject_digits_8_and_9_and_letters(string text) =>
        Assert.Equal(AddressProblem.Format, DeviceAddress.Parse(text, new AddressBases(Xy: 8)).Problem);

    [Fact]
    public void Hexadecimal_addresses_reject_non_hex_letters_and_W_follows_its_own_base()
    {
        Assert.Equal(AddressProblem.Format, DeviceAddress.Parse("XG", new AddressBases(Xy: 16)).Problem);
        Assert.Equal(100, N("W100", new AddressBases(W: 10)));
        Assert.Equal(256, N("W100", new AddressBases(W: 16)));
        Assert.Equal(AddressProblem.Format, DeviceAddress.Parse("W1A", new AddressBases(W: 10)).Problem);
        Assert.Equal(26, N("W1A", new AddressBases(W: 16)));
    }

    [Fact]
    public void D_and_M_are_always_decimal_whatever_the_bases_are()
    {
        var any = new AddressBases(Xy: 16, W: 16);
        Assert.Equal(100, N("D100", any)); Assert.Equal(0, N("M0", any)); Assert.Equal(9000, N("m9000", null));
        Assert.Equal(AddressProblem.Format, DeviceAddress.Parse("DA", any).Problem);
        Assert.Equal(AddressProblem.Format, DeviceAddress.Parse("M1F", any).Problem);
    }

    [Theory]
    [InlineData("")] [InlineData("  ")] [InlineData("Z10")] [InlineData("X")] [InlineData("D1.5")] [InlineData("D 5")] [InlineData("D-1")] [InlineData("D16777216")] [InlineData("D12345678901")]
    public void Malformed_or_out_of_range_addresses_are_refused(string text)
    {
        var r = DeviceAddress.Parse(text, new AddressBases(Xy: 8, W: 10));
        Assert.Null(r.Address); Assert.Equal(AddressProblem.Format, r.Problem);
    }

    [Fact]
    public void The_largest_frame_address_is_accepted() => Assert.Equal(0xFFFFFF, N("D16777215", null));

    [Fact]
    public void Stored_text_keeps_the_notation_of_the_engineer_and_is_never_converted()
    {
        var oct = new AddressBases(Xy: 8, W: 16);
        Assert.Equal("X10", DeviceAddress.Normalize(" x010 ", oct));      // NOT "X8"
        Assert.Equal("Y0", DeviceAddress.Normalize("y000", oct));
        Assert.Equal("W1A", DeviceAddress.Normalize("w01a", oct));
        Assert.Equal("M100", DeviceAddress.Normalize("m0100", oct));
        Assert.Equal("D1500", DeviceAddress.Normalize("d1500", null));
    }

    // ---- validator
    private static PointSpec Spec(PlcDataType t, string? a, PlcSignalDirection? d = null, int? words = null, string signal = "S") => new(Guid.NewGuid(), "A1", signal, t, a, words, d);

    [Fact]
    public void Validator_applies_the_device_rules_of_the_extended_driver()
    {
        var b = new AddressBases(Xy: 8, W: 16);
        Assert.Null(AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "X10", PlcSignalDirection.PlcToSoftware), b));
        Assert.Null(AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "Y10", PlcSignalDirection.SoftwareToPlc), b));
        Assert.Null(AddressValidator.ValidateOne(Spec(PlcDataType.Int, "W10", PlcSignalDirection.PlcToSoftware), b));
        Assert.Null(AddressValidator.ValidateOne(Spec(PlcDataType.String, "W100", words: 10), b));
        Assert.Equal("X_READ_ONLY", AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "X10", PlcSignalDirection.SoftwareToPlc), b)!.Code);
        Assert.Equal("TYPE_DEVICE", AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "D10"), b)!.Code);
        Assert.Equal("TYPE_DEVICE", AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "W10"), b)!.Code);
        Assert.Equal("TYPE_DEVICE", AddressValidator.ValidateOne(Spec(PlcDataType.Int, "M10"), b)!.Code);
        Assert.Equal("TYPE_DEVICE", AddressValidator.ValidateOne(Spec(PlcDataType.Int, "X10"), b)!.Code);
        Assert.Equal("STRING_LENGTH", AddressValidator.ValidateOne(Spec(PlcDataType.String, "D10"), b)!.Code);
        Assert.Equal("ADDRESS_FORMAT", AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "M1F"), b)!.Code);
        Assert.Equal("RADIX_UNCONFIRMED", AddressValidator.ValidateOne(Spec(PlcDataType.Bool, "X10"), null)!.Code);
        Assert.Equal("RADIX_UNCONFIRMED", AddressValidator.ValidateOne(Spec(PlcDataType.Int, "W10"), new AddressBases(Xy: 8))!.Code);
    }

    [Fact]
    public void Overlaps_are_detected_on_the_resolved_device_number_inside_one_PLC_only()
    {
        var b = new AddressBases(Xy: 8, W: 10);
        var points = new[]
        {
            Spec(PlcDataType.Bool, "Y10", signal: "a"), Spec(PlcDataType.Bool, "Y010", signal: "b"),         // same device, written differently
            Spec(PlcDataType.Bool, "M8", signal: "c"), Spec(PlcDataType.Bool, "X10", PlcSignalDirection.PlcToSoftware, signal: "d"),   // M8 and X10(octal=8) are different devices
            Spec(PlcDataType.String, "W110", words: 5, signal: "e"), Spec(PlcDataType.Int, "W114", signal: "f"),       // W110..W114 overlaps W114
            Spec(PlcDataType.Int, "D100", signal: "g"), Spec(PlcDataType.Int, "W100", signal: "h"),                   // D and W are different devices
        };
        var issues = AddressValidator.Validate(points, b);
        Assert.Equal(2, issues.Count(i => i.Code == "ADDRESS_CONFLICT"));
        Assert.Contains(issues, i => i.Signal == "b"); Assert.Contains(issues, i => i.Signal == "f");
        Assert.DoesNotContain(issues, i => i.Signal is "c" or "d" or "g" or "h");
    }

    [Fact]
    public void A_point_is_polled_only_when_it_has_an_address_and_is_approved()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new StationIoPoint("PLC-01", "A1", 1, "Status", "Pickup_Request", PlcSignalDirection.PlcToSoftware, PlcDataType.Bool, "M100", "", now);
        Assert.False(p.IsPollable);                                  // template only
        p.ConfigureAddress("M200", null, null, now);
        Assert.True(p.HasConfirmedAddress); Assert.False(p.IsPollable);   // entered but not approved by the engineer
        p.Approve("admin", now);
        Assert.True(p.IsPollable);
        p.ConfigureAddress("M201", null, null, now);                // changing the address withdraws the approval
        Assert.False(p.IsPollable);
        p.Approve("admin", now); p.ClearAddress(now);
        Assert.False(p.IsPollable);
    }
}

public class McCr003SocketTests
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);   // TEST values

    private static MelsecMcMockServer Server() { var s = new MelsecMcMockServer { ExpectedRoute = Route }; s.Start(); return s; }

    private static McTcpClient Client(MelsecMcMockServer s, int timeoutMs = 400, int retries = 1, int? readMs = null, int? writeMs = null) =>
        new(new McClientSettings("127.0.0.1", s.Port, Route, 0x0010, TimeSpan.FromMilliseconds(timeoutMs), retries, TimeSpan.FromMilliseconds(20),
            ReadTimeout: readMs is { } r ? TimeSpan.FromMilliseconds(r) : null, WriteTimeout: writeMs is { } w ? TimeSpan.FromMilliseconds(w) : null, PlcId: "PLC-T"));

    private static DeviceAddress A(PlcDevice d, int n) => new(d, n);

    private static bool IsWrite(byte[] req) => req[11] == 0x01 && req[12] == 0x14;

    // ---------------------------------------------------------------------------------------------------------- devices
    [Fact]
    public async Task W_X_and_Y_devices_are_read_and_written_with_the_right_device_codes_over_TCP()
    {
        await using var server = Server();
        server.SetWord(PlcDevice.W, 16, 0xBEEF); server.SetWord(PlcDevice.W, 17, 0x0102);
        server.SetBit(PlcDevice.X, 8, true); server.SetBit(PlcDevice.Y, 16, true);
        await using var c = Client(server);

        Assert.Equal(new ushort[] { 0xBEEF, 0x0102 }, await c.ReadWordsAsync(A(PlcDevice.W, 16), 2, default));
        Assert.Equal(new[] { true, false, false }, await c.ReadBitsAsync(A(PlcDevice.X, 8), 3, default));
        Assert.Equal(new[] { true }, await c.ReadBitsAsync(A(PlcDevice.Y, 16), 1, default));

        await c.WriteBitsAsync(A(PlcDevice.Y, 16), [false, true, true], default);
        await c.WriteWordsAsync(A(PlcDevice.W, 100), [1, 2, 0xFFFF], default);
        Assert.False(server.GetBit(PlcDevice.Y, 16)); Assert.True(server.GetBit(PlcDevice.Y, 17)); Assert.True(server.GetBit(PlcDevice.Y, 18));
        Assert.Equal(new ushort[] { 1, 2, 0xFFFF }, new[] { server.GetWord(PlcDevice.W, 100), server.GetWord(PlcDevice.W, 101), server.GetWord(PlcDevice.W, 102) });

        var codes = server.ReceivedFrames.Select(f => f[18]).ToArray();
        Assert.Equal(new byte[] { 0xB4, 0x9C, 0x9D, 0x9D, 0xB4 }, codes);
        Assert.Equal(0, server.PipelinedRequests);
    }

    [Fact]
    public async Task Writing_to_X_is_refused_before_anything_reaches_the_network()
    {
        await using var server = Server();
        await using var c = Client(server);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.WriteBitsAsync(A(PlcDevice.X, 0), [true], default));
        Assert.Equal(0, server.RequestCount); Assert.Equal(0, server.ConnectionCount);
    }

    // ---------------------------------------------------------------------------------------------------------- strict response validation
    [Theory]
    [InlineData("subheader", 0, 0xD1)] [InlineData("subheader 2nd byte", 1, 0x01)] [InlineData("network", 2, 0x09)] [InlineData("PC", 3, 0x09)]
    [InlineData("module I/O low", 4, 0x00)] [InlineData("module I/O high", 5, 0x00)] [InlineData("station", 6, 0x04)]
    public async Task A_response_with_a_wrong_header_field_is_rejected_the_socket_is_closed_and_the_next_request_recovers(string field, int offset, int value)
    {
        await using var server = Server();
        server.SetWord(10, 7);
        await using var c = Client(server, retries: 0);
        server.ResponseFilter = (_, resp) => { var x = (byte[])resp.Clone(); x[offset] = (byte)value; return x; };

        await Assert.ThrowsAsync<McProtocolException>(() => c.ReadWordsAsync(A(PlcDevice.D, 10), 1, default));
        Assert.Equal(EndpointState.Faulted, c.State);
        var failed = c.Diagnostics.LastFailedExchange;
        Assert.NotNull(failed); Assert.Equal("FAILED", failed!.Outcome); Assert.False(failed.IsAmbiguous);
        Assert.True(field.Length > 0);

        server.ResponseFilter = null;
        Assert.Equal(new ushort[] { 7 }, await c.ReadWordsAsync(A(PlcDevice.D, 10), 1, default));       // reconnects by itself
        Assert.Equal(EndpointState.Connected, c.State);
        Assert.Equal(2, server.ConnectionCount);
    }

    [Fact]
    public async Task An_announced_length_shorter_than_the_data_is_rejected_and_the_leftover_bytes_cannot_poison_the_next_request()
    {
        await using var server = Server();
        server.SetWord(10, 7);
        await using var c = Client(server, retries: 0);
        server.ResponseFilter = (_, resp) => { var x = (byte[])resp.Clone(); x[7] -= 2; return x; };
        await Assert.ThrowsAsync<McProtocolException>(() => c.ReadWordsAsync(A(PlcDevice.D, 10), 1, default));
        server.ResponseFilter = null;
        Assert.Equal(new ushort[] { 7 }, await c.ReadWordsAsync(A(PlcDevice.D, 10), 1, default));
    }

    [Fact]
    public async Task An_oversized_announced_length_is_rejected_without_waiting_for_the_phantom_bytes()
    {
        await using var server = Server();
        await using var c = Client(server, timeoutMs: 3000, retries: 0);
        server.ResponseFilter = (_, resp) => { var x = (byte[])resp.Clone(); x[7] = 0xFF; x[8] = 0xFF; return x; };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<McProtocolException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        Assert.True(sw.ElapsedMilliseconds < 1500, "must fail on the header, not on a timeout");
    }

    [Fact]
    public async Task An_announced_length_longer_than_the_data_times_out_and_the_partial_bytes_are_kept_for_diagnosis()
    {
        await using var server = Server();
        await using var c = Client(server, timeoutMs: 300, retries: 0);
        server.ResponseFilter = (_, resp) => { var x = (byte[])resp.Clone(); x[7] += 2; return x; };
        await Assert.ThrowsAsync<TimeoutException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        var f = c.Diagnostics.LastFailedExchange!;
        Assert.Equal("TIMEOUT", f.Outcome); Assert.StartsWith("D0 00 00 FF FF 03 00", f.ResponseHex);
        Assert.Equal(1, c.Diagnostics.Timeouts);
    }

    [Fact]
    public async Task A_partial_response_followed_by_silence_is_a_timeout_and_a_partial_response_followed_by_a_disconnect_is_an_IO_fault()
    {
        await using var server = Server();
        await using var c = Client(server, timeoutMs: 250, retries: 0);
        server.ResponseFilter = (_, resp) => resp[..5];
        await Assert.ThrowsAsync<TimeoutException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        Assert.Equal("D0 00 00 FF FF", c.Diagnostics.LastFailedExchange!.ResponseHex);

        server.CloseAfterResponse = true;
        await Assert.ThrowsAsync<IOException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        Assert.Equal(EndpointState.Faulted, c.State);
    }

    [Fact]
    public async Task Reads_are_retried_once_on_transport_faults_but_never_more_than_configured()
    {
        await using var server = Server();
        await using var c = Client(server, timeoutMs: 250, retries: 1);
        server.ResponseFilter = (_, resp) => resp[..5];
        server.CloseAfterResponse = true;
        await Assert.ThrowsAsync<IOException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        Assert.Equal(2, server.RequestCount); Assert.Equal(2, server.ConnectionCount);       // original + one retry on a fresh connection
    }

    [Fact]
    public async Task A_PLC_end_code_is_not_a_transport_fault_keeps_the_connection_and_is_recorded()
    {
        await using var server = Server();
        await using var c = Client(server);
        server.ForceEndCode(0xC05B, 1);
        var ex = await Assert.ThrowsAsync<McEndCodeException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        Assert.Equal(0xC05B, ex.EndCode);
        Assert.Equal(EndpointState.Connected, c.State);
        Assert.Equal("END_CODE", c.Diagnostics.LastFailedExchange!.Outcome); Assert.Equal(0xC05B, c.Diagnostics.LastFailedExchange.EndCode);
        Assert.Equal(1, server.ConnectionCount);
        await c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default);                              // same connection still usable
        Assert.Equal(1, server.ConnectionCount);
    }

    // ---------------------------------------------------------------------------------------------------------- ambiguous writes
    [Fact]
    public async Task A_write_that_gets_no_answer_is_ambiguous_and_is_never_retried()
    {
        await using var server = Server();
        server.Mute = true;
        await using var c = Client(server, timeoutMs: 400, retries: 3, writeMs: 200);
        var ex = await Assert.ThrowsAsync<PlcWriteAmbiguousException>(() => c.WriteWordsAsync(A(PlcDevice.D, 5), [7], default));
        Assert.Contains("KHÔNG", ex.Message);
        await Task.Delay(500);
        Assert.Equal(1, server.RequestCount);                                              // MaxReadRetries = 3 does not apply to writes
        var f = c.Diagnostics.LastFailedExchange!;
        Assert.True(f.IsAmbiguous); Assert.Equal("AMBIGUOUS", f.Outcome); Assert.Equal("WRITE", f.Operation);
    }

    [Fact]
    public async Task A_write_that_was_executed_but_whose_answer_was_lost_is_reported_ambiguous_which_is_why_retrying_is_forbidden()
    {
        await using var server = Server();
        server.ResponseFilter = (req, resp) => IsWrite(req) ? null : resp;                 // the PLC executes the write, the confirmation never arrives
        await using var c = Client(server, timeoutMs: 300, retries: 2);
        await Assert.ThrowsAsync<PlcWriteAmbiguousException>(() => c.WriteWordsAsync(A(PlcDevice.D, 5), [7], default));
        Assert.Equal(7, server.GetWord(5));                                                // it DID happen
        await Task.Delay(400);
        Assert.Equal(1, server.RequestCount);                                              // and it was not sent a second time
    }

    [Fact]
    public async Task A_connection_dropped_after_the_write_request_is_ambiguous()
    {
        await using var server = Server();
        await using var c = Client(server, retries: 2);
        await c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default);                              // connection established
        server.DropNextRequests(1);
        await Assert.ThrowsAsync<PlcWriteAmbiguousException>(() => c.WriteBitsAsync(A(PlcDevice.M, 1), [true], default));
        Assert.Equal(2, server.RequestCount);                                              // 1 read + 1 write, no resend
    }

    [Fact]
    public async Task A_failure_before_the_request_is_sent_is_not_ambiguous_and_a_PLC_error_is_not_ambiguous()
    {
        var server = Server(); var port = server.Port; await server.DisposeAsync();         // nothing listens
        await using (var c = new McTcpClient(new McClientSettings("127.0.0.1", port, Route, 0x0010, TimeSpan.FromMilliseconds(300), PlcId: "PLC-T")))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => c.WriteWordsAsync(A(PlcDevice.D, 5), [7], default));
            Assert.IsNotType<PlcWriteAmbiguousException>(ex);
        }
        await using var live = Server();
        live.ForceEndCode(0xC05B, 1);
        await using var c2 = Client(live);
        await Assert.ThrowsAsync<McEndCodeException>(() => c2.WriteWordsAsync(A(PlcDevice.D, 5), [7], default));
        Assert.Equal(0, live.GetWord(5));
    }

    [Fact]
    public async Task Cancelling_after_the_request_was_sent_is_also_ambiguous()
    {
        await using var server = Server();
        server.Mute = true;
        await using var c = Client(server, timeoutMs: 5000);
        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAsync<PlcWriteAmbiguousException>(() => c.WriteWordsAsync(A(PlcDevice.D, 5), [7], cts.Token));
    }

    // ---------------------------------------------------------------------------------------------------------- timeouts
    [Fact]
    public async Task Read_and_write_use_their_own_timeouts_and_fall_back_to_the_general_one()
    {
        var s = new McClientSettings("h", 1, Route, 1, TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), s.EffectiveConnectTimeout); Assert.Equal(TimeSpan.FromSeconds(3), s.EffectiveReadTimeout); Assert.Equal(TimeSpan.FromSeconds(3), s.EffectiveWriteTimeout);

        await using var server = Server();
        server.Mute = true;
        await using var c = Client(server, timeoutMs: 5000, retries: 0, readMs: 150, writeMs: 700);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => c.ReadWordsAsync(A(PlcDevice.D, 0), 1, default));
        var read = sw.ElapsedMilliseconds;
        sw.Restart();
        await Assert.ThrowsAsync<PlcWriteAmbiguousException>(() => c.WriteWordsAsync(A(PlcDevice.D, 0), [1], default));
        var write = sw.ElapsedMilliseconds;
        Assert.InRange(read, 100, 1200);
        Assert.InRange(write, 600, 3000);
        Assert.True(write > read);
    }

    // ---------------------------------------------------------------------------------------------------------- concurrency
    [Fact]
    public async Task Mixed_concurrent_requests_on_one_socket_are_serialized_and_each_gets_its_own_answer()
    {
        await using var server = Server();
        for (var i = 0; i < 20; i++) { server.SetWord(PlcDevice.D, 100 + i, (ushort)(1000 + i)); server.SetWord(PlcDevice.W, 100 + i, (ushort)(2000 + i)); server.SetBit(PlcDevice.X, i, i % 3 == 0); }
        await using var c = Client(server, timeoutMs: 2000);
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            Assert.Equal((ushort)(1000 + i), (await c.ReadWordsAsync(A(PlcDevice.D, 100 + i), 1, default))[0]);
            Assert.Equal((ushort)(2000 + i), (await c.ReadWordsAsync(A(PlcDevice.W, 100 + i), 1, default))[0]);
            Assert.Equal(i % 3 == 0, (await c.ReadBitsAsync(A(PlcDevice.X, i), 1, default))[0]);
            await c.WriteBitsAsync(A(PlcDevice.Y, 200 + i), [true], default);
        })).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(0, server.PipelinedRequests);
        Assert.Equal(1, server.ConnectionCount);
        Assert.Equal(80, server.RequestCount);
        for (var i = 0; i < 20; i++) Assert.True(server.GetBit(PlcDevice.Y, 200 + i));
    }

    // ---------------------------------------------------------------------------------------------------------- diagnostics
    [Fact]
    public async Task The_last_exchange_records_the_request_and_response_hex_command_device_and_duration()
    {
        await using var server = Server();
        server.SetWord(100, 0x1234);
        await using var c = Client(server);
        await c.ReadWordsAsync(A(PlcDevice.D, 100), 1, default);
        var x = c.Diagnostics.LastExchange!;
        Assert.Equal("PLC-T", x.PlcId); Assert.Equal("READ", x.Operation); Assert.Equal("0x0401", x.Command); Assert.Equal("D", x.Device);
        Assert.Equal(100, x.StartAddress); Assert.Equal(1, x.Points); Assert.Equal(0, x.EndCode); Assert.Equal("OK", x.Outcome); Assert.False(x.IsAmbiguous);
        Assert.Equal("50 00 00 FF FF 03 00 0C 00 10 00 01 04 00 00 64 00 00 A8 01 00", x.RequestHex);
        Assert.Equal("D0 00 00 FF FF 03 00 04 00 00 00 34 12", x.ResponseHex);
        Assert.True(x.DurationMs >= 0 && (DateTimeOffset.UtcNow - x.At).TotalSeconds < 5);
        Assert.Null(c.Diagnostics.LastFailedExchange);

        await c.WriteBitsAsync(A(PlcDevice.Y, 16), [true, false, true], default);
        var w = c.Diagnostics.LastExchange!;
        Assert.Equal("WRITE", w.Operation); Assert.Equal("0x1401", w.Command); Assert.Equal("Y", w.Device); Assert.Equal(3, w.Points);
        Assert.Equal("50 00 00 FF FF 03 00 0E 00 10 00 01 14 01 00 10 00 00 9D 03 00 10 10", w.RequestHex);
        Assert.Equal("D0 00 00 FF FF 03 00 02 00 00 00", w.ResponseHex);
    }

    [Fact]
    public void Hex_dumps_are_capped_so_a_large_write_cannot_bloat_the_diagnostics()
    {
        var text = McTcpClient.Hex(new byte[200]);
        Assert.EndsWith("…(+104 bytes)", text);
        Assert.Equal(96 * 3 - 1 + " …(+104 bytes)".Length, text.Length);
        Assert.Equal("", McTcpClient.Hex([]));
        Assert.Equal("0A FF", McTcpClient.Hex([0x0A, 0xFF]));
    }
}

public class McCr003EndpointTests
{
    private static readonly McRoute Route = new(0x00, 0xFF, 0x03FF, 0x00);

    private static PlcEndpointConfig Config(int port, int? xy = null, int? w = null, int timeout = 300) =>
        new("PLC-T", "127.0.0.1", port, "McProtocol3EBinaryTcp", 0, 0xFF, 0x03FF, 0, 0x0010, timeout, 50, XyAddressBase: xy, WAddressBase: w);

    private static MelsecMcMockServer Server() { var s = new MelsecMcMockServer { ExpectedRoute = Route }; s.Start(); return s; }

    private static IoPointRef Pt(string signal, PlcDataType t, string? a, PlcSignalDirection d = PlcSignalDirection.PlcToSoftware, int? words = null) =>
        McEndpointTests.Pt(signal, t, a, d, words, words is null ? null : "ASCII", write: true);

    [Fact]
    public async Task X_and_Y_points_resolve_their_device_number_with_the_configured_base()
    {
        await using var server = Server();
        server.SetBit(PlcDevice.X, 8, true);       // octal X10
        server.SetBit(PlcDevice.X, 16, false);     // hex   X10
        server.SetBit(PlcDevice.Y, 16, true);
        server.SetWord(PlcDevice.W, 256, 77);      // hex W100
        var points = new[] { Pt("Station_Online", PlcDataType.Bool, "X10"), Pt("Request_Ack", PlcDataType.Bool, "Y20", PlcSignalDirection.SoftwareToPlc), Pt("Rack_Full", PlcDataType.Int, "W100") };

        await using (var oct = new McPlcEndpoint(Config(server.Port, xy: 8, w: 16)))
        {
            var r = await oct.ReadStationAsync(points, default);
            Assert.All(r, x => Assert.Equal(PointQuality.Good, x.Quality));
            Assert.Equal(new object[] { true, true, 77 }, r.Select(x => x.Value!.Value));          // X10 -> bit 8, Y20 -> bit 16, W100(hex) -> 256
        }
        await using (var hex = new McPlcEndpoint(Config(server.Port, xy: 16, w: 16)))
        {
            var r = await hex.ReadStationAsync([points[0]], default);
            Assert.Equal(false, r[0].Value!.Value);                                                 // the SAME text X10 now means bit 16
        }
    }

    [Fact]
    public async Task X_Y_and_W_points_stay_Bad_with_a_NEEDS_CONFIRMATION_message_when_their_base_is_not_configured_and_nothing_is_sent()
    {
        await using var server = Server();
        await using var ep = new McPlcEndpoint(Config(server.Port));
        var r = await ep.ReadStationAsync([Pt("Station_Online", PlcDataType.Bool, "X10"), Pt("Rack_Full", PlcDataType.Int, "W100")], default);
        Assert.All(r, x => { Assert.Equal(PointQuality.Bad, x.Quality); Assert.Contains("NEEDS_CONFIRMATION", x.Error); });
        Assert.Equal(0, server.RequestCount);
    }

    [Fact]
    public async Task Writes_go_to_Y_and_W_and_X_can_never_be_written_through_the_endpoint()
    {
        await using var server = Server();
        await using var ep = new McPlcEndpoint(Config(server.Port, xy: 8, w: 10));
        await ep.WritePointAsync(Pt("Request_Ack", PlcDataType.Bool, "Y10", PlcSignalDirection.SoftwareToPlc), PointValue.OfBool(true), default);
        await ep.WritePointAsync(Pt("Request_ID", PlcDataType.Int, "W5", PlcSignalDirection.SoftwareToPlc), PointValue.OfInt(-3), default);
        Assert.True(server.GetBit(PlcDevice.Y, 8)); Assert.Equal(unchecked((ushort)-3), server.GetWord(PlcDevice.W, 5));
        var before = server.RequestCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ep.WritePointAsync(Pt("Request_Ack", PlcDataType.Bool, "X10", PlcSignalDirection.SoftwareToPlc), PointValue.OfBool(true), default));
        Assert.Equal(before, server.RequestCount);
    }

    [Fact]
    public async Task An_entered_but_unapproved_address_stays_visible_to_the_Admin_but_causes_no_traffic_and_cannot_be_written()
    {
        await using var server = Server();
        await using var ep = new McPlcEndpoint(Config(server.Port));
        var unapproved = Pt("Pickup_Request", PlcDataType.Bool, "M100") with { Pollable = false };
        var ack = Pt("Request_Ack", PlcDataType.Bool, "M101", PlcSignalDirection.SoftwareToPlc) with { Pollable = false };
        var r = await ep.ReadStationAsync([unapproved, ack], default);
        Assert.All(r, x => { Assert.Equal(PointQuality.NotConfigured, x.Quality); Assert.Null(x.Value); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ep.WritePointAsync(ack, PointValue.OfBool(true), default));
        Assert.Equal(0, server.RequestCount); Assert.Equal(0, server.ConnectionCount);

        // the Admin view still shows the address the Admin entered; the Operator view never does
        var handler = new StationIoHandler("PLC-T", "A1", [new PointInfo(unapproved with { PlcId = "PLC-T" }, 1, IoConfigStatus.Configured)]);
        Assert.Equal("M100", handler.Snapshot(true).Points.Single().Address);
        Assert.Null(handler.Snapshot(false).Points.Single().Address);

        // and the write policy names the reason
        var options = new PlcRuntimeOptions { Mode = PlcRuntimeMode.RealReadWrite, AllowLiveConnection = true, Write = { Enabled = true, EngineerApprovalReference = "T-1", HandshakeApproved = true } };
        var denied = WritePolicy.Evaluate(options, ack with { WriteAllowed = true });
        Assert.False(denied.Allowed); Assert.Equal("ADDRESS_NOT_APPROVED", denied.Code);
        Assert.True(WritePolicy.Evaluate(options, ack with { WriteAllowed = true, Pollable = true }).Allowed);
    }

    [Fact]
    public async Task An_ambiguous_write_surfaces_from_the_endpoint_and_is_not_repeated()
    {
        await using var server = Server();
        server.ResponseFilter = (req, resp) => req[11] == 0x01 && req[12] == 0x14 ? null : resp;
        await using var ep = new McPlcEndpoint(Config(server.Port, timeout: 250));
        await ep.ConnectAsync(default);
        await Assert.ThrowsAsync<PlcWriteAmbiguousException>(() => ep.WritePointAsync(Pt("Request_Ack", PlcDataType.Bool, "M101", PlcSignalDirection.SoftwareToPlc), PointValue.OfBool(true), default));
        await Task.Delay(400);
        Assert.Equal(1, server.RequestCount);
        Assert.NotNull(ep.Diagnostics.LastFailedExchange); Assert.True(ep.Diagnostics.LastFailedExchange!.IsAmbiguous);
    }

    [Fact]
    public async Task Per_operation_timeouts_from_the_configuration_reach_the_client()
    {
        var cfg = Config(1) with { ConnectTimeoutMs = 111, ReadTimeoutMs = 222, WriteTimeoutMs = 333 };
        Assert.Equal(new AddressBases(null, null), Config(1).Bases);
        Assert.Equal(new AddressBases(8, 16), Config(1, 8, 16).Bases);
        await using var _ = new McPlcEndpoint(cfg);      // constructs without error: the optional timeouts are honoured by McClientSettings (see the socket tests)
    }

    // ---- worker level: the write queue reports ambiguity and never resends
    private sealed class OneStation
    {
        public static List<StationIoHandler> Build() =>
        [
            new StationIoHandler("PLC-T", "A1",
            [
                new PointInfo(new IoPointRef(Guid.NewGuid(), "PLC-T", "A1", "Pickup_Request", PlcSignalDirection.PlcToSoftware, PlcDataType.Bool, "M100", null, null, false, "Status"), 1, IoConfigStatus.Approved),
                new PointInfo(new IoPointRef(Guid.NewGuid(), "PLC-T", "A1", "Request_Ack", PlcSignalDirection.SoftwareToPlc, PlcDataType.Bool, "M101", null, null, true, "Status"), 2, IoConfigStatus.Approved),
            ]),
        ];
    }

    [Fact]
    public async Task The_worker_reports_an_ambiguous_write_logs_it_never_resends_it_and_keeps_polling()
    {
        await using var server = Server();
        server.SetBit(100, true);
        var events = new FakeEvents();
        var options = new PlcRuntimeOptions { ReconnectInitialDelayMs = 40, ReconnectMaxDelayMs = 160 };
        await using var w = new PlcWorker("PLC-T", "PLC-T", PlcRuntimeMode.RealReadOnly, options, Config(server.Port, timeout: 250), true, null, OneStation.Build(), new McTestFactory(), events, new SystemClock());
        w.Start();
        await WaitFor(() => w.Status(true).State == "Connected");
        server.ResponseFilter = (req, resp) => req[11] == 0x01 && req[12] == 0x14 ? null : resp;

        var result = await w.EnqueueWriteAsync(w.Stations[0].Find("Request_Ack")!, PointValue.OfBool(true)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success); Assert.True(result.IsAmbiguous); Assert.Equal("WRITE_AMBIGUOUS", result.Code);
        Assert.True(events.HasLog("WRITE_AMBIGUOUS"));
        server.ResponseFilter = null;
        await Task.Delay(700);
        Assert.Equal(1, server.ReceivedFrames.Count(f => f[11] == 0x01 && f[12] == 0x14));          // exactly one write frame ever reached the PLC
        Assert.True(await WaitFor(() => w.Status(true).State == "Connected"));                      // the worker reconnected and keeps polling
        Assert.True(await WaitFor(() => Equals(w.Stations[0].Snapshot(true).Points.Single(p => p.SignalName == "Pickup_Request").Value, true)));
    }

    private static async Task<bool> WaitFor(Func<bool> cond, int ms = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(15); }
        return cond();
    }
}
