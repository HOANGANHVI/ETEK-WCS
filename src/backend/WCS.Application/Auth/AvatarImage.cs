using WCS.Application.Common;

namespace WCS.Application.Auth;

/// <summary>
/// Validation of an uploaded avatar. The type comes from the CONTENT (magic bytes + a structurally complete file), never from the file name or the
/// client-supplied content type. Only PNG and JPEG are accepted (no SVG/HTML/script-capable formats, no animation, no metadata containers we would not parse).
/// </summary>
public static class AvatarImage
{
    public const int MaxBytes = 512 * 1024;
    public const int MinPixels = 32;
    public const int MaxPixels = 2048;
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    public sealed record Checked(string ContentType, int Width, int Height);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngIend = [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    public static Checked Validate(byte[] data)
    {
        if (data.Length == 0) throw AppException.Validation("AVATAR_EMPTY", "Chưa chọn tệp ảnh.");
        if (data.Length > MaxBytes) throw AppException.Validation("AVATAR_TOO_LARGE", "Ảnh đại diện tối đa 512 KB.");

        Checked? result = null;
        if (data.AsSpan().StartsWith(PngSignature)) result = ReadPng(data);
        else if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) result = ReadJpeg(data);
        else throw AppException.Validation("AVATAR_UNSUPPORTED_TYPE", "Chỉ chấp nhận ảnh PNG hoặc JPEG.");

        if (result is null) throw AppException.Validation("AVATAR_INVALID_IMAGE", "Tệp không phải ảnh hợp lệ.");
        if (result.Width < MinPixels || result.Height < MinPixels || result.Width > MaxPixels || result.Height > MaxPixels)
            throw AppException.Validation("AVATAR_DIMENSIONS", "Kích thước ảnh phải từ 32×32 đến 2048×2048 điểm ảnh.");
        return result;
    }

    private static int BigEndian32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
    private static int BigEndian16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

    private static Checked? ReadPng(byte[] d)
    {
        // signature (8) + IHDR chunk (length 13, "IHDR", 13 bytes, CRC) and an IEND trailer.
        if (d.Length < 8 + 25 + PngIend.Length) return null;
        if (BigEndian32(d, 8) != 13 || d[12] != 'I' || d[13] != 'H' || d[14] != 'D' || d[15] != 'R') return null;
        if (!d.AsSpan(d.Length - PngIend.Length).SequenceEqual(PngIend)) return null;
        var w = BigEndian32(d, 16); var h = BigEndian32(d, 20);
        return w <= 0 || h <= 0 ? null : new Checked(Png, w, h);
    }

    private static Checked? ReadJpeg(byte[] d)
    {
        if (d[^2] != 0xFF || d[^1] != 0xD9) return null;
        var pos = 2;
        while (pos + 4 <= d.Length)
        {
            if (d[pos] != 0xFF) return null;
            var marker = d[pos + 1];
            if (marker == 0xFF) { pos++; continue; }                       // fill byte
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { pos += 2; continue; }   // markers without a length
            if (marker == 0xDA || marker == 0xD9) return null;            // image data / end before any frame header
            var len = BigEndian16(d, pos + 2);
            if (len < 2 || pos + 2 + len > d.Length) return null;
            var isFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrame)
            {
                if (len < 8) return null;
                var h = BigEndian16(d, pos + 5); var w = BigEndian16(d, pos + 7);
                return w <= 0 || h <= 0 ? null : new Checked(Jpeg, w, h);
            }
            pos += 2 + len;
        }
        return null;
    }
}
