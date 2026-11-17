using System.Text.Json;
using System.Text.RegularExpressions;

namespace WCS.Domain.Tasks;

/// <summary>
/// Stable, language-neutral description of a task history entry or state reason: a CODE plus its parameters. The UI phrases it in the language the user
/// selected; the original English text stays in <c>Detail</c> / <c>StateReason</c> for diagnostics and audit. Parameters are technical values (ids, codes,
/// counts, user text) and are never translated.
/// </summary>
public sealed record HistoryDetail(string Code, IReadOnlyDictionary<string, string>? Args = null)
{
    public static HistoryDetail Of(string code, params (string Key, object? Value)[] args) =>
        new(code, args.Length == 0 ? null : args.Where(a => a.Value is not null).ToDictionary(a => a.Key, a => Convert.ToString(a.Value, System.Globalization.CultureInfo.InvariantCulture) ?? ""));

    public string? ArgsJson => Args is { Count: > 0 } ? JsonSerializer.Serialize(Args) : null;

    public static IReadOnlyDictionary<string, string>? ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); } catch (JsonException) { return null; }
    }

    // ------------------------------------------------------------------------------------------------------------- reasons that already are codes
    /// <summary>Reason codes the orchestrator uses as the reason text itself (the text IS the code).</summary>
    public static readonly IReadOnlySet<string> ReasonCodes = new HashSet<string>
    {
        "AGV_UNAVAILABLE", "NO_SOURCE_AVAILABLE", "RCS_DISPATCH_UNCERTAIN", "CANCEL_RETRY_LIMIT_REACHED", "CANCEL_RACE_RCS_COMPLETED",
        "CANCEL_RACE_RCS_INTERRUPTED", "RCS_NOTFOUND_AFTER_ACCEPT", "RCS_ENDED_WITHOUT_DROP",
    };

    private static readonly Regex Timeout = new(@"^TIMEOUT_([A-Za-z]+)$", RegexOptions.Compiled);
    private static readonly Regex Rejected = new(@"^(RCS_REJECTED|RCS_CONTINUE_REJECTED): ?([\s\S]*)$", RegexOptions.Compiled);

    /// <summary>
    /// The code of a reason text that the code itself produced (<c>NO_SOURCE_AVAILABLE</c>, <c>TIMEOUT_Executing</c>, <c>RCS_REJECTED: msg</c>). Any other text returns
    /// null: it is never guessed.
    /// </summary>
    public static HistoryDetail? FromCodedReason(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return null;
        if (ReasonCodes.Contains(reason)) return new(reason);
        if (Timeout.Match(reason) is { Success: true } t) return Of("TIMEOUT", ("state", t.Groups[1].Value));
        if (Rejected.Match(reason) is { Success: true } r) return Of(r.Groups[1].Value, ("message", r.Groups[2].Value));
        return null;
    }

    // ------------------------------------------------------------------------------------------------------------- history written before codes existed
    private static readonly Dictionary<string, string> Exact = new()
    {
        ["Validating business conditions"] = "VALIDATING",
        ["AGV busy; request queued"] = "QUEUED_AGV_BUSY",
        ["AGV on dispatch hold; request queued"] = "QUEUED_AGV_HOLD",
        ["AGV free; claimed"] = "DISPATCHING_CLAIMED",
        ["AGV free; claimed from queue"] = "DISPATCHING_FROM_QUEUE",
        ["RCS accepted the task"] = "RCS_ACCEPTED",
        ["AGV executing"] = "AGV_EXECUTING",
        ["PLC permission granted"] = "PLC_PERMISSION_GRANTED",
        ["No destination available"] = "NO_DESTINATION_AVAILABLE",
        ["RCS continued the task"] = "RCS_CONTINUED",
        ["Drop confirmed; inventory updated"] = "DROP_CONFIRMED",
        ["RCS reported cancellation"] = "RCS_REPORTED_CANCELLATION",
        ["Cancel confirmed by RCS"] = "CANCEL_CONFIRMED_BY_RCS",
        ["Cancel confirmed by RCS status query"] = "CANCEL_CONFIRMED_BY_RCS_QUERY",
        ["RCS reported the task interrupted"] = "RCS_REPORTED_INTERRUPTED",
        ["Cancelled before reaching RCS"] = "CANCELLED_BEFORE_RCS",
        ["No source rack available yet; will be re-evaluated"] = "WAITING_FOR_SOURCE",
        ["Status query inconclusive; will retry"] = "RCS_QUERY_INCONCLUSIVE",
        ["RCS is cancelling the task"] = "RCS_CANCEL_IN_PROGRESS",
        ["Pick confirmed while cancelling; rack is IN_TRANSIT"] = "RECON_PICK_WHILE_CANCELLING",
        ["RCS reported end without WCS drop confirmation"] = "RECON_RCS_ENDED_WITHOUT_DROP",
    };

    private static readonly (string? Event, Regex Pattern, string Code, string[] Names)[] Patterns =
    [
        (null, new(@"^Waiting for PLC permission \((Pick|Drop)\)$", RegexOptions.Compiled), "WAITING_PLC_PERMISSION", ["leg"]),
        (null, new(@"^Destination assigned: (\S+)$", RegexOptions.Compiled), "DESTINATION_ASSIGNED", ["location"]),
        (null, new(@"^Cancel requested by ([^:]+): ([\s\S]*)$", RegexOptions.Compiled), "CANCEL_REQUESTED", ["user", "reason"]),
        ("SOURCE_RESERVED", new(@"^location=(\S+)$", RegexOptions.Compiled), "SOURCE_RESERVED", ["location"]),
        ("DESTINATION_RESERVED", new(@"^location=(\S+)$", RegexOptions.Compiled), "DESTINATION_RESERVED", ["location"]),
        ("LATE_EVENT_IGNORED", new(@"^(\w+) after (\w+)$", RegexOptions.Compiled), "LATE_EVENT_IGNORED", ["kind", "state"]),
        ("OUT_OF_ORDER_EVENT_IGNORED", new(@"^(\w+) in (\w+)$", RegexOptions.Compiled), "OUT_OF_ORDER_EVENT_IGNORED", ["kind", "state"]),
        ("RCS_CONTINUE_RETRY_EXHAUSTED", new(@"^(\d+) attempts without a confirmed result; automatic retry stopped$", RegexOptions.Compiled), "RCS_CONTINUE_RETRY_EXHAUSTED", ["attempts"]),
        ("RCS_CANCEL_RETRY_EXHAUSTED", new(@"^RCS did not confirm the cancellation after (\d+) automatic attempts \(last attempt (.+?), limit reached at (.+?)\)\. Automatic retry stopped; this is not a confirmation that RCS cancelled the task\.$", RegexOptions.Compiled), "RCS_CANCEL_RETRY_EXHAUSTED", ["attempts", "lastAttempt", "limitAt"]),
        ("AGV_DISPATCH_HOLD", new(@"^(\S+) placed on dispatch hold: ([\s\S]+)$", RegexOptions.Compiled), "AGV_DISPATCH_HOLD", ["agv", "reason"]),
        ("AGV_DISPATCH_HOLD_RELEASED", new(@"^([^:]+): inventory reconciled=yes, AGV verified on site=yes\. ([\s\S]*)$", RegexOptions.Compiled), "AGV_DISPATCH_HOLD_RELEASED", ["actor", "note"]),
        ("RECONCILED", new(@"^actual=([^;]+); by=([^;]*); ([\s\S]*)$", RegexOptions.Compiled), "RECONCILED", ["actual", "by", "note"]),
        ("RECONCILIATION_CHECK_UNRESOLVED", new(@"^([^:]+): ([\s\S]*)$", RegexOptions.Compiled), "RECONCILIATION_CHECK_UNRESOLVED", ["actor", "note"]),
    ];

    /// <summary>
    /// Reads a stored entry that has no code. ONLY the exact sentences / formats this system wrote itself are recognised (anchored on the whole text, and scoped to the
    /// event name where the format is ambiguous); everything else returns null so the original text is shown unchanged. Nothing is written back to the database.
    /// </summary>
    public static HistoryDetail? Legacy(string eventName, string? detail)
    {
        if (string.IsNullOrEmpty(detail)) return null;
        if (Exact.TryGetValue(detail, out var code)) return new(code);
        if (FromCodedReason(detail) is { } coded) return coded;
        foreach (var (ev, pattern, c, names) in Patterns)
        {
            if (ev is not null && ev != eventName) continue;
            if (pattern.Match(detail) is not { Success: true } m) continue;
            return Of(c, names.Select((n, i) => (n, (object?)m.Groups[i + 1].Value)).ToArray());
        }
        return null;
    }
}
