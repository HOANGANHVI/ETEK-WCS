using System.Buffers.Binary;
using WCS.Application.Plc;

namespace WCS.Infrastructure.Plc.Mc;

/// <summary>Routing fields of an MC 3E frame. Every value comes from the confirmed PLC configuration; none is defaulted.</summary>
public readonly record struct McRoute(byte Network, byte Pc, ushort ModuleIo, byte Station);

public sealed record McResponse(McRoute Route, ushort EndCode, byte[] Data);

public sealed record McRequest(McRoute Route, ushort MonitoringTimer, ushort Command, ushort Subcommand, DeviceAddress Head, int Points, byte[] Data);

public class McProtocolException(string message) : Exception(message);

/// <summary>The PLC answered with a non-zero end code (the link itself is healthy).</summary>
public sealed class McEndCodeException(ushort endCode) : McProtocolException($"PLC end code 0x{endCode:X4}")
{
    public ushort EndCode { get; } = endCode;
}

/// <summary>
/// MELSEC MC Protocol, 3E frame, binary code, Q/L series (batch read/write 0401/1401, sub-command 0000 = word units, 0001 = bit units,
/// devices M and D). Pure functions: no I/O, fully unit-testable. iQ-R sub-commands are not used.
/// </summary>
public static class McFrame
{
    public const ushort CommandBatchRead = 0x0401;
    public const ushort CommandBatchWrite = 0x1401;
    public const ushort SubcommandWord = 0x0000;
    public const ushort SubcommandBit = 0x0001;
    public const int MaxWordPoints = 960;
    public const int MaxBitPoints = 7168;

    private const byte RequestSubheader0 = 0x50, ResponseSubheader0 = 0xD0;
    /// <summary>Bytes of a response before the data: subheader(2) network(1) pc(1) io(2) station(1) length(2).</summary>
    public const int ResponseHeaderLength = 9;

    public static byte DeviceCode(PlcDevice d) => d switch { PlcDevice.M => 0x90, PlcDevice.D => 0xA8, _ => throw new ArgumentOutOfRangeException(nameof(d)) };
    public static PlcDevice DeviceFromCode(byte code) => code switch { 0x90 => PlcDevice.M, 0xA8 => PlcDevice.D, _ => throw new McProtocolException($"unsupported device code 0x{code:X2}") };

    // ------------------------------------------------------------------------------------------------------------------ requests
    public static byte[] BuildBatchRead(McRoute route, ushort monitoringTimer, DeviceAddress head, int points, bool bitUnits)
    {
        ValidatePoints(points, bitUnits);
        return Build(route, monitoringTimer, CommandBatchRead, bitUnits ? SubcommandBit : SubcommandWord, head, points, []);
    }

