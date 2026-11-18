using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>
/// CR-014: checks a value against the signal it is written to, BEFORE anything is queued for the PLC. Strict: nothing is converted or truncated.
/// It mirrors what the MC driver can really send (BOOL = one bit; INT = signed 16-bit word; STRING = ASCII padded with zeros into <c>WordLength</c> words), so a value that
/// passes here is never rejected later for its shape. A data type or encoding the driver cannot write is reported as unsupported, never coerced.
/// </summary>
public static class WriteValueValidator
{
    public const int IntMin = short.MinValue, IntMax = short.MaxValue;

    public sealed record Verdict(bool Ok, string? Code = null, string? Message = null)
    {
        public static readonly Verdict Valid = new(true);
    }

    /// <summary>Whether writing this combination is supported at all (shown as "not supported" by the UI).</summary>
    public static bool IsWriteSupported(PlcDataType type, string? encoding) =>
        type is PlcDataType.Bool or PlcDataType.Int || (type == PlcDataType.String && string.Equals(encoding, "ASCII", StringComparison.OrdinalIgnoreCase));

    public static Verdict Check(PlcDataType type, int? wordLength, string? encoding, PointValue value)
    {
        if (!IsWriteSupported(type, encoding))
            return new(false, "WRITE_TYPE_UNSUPPORTED", type == PlcDataType.String ? "Chỉ ghi được STRING mã hóa ASCII." : $"Kiểu dữ liệu {type} chưa hỗ trợ ghi.");
        if (value.Type != type || value.Value is null) return new(false, "TYPE_MISMATCH", $"Tín hiệu kiểu {type}, giá trị kiểu {value.Type}.");
        switch (type)
        {
            case PlcDataType.Bool:
                return value.Value is bool ? Verdict.Valid : new(false, "TYPE_MISMATCH", "BOOL chỉ nhận true/false.");
            case PlcDataType.Int:
                if (value.Value is not int i) return new(false, "TYPE_MISMATCH", "INT cần một số nguyên.");
                return i is < IntMin or > IntMax ? new(false, "VALUE_OUT_OF_RANGE", $"INT (16 bit có dấu) chỉ nhận từ {IntMin} đến {IntMax}.") : Verdict.Valid;
            default:
                if (value.Value is not string text) return new(false, "TYPE_MISMATCH", "STRING cần một chuỗi.");
                if (wordLength is not > 0) return new(false, "STRING_LENGTH_UNKNOWN", "STRING chưa có độ dài (word) được cấu hình.");
                if (text.Any(ch => ch > 0x7F)) return new(false, "STRING_NOT_ASCII", "STRING chỉ chấp nhận ký tự ASCII.");
                return text.Length > wordLength * 2 ? new(false, "STRING_TOO_LONG", $"STRING dài tối đa {wordLength * 2} ký tự ({wordLength} word).") : Verdict.Valid;
        }
    }
}
