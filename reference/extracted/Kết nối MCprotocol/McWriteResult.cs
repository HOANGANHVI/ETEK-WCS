namespace MitsubishiMc.Client
{
    /// <summary>
    /// Kết quả của một thao tác ghi dữ liệu qua Mitsubishi MC Protocol (3E Binary).
    /// </summary>
    public class McWriteResult
    {
        /// <summary>True nếu PLC phản hồi End Code = 0x0000 (thành công).</summary>
        public bool Success { get; private set; }

        /// <summary>Thông báo lỗi chi tiết khi Success = false.</summary>
        public string ErrorMessage { get; private set; }

        /// <summary>Mã End Code từ PLC Mitsubishi (0x0000 nếu thành công).</summary>
        public ushort EndCode { get; private set; }

        /// <summary>
        /// True nếu kết quả ghi bị mơ hồ (ambiguous) do timeout hoặc đứt mạng giữa chừng.
        /// Khi xảy ra, lệnh có thể đã được PLC ghi nhưng không nhận được phản hồi xác nhận.
        /// Tuyệt đối KHÔNG tự ý thử ghi lại (retry) mà cần kiểm tra trạng thái thực tế trên PLC.
        /// </summary>
        public bool IsAmbiguous { get; private set; }

        /// <summary>Chuỗi hex của khung request đã gửi (để phục vụ chẩn đoán truyền thông).</summary>
        public string RequestHex { get; internal set; }

        /// <summary>Chuỗi hex của khung response nhận được từ PLC (null nếu timeout/mất kết nối).</summary>
        public string ResponseHex { get; internal set; }

        private McWriteResult() { }

        internal static McWriteResult Ok(string reqHex = null, string respHex = null) =>
            new McWriteResult
            {
                Success = true,
                EndCode = 0x0000,
                RequestHex = reqHex,
                ResponseHex = respHex
            };

        internal static McWriteResult Fail(string error, ushort endCode = 0, bool isAmbiguous = false, string reqHex = null, string respHex = null) =>
            new McWriteResult
            {
                Success = false,
                ErrorMessage = error ?? "Lỗi ghi không xác định.",
                EndCode = endCode,
                IsAmbiguous = isAmbiguous,
                RequestHex = reqHex,
                ResponseHex = respHex
            };
    }
}
