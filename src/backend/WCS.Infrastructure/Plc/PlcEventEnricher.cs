using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WCS.Infrastructure.Plc;

/// <summary>Structured facts derived from a PLC event (CR-007): where it happened, what changed and what kind of failure it was.</summary>
public sealed record PlcEventDetails(string? Ip, int? Port, string? PreviousState, string? NewState, string? Kind, string? EndCode, string? Reason)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [JsonIgnore]
    public bool IsEmpty => Ip is null && Port is null && PreviousState is null && NewState is null && Kind is null && EndCode is null && Reason is null;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Short human text appended to the log line, e.g. " (Connected -> Faulted; 192.168.10.101:5002; TIMEOUT)".</summary>
    public string Context()
    {
        var parts = new List<string>();
        if (PreviousState is not null || NewState is not null) parts.Add($"{PreviousState ?? "?"} -> {NewState ?? "?"}");
        if (Ip is not null) parts.Add(Port is { } p ? $"{Ip}:{p}" : Ip);
        if (Kind is not null) parts.Add(Kind);
        if (EndCode is not null) parts.Add($"end code {EndCode}");
        if (Reason is not null) parts.Add(Reason);
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }
}

/// <summary>
/// Classifies PLC events from text the workers already produce, so the worker, the MC driver and the polling logic stay untouched. Pure functions (unit tested).
/// </summary>
public static partial class PlcEventEnricher
{
    [GeneratedRegex(@"end code 0x([0-9A-Fa-f]{1,4})", RegexOptions.IgnoreCase)]
    private static partial Regex EndCodeRx();

    public static PlcEventDetails Build(string? plcId, string eventName, string message, string? ip, int? port, string? previousState, double? faultedForSeconds)
    {
        string? prev = null, now = null, kind = null, endCode = null, reason = null;

        if (eventName == "STATE" && plcId is not null)
        {
            var prefix = plcId + ": ";
            var text = message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
            var cut = text.IndexOf(" - ", StringComparison.Ordinal);
            now = (cut < 0 ? text : text[..cut]).Trim();
            if (cut >= 0) reason = text[(cut + 3)..].Trim();
            prev = previousState;
            if (now == "Connected" && prev is "Faulted" or "Connecting" && faultedForSeconds is { } s)
            {
                kind = "RECONNECTED";
                reason = $"reconnected after {s:0} s";
            }
        }

        var m = EndCodeRx().Match(message);
        if (m.Success) { kind ??= "END_CODE"; endCode = "0x" + m.Groups[1].Value.ToUpperInvariant().PadLeft(4, '0'); }
        else if (message.Contains("no response within", StringComparison.OrdinalIgnoreCase) || message.Contains("timed out", StringComparison.OrdinalIgnoreCase) || message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            kind ??= "TIMEOUT";
        else if (eventName == "WRITE_AMBIGUOUS") kind ??= "WRITE_AMBIGUOUS";
        else if (eventName is "CONNECT_FAILED" or "LINK_LOST" || message.Contains("connection closed", StringComparison.OrdinalIgnoreCase)) kind ??= "CONNECTION";

        return new PlcEventDetails(ip, port, prev, now, kind, endCode, reason is { Length: > 200 } r ? r[..200] : reason);
    }
}
