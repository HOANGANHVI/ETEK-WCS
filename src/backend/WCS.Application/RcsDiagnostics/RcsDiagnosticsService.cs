using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Domain.RcsDiagnostics;
using WCS.Domain.Users;

namespace WCS.Application.RcsDiagnostics;

public sealed class RcsDiagnosticsOptions
{
    public const string SectionName = "Rcs:Diagnostics";
    /// <summary>Manual requests that may be in flight at the same time (the rest are refused with RCS_DIAG_BUSY, never queued).</summary>
    public int MaxConcurrent { get; set; } = 2;
    /// <summary>Used when the saved RCS connection has no timeout.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 10;
    public int MaxResponseBytes { get; set; } = 64 * 1024;
    /// <summary>Newest history rows kept (older ones are removed in small steps).</summary>
    public int HistoryKeep { get; set; } = 5000;
    /// <summary>The MAP ID of this installation (confirmed: "TT"). The central default of the map alias parameter; the Admin can override it in the diagnostics parameters. Change it here (Rcs:Diagnostics:MapId) when deploying another system.</summary>
    public string MapId { get; set; } = "TT";
}

// ---- the saved RCS connection as the diagnostics module sees it (no secret in it)
public sealed record RcsEndpointInfo(bool Configured, bool Enabled, string? Scheme, string? Host, int? Port, string? BaseUrl, string AuthMode, int TimeoutMs, string? Problem)
{
    public bool Ready => Configured && Enabled && Problem is null && Host is not null && Port is not null;
    public string? Address => Host is null || Port is null ? null : $"{Host}:{Port}";
}

public enum RcsTransportFailure { None, Timeout, Connection }
public sealed record RcsTransportResult(RcsTransportFailure Failure, int? HttpStatus, IReadOnlyDictionary<string, string> Headers, string? Body, bool Truncated, long DurationMs, string? Error);
public sealed record RcsTcpResult(bool Success, long DurationMs, string? Error);

/// <summary>The one place that talks HTTP / TCP to the RCS for diagnostics. The destination is the saved RCS connection plus a catalog path; nothing else can reach it.</summary>
public interface IRcsDiagnosticsTransport
{
    /// <param name="dpsPath">true for the /rcms-dps/ path (queryAgvStatus), which honours the same port override as the fleet monitor.</param>
    Task<RcsEndpointInfo> ResolveAsync(bool dpsPath, CancellationToken ct = default);
    Task<RcsTransportResult> SendAsync(RcsEndpointInfo ep, string path, string jsonBody, int maxBytes, CancellationToken ct = default);
    Task<RcsTcpResult> TcpCheckAsync(RcsEndpointInfo ep, CancellationToken ct = default);
}

public sealed record RcsHistoryQuery(DateTimeOffset? From, DateTimeOffset? To, string? Api, string? Username, string? Status, int Skip, int Take);

public interface IRcsDiagnosticsRepository
{
    void Add(RcsApiTest test);
    Task<IReadOnlyList<RcsApiTest>> QueryAsync(RcsHistoryQuery q, CancellationToken ct = default);
    Task<int> CountAsync(RcsHistoryQuery q, CancellationToken ct = default);
    Task<RcsApiTest?> GetAsync(Guid id, CancellationToken ct = default);
    Task TrimAsync(int keep, CancellationToken ct = default);
    Task<IReadOnlyList<RcsDiagParam>> ListParamsAsync(CancellationToken ct = default);
    Task<RcsDiagParam?> GetParamForUpdateAsync(string key, CancellationToken ct = default);
    void AddParam(RcsDiagParam p);
    void RemoveParam(RcsDiagParam p);
    Task<IReadOnlyList<RcsCallbackLogEntry>> ListCallbacksAsync(string? callback, int skip, int take, CancellationToken ct = default);
}

public static class RcsOutcomes
{
    public const string ApiSuccess = "API_SUCCESS", TcpFailed = "TCP_CONNECTION_FAILED", Timeout = "TIMEOUT", HttpFailure = "HTTP_FAILURE", RcsCodeNotSuccess = "RCS_CODE_NOT_SUCCESS",
        JsonParseFailed = "JSON_PARSE_FAILED", ResponseInvalid = "RESPONSE_INVALID", NotConfigured = "NOT_CONFIGURED";
}