    public static byte[] BuildBatchWriteWords(McRoute route, ushort monitoringTimer, DeviceAddress head, IReadOnlyList<ushort> values)
    {
        ValidatePoints(values.Count, bitUnits: false);
        var data = new byte[values.Count * 2];
        for (var i = 0; i < values.Count; i++) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), values[i]);
        return Build(route, monitoringTimer, CommandBatchWrite, SubcommandWord, head, values.Count, data);
    }

    public static byte[] BuildBatchWriteBits(McRoute route, ushort monitoringTimer, DeviceAddress head, IReadOnlyList<bool> values)
    {
        ValidatePoints(values.Count, bitUnits: true);
        return Build(route, monitoringTimer, CommandBatchWrite, SubcommandBit, head, values.Count, EncodeBits(values));
    }

    private static void ValidatePoints(int points, bool bitUnits)
    {
        var max = bitUnits ? MaxBitPoints : MaxWordPoints;
        if (points < 1 || points > max) throw new ArgumentOutOfRangeException(nameof(points), $"points must be 1..{max}");
    }

    private static byte[] Build(McRoute r, ushort timer, ushort command, ushort sub, DeviceAddress head, int points, byte[] data)
    {
        // request data length = monitoring timer(2) + command(2) + subcommand(2) + head device(3) + device code(1) + points(2) + data
        var dataLength = 12 + data.Length;
        var f = new byte[9 + dataLength];
        f[0] = RequestSubheader0; f[1] = 0x00;
        f[2] = r.Network; f[3] = r.Pc;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(4), r.ModuleIo);
        f[6] = r.Station;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(7), (ushort)dataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(9), timer);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(11), command);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(13), sub);
        f[15] = (byte)(head.Number & 0xFF); f[16] = (byte)((head.Number >> 8) & 0xFF); f[17] = (byte)((head.Number >> 16) & 0xFF);
        f[18] = DeviceCode(head.Device);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(19), (ushort)points);
        data.CopyTo(f, 21);
        return f;
    }

    /// <summary>Server side (mock) and test helper: parses a complete request frame.</summary>
    public static McRequest ParseRequest(ReadOnlySpan<byte> f)
    {
        if (f.Length < 21 || f[0] != RequestSubheader0 || f[1] != 0x00) throw new McProtocolException("not a 3E binary request");
        var len = BinaryPrimitives.ReadUInt16LittleEndian(f[7..]);
        if (f.Length != 9 + len) throw new McProtocolException("request length mismatch");
        var route = new McRoute(f[2], f[3], BinaryPrimitives.ReadUInt16LittleEndian(f[4..]), f[6]);
        var head = new DeviceAddress(DeviceFromCode(f[18]), f[15] | (f[16] << 8) | (f[17] << 16));
        return new McRequest(route, BinaryPrimitives.ReadUInt16LittleEndian(f[9..]), BinaryPrimitives.ReadUInt16LittleEndian(f[11..]),
            BinaryPrimitives.ReadUInt16LittleEndian(f[13..]), head, BinaryPrimitives.ReadUInt16LittleEndian(f[19..]), f[21..].ToArray());
    }

    // ------------------------------------------------------------------------------------------------------------------ responses
    /// <summary>Length of the remaining response bytes (end code + data) announced by the 9-byte response header.</summary>
    public static int RemainingLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < ResponseHeaderLength) throw new McProtocolException("short response header");
        if (header[0] != ResponseSubheader0 || header[1] != 0x00) throw new McProtocolException($"unexpected subheader {header[0]:X2}{header[1]:X2}");
        var len = BinaryPrimitives.ReadUInt16LittleEndian(header[7..]);
        if (len < 2) throw new McProtocolException("response length too small");
        return len;
    }

    /// <summary>Parses a complete response frame (header + end code + data).</summary>
    public static McResponse ParseResponse(ReadOnlySpan<byte> f)
    {
        var remaining = RemainingLength(f);
        if (f.Length != ResponseHeaderLength + remaining) throw new McProtocolException("response length mismatch");
        var route = new McRoute(f[2], f[3], BinaryPrimitives.ReadUInt16LittleEndian(f[4..]), f[6]);
        return new McResponse(route, BinaryPrimitives.ReadUInt16LittleEndian(f[9..]), f[11..].ToArray());
    }

    /// <summary>Builds a response frame (used by the mock server). Error responses carry the 9-byte error information block.</summary>
    public static byte[] BuildResponse(McRoute route, ushort endCode, ReadOnlySpan<byte> data)
    {
        var f = new byte[ResponseHeaderLength + 2 + data.Length];
        f[0] = ResponseSubheader0; f[1] = 0x00; f[2] = route.Network; f[3] = route.Pc;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(4), route.ModuleIo); f[6] = route.Station;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(7), (ushort)(2 + data.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(9), endCode);
        data.CopyTo(f.AsSpan(11));
        return f;
    }

    // ------------------------------------------------------------------------------------------------------------------ data
    /// <summary>Bit units: one nibble per point, the first point in the HIGH nibble of the first byte.</summary>
    public static byte[] EncodeBits(IReadOnlyList<bool> bits)
    {
        var b = new byte[(bits.Count + 1) / 2];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) b[i / 2] |= (byte)(i % 2 == 0 ? 0x10 : 0x01);
        return b;
    }

    public static bool[] DecodeBits(ReadOnlySpan<byte> data, int points)
    {
        if (data.Length != (points + 1) / 2) throw new McProtocolException($"bit data length {data.Length} does not match {points} points");
        var r = new bool[points];
        for (var i = 0; i < points; i++) r[i] = ((data[i / 2] >> (i % 2 == 0 ? 4 : 0)) & 0x0F) != 0;
        return r;
    }

    public static ushort[] DecodeWords(ReadOnlySpan<byte> data, int points)
    {
        if (data.Length != points * 2) throw new McProtocolException($"word data length {data.Length} does not match {points} points");
        var r = new ushort[points];
        for (var i = 0; i < points; i++) r[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
        return r;
    }

    public static byte[] EncodeWords(IReadOnlyList<ushort> words)
    {
        var d = new byte[words.Count * 2];
        for (var i = 0; i < words.Count; i++) BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(i * 2), words[i]);
        return d;
    }
}
