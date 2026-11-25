using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Common;

namespace WCS.Application.RealDispatch;

public enum RcsCallbackStatus { Disabled, Unauthorized, Invalid, Applied, Duplicate, Ignored, Rejected }

/// <param name="Code">The RCS-2000 status code returned in the answer ("0" succeeded, "1" failed; Appendix A).</param>
public sealed record RcsCallbackResult(RcsCallbackStatus Status, string Code, string Message, string? ReqCode);

/// <summary>
/// CR-030: the RCS -> WCS callback <c>agvCallback</c> (RCS-2000 API V3.3, 5.3.1). Authentication is NOT defined by the API document, so none is invented: a callback is accepted only when the
/// WCS-side checks that ARE configured all pass, and it is refused outright when none is configured. A callback never confirms a pick or a drop and never completes a task: it can only
/// tell WCS which AMR the RCS chose (recorded by the existing CR-028 rules), that the AMR started, that the RCS ended the task (does NOT complete it without a confirmed drop) or that the task was cancelled.
/// A repeated reqCode is a duplicate (the RCS repeats a request with the same reqCode); an out-of-order event is noted and ignored by the existing state machine.
/// </summary>
public sealed class RcsCallbackService(IOptions<RealDispatchOptions> options, ITaskOrchestrator orchestrator, IRcsMessageRecorder recorder, ILogger<RcsCallbackService> logger)
{
    public bool Enabled => options.Value.Callback.Enabled;
    public string Path => options.Value.Callback.Path;
    public string HeaderName => options.Value.Callback.HeaderName;
    public int MaxBodyBytes => Math.Max(1024, options.Value.Callback.MaxBodyBytes);

    /// <summary>True when the caller is authenticated by EVERY configured mechanism; false when none is configured.</summary>
    public RcsCallbackStatus Authenticate(string? headerValue, IPAddress? remote)
    {
        var o = options.Value.Callback;
        if (!o.Enabled) return RcsCallbackStatus.Disabled;
        if (!o.AuthenticationConfigured) return RcsCallbackStatus.Unauthorized;   // fail closed
        if (!string.IsNullOrEmpty(o.SharedSecret))
        {
            if (string.IsNullOrEmpty(headerValue)) return RcsCallbackStatus.Unauthorized;
            var a = SHA256.HashData(Encoding.UTF8.GetBytes(o.SharedSecret));
            var b = SHA256.HashData(Encoding.UTF8.GetBytes(headerValue));
            if (!CryptographicOperations.FixedTimeEquals(a, b)) return RcsCallbackStatus.Unauthorized;
        }
        if (o.EffectiveSources.Count > 0 && (remote is null || !o.EffectiveSources.Any(s => InRange(remote, s)))) return RcsCallbackStatus.Unauthorized;
        return RcsCallbackStatus.Applied;   // "authenticated"
    }