public sealed record RcsCheckState(DateTimeOffset At, bool Success, long DurationMs, string? Detail, string By);

/// <summary>In-memory, process-wide: the concurrency gate of the manual requests and the last connectivity checks. Not operational state; lost on restart on purpose.</summary>
public sealed class RcsDiagnosticsState(IOptions<RcsDiagnosticsOptions> options)
{
    public SemaphoreSlim Gate { get; } = new(Math.Max(1, options.Value.MaxConcurrent), Math.Max(1, options.Value.MaxConcurrent));
    public int Max { get; } = Math.Max(1, options.Value.MaxConcurrent);
    public RcsCheckState? LastTcp { get; set; }
    public RcsCheckState? LastApi { get; set; }
    public int Executions;
}

public sealed record RcsOverview(string Source, bool Configured, bool Enabled, string? Scheme, string? Host, int? Port, string? BaseUrl, string AuthMode, int TimeoutMs, string? Problem,
    string FleetSource, RcsCheckState? LastTcpCheck, RcsCheckState? LastApiCheck, bool? ApiAvailable, int MaxConcurrent, int InUse);

public sealed record RcsPreview(string Api, string Method, string? Endpoint, IReadOnlyDictionary<string, string> Headers, string Body, bool Valid, IReadOnlyList<RcsValidationIssue> Errors, IReadOnlyList<RcsValidationIssue> Warnings, string? ConfigProblem);

public sealed record RcsExecution(Guid Id, DateTimeOffset ExecutedAt, string Api, string Group, string User, string RequestId, string Method, string? Endpoint, IReadOnlyDictionary<string, string> RequestHeaders, string? RequestBody,
    int? HttpStatus, IReadOnlyDictionary<string, string> ResponseHeaders, string? ResponseBody, bool ResponseTruncated, string? RcsCode, string? RcsMessage, long DurationMs, string Outcome, bool Success, string? ErrorMessage,
    IReadOnlyList<RcsValidationIssue> Warnings);

public sealed record RcsParameterView(string Key, string Status, IReadOnlyList<string> Fields, int MaxLength, string? ConfiguredDefault, string? SavedValue, string? EffectiveValue, string Source, string? UpdatedBy, DateTimeOffset? UpdatedAt);
public sealed record RcsParametersView(IReadOnlyList<RcsParameterView> Items, IReadOnlyList<string> Undetermined);

public sealed record RcsHistoryPage(int Total, IReadOnlyList<RcsHistoryItem> Items);
public sealed record RcsHistoryItem(Guid Id, DateTimeOffset ExecutedAt, string User, string Role, string Api, string Group, string RequestId, string? Endpoint, string? RequestBody, string? ResponseBody, bool ResponseTruncated,
    int? HttpStatus, string? RcsCode, string? RcsMessage, long DurationMs, string Outcome, bool Success, string? ErrorMessage);

