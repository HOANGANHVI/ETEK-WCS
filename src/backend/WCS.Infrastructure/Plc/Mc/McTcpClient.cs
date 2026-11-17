using System.Buffers.Binary;
using System.Net.Sockets;
using WCS.Application.Plc;

namespace WCS.Infrastructure.Plc.Mc;

/// <summary>
/// Connection settings of one PLC, all taken from the confirmed configuration. Retry values are technical (not PLC parameters).
/// <see cref="ConnectTimeout"/>, <see cref="ReadTimeout"/> and <see cref="WriteTimeout"/> are optional per-operation overrides of <see cref="Timeout"/>.
/// </summary>
public sealed record McClientSettings(string Host, int Port, McRoute Route, ushort MonitoringTimer, TimeSpan Timeout, int MaxReadRetries = 1, TimeSpan? RetryDelay = null,
    TimeSpan? ConnectTimeout = null, TimeSpan? ReadTimeout = null, TimeSpan? WriteTimeout = null, string PlcId = "")
{
    public TimeSpan EffectiveConnectTimeout => ConnectTimeout ?? Timeout;
    public TimeSpan EffectiveReadTimeout => ReadTimeout ?? Timeout;
    public TimeSpan EffectiveWriteTimeout => WriteTimeout ?? Timeout;
}

/// <summary>
/// TCP client for ONE PLC (MC 3E binary). All exchanges are serialized by a gate, so two commands can never overlap on the socket.
/// Timeouts, connection loss and framing errors close the socket (a late answer must never be matched to the next request) and are
/// reported as transport faults; a PLC end code is reported as <see cref="McEndCodeException"/> and keeps the connection.
/// Reads are retried (idempotent); writes are never retried automatically, and a write whose request may have reached the PLC without a
/// confirming response raises <see cref="PlcWriteAmbiguousException"/>.
/// </summary>
public sealed class McTcpClient(McClientSettings settings) : IAsyncDisposable
{
    private const int HexLimitBytes = 96;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly object _diag = new();
    private long _requests, _failures, _timeouts, _reconnects, _bytesSent, _bytesReceived;
    private int? _lastEndCode; private string? _lastError; private long? _lastLatency; private DateTimeOffset? _lastOk, _lastErrAt;
    private PlcExchange? _lastExchange, _lastFailedExchange;

    public McClientSettings Settings { get; } = settings;
    public EndpointState State { get; private set; } = EndpointState.Disconnected;

    public PlcDiagnostics Diagnostics
    {
        get { lock (_diag) return new PlcDiagnostics(_requests, _failures, _timeouts, _reconnects, _lastEndCode, _lastError, _lastLatency, _lastOk, _lastErrAt, _bytesSent, _bytesReceived, _lastExchange, _lastFailedExchange); }
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await OpenAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try { Close(); State = EndpointState.Disconnected; }
        finally { _gate.Release(); }
    }

