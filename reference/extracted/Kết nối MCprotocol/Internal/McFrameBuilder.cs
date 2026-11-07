using System;

namespace MitsubishiMc.Client.Internal
{
    /// <summary>
    /// Xây dựng Mitsubishi MC Protocol 3E Binary request frame cho các lệnh đọc và ghi.
    ///
    /// Tài liệu tham chiếu: MELSEC Communication Protocol Reference Manual (SH-080008).
    ///
    /// Cấu trúc 3E Binary Request Header:
    ///   [0-1]  Subheader: 0x50 0x00
    ///   [2]    Network No (0x00 = local network)
    ///   [3]    PC No (0xFF = target CPU)
    ///   [4-5]  IO No: 0xFF 0x03 (0x03FF)
    ///   [6]    Station No: 0x00
    ///   [7-8]  Request Data Length (LE): số byte từ CPU timer đến hết frame
    ///   [9-10] CPU Monitoring Timer (LE): 0x0A 0x00 = 2500ms (hoặc tính theo timeoutMs)
    /// </summary>
    internal static class McFrameBuilder
    {
        // ─── Device Codes ────────────────────────────────────────────────────
        internal const byte DevD = 0xA8; // Word: Data Register
        internal const byte DevW = 0xB4; // Word: Link Register
        internal const byte DevM = 0x90; // Bit:  Internal Relay
        internal const byte DevX = 0x9C; // Bit:  Input Relay (Read only)
        internal const byte DevY = 0x9D; // Bit:  Output Relay

        internal static byte GetDeviceCode(McDeviceType device)
        {
            switch (device)
            {
                case McDeviceType.D: return DevD;
                case McDeviceType.W: return DevW;
                case McDeviceType.M: return DevM;
                case McDeviceType.X: return DevX;
                case McDeviceType.Y: return DevY;
                default: throw new ArgumentException($"Device không được hỗ trợ: {device}");
            }
        }

        // ─── Đọc dữ liệu (Command 0x0401) ────────────────────────────────────

        /// <summary>Tạo request đọc word device (D, W). Command 0x0401, Subcommand 0x0000.</summary>
        public static byte[] BuildWordReadRequest(
            byte networkNo, byte pcNo,
            McDeviceType device, int startAddress, ushort count, ushort cpuTimerUnits = 10)
        {
            if (device != McDeviceType.D && device != McDeviceType.W)
                throw new ArgumentException($"Chỉ hỗ trợ đọc Word cho thiết bị D hoặc W, không hỗ trợ {device}.");

            return BuildBatchRead(networkNo, pcNo, device, startAddress, count, isWord: true, cpuTimerUnits: cpuTimerUnits);
        }

        /// <summary>Tạo request đọc bit device (M, X, Y). Command 0x0401, Subcommand 0x0001.</summary>
        public static byte[] BuildBitReadRequest(
            byte networkNo, byte pcNo,
            McDeviceType device, int startAddress, ushort count, ushort cpuTimerUnits = 10)
        {
            if (device != McDeviceType.M && device != McDeviceType.X && device != McDeviceType.Y)
                throw new ArgumentException($"Chỉ hỗ trợ đọc Bit cho thiết bị M, X hoặc Y, không hỗ trợ {device}.");

            return BuildBatchRead(networkNo, pcNo, device, startAddress, count, isWord: false, cpuTimerUnits: cpuTimerUnits);
        }

        private static byte[] BuildBatchRead(
            byte networkNo, byte pcNo,
            McDeviceType device, int startAddress, ushort count, bool isWord, ushort cpuTimerUnits)
        {
            byte deviceCode = GetDeviceCode(device);
            const ushort reqDataLen = 12; // 2(timer) + 2(cmd) + 2(subcmd) + 3(addr) + 1(dev) + 2(points)

            byte[] f = new byte[21];

            // [0-1] Subheader
            f[0] = 0x50; f[1] = 0x00;
            // [2-3] Network No, PC No
            f[2] = networkNo;
            f[3] = pcNo;
            // [4-5] IO No = 0x03FF
            f[4] = 0xFF; f[5] = 0x03;
            // [6] Station No
            f[6] = 0x00;
            // [7-8] Request data length (LE)
            f[7] = (byte)(reqDataLen & 0xFF);
            f[8] = (byte)(reqDataLen >> 8);
            // [9-10] CPU monitoring timer (mỗi đơn vị = 250ms, mặc định 10 = 2500ms)
            f[9] = (byte)(cpuTimerUnits & 0xFF);
            f[10] = (byte)(cpuTimerUnits >> 8);
            // [11-12] Command = 0x0401 (Batch Read)
            f[11] = 0x01; f[12] = 0x04;
            // [13-14] Subcommand (word=0x0000, bit=0x0001)
            f[13] = isWord ? (byte)0x00 : (byte)0x01;
            f[14] = 0x00;
            // [15-17] Starting device address (LE, 3 bytes)
            f[15] = (byte)(startAddress & 0xFF);
            f[16] = (byte)((startAddress >> 8) & 0xFF);
            f[17] = (byte)((startAddress >> 16) & 0xFF);
            // [18] Device code
            f[18] = deviceCode;
            // [19-20] Number of points (LE)
            f[19] = (byte)(count & 0xFF);
            f[20] = (byte)(count >> 8);

            return f;
        }

        // ─── Ghi dữ liệu (Command 0x1401) ────────────────────────────────────

