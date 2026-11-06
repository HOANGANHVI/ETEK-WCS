using System.Net;
using System.Text.RegularExpressions;

namespace WCS.Application.Config;

/// <summary>Pure validation of connection settings. When <c>enabled</c> is true the settings must be complete enough to attempt a connection.</summary>
public static partial class ConnectionValidator
{
    [GeneratedRegex(@"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$")]
    private static partial Regex HostnameRegex();

    /// <summary>Strict IP: IPAddress.TryParse alone also accepts legacy shorthand such as "192.168.1" or "3232235777".</summary>
    public static bool IsStrictIp(string value)
    {
        if (!IPAddress.TryParse(value, out _)) return false;
        if (value.Contains(':')) return true; // IPv6
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(p => p.Length is >= 1 and <= 3 && p.All(char.IsAsciiDigit));
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyList<FieldError> ValidatePlc(PlcSettings s, bool enabled)
    {
        var e = new List<FieldError>();
        if (s.DeviceName is { Length: > 100 }) e.Add(new("deviceName", "Tên thiết bị tối đa 100 ký tự."));

        if (s.IpAddress is not null)
        {
            if (!IsStrictIp(s.IpAddress)) e.Add(new("ipAddress", "Địa chỉ IP không hợp lệ."));
        }
        else if (enabled) e.Add(new("ipAddress", "Cần nhập địa chỉ IP trước khi bật kết nối."));

        Port(e, "port", s.Port, enabled);
        Range(e, "networkNumber", s.NetworkNumber, 0, 255, "Network number");
        Range(e, "pcNumber", s.PcNumber, 0, 255, "PC number");
        Range(e, "stationNumber", s.StationNumber, 0, 255, "PLC station number");
        Range(e, "pollingIntervalMs", s.PollingIntervalMs, 50, 3_600_000, "Chu kỳ đọc dữ liệu (ms)");

        if (s.Protocol is not null)
        {
            if (!ConnectionOptions.PlcProtocols.Contains(s.Protocol)) e.Add(new("protocol", "Giao thức không nằm trong danh sách được hỗ trợ."));
        }
        else if (enabled) e.Add(new("protocol", "Cần chọn giao thức trước khi bật kết nối."));

        Timeout(e, s.TimeoutMs, enabled, 100, 60_000);
        return e;
    }

    public static IReadOnlyList<FieldError> ValidateRcs(RcsSettings s, bool enabled, bool hasPassword, bool hasToken)
    {
        var e = new List<FieldError>();
        if (s.SystemName is { Length: > 100 }) e.Add(new("systemName", "Tên hệ thống tối đa 100 ký tự."));

        if (s.Host is not null)
        {
            var numericOnly = s.Host.All(ch => char.IsAsciiDigit(ch) || ch == '.');
            if (numericOnly ? !IsStrictIp(s.Host) : !HostnameRegex().IsMatch(s.Host))
                e.Add(new("host", "Địa chỉ IP hoặc hostname không hợp lệ."));
        }
        else if (enabled) e.Add(new("host", "Cần nhập địa chỉ IP hoặc hostname trước khi bật kết nối."));

        Port(e, "port", s.Port, enabled);

        if (s.BaseUrl is not null)
        {
            if (s.BaseUrl.Length > 300) e.Add(new("baseUrl", "Base URL tối đa 300 ký tự."));
            else if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                e.Add(new("baseUrl", "Base URL phải là địa chỉ tuyệt đối bắt đầu bằng http:// hoặc https://."));
            else if (!string.IsNullOrEmpty(uri.UserInfo)) e.Add(new("baseUrl", "Không nhập tài khoản/mật khẩu trong Base URL."));
            else if (s.Host is not null && !string.Equals(uri.Host, s.Host, StringComparison.OrdinalIgnoreCase))
                e.Add(new("baseUrl", "Host trong Base URL phải trùng với địa chỉ IP/hostname đã nhập."));
            else if (s.Port is not null && !uri.IsDefaultPort && uri.Port != s.Port)
                e.Add(new("baseUrl", "Port trong Base URL phải trùng với Port đã nhập."));
        }

        if (!ConnectionOptions.RcsAuthModes.Contains(s.AuthMode)) e.Add(new("authMode", "Phương thức xác thực không hợp lệ."));
        if (s.Username is { Length: > 100 }) e.Add(new("username", "Tên đăng nhập tối đa 100 ký tự."));
        if (s.AuthMode == ConnectionOptions.AuthUserPassword)
        {
            if (s.Username is null) e.Add(new("username", "Cần nhập tên đăng nhập."));
            if (enabled && !hasPassword) e.Add(new("password", "Cần nhập mật khẩu trước khi bật kết nối."));
        }
        if (s.AuthMode == ConnectionOptions.AuthToken && enabled && !hasToken) e.Add(new("token", "Cần nhập token trước khi bật kết nối."));

        Timeout(e, s.TimeoutMs, enabled, 100, 120_000);
        return e;
    }

    private static void Port(List<FieldError> e, string field, int? value, bool required)
    {
        if (value is null) { if (required) e.Add(new(field, "Cần nhập cổng trước khi bật kết nối.")); }
        else if (value is < 1 or > 65535) e.Add(new(field, "Cổng phải nằm trong khoảng 1–65535."));
    }

    private static void Range(List<FieldError> e, string field, int? value, int min, int max, string label)
    {
        if (value is not null && (value < min || value > max)) e.Add(new(field, $"{label} phải nằm trong khoảng {min}–{max}."));
    }

    private static void Timeout(List<FieldError> e, int? value, bool required, int min, int max)
    {
        if (value is null) { if (required) e.Add(new("timeoutMs", "Cần nhập timeout trước khi bật kết nối.")); }
        else if (value < min || value > max) e.Add(new("timeoutMs", $"Timeout phải nằm trong khoảng {min}–{max} ms."));
    }
}
