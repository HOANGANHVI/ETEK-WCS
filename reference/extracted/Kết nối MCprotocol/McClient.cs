using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MitsubishiMc.Client.Internal;

namespace MitsubishiMc.Client
{
    /// <summary>
    /// Mitsubishi MC Protocol 3E Binary Client.
    /// Kết nối, đọc và ghi dữ liệu từ PLC Mitsubishi (Q-series, iQ-R, iQ-F FX5) qua Ethernet TCP.
    ///
    /// Tính năng:
    ///   - Đọc Word (D, W) và Bit (M, X, Y) qua Command 0x0401 (Batch Read)
    ///   - Ghi Word (D, W) và Bit (M, Y) qua Command 0x1401 (Batch Write)
    ///   - Quản lý vòng đời Socket và NetworkStream nhất quán, an toàn đa luồng
    ///   - Ngăn ngừa ObjectDisposedException khi đóng kết nối hoặc timeout
    ///   - Tuần tự hóa toàn bộ request/response trên cùng 1 socket bằng SemaphoreSlim
    ///   - Kiểm soát Timeout thực tế cho mọi thao tác I/O
    ///   - Đóng gói và giải nén Bit Nibble chuẩn xác theo Mitsubishi SH-080008 (Even=Upper, Odd=Lower)
    ///   - Cung cấp RequestHex / ResponseHex chi tiết phục vụ chẩn đoán hiện trường
    ///   - Hỗ trợ CancellationToken và ngắt kết nối an toàn, idempotent
    ///   - Độc lập hoàn toàn với WPF
    /// </summary>
    public class McClient : IDisposable
    {
        private readonly McProtocolSettings _settings;
        private Socket _socket;
        private NetworkStream _stream;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _streamLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _connectionCts;
        private volatile bool _disposed;
        private long _lastCommunicationTicks;

        /// <summary>
        /// True khi kết nối TCP đang mở theo socket socket.Connected.
        /// Chú ý: Đây là trạng thái kết nối TCP tầng transport. Để kiểm tra PLC còn phản hồi hay không,
        /// sử dụng IsHealthy hoặc phương thức PingAsync().
        /// </summary>
        public bool IsConnected
        {
            get
            {
                if (_disposed) return false;
                try
                {
                    var s = _socket;
                    var st = _stream;
                    return s != null && s.Connected && st != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// True nếu kết nối TCP mở và thời điểm giao tiếp thành công gần nhất nằm trong ngưỡng cho phép (mặc định 3 lần TimeoutMs).
        /// </summary>
        public bool IsHealthy
        {
            get
            {
                if (!IsConnected) return false;
                long lastTicks = Interlocked.Read(ref _lastCommunicationTicks);
                if (lastTicks == 0) return true; // Vừa kết nối thành công, chưa có giao tiếp
                var elapsed = DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
                int maxSilenceMs = Math.Max(_settings.TimeoutMs * 3, 5000);
                return elapsed.TotalMilliseconds <= maxSilenceMs;
            }
        }

        /// <summary>Thời điểm giao tiếp thành công gần nhất với PLC (UTC).</summary>
        public DateTime? LastSuccessfulCommunicationUtc
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastCommunicationTicks);
                return ticks > 0 ? (DateTime?)new DateTime(ticks, DateTimeKind.Utc) : null;
            }
        }