        /// <summary>
        /// Tạo request ghi Word device (D, W). Command 0x1401, Subcommand 0x0000.
        /// </summary>
        public static byte[] BuildWordWriteRequest(
            byte networkNo, byte pcNo,
            McDeviceType device, int startAddress, ushort[] values, ushort cpuTimerUnits = 10)
        {
            if (values == null || values.Length == 0 || values.Length > 960)
                throw new ArgumentException("Số lượng word ghi phải từ 1 đến 960.", nameof(values));

            if (device != McDeviceType.D && device != McDeviceType.W)
                throw new ArgumentException($"Chỉ cho phép ghi Word vào thiết bị D hoặc W (không hỗ trợ {device}).");

            ushort count = (ushort)values.Length;
            byte deviceCode = GetDeviceCode(device);
            int dataBytes = count * 2;
            ushort reqDataLen = (ushort)(12 + dataBytes);

            byte[] f = new byte[9 + reqDataLen];

            // Header 3E
            f[0] = 0x50; f[1] = 0x00;
            f[2] = networkNo;
            f[3] = pcNo;
            f[4] = 0xFF; f[5] = 0x03;
            f[6] = 0x00;
            f[7] = (byte)(reqDataLen & 0xFF);
            f[8] = (byte)(reqDataLen >> 8);

            // CPU Timer
            f[9] = (byte)(cpuTimerUnits & 0xFF);
            f[10] = (byte)(cpuTimerUnits >> 8);

            // Command = 0x1401 (Batch Write)
            f[11] = 0x01; f[12] = 0x14;

            // Subcommand = 0x0000 (Word)
            f[13] = 0x00; f[14] = 0x00;

            // Start Address (3 bytes LE)
            f[15] = (byte)(startAddress & 0xFF);
            f[16] = (byte)((startAddress >> 8) & 0xFF);
            f[17] = (byte)((startAddress >> 16) & 0xFF);

            // Device code
            f[18] = deviceCode;

            // Number of points (LE)
            f[19] = (byte)(count & 0xFF);
            f[20] = (byte)(count >> 8);

            // Write Data (Word LE)
            for (int i = 0; i < count; i++)
            {
                int offset = 21 + (i * 2);
                f[offset] = (byte)(values[i] & 0xFF);
                f[offset + 1] = (byte)(values[i] >> 8);
            }

            return f;
        }

        /// <summary>
        /// Tạo request ghi Bit device (M, Y). Command 0x1401, Subcommand 0x0001.
        /// Chú ý an toàn: Từ chối thiết bị X (Input Relay là ngõ vào vật lý, không được ghi).
        /// </summary>
        public static byte[] BuildBitWriteRequest(
            byte networkNo, byte pcNo,
            McDeviceType device, int startAddress, bool[] values, ushort cpuTimerUnits = 10)
        {
            if (values == null || values.Length == 0 || values.Length > 1440)
                throw new ArgumentException("Số lượng bit ghi phải từ 1 đến 1440.", nameof(values));

            if (device == McDeviceType.X)
                throw new InvalidOperationException("Thiết bị X (Input Relay) là ngõ vào phần cứng của PLC, không được phép thực hiện thao tác ghi.");

            if (device != McDeviceType.M && device != McDeviceType.Y)
                throw new ArgumentException($"Chỉ cho phép ghi Bit vào thiết bị M hoặc Y (không hỗ trợ {device}).");

            ushort count = (ushort)values.Length;
            byte deviceCode = GetDeviceCode(device);
            int dataBytes = (count + 1) / 2; // Mỗi byte chứa 2 nibble
            ushort reqDataLen = (ushort)(12 + dataBytes);

            byte[] f = new byte[9 + reqDataLen];

            // Header 3E
            f[0] = 0x50; f[1] = 0x00;
            f[2] = networkNo;
            f[3] = pcNo;
            f[4] = 0xFF; f[5] = 0x03;
            f[6] = 0x00;
            f[7] = (byte)(reqDataLen & 0xFF);
            f[8] = (byte)(reqDataLen >> 8);

            // CPU Timer
            f[9] = (byte)(cpuTimerUnits & 0xFF);
            f[10] = (byte)(cpuTimerUnits >> 8);

            // Command = 0x1401 (Batch Write)
            f[11] = 0x01; f[12] = 0x14;

            // Subcommand = 0x0001 (Bit)
            f[13] = 0x01; f[14] = 0x00;

            // Start Address (3 bytes LE)
            f[15] = (byte)(startAddress & 0xFF);
            f[16] = (byte)((startAddress >> 8) & 0xFF);
            f[17] = (byte)((startAddress >> 16) & 0xFF);

            // Device code
            f[18] = deviceCode;

            // Number of points (LE)
            f[19] = (byte)(count & 0xFF);
            f[20] = (byte)(count >> 8);

            // Write Data (Nibble packing: 2 points mỗi byte)
            byte[] packed = PackBitsToNibbles(values);
            Array.Copy(packed, 0, f, 21, packed.Length);

            return f;
        }

        /// <summary>
        /// Đóng gói các bit thành các byte theo định dạng nibble của MC Protocol (1 point = 1 nibble).
        /// Theo chuẩn MELSEC Communication Protocol Reference Manual (SH-080008):
        /// - Point chẵn (0, 2, 4...) đặt ở Upper Nibble (bits 4..7): 0x10 = ON, 0x00 = OFF.
        /// - Point lẻ (1, 3, 5...) đặt ở Lower Nibble (bits 0..3): 0x01 = ON, 0x00 = OFF.
        /// </summary>
        internal static byte[] PackBitsToNibbles(bool[] values)
        {
            int byteCount = (values.Length + 1) / 2;
            byte[] bytes = new byte[byteCount];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    int byteIdx = i / 2;
                    if (i % 2 == 0)
                        bytes[byteIdx] |= 0x10; // First point in upper nibble (MSB)
                    else
                        bytes[byteIdx] |= 0x01; // Second point in lower nibble (LSB)
                }
            }
            return bytes;
        }
    }
}
