using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using WCS.Application.Plc;
using WCS.Infrastructure.Plc.Mc;

namespace WCS.Infrastructure.Simulation;

/// <summary>
/// A TCP server that speaks the MC 3E binary subset the driver uses (batch read/write of M/X/Y bits and D/W words) over a real socket.
/// It exists so the driver's framing, timeouts and reconnect logic are exercised against real TCP, never against a stubbed class.
/// It is a protocol mock, not a model of a PLC program: memory is plain per-device arrays that tests read and write directly.
/// Fault injection: delayed/absent answers, forced end codes, dropped connections, and a response filter that can corrupt, shorten or
/// replace any answer (wrong subheader, wrong length, wrong routing echo, partial frame). Frames received are recorded for byte-level assertions.
/// </summary>
public sealed class MelsecMcMockServer : IAsyncDisposable
{
    public const ushort EndCodeOk = 0x0000;
    public const ushort EndCodePointsOutOfRange = 0xC051;
    public const ushort EndCodeDeviceOutOfRange = 0xC056;
    public const ushort EndCodeCommandUnsupported = 0xC059;
    public const ushort EndCodeLengthMismatch = 0xC061;

    private readonly object _mem = new();
    private readonly Dictionary<int, bool> _m = [], _x = [], _y = [];
    private readonly Dictionary<int, ushort> _d = [], _w = [];
    private Dictionary<int, bool> Bits(PlcDevice d) => d switch { PlcDevice.X => _x, PlcDevice.Y => _y, _ => _m };
    private Dictionary<int, ushort> Words(PlcDevice d) => d == PlcDevice.W ? _w : _d;
    private readonly ConcurrentQueue<byte[]> _frames = new();
    private readonly List<TcpClient> _clients = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _requests, _pipelined, _connections;
    private volatile int _forcedEndCodeCount; private volatile int _forcedEndCode;
    private volatile int _dropNext;

    /// <summary>Highest device number the mock accepts (requests beyond it get end code C056). Null = unlimited.</summary>
    public int? MaxDeviceNumber { get; set; }
    /// <summary>Expected routing (network, PC, module I/O, station). When set, other routes are ignored like a mismatching PLC would.</summary>
    public McRoute? ExpectedRoute { get; set; }
    /// <summary>Delay before every answer (simulates a slow PLC / timeouts).</summary>
    public TimeSpan ResponseDelay { get; set; }
    /// <summary>When true the server reads requests but never answers.</summary>
    public bool Mute { get; set; }
    /// <summary>
    /// Optional hook applied to every answer: (request, answer) -> bytes to send (null = send nothing). Used to corrupt a header field,
    /// announce a wrong length, echo a wrong route or send only part of the frame.
    /// </summary>
    public Func<byte[], byte[], byte[]?>? ResponseFilter { get; set; }
    /// <summary>Close the connection right after the (possibly partial) answer was written.</summary>
    public bool CloseAfterResponse { get; set; }

    public int Port { get; private set; }
    public int RequestCount => _requests;
    public int ConnectionCount => _connections;
    /// <summary>Requests that arrived while the previous one on the same connection was still unanswered (must stay 0: the client serializes).</summary>
    public int PipelinedRequests => _pipelined;
    public IReadOnlyCollection<byte[]> ReceivedFrames => _frames.ToArray();