        /// <summary>
        /// Kiểm tra tính sẵn sàng (Health check / Ping) của PLC bằng cách thử đọc 1 word (D0).
        /// Trả về true nếu PLC phản hồi thành công và giao tiếp thông suốt.
        /// </summary>
        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return false;
            try
            {
                var result = await ReadWordsAsync(McDeviceType.D, 0, 1, cancellationToken).ConfigureAwait(false);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Khởi tạo client với cài đặt kết nối.</summary>
        public McClient(McProtocolSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // ─── Connection Lifecycle ────────────────────────────────────────────

        /// <summary>
        /// Kết nối TCP tới Ethernet module của PLC bất đồng bộ có kiểm soát timeout và không gây crash tại EndConnect.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(McClient));
            if (IsConnected) return;

            await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected) return;

                // Đảm bảo dọn dẹp an toàn kết nối cũ nếu có
                await DisconnectInternalAsync().ConfigureAwait(false);

                _connectionCts = new CancellationTokenSource();

                // Phân giải IP
                IPAddress ip;
                if (!IPAddress.TryParse(_settings.IpAddress, out ip))
                {
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(_settings.IpAddress).ConfigureAwait(false);
                    if (addresses == null || addresses.Length == 0)
                        throw new SocketException((int)SocketError.HostNotFound);
                    ip = addresses[0];
                }

                var endPoint = new IPEndPoint(ip, _settings.Port);
                var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;
                socket.SendTimeout = _settings.TimeoutMs;
                socket.ReceiveTimeout = _settings.TimeoutMs;

                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectionCts.Token))
                {
                    linkedCts.CancelAfter(_settings.TimeoutMs);
                    var tcs = new TaskCompletionSource<bool>();

                    using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
                    {
                        var asyncResult = socket.BeginConnect(endPoint, ar =>
                        {
                            try
                            {
                                socket.EndConnect(ar);
                                tcs.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                tcs.TrySetException(ex);
                            }
                        }, null);

                        try
                        {
                            await tcs.Task.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            try { socket.Close(); } catch { }
                            if (cancellationToken.IsCancellationRequested)
                                throw;
                            throw new TimeoutException(
                                $"Không thể kết nối tới {_settings.IpAddress}:{_settings.Port} trong {_settings.TimeoutMs} ms. Kiểm tra IP, Port và cấu hình Ethernet trên PLC.");
                        }
                        catch
                        {
                            try { socket.Close(); } catch { }
                            throw;
                        }
                    }
                }

                _socket = socket;
                _stream = new NetworkStream(_socket, ownsSocket: true);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// Ngắt kết nối PLC an toàn, hủy các tác vụ in-flight, idempotent.
        /// Chờ các tác vụ stream I/O kết thúc có kiểm soát trước khi đóng socket.
        /// </summary>
        public void Disconnect()
        {
            if (_disposed) return;
            DisconnectSafe();
        }

        private void DisconnectSafe()
        {
            try
            {
                _connectionLock.Wait(1000);
            }
            catch (ObjectDisposedException)
            {
                // Lock đã bị dispose trong quá trình đóng
            }

            try
            {
                // 1. Kích hoạt hủy token để báo hiệu cho các tác vụ đang chờ I/O
                try { _connectionCts?.Cancel(); } catch { }

                // 2. Chờ streamLock nhả ra tối đa 500ms để không dispose socket/stream khi tác vụ đang ghi/đọc
                bool streamLockAcquired = false;
                try
                {
                    streamLockAcquired = _streamLock.Wait(500);
                }
                catch { }

                try
                {
                    DisconnectInternal();
                }
                finally
                {
                    if (streamLockAcquired)
                    {
                        try { _streamLock.Release(); } catch { }
                    }
                }
            }
            finally
            {
                try { _connectionLock.Release(); } catch { }
            }
        }

        private async Task DisconnectInternalAsync()
        {
            try { _connectionCts?.Cancel(); } catch { }

            bool streamLockAcquired = false;
            try
            {
                streamLockAcquired = await _streamLock.WaitAsync(500).ConfigureAwait(false);
            }
            catch { }

            try
            {
                DisconnectInternal();
            }
            finally
            {
                if (streamLockAcquired)
                    _streamLock.Release();
            }
        }

