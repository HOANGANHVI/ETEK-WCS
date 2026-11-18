using System.Text.RegularExpressions;

namespace WCS.Application.Logging;

/// <summary>
/// CR-007: last line of defence so that secrets never reach a log store or the log screen. Applied when a log entry is persisted, when deployment events are
/// imported and again when entries are returned. It never throws: on any problem the text is returned unchanged only if it contains no secret-like key.
/// </summary>
public static partial class LogSanitizer
{
    public const string Mask = "***";

    // key = value / key: value / "key":"value" for the usual secret-bearing names (also inside connection strings: Password=...;)
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|authorization|cookie|set-cookie|connection[_-]?string|private[_-]?key|bootstrap[_-]?admin[_-]?password|postgres_password)\b([""']?\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s;,&""']+)")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"(?i)\b(bearer|basic)\s+[A-Za-z0-9\-\._~\+/]{6,}=*")]
    private static partial Regex AuthScheme();

    public static string? Redact(string? text, int maxLength = 8000)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            var t = text.Length > maxLength ? text[..maxLength] + "…(truncated)" : text;
            t = AuthScheme().Replace(t, m => m.Groups[1].Value + " " + Mask);   // first: "Authorization: Bearer xyz" must not leave xyz behind
            t = KeyValue().Replace(t, m => m.Groups[1].Value + m.Groups[2].Value + Mask);
            return t;
        }
        catch (Exception)
        {
            return KeyValue().IsMatch(text) ? Mask : text;
        }
    }

    /// <summary>True when the text still contains something that looks like an unmasked secret (used by tests and as a guard).</summary>
    public static bool LooksSecret(string? text) =>
        !string.IsNullOrEmpty(text) && KeyValue().Matches(text).Any(m => !m.Groups[3].Value.Trim('"', '\'').Equals(Mask, StringComparison.Ordinal));
}