    private async Task OpenAsync(CancellationToken ct)
    {
        Close();
        State = EndpointState.Connecting;
        var tcp = new TcpClient { NoDelay = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Settings.EffectiveConnectTimeout);
        try { await tcp.ConnectAsync(Settings.Host, Settings.Port, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { tcp.Dispose(); Fail("connect timeout", timeout: true); throw new TimeoutException($"connect to {Settings.Host}:{Settings.Port} timed out"); }
        catch (Exception ex) when (ex is SocketException or IOException) { tcp.Dispose(); Fail(ex.Message); throw; }
        _tcp = tcp; _stream = tcp.GetStream(); State = EndpointState.Connected;
    }

    private void Close()
    {
        try { _stream?.Dispose(); } catch { /* closing */ }
        try { _tcp?.Dispose(); } catch { /* closing */ }
        _stream = null; _tcp = null;
    }

    private void Fail(string error, bool timeout = false, int? endCode = null)
    {
        lock (_diag)
        {
            _failures++; if (timeout) _timeouts++;
            _lastError = error; _lastErrAt = DateTimeOffset.UtcNow; if (endCode is not null) _lastEndCode = endCode;
        }
    }

    /// <summary>What happened on the wire during one exchange (kept for the diagnostics record).</summary>
    private sealed class Trace
    {
        public bool Sent;                        // the request was handed to the socket (it may have reached the PLC)
        public byte[] Received = [];             // bytes received so far (partial on failure)
        public int ReceivedCount;
    }

    /// <summary>Sends one request and returns the parsed response. Throws <see cref="McEndCodeException"/> for a PLC error end code.</summary>
    public async Task<McResponse> SendAsync(byte[] frame, bool retryable, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var timeout = retryable ? Settings.EffectiveReadTimeout : Settings.EffectiveWriteTimeout;
            var attempts = retryable ? Math.Max(0, Settings.MaxReadRetries) : 0;
            for (var attempt = 0; ; attempt++)
            {
                var trace = new Trace();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (_stream is null) { await OpenAsync(ct); if (attempt > 0) lock (_diag) _reconnects++; }
                    var resp = await ExchangeAsync(frame, timeout, trace, ct);
                    lock (_diag) { _requests++; _lastLatency = sw.ElapsedMilliseconds; _lastOk = DateTimeOffset.UtcNow; }
                    if (resp.EndCode != 0)
                    {
                        Fail($"end code 0x{resp.EndCode:X4}", endCode: resp.EndCode);
                        Record(frame, trace, sw, resp.EndCode, "END_CODE", $"PLC end code 0x{resp.EndCode:X4}", false);
                        throw new McEndCodeException(resp.EndCode);
                    }
                    Record(frame, trace, sw, 0, "OK", null, false);
                    State = EndpointState.Connected;
                    return resp;
                }
                catch (McEndCodeException) { throw; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Close(); State = EndpointState.Disconnected;
                    var ambiguous = !retryable && trace.Sent;
                    Record(frame, trace, sw, null, ambiguous ? "AMBIGUOUS" : "CANCELLED", "cancelled", ambiguous);
                    if (ambiguous) throw new PlcWriteAmbiguousException("The write was cancelled after it had been sent to the PLC; the result is undetermined.");
                    throw;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or McProtocolException or ObjectDisposedException)
                {
                    lock (_diag) _requests++;
                    Fail(ex.Message, timeout: ex is TimeoutException);
                    Close();
                    var ambiguous = !retryable && trace.Sent;
                    Record(frame, trace, sw, null, ambiguous ? "AMBIGUOUS" : ex is TimeoutException ? "TIMEOUT" : "FAILED", ex.Message, ambiguous);
                    if (ambiguous)
                    {
                        State = EndpointState.Faulted;
                        throw new PlcWriteAmbiguousException($"No confirmation was received for the write ({ex.Message}). The PLC may have executed the command; it is NOT retried automatically.", ex);
                    }
                    if (attempt >= attempts) { State = EndpointState.Faulted; throw; }
                    await Task.Delay(Settings.RetryDelay ?? TimeSpan.FromMilliseconds(100), ct);
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<McResponse> ExchangeAsync(byte[] frame, TimeSpan timeout, Trace trace, CancellationToken ct)
    {
        var stream = _stream ?? throw new IOException("not connected");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            trace.Sent = true;      // from this point the PLC may have seen (part of) the request
            await stream.WriteAsync(frame, cts.Token);
            await stream.FlushAsync(cts.Token);

            trace.Received = new byte[McFrame.ResponseHeaderLength];
            await ReadExactAsync(stream, trace, 0, McFrame.ResponseHeaderLength, cts.Token);
            var remaining = McFrame.RemainingLength(trace.Received);       // validates subheader and the announced length
            var all = new byte[McFrame.ResponseHeaderLength + remaining];
            trace.Received.CopyTo(all, 0);
            trace.Received = all;
            await ReadExactAsync(stream, trace, McFrame.ResponseHeaderLength, remaining, cts.Token);
            lock (_diag) { _bytesSent += frame.Length; _bytesReceived += all.Length; }

            // The PLC echoes the routing of the request and returns exactly the requested amount of data. Anything else means the
            // answer is not ours (wrong station / desynchronized stream) and must not be decoded.
            return McFrame.ParseAndValidateResponse(frame, all);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"no response within {timeout.TotalMilliseconds:0} ms"); }
    }

    private static async Task ReadExactAsync(NetworkStream s, Trace trace, int offset, int count, CancellationToken ct)
    {
        var read = 0;
        while (read < count)
        {
            var n = await s.ReadAsync(trace.Received.AsMemory(offset + read, count - read), ct);
            if (n == 0) throw new IOException("connection closed by the PLC");
            read += n; trace.ReceivedCount = offset + read;
        }
    }

    // ------------------------------------------------------------------------------------------------------------- diagnostics
    private void Record(byte[] frame, Trace trace, System.Diagnostics.Stopwatch sw, int? endCode, string outcome, string? error, bool ambiguous)
    {
        var command = frame.Length >= 21 ? BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11)) : (ushort)0;
        var device = frame.Length >= 21 ? McFrame.DeviceName(frame[18]) : "?";
        var head = frame.Length >= 21 ? frame[15] | (frame[16] << 8) | (frame[17] << 16) : 0;
        var points = frame.Length >= 21 ? BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(19)) : 0;
        var response = trace.ReceivedCount > 0 ? Hex(trace.Received.AsSpan(0, trace.ReceivedCount)) : null;
        var entry = new PlcExchange(DateTimeOffset.UtcNow, Settings.PlcId, command == McFrame.CommandBatchWrite ? "WRITE" : "READ", $"0x{command:X4}", device, head, points,
            sw.ElapsedMilliseconds, endCode, outcome, error, Hex(frame), response, ambiguous);
        lock (_diag) { _lastExchange = entry; if (outcome != "OK") _lastFailedExchange = entry; }
    }

    /// <summary>Space separated upper-case hex, capped so a large write cannot bloat diagnostics.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        var shown = bytes.Length <= HexLimitBytes ? bytes : bytes[..HexLimitBytes];
        var text = Convert.ToHexString(shown);
        var sb = new System.Text.StringBuilder(text.Length + text.Length / 2);
        for (var i = 0; i < text.Length; i += 2) { if (i > 0) sb.Append(' '); sb.Append(text, i, 2); }
        if (bytes.Length > HexLimitBytes) sb.Append($" …(+{bytes.Length - HexLimitBytes} bytes)");
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------------------------- batch helpers
    public async Task<bool[]> ReadBitsAsync(DeviceAddress head, int count, CancellationToken ct)
    {
        var result = new bool[count];
        for (var done = 0; done < count;)
        {
            var n = Math.Min(McFrame.MaxReadBitPoints, count - done);
            var resp = await SendAsync(McFrame.BuildBatchRead(Settings.Route, Settings.MonitoringTimer, head with { Number = head.Number + done }, n, bitUnits: true), true, ct);
            McFrame.DecodeBits(resp.Data, n).CopyTo(result, done);
            done += n;
        }
        return result;
    }

    public async Task<ushort[]> ReadWordsAsync(DeviceAddress head, int count, CancellationToken ct)
    {
        var result = new ushort[count];
        for (var done = 0; done < count;)
        {
            var n = Math.Min(McFrame.MaxWordPoints, count - done);
            var resp = await SendAsync(McFrame.BuildBatchRead(Settings.Route, Settings.MonitoringTimer, head with { Number = head.Number + done }, n, bitUnits: false), true, ct);
            McFrame.DecodeWords(resp.Data, n).CopyTo(result, done);
            done += n;
        }
        return result;
    }

    public Task WriteBitsAsync(DeviceAddress head, IReadOnlyList<bool> values, CancellationToken ct) =>
        SendAsync(McFrame.BuildBatchWriteBits(Settings.Route, Settings.MonitoringTimer, head, values), retryable: false, ct);

    public Task WriteWordsAsync(DeviceAddress head, IReadOnlyList<ushort> values, CancellationToken ct) =>
        SendAsync(McFrame.BuildBatchWriteWords(Settings.Route, Settings.MonitoringTimer, head, values), retryable: false, ct);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _gate.Dispose();
    }
}