        private void DisconnectInternal()
        {
            try { _connectionCts?.Cancel(); } catch { }
            try { _connectionCts?.Dispose(); } catch { }
            _connectionCts = null;

            var stream = _stream;
            var socket = _socket;
            _stream = null;
            _socket = null;

            try { stream?.Close(); } catch { }
            try { socket?.Close(); } catch { }
        }

        // ─── Read Operations ─────────────────────────────────────────────────

        /// <summary>
        /// Đọc Word device (D, W). Tối đa 960 words mỗi request.
        /// </summary>
        public Task<McReadResult> ReadWordsAsync(McDeviceType device, int startAddress, ushort count, CancellationToken cancellationToken = default)
        {
            if (device != McDeviceType.D && device != McDeviceType.W)
                return Task.FromResult(McReadResult.Fail($"{device} là bit device. Dùng ReadBitsAsync() để đọc bit."));
            if (count == 0 || count > 960)
                return Task.FromResult(McReadResult.Fail("Số lượng word phải từ 1 đến 960 mỗi request."));
            if (startAddress < 0)
                return Task.FromResult(McReadResult.Fail("Địa chỉ bắt đầu phải >= 0."));

            return ExecuteReadAsync(device, startAddress, count, isWord: true, cancellationToken);
        }

        /// <summary>
        /// Đọc Bit device (M, X, Y). Tối đa 3584 bits mỗi request.
        /// Lưu ý X/Y: địa chỉ truyền vào là số thập phân.
        /// </summary>
        public Task<McReadResult> ReadBitsAsync(McDeviceType device, int startAddress, ushort count, CancellationToken cancellationToken = default)
        {
            if (device != McDeviceType.M && device != McDeviceType.X && device != McDeviceType.Y)
                return Task.FromResult(McReadResult.Fail($"{device} là word device. Dùng ReadWordsAsync() để đọc word."));
            if (count == 0 || count > 3584)
                return Task.FromResult(McReadResult.Fail("Số lượng bit phải từ 1 đến 3584 mỗi request."));
            if (startAddress < 0)
                return Task.FromResult(McReadResult.Fail("Địa chỉ bắt đầu phải >= 0."));

            return ExecuteReadAsync(device, startAddress, count, isWord: false, cancellationToken);
        }

        // ─── Write Operations ────────────────────────────────────────────────

        /// <summary>
        /// Ghi Word device (D, W) qua Command 0x1401 (Batch Write). Tối đa 960 words.
        /// </summary>
        public Task<McWriteResult> WriteWordsAsync(McDeviceType device, int startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length == 0 || values.Length > 960)
                return Task.FromResult(McWriteResult.Fail("Số lượng word ghi phải từ 1 đến 960."));
            if (device != McDeviceType.D && device != McDeviceType.W)
                return Task.FromResult(McWriteResult.Fail($"Chỉ cho phép ghi Word vào thiết bị D hoặc W (không hỗ trợ {device})."));
            if (startAddress < 0)
                return Task.FromResult(McWriteResult.Fail("Địa chỉ bắt đầu phải >= 0."));

