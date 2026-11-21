using System.Text.Json;

namespace WCS.Application.Fleet;

/// <summary>
/// Reads the response of <c>POST /rcms-dps/rest/queryAgvStatus</c> exactly as RCS-2000 API Developer Guide V3.3 (5.2.14, pp. 46-48) describes it:
/// <c>code</c> ("0" = succeeded, Appendix A), <c>message</c>, <c>reqCode</c> and <c>data[]</c> with robotCode, robotDir, robotIp, battery, posX, posY, mapCode, speed, status,
/// exclType, stop, podCode, podDir and path[]. A response that does not have this shape is a failure (RCS_RESPONSE_INVALID): no AMR is invented from it.
/// </summary>
public static class HikQueryAgvStatusParser
{
    public static RcsFleetResult Parse(string json, long? latencyMs = null)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return RcsFleetResult.Fail(FleetErrorCodes.InvalidResponse, "The response is not valid JSON.", latencyMs); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return RcsFleetResult.Fail(FleetErrorCodes.InvalidResponse, "The response is not a JSON object.", latencyMs);
            if (!root.TryGetProperty("code", out var codeEl)) return RcsFleetResult.Fail(FleetErrorCodes.InvalidResponse, "The response has no 'code'.", latencyMs);
            var code = Text(codeEl);
            if (code != "0")
            {
                var message = root.TryGetProperty("message", out var m) ? Text(m) : null;
                // Appendix A: 1 failed, 6 no need to resend, 99 unknown error - the number and the RCS message are reported as they are
                // (only the two checked fields, bounded and stripped of anything that looks like a credential; the body itself is never kept)
                var bounded = FleetService.Sanitize(message);
                return RcsFleetResult.Fail($"{FleetErrorCodes.Rejected}:{code}", bounded, latencyMs) with { RcsCode = code, RcsMessage = bounded };
            }
            if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return RcsFleetResult.Ok([], latencyMs);   // no AMR
            if (data.ValueKind != JsonValueKind.Array) return RcsFleetResult.Fail(FleetErrorCodes.InvalidResponse, "'data' is not a list.", latencyMs);

            var amrs = new List<RcsFleetAmr>(); var seen = new HashSet<string>(StringComparer.Ordinal); var skipped = 0;
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { skipped++; continue; }
                var robotCode = Str(item, "robotCode");
                if (string.IsNullOrWhiteSpace(robotCode)) { skipped++; continue; }              // an entry without an id cannot be identified: it is not kept
                if (!seen.Add(robotCode)) { skipped++; continue; }                               // the same robotCode twice: the first entry stays
                var path = new List<string>();
                if (item.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.Array)
                    foreach (var step in p.EnumerateArray()) if (Text(step) is { } s) path.Add(s);
                amrs.Add(new RcsFleetAmr(robotCode, Str(item, "robotDir"), Str(item, "robotIp"), Str(item, "battery"), Str(item, "posX"), Str(item, "posY"), Str(item, "mapCode"),
                    Str(item, "speed"), Str(item, "status"), Str(item, "exclType"), Str(item, "stop"), Str(item, "podCode"), Str(item, "podDir"), path));
            }
            return RcsFleetResult.Ok(amrs, latencyMs, skipped);
        }
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) ? Text(v) : null;

    /// <summary>The API sends every value as a string; a number is accepted too and kept as its text.</summary>
    private static string? Text(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };
}