    public void Start(int port = 0)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>Stops listening and drops every connection (a PLC power-off / cable pull).</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* stopping */ }
        lock (_clients) { foreach (var c in _clients) try { c.Dispose(); } catch { /* closing */ } _clients.Clear(); }
    }

    public void ForceEndCode(ushort endCode, int times) { _forcedEndCode = endCode; _forcedEndCodeCount = times; }
    /// <summary>Closes the next <paramref name="count"/> connections right after the next request was read (no answer).</summary>
    public void DropNextRequests(int count) => _dropNext = count;

    // ------------------------------------------------------------------------------------------------------------ memory access
    public void SetBit(int number, bool value) { lock (_mem) _m[number] = value; }
    public bool GetBit(int number) { lock (_mem) return _m.GetValueOrDefault(number); }
    public void SetWord(int number, ushort value) { lock (_mem) _d[number] = value; }
    public ushort GetWord(int number) { lock (_mem) return _d.GetValueOrDefault(number); }
    /// <summary>Device-aware accessors (M, X, Y bits; D, W words). Numbers are the decimal values carried in the frame.</summary>
    public void SetBit(PlcDevice device, int number, bool value) { lock (_mem) Bits(device)[number] = value; }
    public bool GetBit(PlcDevice device, int number) { lock (_mem) return Bits(device).GetValueOrDefault(number); }
    public void SetWord(PlcDevice device, int number, ushort value) { lock (_mem) Words(device)[number] = value; }
    public ushort GetWord(PlcDevice device, int number) { lock (_mem) return Words(device).GetValueOrDefault(number); }
    public void SetAscii(int number, int words, string text)
    {
        var buf = new byte[words * 2]; System.Text.Encoding.ASCII.GetBytes(text).CopyTo(buf, 0);
        for (var i = 0; i < words; i++) SetWord(number + i, (ushort)(buf[i * 2] | (buf[i * 2 + 1] << 8)));
    }

    // ------------------------------------------------------------------------------------------------------------ server loop
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch { return; }
            Interlocked.Increment(ref _connections);
            lock (_clients) _clients.Add(client);
            _ = Task.Run(() => HandleAsync(client, ct));
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        try
        {
            var stream = client.GetStream();
            var header = new byte[9];
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(stream, header, ct);
                var len = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7));
                var frame = new byte[9 + len];
                header.CopyTo(frame, 0);
                await ReadExactAsync(stream, frame.AsMemory(9), ct);
                _frames.Enqueue(frame);
                Interlocked.Increment(ref _requests);
                if (client.Available > 0) Interlocked.Increment(ref _pipelined);

                if (_dropNext > 0) { Interlocked.Decrement(ref _dropNext); client.Dispose(); return; }
                if (Mute) continue;
                if (ResponseDelay > TimeSpan.Zero) await Task.Delay(ResponseDelay, ct);

                var response = Process(frame);
                if (response is null) continue;
                if (ResponseFilter is { } filter) { response = filter(frame, response); if (response is null) continue; }
                await stream.WriteAsync(response, ct);
                if (CloseAfterResponse) { client.Dispose(); return; }
            }
        }
        catch { /* connection closed / server stopped */ }
        finally { client.Dispose(); lock (_clients) _clients.Remove(client); }
    }

    private byte[]? Process(byte[] frame)
    {
        McRequest req;
        try { req = McFrame.ParseRequest(frame); }
        catch (McProtocolException) { return McFrame.BuildResponse(new McRoute(frame[2], frame[3], BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)), frame[6]), EndCodeLengthMismatch, []); }
        if (ExpectedRoute is { } er && er != req.Route) return null;       // addressed to another PLC: no answer

        if (_forcedEndCodeCount > 0) { _forcedEndCodeCount--; return McFrame.BuildResponse(req.Route, (ushort)_forcedEndCode, new byte[9]); }
        var bit = req.Subcommand == McFrame.SubcommandBit;
        if (req.Command is not (McFrame.CommandBatchRead or McFrame.CommandBatchWrite) || (req.Subcommand is not (McFrame.SubcommandBit or McFrame.SubcommandWord)))
            return McFrame.BuildResponse(req.Route, EndCodeCommandUnsupported, new byte[9]);
        if (req.Points < 1 || req.Points > (!bit ? McFrame.MaxWordPoints : req.Command == McFrame.CommandBatchRead ? McFrame.MaxReadBitPoints : McFrame.MaxWriteBitPoints))
            return McFrame.BuildResponse(req.Route, EndCodePointsOutOfRange, new byte[9]);
        if (MaxDeviceNumber is { } max && req.Head.Number + req.Points - 1 > max)
            return McFrame.BuildResponse(req.Route, EndCodeDeviceOutOfRange, new byte[9]);
        if (bit != req.Head.IsBitDevice)       // M/X/Y are bit devices, D/W are word devices
            return McFrame.BuildResponse(req.Route, EndCodeCommandUnsupported, new byte[9]);
        if (req.Command == McFrame.CommandBatchWrite && req.Head.Device == PlcDevice.X)    // input relays are read-only
            return McFrame.BuildResponse(req.Route, EndCodeCommandUnsupported, new byte[9]);

        lock (_mem)
        {
            if (req.Command == McFrame.CommandBatchRead)
            {
                if (bit) { var bm = Bits(req.Head.Device); return McFrame.BuildResponse(req.Route, EndCodeOk, McFrame.EncodeBits(Enumerable.Range(req.Head.Number, req.Points).Select(n => bm.GetValueOrDefault(n)).ToList())); }
                var wm = Words(req.Head.Device);
                return McFrame.BuildResponse(req.Route, EndCodeOk, McFrame.EncodeWords(Enumerable.Range(req.Head.Number, req.Points).Select(n => wm.GetValueOrDefault(n)).ToList()));
            }
            if (bit)
            {
                if (req.Data.Length != (req.Points + 1) / 2) return McFrame.BuildResponse(req.Route, EndCodeLengthMismatch, new byte[9]);
                var bits = McFrame.DecodeBits(req.Data, req.Points);
                var bw = Bits(req.Head.Device);
                for (var i = 0; i < bits.Length; i++) bw[req.Head.Number + i] = bits[i];
            }
            else
            {
                if (req.Data.Length != req.Points * 2) return McFrame.BuildResponse(req.Route, EndCodeLengthMismatch, new byte[9]);
                var words = McFrame.DecodeWords(req.Data, req.Points);
                var ww = Words(req.Head.Device);
                for (var i = 0; i < words.Length; i++) ww[req.Head.Number + i] = words[i];
            }
            return McFrame.BuildResponse(req.Route, EndCodeOk, []);
        }
    }

    private static async Task ReadExactAsync(NetworkStream s, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer[read..], ct);
            if (n == 0) throw new IOException("closed");
            read += n;
        }
    }

    public ValueTask DisposeAsync() { Stop(); return ValueTask.CompletedTask; }
}