            ushort timerUnits = GetCpuTimerUnits();
            return ExecuteWriteAsync(
                () => McFrameBuilder.BuildWordWriteRequest(_settings.NetworkNo, _settings.PcNo, device, startAddress, values, timerUnits),
                cancellationToken);
        }

        /// <summary>
        /// Ghi Bit device (M, Y) qua Command 0x1401 (Batch Write). Tối đa 1440 bits.
        /// Lưu ý an toàn: Từ chối ghi vào thiết bị X (Input Relay là ngõ vào vật lý).
        /// </summary>
        public Task<McWriteResult> WriteBitsAsync(McDeviceType device, int startAddress, bool[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length == 0 || values.Length > 1440)
                return Task.FromResult(McWriteResult.Fail("Số lượng bit ghi phải từ 1 đến 1440."));
            if (device == McDeviceType.X)
                return Task.FromResult(McWriteResult.Fail("Thiết bị X (Input Relay) là ngõ vào phần cứng của PLC, không được phép ghi."));
            if (device != McDeviceType.M && device != McDeviceType.Y)
                return Task.FromResult(McWriteResult.Fail($"Chỉ cho phép ghi Bit vào thiết bị M hoặc Y (không hỗ trợ {device})."));
            if (startAddress < 0)
                return Task.FromResult(McWriteResult.Fail("Địa chỉ bắt đầu phải >= 0."));

            ushort timerUnits = GetCpuTimerUnits();
            return ExecuteWriteAsync(
                () => McFrameBuilder.BuildBitWriteRequest(_settings.NetworkNo, _settings.PcNo, device, startAddress, values, timerUnits),
                cancellationToken);
        }

        // ─── Core Execution Engine (Serialization + Verification) ─────────────

        private async Task<McReadResult> ExecuteReadAsync(
            McDeviceType device, int startAddress, ushort count, bool isWord, CancellationToken cancellationToken)
        {
            if (!IsConnected)
                return McReadResult.Fail("Chưa kết nối tới PLC Mitsubishi.");

            string reqHex = null;
            using (var linkedCts = CreateLinkedCts(cancellationToken))
            {
                bool lockAcquired = false;
                try
                {
                    try
                    {
                        lockAcquired = await _streamLock.WaitAsync(_settings.TimeoutMs, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Hủy trong khi đang chờ lock tài nguyên socket -> CHƯA gửi gì xuống socket
                        // Không được ngắt kết nối PLC, bảo toàn kết nối cho các tác vụ khác
                        return cancellationToken.IsCancellationRequested
                            ? McReadResult.Fail("Thao tác đọc bị hủy bởi người dùng khi đang chờ hàng đợi socket.")
                            : McReadResult.Fail("Thao tác đọc bị timeout khi chờ hàng đợi tài nguyên socket.");
                    }

                    if (!lockAcquired)
                        return McReadResult.Fail("Thao tác bị timeout khi chờ tài nguyên kết nối socket.");

                    var stream = _stream;
                    if (stream == null || !IsConnected)
                        return McReadResult.Fail("Kết nối đã bị ngắt.");

                    ushort timerUnits = GetCpuTimerUnits();
                    byte[] request = isWord
                        ? McFrameBuilder.BuildWordReadRequest(_settings.NetworkNo, _settings.PcNo, device, startAddress, count, timerUnits)
                        : McFrameBuilder.BuildBitReadRequest(_settings.NetworkNo, _settings.PcNo, device, startAddress, count, timerUnits);

                    reqHex = ToHex(request);

                    // 1. Gửi request frame
                    await stream.WriteAsync(request, 0, request.Length, linkedCts.Token).ConfigureAwait(false);

                    // 2. Nhận response
                    byte[] fullResponse = await Receive3EResponseAsync(stream, linkedCts.Token).ConfigureAwait(false);
                    string respHex = ToHex(fullResponse);

                    var parseResult = isWord
                        ? McResponseParser.ParseWordResponse(fullResponse, _settings.NetworkNo, _settings.PcNo, count, reqHex, respHex)
                        : McResponseParser.ParseBitResponse(fullResponse, _settings.NetworkNo, _settings.PcNo, count, reqHex, respHex);

                    if (parseResult.Success)
                    {
                        Interlocked.Exchange(ref _lastCommunicationTicks, DateTime.UtcNow.Ticks);
                    }

                    return parseResult;
                }
                catch (OperationCanceledException)
                {
                    // Đã gửi request xuống đường truyền mà bị hủy hoặc timeout
                    // Bắt buộc phải đóng kết nối để dọn sạch stream buffer, tránh byte trôi dạt làm lệch frame sau
                    DisconnectInternal();
                    if (cancellationToken.IsCancellationRequested)
                        return McReadResult.Fail("Thao tác đọc bị hủy bởi người dùng.", 0, reqHex);
                    return McReadResult.Fail($"Timeout quá {_settings.TimeoutMs} ms khi đọc từ PLC. PLC không gửi byte phản hồi nào.", 0, reqHex);
                }
                catch (ObjectDisposedException)
                {
                    DisconnectInternal();
                    return McReadResult.Fail("Kết nối đã bị đóng trong lúc thực hiện thao tác đọc.", 0, reqHex);
                }
                catch (SocketException ex)
                {
                    DisconnectInternal();
                    return McReadResult.Fail($"Lỗi Socket ({ex.SocketErrorCode}): {ex.Message}", 0, reqHex);
                }
                catch (IOException ex)
                {
                    DisconnectInternal();
                    return McReadResult.Fail($"Lỗi I/O truyền thông: {ex.Message}", 0, reqHex);
                }
                catch (Exception ex)
                {
                    DisconnectInternal();
                    return McReadResult.Fail($"Lỗi đọc: {ex.Message}", 0, reqHex);
                }
                finally
                {
                    if (lockAcquired)
                        _streamLock.Release();
                }
            }
        }

        private async Task<McWriteResult> ExecuteWriteAsync(
            Func<byte[]> buildRequest, CancellationToken cancellationToken)
        {
            if (!IsConnected)
                return McWriteResult.Fail("Chưa kết nối tới PLC Mitsubishi.");

            string reqHex = null;
            using (var linkedCts = CreateLinkedCts(cancellationToken))
            {
                bool lockAcquired = false;
                bool requestSent = false;
                try
                {
                    try
                    {
                        lockAcquired = await _streamLock.WaitAsync(_settings.TimeoutMs, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Hủy trong khi đang chờ lock socket -> CHƯA gửi frame xuống mạng
                        // Giữ nguyên kết nối, không đóng socket
                        return cancellationToken.IsCancellationRequested
                            ? McWriteResult.Fail("Thao tác ghi bị hủy bởi người dùng khi đang chờ hàng đợi socket.", 0, false)
                            : McWriteResult.Fail("Thao tác ghi bị timeout khi chờ hàng đợi tài nguyên socket.", 0, false);
                    }

                    if (!lockAcquired)
                        return McWriteResult.Fail("Thao tác bị timeout khi chờ tài nguyên kết nối socket.");

                    var stream = _stream;
                    if (stream == null || !IsConnected)
                        return McWriteResult.Fail("Kết nối đã bị ngắt.");

                    byte[] request = buildRequest();
                    reqHex = ToHex(request);

                    // Gửi request frame
                    await stream.WriteAsync(request, 0, request.Length, linkedCts.Token).ConfigureAwait(false);
                    requestSent = true;

                    // Nhận response
                    byte[] fullResponse = await Receive3EResponseAsync(stream, linkedCts.Token).ConfigureAwait(false);
                    string respHex = ToHex(fullResponse);

                    var parseResult = McResponseParser.ParseWriteResponse(fullResponse, _settings.NetworkNo, _settings.PcNo, reqHex, respHex);
                    if (parseResult.Success)
                    {
                        Interlocked.Exchange(ref _lastCommunicationTicks, DateTime.UtcNow.Ticks);
                    }

                    return parseResult;
                }
                catch (OperationCanceledException)
                {
                    DisconnectInternal();
                    if (cancellationToken.IsCancellationRequested)
                        return McWriteResult.Fail("Thao tác ghi bị hủy bởi người dùng.", 0, requestSent, reqHex);
                    return McWriteResult.Fail(
                        $"Timeout quá {_settings.TimeoutMs} ms khi ghi. Kết quả ghi không xác định (ambiguous). Tuyệt đối không tự động retry!",
                        0, isAmbiguous: requestSent, reqHex: reqHex);
                }
                catch (ObjectDisposedException)
                {
                    DisconnectInternal();
                    return McWriteResult.Fail("Kết nối đã bị đóng trong lúc thực hiện thao tác ghi.", 0, isAmbiguous: requestSent, reqHex: reqHex);
                }
                catch (SocketException ex)
                {
                    DisconnectInternal();
                    return McWriteResult.Fail($"Lỗi Socket ({ex.SocketErrorCode}): {ex.Message}", 0, isAmbiguous: requestSent, reqHex: reqHex);
                }
                catch (IOException ex)
                {
                    DisconnectInternal();
                    return McWriteResult.Fail($"Lỗi I/O: {ex.Message}", 0, isAmbiguous: requestSent, reqHex: reqHex);
                }
                catch (Exception ex)
                {
                    DisconnectInternal();
                    return McWriteResult.Fail($"Lỗi ghi dữ liệu: {ex.Message}", 0, isAmbiguous: requestSent, reqHex: reqHex);
                }
                finally
                {
                    if (lockAcquired)
                        _streamLock.Release();
                }
            }
        }

        private async Task<byte[]> SendAndReceive3EFrameAsync(NetworkStream stream, byte[] request, CancellationToken ct)
        {
            // 1. Gửi request frame
            await stream.WriteAsync(request, 0, request.Length, ct).ConfigureAwait(false);

            // 2. Nhận response frame
            return await Receive3EResponseAsync(stream, ct).ConfigureAwait(false);
        }

        private static async Task<byte[]> Receive3EResponseAsync(NetworkStream stream, CancellationToken ct)
        {
            // Nhận fixed header (11 bytes: subheader + routing + length + endcode)
            byte[] header = await ReadExactAsync(stream, 11, ct).ConfigureAwait(false);

            // Thẩm định sơ bộ subheader
            if (header[0] != 0xD0 || header[1] != 0x00)
                throw new IOException($"Subheader phản hồi không hợp lệ: 0x{header[0]:X2} 0x{header[1]:X2} (yêu cầu 0xD0 0x00).");

            // Độ dài dữ liệu từ byte [9] trở đi (EndCode 2 bytes + data payload)
            ushort respDataLen = (ushort)(header[7] | (header[8] << 8));
            int dataByteCount = respDataLen >= 2 ? respDataLen - 2 : 0;

            byte[] fullResponse = new byte[11 + dataByteCount];
            Array.Copy(header, 0, fullResponse, 0, 11);

            // Nhận phần data payload còn lại nếu có
            if (dataByteCount > 0)
            {
                byte[] data = await ReadExactAsync(stream, dataByteCount, ct).ConfigureAwait(false);
                Array.Copy(data, 0, fullResponse, 11, dataByteCount);
            }

            return fullResponse;
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                ct.ThrowIfCancellationRequested();
                int n;
                try
                {
                    n = await stream.ReadAsync(buf, read, count - read, ct).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    throw new IOException("Kết nối đã bị đóng bởi ứng dụng hoặc PLC.");
                }

                if (n == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);
                read += n;
                ct.ThrowIfCancellationRequested();
            }
            return buf;
        }

        private ushort GetCpuTimerUnits()
        {
            if (_settings.CpuMonitoringTimerUnits > 0)
                return _settings.CpuMonitoringTimerUnits;
            return (ushort)Math.Max(1, Math.Min(_settings.TimeoutMs / 250, 0xFFFF));
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        private CancellationTokenSource CreateLinkedCts(CancellationToken externalToken)
        {
            var connCts = _connectionCts;
            var cts = connCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(externalToken, connCts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            cts.CancelAfter(_settings.TimeoutMs);
            return cts;
        }

        // ─── Disposal ────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Đóng kết nối mạng và giải phóng socket/stream trước
            try
            {
                DisconnectSafe();
            }
            catch { }

            // Giải phóng các đối tượng đồng bộ luồng
            try
            {
                _connectionLock.Dispose();
            }
            catch { }

            try
            {
                _streamLock.Dispose();
            }
            catch { }
        }
    }
}
