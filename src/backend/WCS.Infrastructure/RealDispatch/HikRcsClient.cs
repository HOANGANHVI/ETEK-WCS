using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Application.RealDispatch;
using WCS.Domain.Common;

namespace WCS.Infrastructure.RealDispatch;

/// <summary>
/// CR-030: the REAL RCS dispatch client (HIKROBOT RCS-2000 API Developer Guide V3.3): genAgvSchedulingTask (5.2.1), continueTask (5.2.2), cancelTask (5.2.3), queryTaskStatus (5.2.15).
/// It is used only when <c>Rcs:RealDispatch:Enabled=true</c>; it is separate from the fleet monitoring client of CR-027 and from the simulator.
///
/// Rules (all from the task statement / CR-028 / the API document):
///  - FAIL CLOSED: without the RCS connection, the location mapping or the process mapping nothing is sent; the call returns a clear rejection. Nothing is generated or guessed.
///  - NO agvCode is sent for a task that has none (the RCS chooses the AMR); the AMR is learned from queryTaskStatus (\"agvCode\" exists when the task has been assigned).
///  - The WCS request code becomes the RCS \"taskCode\" (a task id WCS may set, max 64); the \"reqCode\" (max 32) is derived from it deterministically, so a repeated request keeps the SAME reqCode.
///  - Timeouts, connection errors and \"99\" are UNKNOWN outcomes, never a certain failure. \"6\" (same reqCode, task not completed) means the task exists: accepted.
///  - The token is read from the encrypted RCS connection and sent only as \"tokenCode\"; it is never logged and never part of an error message.
/// Verified only against a mock server that answers like the API document; NOT verified against a real RCS-2000.
/// </summary>
public sealed class HikRcsClient(IServiceScopeFactory scopes, IOptions<RealDispatchOptions> options, IClock clock, ILogger<HikRcsClient> logger) : IRcsClient
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(10) }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private sealed record Secrets(string? Password, string? Token);
    private sealed record Endpoint(string BaseUrl, string? Token);

    public const string NotReady = "RCS_REAL_NOT_READY";
    public const string MappingMissing = "RCS_MAPPING_MISSING";
    public const string NotConfigured = "RCS_NOT_CONFIGURED";

    private RealDispatchOptions O => options.Value;

    // ------------------------------------------------------------------------------------------------ create
    public async Task<RcsCallResult> CreateTaskAsync(RcsCreateTaskRequest request, CancellationToken ct = default)
    {
        if (GlobalCheck() is { } notReady) return notReady;
        var process = O.ProcessOf(request.Flow);
        if (RealDispatchReadiness.ProcessProblem(request.Flow, process) is { } pp) return Reject(MappingMissing, pp);

        var path = new List<object>();
        foreach (var role in process!.PositionPath)
        {
            var code = role.Equals("Source", StringComparison.OrdinalIgnoreCase) ? request.SourceLocationCode : request.RequestLocationCode;
            if (string.IsNullOrWhiteSpace(code)) return Reject(MappingMissing, $"The {role} location of the task is not known; nothing was sent.");
            var map = O.Locations.TryGetValue(code, out var lm) ? lm : null;
            if (RealDispatchReadiness.LocationProblem(code, map) is { } lp) return Reject(MappingMissing, lp);
            path.Add(new { positionCode = map!.PositionCode, type = map.Type });
        }
        if (request.RequestCode.Length is 0 or > 64) return Reject(MappingMissing, "The request code is empty or longer than 64 characters (taskCode); nothing was sent.");
        if (request.RobotCode is { Length: > 5 }) return Reject(MappingMissing, "The AMR code is longer than 5 characters (agvCode); nothing was sent.");

        var body = Base(ReqCodeOf(request.RequestCode));
        body["taskTyp"] = process.TaskTyp;
        body["positionCodePath"] = path;
        body["taskCode"] = request.RequestCode;                                       // the RCS task id is the WCS request code (queryTaskStatus / callbacks use it)
        if (!string.IsNullOrEmpty(request.RobotCode)) body["agvCode"] = request.RobotCode;   // only a Legacy task names an AMR; RcsManaged sends none
        Opt(body, "podDir", process.PodDir); Opt(body, "podTyp", process.PodTyp); Opt(body, "priority", process.Priority); Opt(body, "taskMode", process.TaskMode);
        return await PostAsync("genAgvSchedulingTask", body, request.RequestCode, ct);
    }

    // ------------------------------------------------------------------------------------------------ create (CR-035: configurable routing leg)
    public async Task<RcsCallResult> CreateLegTaskAsync(string requestCode, string? robotCode, string sourceLocationCode, string? destinationLocationCode, CancellationToken ct = default)
    {
        if (GlobalCheck() is { } notReady) return notReady;
        var template = O.LegTemplate;
        if (template is null || string.IsNullOrWhiteSpace(template.TaskTyp) || template.PositionPath.Count == 0)
            return Reject(MappingMissing, "No RCS leg template is configured (Rcs:RealDispatch:LegTemplate); nothing was sent.");
        var path = new List<object>();
        foreach (var role in template.PositionPath)
        {
            var code = role.Equals("Source", StringComparison.OrdinalIgnoreCase) ? sourceLocationCode : role.Equals("Destination", StringComparison.OrdinalIgnoreCase) ? destinationLocationCode : null;
            if (string.IsNullOrWhiteSpace(code)) return Reject(MappingMissing, $"The {role} location of the leg is not known; nothing was sent.");
            var map = O.Locations.TryGetValue(code, out var lm) ? lm : null;
            if (RealDispatchReadiness.LocationProblem(code, map) is { } lp) return Reject(MappingMissing, lp);
            path.Add(new { positionCode = map!.PositionCode, type = map.Type });
        }
        if (requestCode.Length is 0 or > 64) return Reject(MappingMissing, "The request code is empty or longer than 64 characters (taskCode); nothing was sent.");
        if (robotCode is { Length: > 5 }) return Reject(MappingMissing, "The AMR code is longer than 5 characters (agvCode); nothing was sent.");
        var body = Base(ReqCodeOf(requestCode));
        body["taskTyp"] = template.TaskTyp;
        body["positionCodePath"] = path;
        body["taskCode"] = requestCode;
        if (!string.IsNullOrEmpty(robotCode)) body["agvCode"] = robotCode;
        Opt(body, "podDir", template.PodDir); Opt(body, "podTyp", template.PodTyp); Opt(body, "priority", template.Priority); Opt(body, "taskMode", template.TaskMode);
        return await PostAsync("genAgvSchedulingTask", body, requestCode, ct);
    }

    // ------------------------------------------------------------------------------------------------ continue
    public async Task<RcsCallResult> ContinueTaskAsync(string requestCode, string? rcsTaskCode, string destinationLocationCode, CancellationToken ct = default)
    {
        if (GlobalCheck() is { } notReady) return notReady;
        var map = O.Locations.TryGetValue(destinationLocationCode ?? "", out var lm) ? lm : null;
        if (RealDispatchReadiness.LocationProblem(destinationLocationCode ?? "", map) is { } lp) return Reject(MappingMissing, lp);
        var taskCode = string.IsNullOrWhiteSpace(rcsTaskCode) ? requestCode : rcsTaskCode;
        var body = Base(ReqCodeOf($"{requestCode}|continue|{destinationLocationCode}"));
        body["taskCode"] = taskCode;                                                  // only ONE identifier (taskCode is the recommended one, 5.2.2)
        body["nextPositionCode"] = new { positionCode = map!.PositionCode, type = map.Type };
        return await PostAsync("continueTask", body, requestCode, ct);
    }

    // ------------------------------------------------------------------------------------------------ cancel
    public async Task<RcsCallResult> CancelTaskAsync(string requestCode, string? rcsTaskCode, CancellationToken ct = default)
    {
        if (GlobalCheck() is { } notReady) return notReady;
        var body = Base(ReqCodeOf($"{requestCode}|cancel"));
        body["taskCode"] = string.IsNullOrWhiteSpace(rcsTaskCode) ? requestCode : rcsTaskCode;   // by task id: the task is cancelled, never "the AMR is freed" (agvCode would take precedence)
        Opt(body, "forceCancel", O.CancelMode);
        return await PostAsync("cancelTask", body, requestCode, ct);
    }

    // ------------------------------------------------------------------------------------------------ query
    public async Task<(RcsTaskStatus Status, string? RcsTaskCode)> QueryTaskStatusAsync(string requestCode, CancellationToken ct = default)
    {
        var info = await QueryTaskAsync(requestCode, ct);
        return (info.Status, info.RcsTaskCode);
    }

    public async Task<RcsTaskInfo> QueryTaskAsync(string requestCode, CancellationToken ct = default)
    {
        var unknown = new RcsTaskInfo(RcsTaskStatus.Unknown, null, null);
        if (GlobalCheck() is not null) return unknown;                               // a read that cannot be made is inconclusive, never "not found"
        var body = Base(Guid.NewGuid().ToString("N"));                               // a polling query is a new request each time
        body["taskCodes"] = new[] { requestCode };
        var (outcome, doc, _) = await SendAsync("queryTaskStatus", body, requestCode, ct);
        if (outcome != RcsCallOutcome.Accepted || doc is null) return unknown;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return unknown;
            foreach (var item in data.EnumerateArray())
            {
                if (!string.Equals(Str(item, "taskCode"), requestCode, StringComparison.Ordinal)) continue;
                var agv = Str(item, "agvCode");
                return new RcsTaskInfo(MapStatus(Str(item, "taskStatus")), requestCode, string.IsNullOrWhiteSpace(agv) ? null : agv.Trim());
            }
            return new RcsTaskInfo(RcsTaskStatus.NotFound, null, null);              // the RCS answered and does not know the task
        }
    }

    /// <summary>The AMR status of one robot is not an operation of the dispatch client (fleet monitoring is CR-027): no answer.</summary>
    public Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default) => Task.FromResult<RcsAgvStatus?>(null);

    /// <summary>
    /// queryTaskStatus "taskStatus" (5.2.15): 0 sending exception, 1 created, 2 executing, 3 sending, 4 canceling, 5 canceled, 6 resending, 9 completed, 10 interrupted. The in-between values
    /// (sending / resending) are "created, not executing yet"; an exception or an unknown value is inconclusive. (Vendor question 7b: which values are final.)
    /// </summary>
    public static RcsTaskStatus MapStatus(string? s) => s?.Trim() switch
    {
        "1" or "3" or "6" => RcsTaskStatus.Created,
        "2" => RcsTaskStatus.Executing,
        "4" => RcsTaskStatus.Cancelling,
        "5" => RcsTaskStatus.Cancelled,
        "9" => RcsTaskStatus.Completed,
        "10" => RcsTaskStatus.Interrupted,
        _ => RcsTaskStatus.Unknown,                                                  // "0" sending exception and anything undefined
    };

    // ------------------------------------------------------------------------------------------------ plumbing
    private RcsCallResult? GlobalCheck()
    {
        var problems = RealDispatchReadiness.GlobalProblems(O);
        return problems.Count == 0 ? null : Reject(NotReady, string.Join(" ", problems));
    }

    private static RcsCallResult Reject(string code, string message) => new(RcsCallOutcome.Rejected, null, $"{code}: {message}");

    private async Task<RcsCallResult> PostAsync(string api, Dictionary<string, object?> body, string requestCode, CancellationToken ct)
    {
        var (outcome, doc, message) = await SendAsync(api, body, requestCode, ct);
        doc?.Dispose();
        return outcome == RcsCallOutcome.Accepted ? new RcsCallResult(RcsCallOutcome.Accepted, requestCode, null) : new RcsCallResult(outcome, null, message);
    }

    /// <summary>One POST. Accepted = code "0" or "6"; Rejected = code "1" (or a configuration problem); Unknown = timeout, connection error, HTTP error, "99", an unreadable or unexpected answer.</summary>
    private async Task<(RcsCallOutcome Outcome, JsonDocument? Doc, string? Message)> SendAsync(string api, Dictionary<string, object?> body, string requestCode, CancellationToken ct)
    {
        var ep = await ResolveAsync(ct);
        if (ep.Error is not null) return (RcsCallOutcome.Rejected, null, ep.Error);
        if (!string.IsNullOrEmpty(ep.Value!.Token)) body["tokenCode"] = ep.Value.Token;
        var url = $"{ep.Value.BaseUrl}{O.ServicePath.TrimEnd('/')}/{api}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, O.TimeoutSeconds)));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json") };
            using var res = await Http.SendAsync(req, cts.Token);
            var text = await res.Content.ReadAsStringAsync(cts.Token);
            if (!res.IsSuccessStatusCode) return (RcsCallOutcome.Unknown, null, $"{api}: HTTP {(int)res.StatusCode}");
            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); } catch (JsonException) { return (RcsCallOutcome.Unknown, null, $"{api}: the answer is not JSON"); }
            var code = Str(doc.RootElement, "code");
            var message = Str(doc.RootElement, "message");
            switch (code)
            {
                case "0": case "6": return (RcsCallOutcome.Accepted, doc, null);      // "6": no need to resend - the task of the same reqCode is not completed (Appendix A)
                case "1": doc.Dispose(); return (RcsCallOutcome.Rejected, null, $"{api}: the RCS refused (code 1): {Trim(message)}");
                default: doc.Dispose(); return (RcsCallOutcome.Unknown, null, $"{api}: the RCS answered code {Trim(code)}: {Trim(message)}");   // "99" unknown error, try again, or an undefined code
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (RcsCallOutcome.Unknown, null, $"{api}: the RCS did not answer within {O.TimeoutSeconds} s"); }
        catch (HttpRequestException ex) { return (RcsCallOutcome.Unknown, null, $"{api}: connection error ({ex.GetType().Name})"); }
    }

    private async Task<(Endpoint? Value, string? Error)> ResolveAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IConnectionSettingRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var s = await settings.GetAsync(ConnectionKind.Rcs, ct);
        if (s is null || !s.Enabled) return (null, $"{NotConfigured}: the RCS connection is not saved or is switched off.");
        RcsSettings? r;
        try { r = JsonSerializer.Deserialize<RcsSettings>(s.SettingsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)); } catch (JsonException) { r = null; }
        if (r is null || string.IsNullOrWhiteSpace(r.Host)) return (null, $"{NotConfigured}: the RCS address (host) is not set.");
        var port = O.Port > 0 ? O.Port : r.Port ?? 0;
        if (port is < 1 or > 65535) return (null, $"{NotConfigured}: the RCS port is not set (RCS connection or Rcs:RealDispatch:Port).");
        string? token = null;
        if (r.AuthMode == ConnectionOptions.AuthToken)
        {
            if (string.IsNullOrEmpty(s.ProtectedSecrets)) return (null, $"{NotConfigured}: the RCS connection uses a token but none is stored.");
            try { token = JsonSerializer.Deserialize<Secrets>(protector.Unprotect(s.ProtectedSecrets), new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Token; }
            catch (Exception ex) when (ex is JsonException or CryptographicException) { return (null, $"{NotConfigured}: the stored RCS token cannot be read; enter it again."); }
            if (string.IsNullOrEmpty(token)) return (null, $"{NotConfigured}: the RCS connection uses a token but none is stored.");
        }
        var scheme = Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out var b) && b.Scheme == Uri.UriSchemeHttps ? "https" : "http";
        return (new Endpoint($"{scheme}://{r.Host.Trim()}:{port}", token), null);
    }

    private Dictionary<string, object?> Base(string reqCode)
    {
        var d = new Dictionary<string, object?> { ["reqCode"] = reqCode, ["reqTime"] = clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") };
        if (!string.IsNullOrEmpty(O.ClientCode)) d["clientCode"] = O.ClientCode;
        return d;
    }

    private static void Opt(Dictionary<string, object?> d, string key, string? value) { if (!string.IsNullOrEmpty(value)) d[key] = value; }

    /// <summary>"reqCode" is at most 32 characters and must be the SAME when a request is repeated: the WCS code itself when it fits, else a fixed hash of it (never random).</summary>
    public static string ReqCodeOf(string key) => key.Length <= 32 ? key : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind is JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.ToString() : null : null;
    private static string Trim(string? s) => s is null ? "" : s.Length > 100 ? s[..100] : s;
}
