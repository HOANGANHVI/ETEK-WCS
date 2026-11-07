using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Plc.Mc;
using Ref = WCS.Tests.Plc.ReferenceOracle;

namespace WCS.Tests.Plc;

/// <summary>
/// CR-003: proves that the WCS MC 3E codec is equivalent to the reference library that worked on real PLCs. The reference code is ported
/// verbatim into <see cref="Ref"/> (test-only); every comparison below is byte-for-byte or decision-for-decision (accept / PLC error / reject).
/// </summary>
public class McReferenceParityTests
{
    // The reference hard-codes module I/O 0x03FF and station 0x00; network and PC are parameters.
    private static McRoute R(byte net = 0x00, byte pc = 0xFF) => new(net, pc, 0x03FF, 0x00);

    private static readonly (PlcDevice Wcs, Ref.McDeviceType Ref)[] WordDevices = [(PlcDevice.D, Ref.McDeviceType.D), (PlcDevice.W, Ref.McDeviceType.W)];
    private static readonly (PlcDevice Wcs, Ref.McDeviceType Ref)[] BitDevices = [(PlcDevice.M, Ref.McDeviceType.M), (PlcDevice.X, Ref.McDeviceType.X), (PlcDevice.Y, Ref.McDeviceType.Y)];
    private static readonly (byte Net, byte Pc)[] Routes = [(0x00, 0xFF), (0x01, 0x02), (0xFF, 0x00)];
    private static readonly int[] Heads = [0, 1, 8, 16, 100, 255, 256, 1500, 65535, 65536, 0x7FFFFF];
    private static readonly ushort[] Timers = [1, 10, 12, 0x0100];

    private static string Hex(byte[] b) => Convert.ToHexString(b);

    // ------------------------------------------------------------------------------------------------------------------ requests
    [Fact]
    public void Word_read_requests_are_identical_to_the_reference_for_D_and_W()
    {
        var checkedFrames = 0;
        foreach (var (wcs, rf) in WordDevices)
            foreach (var (net, pc) in Routes) foreach (var head in Heads) foreach (var timer in Timers) foreach (ushort count in new ushort[] { 1, 2, 5, 960 })
            {
                var expected = Ref.McFrameBuilder.BuildWordReadRequest(net, pc, rf, head, count, timer);
                var actual = McFrame.BuildBatchRead(R(net, pc), timer, new DeviceAddress(wcs, head), count, bitUnits: false);
                Assert.True(expected.SequenceEqual(actual), $"{wcs}{head} x{count} timer {timer} net {net} pc {pc}:\n ref {Hex(expected)}\n wcs {Hex(actual)}");
                checkedFrames++;
            }
        Assert.Equal(2 * 3 * 11 * 4 * 4, checkedFrames);
    }

    [Fact]
    public void Bit_read_requests_are_identical_to_the_reference_for_M_X_and_Y()
    {
        foreach (var (wcs, rf) in BitDevices)
            foreach (var (net, pc) in Routes) foreach (var head in Heads) foreach (var timer in Timers) foreach (ushort count in new ushort[] { 1, 2, 3, 15, 16, 17, 3584 })
            {
                var expected = Ref.McFrameBuilder.BuildBitReadRequest(net, pc, rf, head, count, timer);
                var actual = McFrame.BuildBatchRead(R(net, pc), timer, new DeviceAddress(wcs, head), count, bitUnits: true);
                Assert.True(expected.SequenceEqual(actual), $"{wcs}{head} x{count}:\n ref {Hex(expected)}\n wcs {Hex(actual)}");
            }
    }

    [Fact]
    public void Word_write_requests_are_identical_to_the_reference_including_little_endian_data()
    {
        var rnd = new Random(20261107);
        foreach (var (wcs, rf) in WordDevices)
            foreach (var count in new[] { 1, 2, 7, 64, 960 })
            {
                var values = Enumerable.Range(0, count).Select(_ => (ushort)rnd.Next(0, 65536)).ToArray();
                values[0] = 0x1234;                                       // a value whose two bytes differ: proves low byte first
                var expected = Ref.McFrameBuilder.BuildWordWriteRequest(0x00, 0xFF, rf, 200, values, 10);
                var actual = McFrame.BuildBatchWriteWords(R(), 10, new DeviceAddress(wcs, 200), values);
                Assert.True(expected.SequenceEqual(actual), $"{wcs} x{count}");
                Assert.Equal(0x34, actual[21]); Assert.Equal(0x12, actual[22]);
            }
    }

