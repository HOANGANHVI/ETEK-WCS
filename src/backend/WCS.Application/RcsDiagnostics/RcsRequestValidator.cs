using System.Globalization;
using System.Text.Json;

namespace WCS.Application.RcsDiagnostics;

public sealed record RcsValidationIssue(string Path, string Code, string Message);

public sealed record RcsValidationResult(IReadOnlyList<RcsValidationIssue> Errors, IReadOnlyList<RcsValidationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks a request body against the catalog entry (the document's field table): required fields, type, maximum length, enumerations, numeric range, array size and the cross-field rules. A request that fails is NEVER sent.
/// Fields the document does not define only produce a warning (the Admin may be testing something the document leaves open); the keys that could redirect the call (host, port, url ...) are refused outright.
/// </summary>
public static class RcsRequestValidator
{
    /// <summary>Top-level keys that would try to choose WHERE the request goes. The destination is always the saved RCS connection + the catalog path (no SSRF).</summary>
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase) { "host", "port", "url", "uri", "baseurl", "endpoint", "address", "hostname", "server", "path", "scheme" };

    public static RcsValidationResult Validate(RcsApiDef api, JsonElement body)
    {
        var errors = new List<RcsValidationIssue>(); var warnings = new List<RcsValidationIssue>();
        if (body.ValueKind != JsonValueKind.Object) { errors.Add(new("$", "BODY_NOT_OBJECT", "The request body must be a JSON object.")); return new(errors, warnings); }

        foreach (var p in body.EnumerateObject())
            if (Forbidden.Contains(p.Name)) errors.Add(new(p.Name, "FORBIDDEN_FIELD", $"'{p.Name}' is not allowed: the destination is always the saved RCS connection and the catalog path."));

        ValidateFields(RcsApiCatalog.RequestFields(api), body, "", errors, warnings);

        foreach (var rule in api.Rules)
        {
            bool Has(string f) => body.TryGetProperty(f, out var v) && !IsBlank(v);
            switch (rule.Kind)
            {
                case "AtLeastOne" when !rule.Fields.Any(Has): errors.Add(new(string.Join("|", rule.Fields), "RULE_AT_LEAST_ONE", rule.Message)); break;
                case "ExactlyOne" when rule.Fields.Count(Has) != 1: errors.Add(new(string.Join("|", rule.Fields), "RULE_EXACTLY_ONE", rule.Message)); break;
                case "RequiredWhen" when rule.WhenField is not null && body.TryGetProperty(rule.WhenField, out var w) && Text(w) == rule.WhenValue && !Has(rule.Fields[0]):
                    errors.Add(new(rule.Fields[0], "RULE_REQUIRED_WHEN", rule.Message)); break;
            }
        }
        return new(errors, warnings);
    }

    private static bool IsBlank(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()),
        JsonValueKind.Array => v.GetArrayLength() == 0,
        _ => false,
    };

    private static string? Text(JsonElement v) => v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };

    private static void ValidateFields(IReadOnlyList<RcsField> fields, JsonElement obj, string prefix, List<RcsValidationIssue> errors, List<RcsValidationIssue> warnings)
    {
        var known = fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
            if (!known.Contains(p.Name) && !(prefix == "" && Forbidden.Contains(p.Name)))
                warnings.Add(new(prefix + p.Name, "UNKNOWN_FIELD", $"'{prefix}{p.Name}' is not a field of this API in the document; it is sent as typed."));

        foreach (var f in fields)
        {
            var path = prefix + f.Name;
            if (!obj.TryGetProperty(f.Name, out var v) || IsBlank(v))
            {
                if (f.Required) errors.Add(new(path, "REQUIRED", $"'{path}' is required."));
                continue;
            }
            switch (f.Type)
            {
                case "string":
                    if (v.ValueKind != JsonValueKind.String) { errors.Add(new(path, "TYPE_STRING", $"'{path}' must be a string (the document defines every text field as String).")); break; }
                    CheckString(f, path, v.GetString()!, errors); break;
                case "number":
                    if (v.ValueKind == JsonValueKind.Number) { CheckEnum(f, path, v.GetRawText(), errors); break; }
                    if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) { CheckEnum(f, path, v.GetString()!, errors); break; }
                    errors.Add(new(path, "TYPE_NUMBER", $"'{path}' must be a number (or a numeric string).")); break;
                case "string[]":
                    if (v.ValueKind != JsonValueKind.Array) { errors.Add(new(path, "TYPE_ARRAY", $"'{path}' must be an array of strings.")); break; }
                    var i = 0;
                    foreach (var item in v.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) errors.Add(new($"{path}[{i}]", "TYPE_STRING", $"'{path}[{i}]' must be a string."));
                        else if (f.MaxLength is { } m && item.GetString()!.Length > m) errors.Add(new($"{path}[{i}]", "MAX_LENGTH", $"'{path}[{i}]' is longer than {m} characters."));
                        i++;
                    }
                    break;
                case "object":
                    if (v.ValueKind != JsonValueKind.Object) { errors.Add(new(path, "TYPE_OBJECT", $"'{path}' must be an object.")); break; }
                    if (f.Children is { Count: > 0 }) ValidateFields(f.Children, v, path + ".", errors, warnings);
                    break;
                case "object[]":
                    if (v.ValueKind != JsonValueKind.Array) { errors.Add(new(path, "TYPE_ARRAY", $"'{path}' must be an array of objects.")); break; }
                    if (f.MaxItems is { } mi && v.GetArrayLength() > mi) errors.Add(new(path, "MAX_ITEMS", $"'{path}' allows at most {mi} items."));
                    var n = 0;
                    foreach (var item in v.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) errors.Add(new($"{path}[{n}]", "TYPE_OBJECT", $"'{path}[{n}]' must be an object."));
                        else if (f.Children is { Count: > 0 }) ValidateFields(f.Children, item, $"{path}[{n}].", errors, warnings);
                        n++;
                    }
                    break;
            }
        }
    }

    private static void CheckString(RcsField f, string path, string s, List<RcsValidationIssue> errors)
    {
        if (f.MaxLength is { } m && s.Length > m) errors.Add(new(path, "MAX_LENGTH", $"'{path}' is longer than {m} characters (it has {s.Length})."));
        CheckEnum(f, path, s, errors);
        if ((f.Min is not null || f.Max is not null) && !(int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= (f.Min ?? int.MinValue) && n <= (f.Max ?? int.MaxValue)))
            errors.Add(new(path, "RANGE", $"'{path}' must be a whole number between {f.Min} and {f.Max}."));
    }

    private static void CheckEnum(RcsField f, string path, string s, List<RcsValidationIssue> errors)
    {
        if (f.Enum is { Count: > 0 } e && !e.Contains(s)) errors.Add(new(path, "ENUM", $"'{path}' must be one of: {string.Join(", ", e)}."));
    }
}
