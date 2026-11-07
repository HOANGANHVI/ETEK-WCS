using System;

namespace MitsubishiMc.Client.Internal
{
    /// <summary>
    /// Phân tích và thẩm định nghiêm ngặt khung phản hồi 3E Binary từ PLC Mitsubishi.
    ///
    /// Cấu trúc 3E Binary Response:
    ///   [0-1]  Subheader: 0xD0 0x00
    ///   [2]    Network No (khớp với request)
    ///   [3]    PC No (khớp với request)
    ///   [4-5]  IO No
    ///   [6]    Station No
    ///   [7-8]  Response data length (LE) = số byte từ [9] trở đi (End code + Data)
    ///   [9-10] End code (LE): 0x0000 = Thành công
    ///   [11..] Data (Word hoặc Bit)
    /// </summary>
    internal static class McResponseParser
    {
        private const int FixedHeaderSize = 11; // Bytes 0–10

        // ─── Phân tích Đọc Word ──────────────────────────────────────────────

        /// <summary>
        /// Phân tích phản hồi đọc Word device.
        /// Thẩm định: Subheader, Network/PC No, Độ dài data, End Code, và số lượng word nhận được.
        /// </summary>
        public static McReadResult ParseWordResponse(
            byte[] response, byte expectedNetworkNo, byte expectedPcNo, ushort expectedCount,
            string reqHex = null, string respHex = null)
        {
            string err = ValidateHeader(response, expectedNetworkNo, expectedPcNo);
            if (err != null) return McReadResult.Fail(err, 0, reqHex, respHex);

            ushort endCode = ReadUInt16LE(response, 9);
            if (endCode != 0x0000)
                return McReadResult.Fail(FormatEndCode(endCode), endCode, reqHex, respHex);

            ushort respDataLen = ReadUInt16LE(response, 7);
            int neededDataBytes = expectedCount * 2;
            int expectedRespDataLen = 2 + neededDataBytes;

            if (respDataLen != expectedRespDataLen)
            {
                return McReadResult.Fail(
                    $"Độ dài dữ liệu phản hồi không khớp: khai báo {respDataLen} bytes, kỳ vọng {expectedRespDataLen} bytes cho {expectedCount} words.",
                    0, reqHex, respHex);
            }

            int actualDataBytes = response.Length - FixedHeaderSize;
            if (actualDataBytes != neededDataBytes)
            {
                return McReadResult.Fail(
                    $"Dữ liệu Word thực tế bị sai lệch: cần {neededDataBytes} bytes, nhận {actualDataBytes} bytes.",
                    0, reqHex, respHex);
            }

            ushort[] words = new ushort[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                words[i] = ReadUInt16LE(response, FixedHeaderSize + (i * 2));
            }

            return McReadResult.OkWord(words, reqHex, respHex);
        }

        // ─── Phân tích Đọc Bit ───────────────────────────────────────────────

        /// <summary>
        /// Phân tích phản hồi đọc Bit device (nibble unpacking: 1 point = 1 nibble).
        /// Theo chuẩn MELSEC Communication Protocol Reference Manual (SH-080008):
        /// - Point chẵn (0, 2, 4...) ở Upper Nibble (bits 4..7).
        /// - Point lẻ (1, 3, 5...) ở Lower Nibble (bits 0..3).
        /// </summary>
        public static McReadResult ParseBitResponse(
            byte[] response, byte expectedNetworkNo, byte expectedPcNo, ushort expectedCount,
            string reqHex = null, string respHex = null)
        {
            string err = ValidateHeader(response, expectedNetworkNo, expectedPcNo);
            if (err != null) return McReadResult.Fail(err, 0, reqHex, respHex);

            ushort endCode = ReadUInt16LE(response, 9);
            if (endCode != 0x0000)
                return McReadResult.Fail(FormatEndCode(endCode), endCode, reqHex, respHex);

            ushort respDataLen = ReadUInt16LE(response, 7);
            int neededDataBytes = (expectedCount + 1) / 2;
            int expectedRespDataLen = 2 + neededDataBytes;

            if (respDataLen != expectedRespDataLen)
            {
                return McReadResult.Fail(
                    $"Độ dài dữ liệu phản hồi không khớp: khai báo {respDataLen} bytes, kỳ vọng {expectedRespDataLen} bytes cho {expectedCount} bits.",
                    0, reqHex, respHex);
            }

            int actualDataBytes = response.Length - FixedHeaderSize;
            if (actualDataBytes != neededDataBytes)
            {
                return McReadResult.Fail(
                    $"Dữ liệu Bit thực tế bị sai lệch: cần {neededDataBytes} bytes, nhận {actualDataBytes} bytes.",
                    0, reqHex, respHex);
            }

            bool[] bits = new bool[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                int byteIdx = FixedHeaderSize + (i / 2);
                byte byteVal = response[byteIdx];
                // Point chẵn ở upper nibble (bits 4..7), point lẻ ở lower nibble (bits 0..3)
                bool value = (i % 2 == 0)
                    ? ((byteVal >> 4) & 0x0F) != 0
                    : (byteVal & 0x0F) != 0;
                bits[i] = value;
            }

            return McReadResult.OkBit(bits, reqHex, respHex);
        }

