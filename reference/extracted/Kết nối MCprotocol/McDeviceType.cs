namespace MitsubishiMc.Client
{
    /// <summary>
    /// Loại thiết bị PLC Mitsubishi được hỗ trợ qua MC Protocol 3E Binary.
    ///
    /// LƯU Ý VỀ ĐỊA CHỈ:
    ///   D, W, M — địa chỉ thập phân trong PLC và trong thư viện này.
    ///             Vd: D100 → startAddress = 100
    ///   X, Y    — địa chỉ BÁT PHÂN trong tài liệu PLC, nhưng thư viện nhận THẬP PHÂN.
    ///             Vd: X10 (octal) = X008 (decimal) → startAddress = 8
    ///             Vd: Y20 (octal) = Y016 (decimal) → startAddress = 16
    /// </summary>
    public enum McDeviceType
    {
        /// <summary>Data Register — word device, địa chỉ thập phân. Vd: D100</summary>
        D,

        /// <summary>Internal Relay — bit device, địa chỉ thập phân. Vd: M100</summary>
        M,

        /// <summary>
        /// Input Relay — bit device.
        /// Địa chỉ trong PLC manual là bát phân (X0, X10, X20...),
        /// nhưng truyền vào thư viện theo thập phân.
        /// Vd: X10 (octal) → startAddress = 8 (decimal).
        /// </summary>
        X,

        /// <summary>
        /// Output Relay — bit device.
        /// Tương tự X: địa chỉ bát phân trong PLC, truyền thập phân.
        /// Vd: Y20 (octal) → startAddress = 16 (decimal).
        /// </summary>
        Y,

        /// <summary>Link Register — word device, địa chỉ thập phân. Vd: W100</summary>
        W,
    }
}