    [Fact]
    public void Bit_write_requests_are_identical_to_the_reference_for_every_count_and_pattern()
    {
        var rnd = new Random(7);
        foreach (var (wcs, rf) in new[] { (PlcDevice.M, Ref.McDeviceType.M), (PlcDevice.Y, Ref.McDeviceType.Y) })
            foreach (var count in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 1440 })
            {
                var patterns = new List<bool[]>
                {
                    Enumerable.Repeat(true, count).ToArray(), new bool[count],
                    Enumerable.Range(0, count).Select(i => i % 2 == 0).ToArray(), Enumerable.Range(0, count).Select(i => i % 2 == 1).ToArray(),
                    Enumerable.Range(0, count).Select(_ => rnd.Next(2) == 1).ToArray(),
                };
                foreach (var bits in patterns)
                {
                    var expected = Ref.McFrameBuilder.BuildBitWriteRequest(0x00, 0xFF, rf, 50, bits, 10);
                    var actual = McFrame.BuildBatchWriteBits(R(), 10, new DeviceAddress(wcs, 50), bits);
                    Assert.True(expected.SequenceEqual(actual), $"{wcs} x{count}");
                }
            }
    }

    [Fact]
    public void Writing_to_X_input_relays_is_refused_without_building_a_frame_exactly_like_the_reference()
    {
        Assert.Throws<InvalidOperationException>(() => Ref.McFrameBuilder.BuildBitWriteRequest(0, 0xFF, Ref.McDeviceType.X, 0, [true], 10));
        Assert.Throws<InvalidOperationException>(() => McFrame.BuildBatchWriteBits(R(), 10, new DeviceAddress(PlcDevice.X, 0), [true]));
    }

    [Fact]
    public void A_device_in_the_wrong_units_is_refused_like_the_reference()
    {
        Assert.Throws<ArgumentException>(() => Ref.McFrameBuilder.BuildWordReadRequest(0, 0xFF, Ref.McDeviceType.M, 0, 1, 10));
        Assert.Throws<ArgumentException>(() => McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.M, 0), 1, bitUnits: false));
        Assert.Throws<ArgumentException>(() => Ref.McFrameBuilder.BuildBitReadRequest(0, 0xFF, Ref.McDeviceType.D, 0, 1, 10));
        Assert.Throws<ArgumentException>(() => McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.D, 0), 1, bitUnits: true));
        Assert.Throws<ArgumentException>(() => McFrame.BuildBatchWriteWords(R(), 10, new DeviceAddress(PlcDevice.M, 0), [1]));
    }

    [Fact]
    public void The_reference_documentation_sample_request_is_reproduced_exactly()
    {
        // HUONG_DAN_SU_DUNG.md 7.3: ReadWordsAsync(D, 100, 5) with a 3000 ms timeout (12 x 250 ms) on the default route.
        var frame = McFrame.BuildBatchRead(R(), 12, new DeviceAddress(PlcDevice.D, 100), 5, bitUnits: false);
        Assert.Equal("50 00 00 FF FF 03 00 0C 00 0C 00 01 04 00 00 64 00 00 A8 05 00", McTcpClient.Hex(frame));
    }

    [Fact]
    public void Device_codes_follow_the_reference_table()
    {
        Assert.Equal(new byte[] { 0xA8, 0xB4, 0x90, 0x9C, 0x9D }, new[] { PlcDevice.D, PlcDevice.W, PlcDevice.M, PlcDevice.X, PlcDevice.Y }.Select(McFrame.DeviceCode));
        Assert.Equal(Ref.McFrameBuilder.DevD, McFrame.DeviceCode(PlcDevice.D));
        Assert.Equal(Ref.McFrameBuilder.DevW, McFrame.DeviceCode(PlcDevice.W));
        Assert.Equal(Ref.McFrameBuilder.DevM, McFrame.DeviceCode(PlcDevice.M));
        Assert.Equal(Ref.McFrameBuilder.DevX, McFrame.DeviceCode(PlcDevice.X));
        Assert.Equal(Ref.McFrameBuilder.DevY, McFrame.DeviceCode(PlcDevice.Y));
    }

    // ------------------------------------------------------------------------------------------------------------------ nibbles
    [Theory]
    [InlineData(new byte[] { 0x00 }, 2, new[] { false, false })]
    [InlineData(new byte[] { 0x01 }, 2, new[] { false, true })]       // second point = LOW nibble
    [InlineData(new byte[] { 0x10 }, 2, new[] { true, false })]       // first point = HIGH nibble
    [InlineData(new byte[] { 0x11 }, 2, new[] { true, true })]
    [InlineData(new byte[] { 0x10 }, 1, new[] { true })]              // odd count: the unused low nibble is padding
    [InlineData(new byte[] { 0x11, 0x10 }, 3, new[] { true, true, true })]
    [InlineData(new byte[] { 0x01, 0x10, 0x00 }, 5, new[] { false, true, true, false, false })]
    public void Bit_units_use_one_nibble_per_point_with_the_first_point_in_the_high_nibble(byte[] data, int points, bool[] expected)
    {
        Assert.Equal(expected, McFrame.DecodeBits(data, points));
        Assert.Equal(data, McFrame.EncodeBits(expected));
    }

    [Fact]
    public void Bit_decoding_matches_the_reference_parser_for_odd_and_even_counts_over_many_bytes()
    {
        var rnd = new Random(99);
        foreach (var count in new[] { 1, 2, 3, 16, 17, 33, 255, 256, 3584 })
        {
            var bits = Enumerable.Range(0, count).Select(_ => rnd.Next(2) == 1).ToArray();
            var data = McFrame.EncodeBits(bits);
            var response = McFrame.BuildResponse(R(), 0, data);
            var reference = Ref.McResponseParser.ParseBitResponse(response, 0x00, 0xFF, (ushort)count);
            Assert.True(reference.Success, reference.ErrorMessage);
            Assert.Equal(reference.BitValues, McFrame.DecodeBits(data, count));
            Assert.Equal(bits, reference.BitValues);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ responses
    private enum Outcome { Ok, EndCode, Rejected }

    /// <summary>The WCS decision for a response to a word read of <paramref name="count"/> points.</summary>
    private static (Outcome, object?) WcsWordRead(byte[] request, byte[] response, int count)
    {
        try
        {
            var r = McFrame.ParseAndValidateResponse(request, response);
            return r.EndCode != 0 ? (Outcome.EndCode, r.EndCode) : (Outcome.Ok, McFrame.DecodeWords(r.Data, count));
        }
        catch (McProtocolException) { return (Outcome.Rejected, null); }
    }

    private static (Outcome, object?) RefWordRead(byte[] response, int count)
    {
        var r = Ref.McResponseParser.ParseWordResponse(response, 0x00, 0xFF, (ushort)count);
        return r.Success ? (Outcome.Ok, r.WordValues) : r.EndCode != 0 ? (Outcome.EndCode, r.EndCode) : (Outcome.Rejected, null);
    }

    private static byte[] Mutate(byte[] f, Action<byte[]> change) { var c = (byte[])f.Clone(); change(c); return c; }

    [Fact]
    public void Word_read_responses_are_accepted_or_rejected_exactly_like_the_reference()
    {
        const int count = 3;
        var request = McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.D, 10), count, bitUnits: false);
        var good = McFrame.BuildResponse(R(), 0, McFrame.EncodeWords([0x0001, 0xFFFE, 0x1234]));
        var cases = new Dictionary<string, byte[]>
        {
            ["good"] = good,
            ["end code C05B with the 9-byte error block"] = McFrame.BuildResponse(R(), 0xC05B, new byte[9]),
            ["end code 0055"] = McFrame.BuildResponse(R(), 0x0055, new byte[9]),
            ["wrong subheader"] = Mutate(good, f => f[0] = 0xD1),
            ["request subheader echoed"] = Mutate(good, f => f[0] = 0x50),
            ["second subheader byte"] = Mutate(good, f => f[1] = 0x01),
            ["wrong network"] = Mutate(good, f => f[2] = 0x07),
            ["wrong PC"] = Mutate(good, f => f[3] = 0x07),
            ["wrong module I/O low"] = Mutate(good, f => f[4] = 0xFE),
            ["wrong module I/O high"] = Mutate(good, f => f[5] = 0x02),
            ["wrong station"] = Mutate(good, f => f[6] = 0x01),
            ["announced length 2 too small"] = Mutate(good, f => f[7] -= 2),
            ["announced length 2 too large"] = Mutate(good, f => f[7] += 2),
            ["one byte truncated"] = good[..^1],
            ["one byte appended"] = [.. good, 0x00],
            ["only the header"] = good[..9],
            ["shorter than the fixed header"] = good[..8],
            ["empty"] = [],
        };
        foreach (var (name, response) in cases)
        {
            var expected = RefWordRead(response, count);
            var actual = WcsWordRead(request, response, count);
            Assert.True(expected.Item1 == actual.Item1, $"{name}: reference {expected.Item1}, WCS {actual.Item1}");
            if (expected.Item1 == Outcome.Ok) Assert.Equal((ushort[])expected.Item2!, (ushort[])actual.Item2!);
            if (expected.Item1 == Outcome.EndCode) Assert.Equal(expected.Item2, actual.Item2);
        }
        Assert.Equal(Outcome.Ok, WcsWordRead(request, good, count).Item1);
    }

    [Fact]
    public void Bit_read_and_write_responses_are_accepted_or_rejected_exactly_like_the_reference()
    {
        const int count = 5;
        var readReq = McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.M, 0), count, bitUnits: true);
        var readOk = McFrame.BuildResponse(R(), 0, McFrame.EncodeBits([true, false, true, true, false]));
        var writeReq = McFrame.BuildBatchWriteBits(R(), 10, new DeviceAddress(PlcDevice.Y, 0), [true, false]);
        var writeOk = McFrame.BuildResponse(R(), 0, []);

        foreach (var (name, resp) in new Dictionary<string, byte[]>
        {
            ["good"] = readOk, ["end code"] = McFrame.BuildResponse(R(), 0xC056, new byte[9]), ["wrong net"] = Mutate(readOk, f => f[2] = 9),
            ["short data"] = McFrame.BuildResponse(R(), 0, new byte[2]), ["long data"] = McFrame.BuildResponse(R(), 0, new byte[4]),
            ["bad subheader"] = Mutate(readOk, f => f[0] = 0), ["truncated"] = readOk[..^1],
        })
        {
            var rf = Ref.McResponseParser.ParseBitResponse(resp, 0, 0xFF, count);
            var exp = rf.Success ? Outcome.Ok : rf.EndCode != 0 ? Outcome.EndCode : Outcome.Rejected;
            Outcome act;
            try { var r = McFrame.ParseAndValidateResponse(readReq, resp); act = r.EndCode != 0 ? Outcome.EndCode : Outcome.Ok; if (r.EndCode == 0) McFrame.DecodeBits(r.Data, count); }
            catch (McProtocolException) { act = Outcome.Rejected; }
            Assert.True(exp == act, $"read {name}: reference {exp}, WCS {act}");
        }

        foreach (var (name, resp) in new Dictionary<string, byte[]>
        {
            ["good"] = writeOk, ["end code"] = McFrame.BuildResponse(R(), 0xC059, new byte[9]), ["wrong pc"] = Mutate(writeOk, f => f[3] = 1),
            ["wrong station"] = Mutate(writeOk, f => f[6] = 5), ["extra data"] = McFrame.BuildResponse(R(), 0, new byte[2]), ["truncated"] = writeOk[..^1],
        })
        {
            var rf = Ref.McResponseParser.ParseWriteResponse(resp, 0, 0xFF);
            var exp = rf.Success ? Outcome.Ok : rf.EndCode != 0 ? Outcome.EndCode : Outcome.Rejected;
            Outcome act;
            try { act = McFrame.ParseAndValidateResponse(writeReq, resp).EndCode != 0 ? Outcome.EndCode : Outcome.Ok; }
            catch (McProtocolException) { act = Outcome.Rejected; }
            Assert.True(exp == act, $"write {name}: reference {exp}, WCS {act}");
        }
    }

    [Fact]
    public void Where_the_reference_is_weaker_the_WCS_driver_is_stricter_and_the_difference_is_documented()
    {
        // 1. Module I/O and station are configurable in WCS (the reference hard-codes 03FF/00). The echo is compared with what we SENT.
        var route = new McRoute(0x00, 0xFF, 0x0001, 0x05);
        var request = McFrame.BuildBatchRead(route, 10, new DeviceAddress(PlcDevice.D, 0), 1, bitUnits: false);
        Assert.Equal(0, McFrame.ParseAndValidateResponse(request, McFrame.BuildResponse(route, 0, new byte[2])).EndCode);
        Assert.Throws<McProtocolException>(() => McFrame.ParseAndValidateResponse(request, McFrame.BuildResponse(R(), 0, new byte[2])));      // answer from another module
        // 2. A hostile / corrupted length is rejected before anything is allocated or read (the reference would try to read 65 533 bytes).
        var huge = McFrame.BuildResponse(R(), 0, new byte[2]); huge[7] = 0xFF; huge[8] = 0xFF;
        Assert.Throws<McProtocolException>(() => McFrame.RemainingLength(huge));
        // 3. The reference accepts requests of any point count in the builder; WCS enforces the proven protocol limits in the codec itself.
        Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.D, 0), 961, bitUnits: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.M, 0), 3585, bitUnits: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildBatchWriteBits(R(), 10, new DeviceAddress(PlcDevice.M, 0), new bool[1441]));
        Assert.Throws<ArgumentOutOfRangeException>(() => McFrame.BuildBatchRead(R(), 10, new DeviceAddress(PlcDevice.D, 0), 0, bitUnits: false));
    }
}