        // ─── Phân tích Ghi Dữ Liệu ───────────────────────────────────────────

        /// <summary>
        /// Phân tích phản hồi ghi (Batch Write) MC Protocol 3E Binary.
        /// Phản hồi thành công có đúng 11 bytes (Subheader, Routing, Length=2, EndCode=0x0000).
        /// </summary>
        public static McWriteResult ParseWriteResponse(
            byte[] response, byte expectedNetworkNo, byte expectedPcNo,
            string reqHex = null, string respHex = null)
        {
            string err = ValidateHeader(response, expectedNetworkNo, expectedPcNo);
            if (err != null) return McWriteResult.Fail(err, 0, false, reqHex, respHex);

            ushort endCode = ReadUInt16LE(response, 9);
            if (endCode != 0x0000)
                return McWriteResult.Fail(FormatEndCode(endCode), endCode, false, reqHex, respHex);

            ushort respDataLen = ReadUInt16LE(response, 7);
            if (respDataLen != 2)
            {
                return McWriteResult.Fail(
                    $"Độ dài dữ liệu phản hồi ghi không hợp lệ: khai báo {respDataLen} bytes, kỳ vọng 2 bytes (EndCode).",
                    0, false, reqHex, respHex);
            }

            if (response.Length != FixedHeaderSize)
            {
                return McWriteResult.Fail(
                    $"Kích thước phản hồi ghi không hợp lệ: nhận được {response.Length} bytes, kỳ vọng {FixedHeaderSize} bytes.",
                    0, false, reqHex, respHex);
            }

            return McWriteResult.Ok(reqHex, respHex);
        }

        // ─── Thẩm Định Header Chung ──────────────────────────────────────────

        private static string ValidateHeader(byte[] response, byte expectedNetworkNo, byte expectedPcNo)
        {
            if (response == null || response.Length < FixedHeaderSize)
            {
                return $"Phản hồi quá ngắn hoặc rỗng: {response?.Length ?? 0} bytes (cần tối thiểu {FixedHeaderSize} bytes).";
            }

            if (response[0] != 0xD0 || response[1] != 0x00)
            {
                return $"Subheader 3E phản hồi không hợp lệ: 0x{response[0]:X2} 0x{response[1]:X2} (yêu cầu 0xD0 0x00).";
            }

            if (response[2] != expectedNetworkNo)
            {
                return $"Network No không khớp: yêu cầu {expectedNetworkNo}, nhận được {response[2]}.";
            }

            if (response[3] != expectedPcNo)
            {
                return $"PC No không khớp: yêu cầu {expectedPcNo}, nhận được {response[3]}.";
            }

            // [4-5] Destination module I/O No. trong chuẩn 3E request là 0x03FF. Phản hồi phải khớp 0xFF 0x03.
            ushort ioNo = ReadUInt16LE(response, 4);
            if (ioNo != 0x03FF)
            {
                return $"Module I/O No không hợp lệ trong phản hồi 3E: 0x{ioNo:X4} (kỳ vọng 0x03FF).";
            }

            // [6] Destination module station No: mặc định 0x00 cho local CPU.
            if (response[6] != 0x00)
            {
                return $"Station No không hợp lệ trong phản hồi 3E: 0x{response[6]:X2} (kỳ vọng 0x00).";
            }

            return null;
        }

        internal static ushort ReadUInt16LE(byte[] buf, int offset)
            => (ushort)(buf[offset] | (buf[offset + 1] << 8));

        internal static string FormatEndCode(ushort code)
        {
            string desc;
            switch (code)
            {
                case 0x0055: desc = "Lỗi lệnh (command hoặc subcommand không hợp lệ)"; break;
                case 0xC050: desc = "Lỗi cài đặt thông số giao tiếp Ethernet"; break;
                case 0xC051: desc = "Số điểm ghi/đọc vượt quá giới hạn khung truyền"; break;
                case 0xC056: desc = "Mã thiết bị (Device code) không tồn tại"; break;
                case 0xC059: desc = "Không thể ghi dữ liệu vào thiết bị chỉ đọc"; break;
                case 0xC05B: desc = "Địa chỉ thiết bị vượt quá phạm vi bộ nhớ PLC"; break;
                case 0xC05C: desc = "Số lượng điểm đọc/ghi không hợp lệ"; break;
                case 0x4031: desc = "Không có quyền truy cập bộ nhớ"; break;
                default:     desc = "Xem bảng mã lỗi MELSEC Communication Protocol SH-080008"; break;
            }
            return $"PLC báo lỗi, End Code: 0x{code:X4} — {desc}";
        }
    }
}