/// <summary>
/// CR-032: the RCS Diagnostics &amp; Manual API Tester. ADMIN ONLY (checked here as well as by the endpoint policy). Execute sends immediately: there is no approval, no pending state and no second person. It still validates against the
/// document, never sends an invalid request, masks credentials in everything it stores or returns, audits every attempt and records the exchange in the history. It never touches inventory, racks, transport tasks, the fleet snapshot or the 5-second monitoring poll.
/// </summary>
public sealed class RcsDiagnosticsService(IRcsDiagnosticsTransport transport, IRcsDiagnosticsRepository repo, IUnitOfWork uow, IClock clock, IAuditLog audit, IOptions<RcsDiagnosticsOptions> options,
    IOptions<WCS.Application.Fleet.RcsFleetOptions> fleetOptions, RcsDiagnosticsState state)
{
    private static readonly Regex SecretKey = new("token|password|passwd|secret|authorization|credential|apikey", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecretText = new("(?i)(\\w*(?:token|password|passwd|secret|authorization)\\w*)\\s*[\"']?\\s*[:=]\\s*[\"']?[^\\s\"',;}]+", RegexOptions.Compiled);
    private static readonly string[] SafeResponseHeaders = ["content-type", "content-length", "date", "server", "cache-control", "connection", "transfer-encoding", "content-encoding"];
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private RcsDiagnosticsOptions O => options.Value;

    private static void RequireAdmin(ActorInfo actor)
    {
        if (!actor.IsAdmin) throw AppException.Forbidden("RCS_DIAG_ADMIN_ONLY", "The RCS Diagnostics module is for Administrators only.");
    }

    // ------------------------------------------------------------------ read-only views (no call to the RCS)
    public static IReadOnlyList<RcsApiDef> Catalog() => RcsApiCatalog.All;

    public async Task<RcsOverview> OverviewAsync(ActorInfo actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var ep = await transport.ResolveAsync(false, ct);
        return new RcsOverview("Real", ep.Configured, ep.Enabled, ep.Scheme, ep.Host, ep.Port, ep.BaseUrl, ep.AuthMode, ep.TimeoutMs, ep.Problem, fleetOptions.Value.Source,
            state.LastTcp, state.LastApi, state.LastApi?.Success, state.Max, state.Max - state.Gate.CurrentCount);
    }

    public async Task<RcsPreview> PreviewAsync(ActorInfo actor, string apiName, JsonElement body, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var api = RequireApi(apiName);
        var built = Build(body, null);
        var v = ValidateBuilt(api, body, built);
        var ep = await transport.ResolveAsync(IsDps(api), ct);
        return new RcsPreview(api.Name, "POST", Endpoint(ep, api), RequestHeaders(), Mask(built), v.IsValid, v.Errors, v.Warnings, ep.Ready ? null : ep.Problem ?? "NOT_CONFIGURED");
    }

    // ------------------------------------------------------------------ the call
    public async Task<RcsExecution> ExecuteAsync(ActorInfo actor, string apiName, JsonElement body, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var api = RequireApi(apiName);
        var payload = Build(body, clock);
        var v = ValidateBuilt(api, body, payload);
        if (!v.IsValid)
        {
            await Audit(actor, api, "-", AuditResult.Failed, "VALIDATION_FAILED: " + string.Join("; ", v.Errors.Select(e => e.Path + " " + e.Code)), ct);
            throw new AppException(ErrorKind.Validation, "RCS_DIAG_VALIDATION_FAILED", "The request does not match the API document; it was NOT sent. " + string.Join(" ", v.Errors.Take(5).Select(e => e.Message)));
        }
        if (!state.Gate.Wait(0))
        {
            await Audit(actor, api, "-", AuditResult.Failed, "RCS_DIAG_BUSY", ct);
            throw AppException.Conflict("RCS_DIAG_BUSY", $"{state.Max} manual RCS request(s) are already running; try again when one has finished.");
        }
        try
        {
            var reqCode = payload["reqCode"]!.GetValue<string>();
            var ep = await transport.ResolveAsync(IsDps(api), ct);
            var now = clock.UtcNow;
            var endpoint = Endpoint(ep, api);
            RcsTransportResult? res = null; string outcome; string? rcsCode = null, rcsMessage = null, error = null;
            if (!ep.Ready) { outcome = RcsOutcomes.NotConfigured; error = ep.Problem ?? "The RCS connection is not saved or is switched off."; }
            else
            {
                res = await transport.SendAsync(ep, api.Path, payload.ToJsonString(), O.MaxResponseBytes, ct);
                (outcome, rcsCode, rcsMessage, error) = Classify(res);
            }
            var success = outcome == RcsOutcomes.ApiSuccess;
            var reqMasked = Mask(payload);
            var respMasked = res?.Body is null ? null : MaskText(res.Body);
            var duration = res?.DurationMs ?? 0;
            var entity = new RcsApiTest(Guid.NewGuid(), now, actor.Username, actor.Role.ToString(), api.Name, api.Group, reqCode, endpoint, outcome, success);
            entity.Complete(reqMasked, respMasked, res?.Truncated ?? false, res?.HttpStatus, Cut(rcsCode, 20), Cut(rcsMessage, 300), duration, Cut(error, 500));
            await uow.ExecuteAsync(t => { repo.Add(entity); return Task.FromResult(0); }, ct);
            if (Interlocked.Increment(ref state.Executions) % 50 == 0) { try { await repo.TrimAsync(O.HistoryKeep, ct); } catch (Exception) { /* trimming is housekeeping only */ } }
            await Audit(actor, api, reqCode, success ? AuditResult.Success : AuditResult.Failed, $"{outcome}; HTTP {res?.HttpStatus?.ToString() ?? "-"}; RCS code {rcsCode ?? "-"}; {duration} ms; {endpoint}", ct);
            if (api.Name == "queryAgvStatus") state.LastApi = new RcsCheckState(now, success, duration, success ? null : outcome + (rcsCode is null ? "" : ":" + rcsCode), actor.Username);
            return new RcsExecution(entity.Id, now, api.Name, api.Group, actor.Username, reqCode, "POST", endpoint, RequestHeaders(), reqMasked, res?.HttpStatus, res?.Headers ?? new Dictionary<string, string>(),
                respMasked, res?.Truncated ?? false, rcsCode, rcsMessage, duration, outcome, success, error, v.Warnings);
        }
        finally { state.Gate.Release(); }
    }

    /// <summary>TCP reachability only: the port accepts a connection. It says NOTHING about the RCS API (a listening port is not a working API).</summary>
    public async Task<RcsCheckState> TcpCheckAsync(ActorInfo actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var ep = await transport.ResolveAsync(false, ct);
        var r = ep.Ready ? await transport.TcpCheckAsync(ep, ct) : new RcsTcpResult(false, 0, ep.Problem ?? "The RCS connection is not saved or is switched off.");
        var check = new RcsCheckState(clock.UtcNow, r.Success, r.DurationMs, r.Error, actor.Username);
        state.LastTcp = check;
        await audit.WriteAsync(new AuditRecord("RCS_DIAG_TCP_CHECK", actor.Username, actor.Role.ToString(), "RcsConnection", ep.Address, r.Success ? AuditResult.Success : AuditResult.Failed,
            $"{(r.Success ? "TCP_OK" : "TCP_FAILED")}; {r.DurationMs} ms; {r.Error}", IpAddress: actor.IpAddress), ct);
        return check;
    }

    /// <summary>API connectivity: ONE read-only <c>queryAgvStatus</c>. Recorded in the history like any other execution.</summary>
    public Task<RcsExecution> ApiCheckAsync(ActorInfo actor, CancellationToken ct = default) => ExecuteAsync(actor, "queryAgvStatus", JsonDocument.Parse("{}").RootElement, ct);

    // ------------------------------------------------------------------ parameters (pre-fill values for the Request Builder)
    public async Task<RcsParametersView> ParametersAsync(ActorInfo actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var saved = (await repo.ListParamsAsync(ct)).ToDictionary(p => p.Key);
        var items = RcsParameterCatalog.All.Select(d =>
        {
            var configured = RcsParameterCatalog.ConfiguredDefault(d, options.Value);
            saved.TryGetValue(d.Key, out var s);
            var effective = s?.Value ?? configured;
            return new RcsParameterView(d.Key, d.Status, d.Fields, d.MaxLength, configured, s?.Value, effective, s is not null ? "Saved" : configured is not null ? "Config" : "None", s?.UpdatedBy, s?.UpdatedAt);
        }).ToList();
        return new RcsParametersView(items, RcsParameterCatalog.UndeterminedFields);
    }

    /// <summary>Saves (or, with an empty value, removes) the Admin's value of one parameter. Only known parameters can be saved; the value is a plain identifier, never a secret.</summary>
    public async Task<RcsParameterView> SaveParameterAsync(ActorInfo actor, string key, string? value, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var def = RcsParameterCatalog.Find(key) ?? throw AppException.Validation("RCS_DIAG_PARAM_UNKNOWN", "That parameter is not one of the RCS diagnostics parameters.");
        var v = value?.Trim() ?? "";
        if (v.Length > def.MaxLength || v.Any(char.IsControl)) throw AppException.Validation("RCS_DIAG_PARAM_INVALID", $"The value must be plain text of at most {def.MaxLength} characters.");
        var now = clock.UtcNow;
        await uow.ExecuteAsync(async t =>
        {
            var row = await repo.GetParamForUpdateAsync(def.Key, t);
            if (v.Length == 0) { if (row is not null) repo.RemoveParam(row); }
            else if (row is null) repo.AddParam(new RcsDiagParam(def.Key, v, actor.Username, now));
            else row.Set(v, actor.Username, now);
            return 0;
        }, ct);
        await audit.WriteAsync(new AuditRecord("RCS_DIAG_PARAM_SAVE", actor.Username, actor.Role.ToString(), "RcsParameter", def.Key, AuditResult.Success, v.Length == 0 ? "cleared" : $"value={v}", IpAddress: actor.IpAddress), ct);
        return (await ParametersAsync(actor, ct)).Items.Single(i => i.Key == def.Key);
    }

    // ------------------------------------------------------------------ history
    public async Task<RcsHistoryPage> HistoryAsync(ActorInfo actor, RcsHistoryQuery q, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        q = q with { Take = Math.Clamp(q.Take, 1, 200), Skip = Math.Max(0, q.Skip) };
        var rows = await repo.QueryAsync(q, ct);
        return new RcsHistoryPage(await repo.CountAsync(q, ct), rows.Select(ToItem).ToList());
    }

    public async Task<RcsHistoryItem> GetAsync(ActorInfo actor, Guid id, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return ToItem(await repo.GetAsync(id, ct) ?? throw AppException.NotFound("RCS_DIAG_NOT_FOUND", "Execution not found."));
    }

    public async Task<IReadOnlyList<RcsHistoryItem>> ExportAsync(ActorInfo actor, RcsHistoryQuery q, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var rows = await repo.QueryAsync(q with { Skip = 0, Take = 1000 }, ct);
        await audit.WriteAsync(new AuditRecord("RCS_DIAG_EXPORT", actor.Username, actor.Role.ToString(), "RcsApiTest", null, AuditResult.Success, $"{rows.Count} row(s)", IpAddress: actor.IpAddress), ct);
        return rows.Select(ToItem).ToList();
    }

    public async Task<IReadOnlyList<RcsCallbackLogEntry>> CallbacksAsync(ActorInfo actor, string? callback, int skip, int take, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return await repo.ListCallbacksAsync(callback, Math.Max(0, skip), Math.Clamp(take, 1, 200), ct);
    }

    // ------------------------------------------------------------------ helpers
    private static RcsHistoryItem ToItem(RcsApiTest t) => new(t.Id, t.ExecutedAt, t.Username, t.UserRole, t.Api, t.Group, t.RequestId, t.Endpoint, t.RequestJson, t.ResponseJson, t.ResponseTruncated, t.HttpStatus, t.RcsCode, t.RcsMessage, t.DurationMs, t.Outcome, t.Success, t.ErrorMessage);

    /// <summary>reqCode and reqTime are generated when the Admin leaves them out, so the request that is checked is the request that will be sent.</summary>
    private static RcsValidationResult ValidateBuilt(RcsApiDef api, JsonElement body, JsonObject built) =>
        body.ValueKind == JsonValueKind.Object ? RcsRequestValidator.Validate(api, JsonDocument.Parse(built.ToJsonString()).RootElement) : RcsRequestValidator.Validate(api, body);

    private static RcsApiDef RequireApi(string? name) => RcsApiCatalog.Find(name) ?? throw AppException.Validation("RCS_DIAG_API_NOT_IN_CATALOG", "That API is not in the RCS API catalog; only catalogued APIs can be called.");
    private static bool IsDps(RcsApiDef api) => api.Path.StartsWith(RcsApiCatalog.DpsBase, StringComparison.Ordinal);
    private static string? Endpoint(RcsEndpointInfo ep, RcsApiDef api) => ep.Address is null ? null : ep.Address + api.Path;
    private static IReadOnlyDictionary<string, string> RequestHeaders() => new Dictionary<string, string> { ["Content-Type"] = "application/json; charset=utf-8" };   // no Authorization / Cookie header exists in this call
    private static string? Cut(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];

    /// <summary>The request as it will be sent: the Admin's fields, with reqCode (new GUID) and reqTime (local time) filled in only when missing.</summary>
    private static JsonObject Build(JsonElement body, IClock? clock)
    {
        var o = body.ValueKind == JsonValueKind.Object ? JsonNode.Parse(body.GetRawText())!.AsObject() : new JsonObject();
        var code = o["reqCode"] is JsonValue c && c.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
        var time = o["reqTime"] is JsonValue t && t.TryGetValue<string>(out var ts) && !string.IsNullOrWhiteSpace(ts) ? ts : null;
        var ordered = new JsonObject
        {
            ["reqCode"] = code ?? (clock is null ? "<auto>" : Guid.NewGuid().ToString("N")),
            ["reqTime"] = time ?? (clock is null ? "<auto>" : clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
        };
        foreach (var kv in o.ToList()) if (kv.Key is not ("reqCode" or "reqTime")) { o.Remove(kv.Key); ordered[kv.Key] = kv.Value; }
        return ordered;
    }

    private static (string Outcome, string? Code, string? Message, string? Error) Classify(RcsTransportResult r)
    {
        if (r.Failure == RcsTransportFailure.Timeout) return (RcsOutcomes.Timeout, null, null, r.Error ?? "The RCS did not answer in time.");
        if (r.Failure == RcsTransportFailure.Connection) return (RcsOutcomes.TcpFailed, null, null, r.Error ?? "The connection to the RCS could not be made.");
        string? code = null, message = null; var parsed = false; var hasCode = false;
        if (!string.IsNullOrWhiteSpace(r.Body))
        {
            try
            {
                using var doc = JsonDocument.Parse(r.Body);
                parsed = true;
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("code", out var c)) { hasCode = true; code = c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText(); }
                    if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = SecretText.Replace(m.GetString()!, "$1=***");
                }
            }
            catch (JsonException) { /* reported below */ }
        }
        if (r.HttpStatus is not (>= 200 and < 300)) return (RcsOutcomes.HttpFailure, code, message, $"HTTP {r.HttpStatus}");
        if (!parsed) return (RcsOutcomes.JsonParseFailed, null, null, "The response is not valid JSON.");
        if (!hasCode) return (RcsOutcomes.ResponseInvalid, null, null, "The response is JSON but has no \"code\" field (not an RCS answer).");
        return code == "0" ? (RcsOutcomes.ApiSuccess, code, message, null) : (RcsOutcomes.RcsCodeNotSuccess, code, message, $"The RCS answered with code {code}.");
    }

    // ---- masking: token / password / secret fields are replaced, in the request and in the response, before anything is stored or shown
    private static string Mask(JsonNode node)
    {
        var copy = node.DeepClone();
        MaskNode(copy);
        return copy.ToJsonString(Pretty);
    }

    private static void MaskNode(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (SecretKey.IsMatch(key) && o[key] is JsonValue v && !(v.TryGetValue<string>(out var sv) && string.IsNullOrEmpty(sv))) o[key] = "***";
                    else MaskNode(o[key]);
                }
                break;
            case JsonArray a: foreach (var item in a) MaskNode(item); break;
        }
    }

    private static string MaskText(string body)
    {
        try { var n = JsonNode.Parse(body); if (n is not null) return Mask(n); } catch (JsonException) { /* not JSON: keep the text, masked below */ }
        return SecretText.Replace(body, "$1=***");
    }

    private Task Audit(ActorInfo actor, RcsApiDef api, string requestId, AuditResult result, string detail, CancellationToken ct) =>
        audit.WriteAsync(new AuditRecord("RCS_DIAG_EXECUTE", actor.Username, actor.Role.ToString(), "RcsApi", api.Name, result, $"reqCode={requestId}; {detail}", IpAddress: actor.IpAddress), ct);
}
