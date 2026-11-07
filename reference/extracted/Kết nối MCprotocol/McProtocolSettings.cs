namespace MitsubishiMc.Client
{
    /// <summary>
    /// Cài đặt kết nối Mitsubishi MC Protocol (3E Binary frame).
    /// </summary>
    public class McProtocolSettings
    {
        /// <summary>Địa chỉ IP của Ethernet module trên PLC Mitsubishi.</summary>
        public string IpAddress { get; set; } = "192.168.3.39";

        /// <summary>
        /// Cổng TCP. Thường:
        ///   5000 — Q-series, iQ-F (FX5)
        ///   5007 — iQ-R series
        /// Lưu ý: Cấu hình cổng trong GX Works2 Open Settings thường hiển thị dạng HEX (ví dụ 0x1388 = 5000 dec).
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>Thời gian chờ kết nối và nhận dữ liệu (millisecond).</summary>
        public int TimeoutMs { get; set; } = 3000;

        /// <summary>
        /// Network No. trong frame 3E.
        /// 0x00 = mạng nội bộ (local network). Thường để 0.
        /// </summary>
        public byte NetworkNo { get; set; } = 0x00;

        /// <summary>
        /// PC No. trong frame 3E.
        /// 0xFF = chỉ định CPU station (mặc định). Thường để 0xFF.
        /// </summary>
        public byte PcNo { get; set; } = 0xFF;

        /// <summary>
        /// CPU Monitoring Timer trong frame 3E (đơn vị: 250ms).
        /// Mặc định = 0 (tự động đồng bộ theo TimeoutMs: Math.Max(1, TimeoutMs / 250)).
        /// Nếu thiết lập > 0, giá trị này sẽ được ghi đè cố định theo đơn vị 250ms (vd: 12 = 3000ms).
        /// </summary>
        public ushort CpuMonitoringTimerUnits { get; set; } = 0;
    }
}
