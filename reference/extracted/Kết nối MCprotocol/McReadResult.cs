namespace MitsubishiMc.Client
{
    /// <summary>
    /// Kết quả của một thao tác đọc qua MC Protocol (3E Binary).
    /// </summary>
    public class McReadResult
    {
        /// <summary>True nếu đọc thành công.</summary>
        public bool Success { get; private set; }

        /// <summary>Thông báo lỗi (chỉ có giá trị khi Success = false).</summary>
        public string ErrorMessage { get; private set; }

        /// <summary>Mã End Code từ PLC Mitsubishi (0x0000 nếu thành công).</summary>
        public ushort EndCode { get; private set; }

        /// <summary>
        /// Giá trị word device (D, W). Mỗi phần tử = 1 word 16-bit (little-endian từ PLC).
        /// Null khi đọc bit device.
        /// </summary>
        public ushort[] WordValues { get; private set; }

        /// <summary>
        /// Giá trị bit device (M, X, Y). true = ON, false = OFF.
        /// Null khi đọc word device.
        /// </summary>
        public bool[] BitValues { get; private set; }

        /// <summary>Chuỗi hex của khung request đã gửi (để phục vụ chẩn đoán truyền thông).</summary>
        public string RequestHex { get; internal set; }

        /// <summary>Chuỗi hex của khung response nhận được từ PLC (null nếu timeout/mất kết nối).</summary>
        public string ResponseHex { get; internal set; }

        private McReadResult() { }

        internal static McReadResult OkWord(ushort[] values, string reqHex = null, string respHex = null) =>
            new McReadResult
            {
                Success = true,
                EndCode = 0x0000,
                WordValues = values,
                RequestHex = reqHex,
                ResponseHex = respHex
            };

        internal static McReadResult OkBit(bool[] values, string reqHex = null, string respHex = null) =>
            new McReadResult
            {
                Success = true,
                EndCode = 0x0000,
                BitValues = values,
                RequestHex = reqHex,
                ResponseHex = respHex
            };

        internal static McReadResult Fail(string error, ushort endCode = 0, string reqHex = null, string respHex = null) =>
            new McReadResult
            {
                Success = false,
                ErrorMessage = error ?? "Lỗi đọc không xác định.",
                EndCode = endCode,
                RequestHex = reqHex,
                ResponseHex = respHex
            };
    }
}
