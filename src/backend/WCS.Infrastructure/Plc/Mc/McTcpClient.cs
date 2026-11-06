using System.Net.Sockets;
using WCS.Application.Plc;

namespace WCS.Infrastructure.Plc.Mc;

/// <summary>Connection settings of one PLC, all taken from the confirmed configuration. Retry values are technical (not PLC parameters).</summary>
public sealed record McClientSettings(string Host, int Port, McRoute Route, ushort MonitoringTimer, TimeSpan Timeout, int MaxReadRetries = 1, TimeSpan? RetryDelay = null);

/// <summary>
/// TCP client for ONE PLC (MC 3E binary). All exchanges are serialized by a gate, so two commands can never overlap on the socket.
/// Timeouts, connection loss and framing errors close the socket (a late answer must never be matched to the next request) and are
/// reported as transport faults; a PLC end code is reported as <see cref="McEndCodeException"/> and keeps the connection.
/// Reads are retried (idempotent); writes are never retried automatically.
/// </summary>
public sealed class McTcpClient(McClientSettings settings) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly object _diag = new();
    private long _requests, _failures, _timeouts, _reconnects, _bytesSent, _bytesReceived;
    private int? _lastEndCode; private string? _lastError; private long? _lastLatency; private DateTimeOffset? _lastOk, _lastErrAt;

    public McClientSettings Settings { get; } = settings;
    public EndpointState State { get; private set; } = EndpointState.Disconnected;

    public PlcDiagnostics Diagnostics
    {
        get { lock (_diag) return new PlcDiagnostics(_requests, _failures, _timeouts, _reconnects, _lastEndCode, _lastError, _lastLatency, _lastOk, _lastErrAt, _bytesSent, _bytesReceived); }
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
        cts.CancelAfter(Settings.Timeout);
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

    /// <summary>Sends one request and returns the parsed response. Throws <see cref="McEndCodeException"/> for a PLC error end code.</summary>
    public async Task<McResponse> SendAsync(byte[] frame, bool retryable, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var attempts = retryable ? Math.Max(0, Settings.MaxReadRetries) : 0;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (_stream is null) { await OpenAsync(ct); if (attempt > 0) lock (_diag) _reconnects++; }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var resp = await ExchangeAsync(frame, ct);
                    lock (_diag) { _requests++; _lastLatency = sw.ElapsedMilliseconds; _lastOk = DateTimeOffset.UtcNow; }
                    if (resp.EndCode != 0)
                    {
                        Fail($"end code 0x{resp.EndCode:X4}", endCode: resp.EndCode);
                        throw new McEndCodeException(resp.EndCode);
                    }
                    State = EndpointState.Connected;
                    return resp;
                }
                catch (McEndCodeException) { throw; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { Close(); State = EndpointState.Disconnected; throw; }
                catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or McProtocolException or ObjectDisposedException)
                {
                    lock (_diag) _requests++;
                    Fail(ex.Message, timeout: ex is TimeoutException);
                    Close();
                    if (attempt >= attempts) { State = EndpointState.Faulted; throw; }
                    await Task.Delay(Settings.RetryDelay ?? TimeSpan.FromMilliseconds(100), ct);
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<McResponse> ExchangeAsync(byte[] frame, CancellationToken ct)
    {
        var stream = _stream ?? throw new IOException("not connected");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Settings.Timeout);
        try
        {
            await stream.WriteAsync(frame, cts.Token);
            await stream.FlushAsync(cts.Token);
            var header = new byte[McFrame.ResponseHeaderLength];
            await ReadExactAsync(stream, header, cts.Token);
            var remaining = McFrame.RemainingLength(header);
            var all = new byte[McFrame.ResponseHeaderLength + remaining];
            header.CopyTo(all, 0);
            await ReadExactAsync(stream, all.AsMemory(McFrame.ResponseHeaderLength), cts.Token);
            lock (_diag) { _bytesSent += frame.Length; _bytesReceived += all.Length; }
            var resp = McFrame.ParseResponse(all);
            var req = frame; // echo check: network and PC number must come back unchanged
            if (resp.Route.Network != req[2] || resp.Route.Pc != req[3]) throw new McProtocolException("response route does not match the request");
            return resp;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"no response within {Settings.Timeout.TotalMilliseconds:0} ms"); }
    }

    private static async Task ReadExactAsync(NetworkStream s, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer[read..], ct);
            if (n == 0) throw new IOException("connection closed by the PLC");
            read += n;
        }
    }

    // ------------------------------------------------------------------------------------------------------------- batch helpers
    public async Task<bool[]> ReadBitsAsync(DeviceAddress head, int count, CancellationToken ct)
    {
        var result = new bool[count];
        for (var done = 0; done < count;)
        {
            var n = Math.Min(McFrame.MaxBitPoints, count - done);
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