    /// <summary>
    /// CR-038: an AUTHENTICATED callback is kept exactly as received (secrets masked by the recorder) together with what WCS did with it, linked to the task by its taskCode
    /// (the WCS request code). A taskCode that matches no task leaves the message unlinked. The caller authenticates first; a refused call is never stored.
    /// </summary>
    public async Task<RcsCallbackResult> HandleAsync(string? body, CancellationToken ct = default)
    {
        var result = await HandleCoreAsync(body, ct);
        string? taskCode = null, method = null;
        try { using var doc = JsonDocument.Parse(body ?? ""); taskCode = doc.RootElement.TryGetProperty("taskCode", out var tc) && tc.ValueKind == JsonValueKind.String ? tc.GetString() : null; method = doc.RootElement.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null; }
        catch (JsonException) { /* an unreadable body is recorded as it is */ }
        await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Real, WCS.Domain.Rcs.RcsMessageDirection.In, "AGV Callback" + (method is null ? "" : $": {method}"), "RCS callback (HTTP)", taskCode, Options.Callback.Path, "POST", 200, body,
            result.Status.ToString(), result.Status is RcsCallbackStatus.Applied or RcsCallbackStatus.Duplicate or RcsCallbackStatus.Ignored ? null : "RCS_CALLBACK_" + result.Status.ToString().ToUpperInvariant(), result.Status == RcsCallbackStatus.Applied ? null : result.Message), CancellationToken.None);
        return result;
    }

    private RealDispatchOptions Options => options.Value;

    private async Task<RcsCallbackResult> HandleCoreAsync(string? body, CancellationToken ct)
    {
        RcsCallback? cb = Parse(body);
        if (cb is null || string.IsNullOrWhiteSpace(cb.ReqCode) || string.IsNullOrWhiteSpace(cb.Method) || string.IsNullOrWhiteSpace(cb.RobotCode) || string.IsNullOrWhiteSpace(cb.TaskCode))
            return new(RcsCallbackStatus.Invalid, "1", "reqCode, method, robotCode and taskCode are required.", cb?.ReqCode);
        if (cb.ReqCode.Length > 64 || cb.TaskCode.Length > 64 || cb.RobotCode.Length > 64) return new(RcsCallbackStatus.Invalid, "1", "A field is too long.", cb.ReqCode);

        if (!IsKnownMethod(cb.Method)) return new(RcsCallbackStatus.Invalid, "1", "Unknown method.", cb.ReqCode);
        SignalKind? kind = cb.Method!.Trim().ToLowerInvariant() switch
        {
            "start" => SignalKind.AgvStarted,
            "outbin" => SignalKind.AgvStarted,            // "executing" (5.3.1): the AMR is carrying out the task
            "end" => SignalKind.RcsEnded,                 // the RCS ended the task: does NOT complete it without a confirmed drop
            "cancel" => SignalKind.RcsCancelConfirmed,
            _ => null,                                    // "ctu": cancel of a container pick/place request (CTU only): not used by WCS
        };
        return await ApplyAsync(cb, kind, ct);
    }

    /// <summary>Reads the fields WCS needs; a value may arrive as a JSON string or number. Anything unreadable gives null.</summary>
    private static RcsCallback? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            string? Str(string name) => doc.RootElement.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Value: { } v } && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
                ? v.ToString() : null;
            return new RcsCallback(Str("reqCode"), Str("method"), Str("robotCode"), Str("taskCode"));
        }
        catch (JsonException) { return null; }
    }

    private async Task<RcsCallbackResult> ApplyAsync(RcsCallback cb, SignalKind? kind, CancellationToken ct)
    {
        if (kind is null)
        {
            logger.LogInformation("RCS callback {ReqCode} method {Method} for task {TaskCode} is not used by WCS; acknowledged without any change. {EventCode}", cb.ReqCode, cb.Method, cb.TaskCode, "RCS_CALLBACK_NOT_USED");
            return new(RcsCallbackStatus.Ignored, "0", "successful", cb.ReqCode);
        }
        var r = await orchestrator.HandleSignalAsync(new TaskSignal(kind.Value, null, cb.TaskCode.Trim(), cb.RobotCode.Trim(), $"rcs-cb:{cb.ReqCode.Trim()}", ConfirmationSource.Rcs), ct);
        logger.LogInformation("RCS callback {ReqCode} {Method} task {TaskCode} robot {RobotCode} -> {Outcome}. {EventCode}", cb.ReqCode, cb.Method, cb.TaskCode, cb.RobotCode, r.Outcome, "RCS_CALLBACK");
        return r.Outcome switch
        {
            SignalOutcome.Applied => new(RcsCallbackStatus.Applied, "0", "successful", cb.ReqCode),
            SignalOutcome.Duplicate => new(RcsCallbackStatus.Duplicate, "0", "successful", cb.ReqCode),   // the same reqCode again: acknowledged, nothing applied twice
            SignalOutcome.Ignored => new(RcsCallbackStatus.Ignored, "0", "successful", cb.ReqCode),       // late / out of order: noted in the task history
            _ => new(RcsCallbackStatus.Rejected, "1", r.Message, cb.ReqCode),                             // unknown task, robot mismatch
        };
    }

    /// <summary>Unknown methods are refused before anything else (kept out of the switch expression above for clarity).</summary>
    public static bool IsKnownMethod(string? method) => method?.Trim().ToLowerInvariant() is "start" or "outbin" or "end" or "cancel" or "ctu";

    private static bool InRange(IPAddress ip, string rule)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var parts = rule.Trim().Split('/');
        if (!IPAddress.TryParse(parts[0], out var net)) return false;
        if (net.IsIPv4MappedToIPv6) net = net.MapToIPv4();
        if (parts.Length == 1) return net.Equals(ip);
        if (!int.TryParse(parts[1], out var bits) || ip.AddressFamily != net.AddressFamily) return false;
        var a = ip.GetAddressBytes(); var b = net.GetAddressBytes();
        if (bits < 0 || bits > a.Length * 8) return false;
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (0xFF << (8 - bits)) & 0xFF;
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }

    private sealed record RcsCallback(string? ReqCode, string? Method, string? RobotCode, string? TaskCode);
}
