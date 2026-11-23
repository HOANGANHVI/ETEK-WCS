using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using WCS.Application.Config;
using WCS.Application.RcsDiagnostics;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-032 RCS Diagnostics &amp; Manual API Tester. The RCS is a MOCK (a local server that records what it receives): nothing here was observed on a real RCS-2000, and no command API was ever sent to a real AMR.
/// Everything goes through the real HTTP API, so the Admin-only rule is checked at the backend, not in the menu.
/// </summary>
[Collection("Database")]
public class Cr032RcsDiagnosticsTests(DatabaseFixture fixture)
{
    private sealed record Seen(string Method, string Path, string Body);

    private sealed class MockRcs : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public List<Seen> Requests { get; } = [];
        public Func<string, (int Status, string Body, int DelayMs)> Respond { get; set; } = _ => (200, """{"code":"0","message":"successful","reqCode":"x","data":[]}""", 0);
        public int Port { get; }
        private MockRcs(WebApplication app, int port) { _app = app; Port = port; }
        public int Count { get { lock (Requests) return Requests.Count; } }
        public static async Task<MockRcs> StartAsync(int port = 0)
        {
            var b = WebApplication.CreateBuilder(); b.WebHost.UseUrls($"http://127.0.0.1:{port}"); b.Logging.ClearProviders();
            var app = b.Build(); MockRcs? self = null;
            app.MapPost("/{**path}", async (HttpContext c) =>
            {
                using var r = new StreamReader(c.Request.Body); var body = await r.ReadToEndAsync();
                lock (self!.Requests) self.Requests.Add(new Seen(c.Request.Method, c.Request.Path.Value!, body));
                var (status, text, delay) = self.Respond(c.Request.Path.Value!);
                if (delay > 0) await Task.Delay(delay);
                c.Response.StatusCode = status; c.Response.ContentType = "application/json"; await c.Response.WriteAsync(text);
            });
            await app.StartAsync();
            return self = new MockRcs(app, new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port);
        }
        public async ValueTask DisposeAsync() { try { await _app.StopAsync(); } catch { } await _app.DisposeAsync(); }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public Harness H = default!; public WebApplicationFactory<Program> F = default!; public MockRcs Rcs = default!;
        public HttpClient Admin = default!, Operator = default!, Anonymous = default!;
        public async ValueTask DisposeAsync() { Admin.Dispose(); Operator.Dispose(); Anonymous.Dispose(); F.Dispose(); await H.DisposeAsync(); await Rcs.DisposeAsync(); }
    }

    private async Task<Rig> ArrangeAsync(Dictionary<string, string?>? config = null, int? rcsPort = null, bool saveRcs = true, int timeoutMs = 5000)
    {
        var rig = new Rig { H = await Harness.CreateAsync(fixture), Rcs = await MockRcs.StartAsync() };
        var baseFactory = new DbApiFactory(fixture);
        rig.F = config is null ? baseFactory : baseFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(config)));
        using (var scope = rig.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
            await db.SaveChangesAsync();
            if (saveRcs)
                await scope.ServiceProvider.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(true, new RcsSettings("RCS", "127.0.0.1", rcsPort ?? rig.Rcs.Port, null, ConnectionOptions.AuthNone, null, timeoutMs), null, false, null, false, "test"));
        }
        rig.Admin = rig.F.CreateClient().WithClientHeader(); Assert.Equal(200, await rig.Admin.LoginAsync("admin1"));
        rig.Operator = rig.F.CreateClient().WithClientHeader(); Assert.Equal(200, await rig.Operator.LoginAsync("op1"));
        rig.Anonymous = rig.F.CreateClient().WithClientHeader();
        return rig;
    }

    private static Task<(int Status, JsonElement Root, string Raw)> Exec(HttpClient c, string api, object body) => c.SendJsonAsync(HttpMethod.Post, "/api/rcs-diagnostics/execute", new { api, body });
    private static JsonElement Data(JsonElement root) => root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : throw new Xunit.Sdk.XunitException("no data: " + root.GetRawText());

    // ============================================================================ H1: catalog = the 23 outbound APIs
    private static readonly string[] The23 =
    [
        "queryAgvStatus", "queryTaskStatus", "queryPodBerthAndMat", "genAgvSchedulingTask", "continueTask", "cancelTask", "setTaskPriority", "genPreScheduleTask", "genCtuGroupTaskBatch", "stopRobot", "resumeRobot",
        "lockPosition", "syncMapDatas", "blockArea", "clearRoadWay", "blockStgBin", "bindPodAndBerth", "bindCtnrAndBin", "bindPodAndMat", "bindMaterialTypCodeAndBerth", "getOutPod", "returnPod", "boxApplyPass",
    ];

    public static IEnumerable<object[]> AllApis() => The23.Select(n => new object[] { n });

    [Fact]
    public void Catalog_is_exactly_the_23_outbound_APIs_and_none_of_the_five_callbacks()
    {
        Assert.Equal(The23.Order(StringComparer.Ordinal), RcsApiCatalog.All.Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.Equal(23, RcsApiCatalog.All.Select(a => a.Name).Distinct().Count());
        string[] callbacks = ["agvCallback", "warnCallback", "bindNotify", "applyReturnForValid", "applyReturnForBin"];
        Assert.Equal(callbacks.Order(), RcsApiCatalog.Callbacks.Select(c => c.Name).Order());
        Assert.DoesNotContain(RcsApiCatalog.All, a => callbacks.Contains(a.Name));
        Assert.All(RcsApiCatalog.All, a => { Assert.Equal("POST", a.Method); Assert.EndsWith("/" + a.Name, a.Path); Assert.Contains(a.Group, RcsApiCatalog.Groups); Assert.True(a.Executable); Assert.False(string.IsNullOrWhiteSpace(a.Function)); });
        // the paths of the document: hikRpcService for most, hikTpsService for getOutPod / returnPod, rcms-dps for queryAgvStatus
        Assert.Equal("/rcms-dps/rest/queryAgvStatus", RcsApiCatalog.Find("queryAgvStatus")!.Path);
        Assert.Equal("/rcms/services/rest/hikTpsService/getOutPod", RcsApiCatalog.Find("getOutPod")!.Path);
        Assert.Equal("/rcms/services/rest/hikTpsService/returnPod", RcsApiCatalog.Find("returnPod")!.Path);
        Assert.Equal("/rcms/services/rest/hikRpcService/genAgvSchedulingTask", RcsApiCatalog.Find("genAgvSchedulingTask")!.Path);
    }

    [Fact]
    public void Risk_classes_are_ReadOnly_only_when_the_document_shows_a_pure_query_and_syncMapDatas_is_unverified()
    {
        string E(string n) => RcsApiCatalog.Find(n)!.Effect;
        Assert.Equal(["queryAgvStatus", "queryPodBerthAndMat", "queryTaskStatus"], RcsApiCatalog.All.Where(a => a.Effect == "ReadOnly").Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Unverified", E("syncMapDatas"));
        Assert.Contains("UNVERIFIED", string.Join(" ", RcsApiCatalog.Find("syncMapDatas")!.Gaps));
        string[] critical = ["stopRobot", "resumeRobot", "cancelTask", "blockArea", "clearRoadWay", "genAgvSchedulingTask", "continueTask", "genPreScheduleTask", "genCtuGroupTaskBatch", "getOutPod", "returnPod", "boxApplyPass"];
        Assert.All(critical, n => Assert.Equal("Critical", E(n)));
        string[] mutating = ["lockPosition", "blockStgBin", "bindPodAndBerth", "bindCtnrAndBin", "bindPodAndMat", "bindMaterialTypCodeAndBerth", "setTaskPriority"];
        Assert.All(mutating, n => Assert.Equal("Mutating", E(n)));
        Assert.Equal(23, critical.Length + mutating.Length + 4);
        Assert.All(RcsApiCatalog.All, a => Assert.Contains(a.Effect, new[] { "ReadOnly", "Unverified", "Mutating", "Critical" }));
    }

    [Fact]
    public async Task Catalog_endpoint_lists_the_23_with_group_method_path_permission_and_the_callbacks_as_not_provided()
    {
        await using var rig = await ArrangeAsync();
        var r = await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/catalog");
        Assert.Equal(200, r.Status);
        var apis = Data(r.Root).GetProperty("apis").EnumerateArray().ToList();
        Assert.Equal(23, apis.Count);
        Assert.All(apis, a => { Assert.Equal("Admin", a.GetProperty("permission").GetString()); Assert.Equal("POST", a.GetProperty("method").GetString()); Assert.True(a.GetProperty("fields").GetArrayLength() >= 4); });
        var cb = Data(r.Root).GetProperty("callbacks").EnumerateArray().ToList();
        Assert.Equal(5, cb.Count); Assert.All(cb, c => Assert.Equal("NotProvided", c.GetProperty("provided").GetString()));
        Assert.Equal(0, rig.Rcs.Count);   // opening the catalog sent nothing
    }

    // ============================================================================ H2: schema validation per API
    [Theory, MemberData(nameof(AllApis))]
    public void The_documents_sample_of_every_API_is_valid_and_each_required_field_is_enforced(string name)
    {
        var api = RcsApiCatalog.Find(name)!;
        var sample = JsonDocument.Parse(api.Sample).RootElement;
        var withCommon = Merge(sample, new() { ["reqCode"] = "R1" });
        Assert.True(RcsRequestValidator.Validate(api, withCommon).IsValid, name + ": " + string.Join("; ", RcsRequestValidator.Validate(api, withCommon).Errors.Select(e => e.Message)));
        // reqCode is required
        Assert.Contains(RcsRequestValidator.Validate(api, sample).Errors, e => e.Path == "reqCode" && e.Code == "REQUIRED");
        // every required top-level field of the API, removed from the sample, is reported
        foreach (var f in api.Fields.Where(f => f.Required))
        {
            var without = Merge(sample, new() { ["reqCode"] = "R1" }, remove: f.Name);
            Assert.Contains(RcsRequestValidator.Validate(api, without).Errors, e => e.Path == f.Name);
        }
        // a field longer than its maximum is reported
        foreach (var f in api.Fields.Where(f => f.Type == "string" && f.MaxLength is not null && f.Enum is null))
        {
            var longer = Merge(sample, new() { ["reqCode"] = "R1", [f.Name] = new string('9', f.MaxLength!.Value + 1) });
            Assert.Contains(RcsRequestValidator.Validate(api, longer).Errors, e => e.Path == f.Name && e.Code is "MAX_LENGTH" or "RANGE");
        }
        // a value outside the documented enumeration is reported
        foreach (var f in api.Fields.Where(f => f.Type == "string" && f.Enum is { Count: > 0 }))
        {
            var bad = Merge(sample, new() { ["reqCode"] = "R1", [f.Name] = "ZZ" });
            Assert.Contains(RcsRequestValidator.Validate(api, bad).Errors, e => e.Path == f.Name);
        }
        // reqCode longer than 32 is reported
        Assert.Contains(RcsRequestValidator.Validate(api, Merge(sample, new() { ["reqCode"] = new string('a', 33) })).Errors, e => e.Path == "reqCode" && e.Code == "MAX_LENGTH");
    }

    private static JsonElement Merge(JsonElement sample, Dictionary<string, object?> add, string? remove = null)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, object?>>(sample.GetRawText())!;
        foreach (var kv in add) d[kv.Key] = kv.Value;
        if (remove is not null) d.Remove(remove);
        return JsonDocument.Parse(JsonSerializer.Serialize(d)).RootElement;
    }

    [Fact]
    public void Document_rules_are_enforced_one_of_exactly_one_and_conditional_fields()
    {
        JsonElement J(string s) => JsonDocument.Parse(s).RootElement;
        bool Bad(string api, string json, string path) => RcsRequestValidator.Validate(RcsApiCatalog.Find(api)!, J(json)).Errors.Any(e => e.Path.Contains(path));
        Assert.True(Bad("cancelTask", """{"reqCode":"1"}""", "taskCode"));
        Assert.False(Bad("cancelTask", """{"reqCode":"1","agvCode":"7"}""", "taskCode"));
        Assert.True(Bad("queryTaskStatus", """{"reqCode":"1"}""", "taskCodes"));
        Assert.True(Bad("genAgvSchedulingTask", """{"reqCode":"1","taskTyp":"F01"}""", "wbCode"));
        Assert.True(Bad("genAgvSchedulingTask", """{"reqCode":"1","taskTyp":"F99","wbCode":"W"}""", "taskTyp"));
        Assert.True(Bad("continueTask", """{"reqCode":"1","taskCode":"1","wbCode":"W"}""", "wbCode"));    // exactly one
        Assert.True(Bad("stopRobot", """{"reqCode":"1","robotCount":"-1"}""", "mapShortName"));
        Assert.False(Bad("stopRobot", """{"reqCode":"1","robotCount":"-1","mapShortName":"M"}""", "mapShortName"));
        Assert.True(Bad("blockArea", """{"reqCode":"1","matterArea":"A","indBind":"1","pause":"0","controlMod":"2","noticeThird":"0"}""", "targetArea"));
        Assert.True(Bad("setTaskPriority", """{"reqCode":"1","priorities":[{"taskCode":"T","priority":"128"}]}""", "priority"));
        Assert.True(Bad("setTaskPriority", """{"reqCode":"1","priorities":[{"taskCode":"T"}]}""", "priority"));
        Assert.True(Bad("bindCtnrAndBin", """{"reqCode":"1","ctnrCode":"C","ctnrTyp":"T","indBind":"1"}""", "stgBinCode"));
    }

    // ============================================================================ H3: Admin only, enforced at the backend
    [Fact]
    public async Task Only_an_Admin_may_use_any_endpoint_Operator_and_anonymous_are_refused_by_the_backend_and_nothing_is_sent()
    {
        await using var rig = await ArrangeAsync();
        var calls = new (HttpMethod M, string Url, object? Body)[]
        {
            (HttpMethod.Get, "/api/rcs-diagnostics/overview", null), (HttpMethod.Get, "/api/rcs-diagnostics/catalog", null), (HttpMethod.Get, "/api/rcs-diagnostics/history", null),
            (HttpMethod.Get, "/api/rcs-diagnostics/history/export", null), (HttpMethod.Get, "/api/rcs-diagnostics/callbacks", null),
            (HttpMethod.Post, "/api/rcs-diagnostics/preview", new { api = "queryAgvStatus", body = new { } }), (HttpMethod.Post, "/api/rcs-diagnostics/execute", new { api = "queryAgvStatus", body = new { } }),
            (HttpMethod.Post, "/api/rcs-diagnostics/tcp-check", null), (HttpMethod.Post, "/api/rcs-diagnostics/api-check", null),
        };
        foreach (var (m, url, body) in calls)
        {
            Assert.Equal(403, (await rig.Operator.SendJsonAsync(m, url, body)).Status);
            Assert.Equal(401, (await rig.Anonymous.SendJsonAsync(m, url, body)).Status);
            Assert.Equal(200, (await rig.Admin.SendJsonAsync(m, url, body)).Status);
        }
        // only the Admin calls reached the RCS: tcp-check connects (no HTTP), execute + api-check sent exactly one request each
        Assert.Equal(2, rig.Rcs.Count);
        await rig.H.Db(async db => { Assert.Equal(2, await db.RcsApiTests.CountAsync()); return 0; });
    }

    [Fact]
    public async Task The_service_itself_refuses_a_non_admin_actor()
    {
        await using var rig = await ArrangeAsync();
        using var scope = rig.F.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<RcsDiagnosticsService>();
        var op = new WCS.Application.Auth.ActorInfo(Guid.NewGuid(), "op1", UserRole.Operator);
        var ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => svc.ExecuteAsync(op, "queryAgvStatus", JsonDocument.Parse("{}").RootElement));
        Assert.Equal("RCS_DIAG_ADMIN_ONLY", ex.Code); Assert.Equal(0, rig.Rcs.Count);
    }

    // ============================================================================ H4/H5/H6: execute sends immediately; request/response inspector
    [Fact]
    public async Task Execute_sends_immediately_with_no_approval_and_the_inspector_shows_request_response_and_timing()
    {
        await using var rig = await ArrangeAsync();
        rig.Rcs.Respond = _ => (200, """{"code":"0","message":"successful","reqCode":"x","data":[{"robotCode":"1001","battery":"80"}]}""", 0);
        var r = await Exec(rig.Admin, "queryAgvStatus", new { mapShortName = "AA" });
        Assert.Equal(200, r.Status);
        var d = Data(r.Root);
        Assert.True(d.GetProperty("success").GetBoolean()); Assert.Equal("API_SUCCESS", d.GetProperty("outcome").GetString());
        Assert.Equal(200, d.GetProperty("httpStatus").GetInt32()); Assert.Equal("0", d.GetProperty("rcsCode").GetString()); Assert.Equal("successful", d.GetProperty("rcsMessage").GetString());
        Assert.Equal("POST", d.GetProperty("method").GetString()); Assert.Equal($"127.0.0.1:{rig.Rcs.Port}/rcms-dps/rest/queryAgvStatus", d.GetProperty("endpoint").GetString());
        Assert.True(d.GetProperty("durationMs").GetInt64() >= 0); Assert.False(string.IsNullOrEmpty(d.GetProperty("requestId").GetString()));
        Assert.Contains("1001", d.GetProperty("responseBody").GetString());
        Assert.DoesNotContain(d.GetProperty("requestHeaders").EnumerateObject(), h => h.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.False(d.TryGetProperty("pending", out _));
        // exactly ONE request reached the RCS, with the document's body: reqCode + reqTime generated, no tokenCode
        Assert.Equal(1, rig.Rcs.Count);
        var seen = rig.Rcs.Requests[0]; Assert.Equal("POST", seen.Method); Assert.Equal("/rcms-dps/rest/queryAgvStatus", seen.Path);
        var body = JsonDocument.Parse(seen.Body).RootElement;
        Assert.Equal(d.GetProperty("requestId").GetString(), body.GetProperty("reqCode").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", body.GetProperty("reqTime").GetString()!);
        Assert.Equal("AA", body.GetProperty("mapShortName").GetString()); Assert.False(body.TryGetProperty("tokenCode", out _));
    }

    [Theory, MemberData(nameof(AllApis))]
    public async Task Every_API_reaches_the_mock_RCS_by_POST_on_its_documented_path_with_the_documented_body(string name)
    {
        await using var rig = await ArrangeAsync();
        var api = RcsApiCatalog.Find(name)!;
        var sample = JsonDocument.Parse(api.Sample).RootElement;
        var r = await Exec(rig.Admin, name, sample);
        Assert.Equal(200, r.Status);
        Assert.Equal("API_SUCCESS", Data(r.Root).GetProperty("outcome").GetString());
        Assert.Equal(1, rig.Rcs.Count);
        Assert.Equal("POST", rig.Rcs.Requests[0].Method); Assert.Equal(api.Path, rig.Rcs.Requests[0].Path);
        var sent = JsonDocument.Parse(rig.Rcs.Requests[0].Body).RootElement;
        foreach (var p in sample.EnumerateObject()) Assert.True(sent.TryGetProperty(p.Name, out _), $"{name}: '{p.Name}' was not sent");
    }

    [Fact]
    public async Task An_invalid_request_is_not_sent_and_is_audited_as_failed()
    {
        await using var rig = await ArrangeAsync();
        var r = await Exec(rig.Admin, "cancelTask", new { forceCancel = "9" });
        Assert.Equal(400, r.Status); Assert.Equal("RCS_DIAG_VALIDATION_FAILED", r.Root.ErrorCode());
        Assert.Equal(0, rig.Rcs.Count);
        await rig.H.Db(async db =>
        {
            Assert.Equal(0, await db.RcsApiTests.CountAsync());
            var a = await db.AuditLog.AsNoTracking().SingleAsync(x => x.Action == "RCS_DIAG_EXECUTE");
            Assert.Equal("admin1", a.Actor); Assert.Equal(AuditResult.Failed, a.Result); Assert.Equal("cancelTask", a.TargetId);
            return 0;
        });
        var preview = await rig.Admin.SendJsonAsync(HttpMethod.Post, "/api/rcs-diagnostics/preview", new { api = "cancelTask", body = new { forceCancel = "9" } });
        Assert.Equal(200, preview.Status); Assert.False(Data(preview.Root).GetProperty("valid").GetBoolean()); Assert.True(Data(preview.Root).GetProperty("errors").GetArrayLength() >= 2); Assert.Equal(0, rig.Rcs.Count);
    }

    // ============================================================================ H7: outcome classes
    [Fact]
    public async Task HTTP_200_with_an_RCS_code_other_than_0_is_NOT_a_success()
    {
        await using var rig = await ArrangeAsync();
        rig.Rcs.Respond = _ => (200, """{"code":"1","message":"position not found","reqCode":"x"}""", 0);
        var d = Data((await Exec(rig.Admin, "lockPosition", new { positionCode = "p1", indBind = "1" })).Root);
        Assert.False(d.GetProperty("success").GetBoolean()); Assert.Equal("RCS_CODE_NOT_SUCCESS", d.GetProperty("outcome").GetString());
        Assert.Equal(200, d.GetProperty("httpStatus").GetInt32()); Assert.Equal("1", d.GetProperty("rcsCode").GetString()); Assert.Equal("position not found", d.GetProperty("rcsMessage").GetString());
    }

    [Fact]
    public async Task HTTP_failures_invalid_JSON_and_non_RCS_answers_are_told_apart()
    {
        await using var rig = await ArrangeAsync();
        async Task<JsonElement> One(int status, string body)
        {
            rig.Rcs.Respond = _ => (status, body, 0);
            return Data((await Exec(rig.Admin, "queryTaskStatus", new { agvCode = "1" })).Root);
        }
        var e500 = await One(500, "boom"); Assert.Equal("HTTP_FAILURE", e500.GetProperty("outcome").GetString()); Assert.Equal(500, e500.GetProperty("httpStatus").GetInt32()); Assert.False(e500.GetProperty("success").GetBoolean());
        var e404 = await One(404, """{"code":"0","message":"ok"}"""); Assert.Equal("HTTP_FAILURE", e404.GetProperty("outcome").GetString());   // a 4xx with an OK-looking body is still a failure
        var e401 = await One(401, ""); Assert.Equal("HTTP_FAILURE", e401.GetProperty("outcome").GetString());
        var bad = await One(200, "<html>not json</html>"); Assert.Equal("JSON_PARSE_FAILED", bad.GetProperty("outcome").GetString()); Assert.Equal(200, bad.GetProperty("httpStatus").GetInt32());
        var empty = await One(200, ""); Assert.Equal("JSON_PARSE_FAILED", empty.GetProperty("outcome").GetString());
        var nocode = await One(200, """{"hello":"world"}"""); Assert.Equal("RESPONSE_INVALID", nocode.GetProperty("outcome").GetString());
        var arr = await One(200, "[1,2]"); Assert.Equal("RESPONSE_INVALID", arr.GetProperty("outcome").GetString());
        var ok = await One(200, """{"code":"0","message":"successful","reqCode":"x","data":[]}"""); Assert.Equal("API_SUCCESS", ok.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Timeout_and_refused_connection_are_reported_with_their_own_outcome()
    {
        await using var rig = await ArrangeAsync(timeoutMs: 1000);
        rig.Rcs.Respond = _ => (200, """{"code":"0","message":"successful","reqCode":"x"}""", 3000);
        var t = Data((await Exec(rig.Admin, "queryAgvStatus", new { })).Root);
        Assert.Equal("TIMEOUT", t.GetProperty("outcome").GetString()); Assert.False(t.GetProperty("success").GetBoolean()); Assert.Equal(JsonValueKind.Null, t.GetProperty("httpStatus").ValueKind);
        Assert.True(t.GetProperty("durationMs").GetInt64() >= 900);

        var free = new TcpListener(IPAddress.Loopback, 0); free.Start(); var closed = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();
        await using var rig2 = await ArrangeAsync(rcsPort: closed);
        var c = Data((await Exec(rig2.Admin, "queryAgvStatus", new { })).Root);
        Assert.Equal("TCP_CONNECTION_FAILED", c.GetProperty("outcome").GetString()); Assert.False(c.GetProperty("success").GetBoolean());
    }

    // ============================================================================ H8: TCP check is not an API check
    [Fact]
    public async Task A_TCP_connection_that_succeeds_does_not_mean_the_API_works()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var accepting = Task.Run(async () => { while (true) { try { (await listener.AcceptTcpClientAsync()).Dispose(); } catch { return; } } });   // accepts and closes: a port that is not an RCS API
        try
        {
            await using var rig = await ArrangeAsync(rcsPort: ((IPEndPoint)listener.LocalEndpoint).Port, timeoutMs: 1500);
            var tcp = await rig.Admin.SendJsonAsync(HttpMethod.Post, "/api/rcs-diagnostics/tcp-check");
            Assert.True(Data(tcp.Root).GetProperty("success").GetBoolean());
            var api = await rig.Admin.SendJsonAsync(HttpMethod.Post, "/api/rcs-diagnostics/api-check");
            Assert.False(Data(api.Root).GetProperty("success").GetBoolean());
            var ov = Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/overview")).Root);
            Assert.True(ov.GetProperty("lastTcpCheck").GetProperty("success").GetBoolean());
            Assert.False(ov.GetProperty("lastApiCheck").GetProperty("success").GetBoolean()); Assert.False(ov.GetProperty("apiAvailable").GetBoolean());
            Assert.Equal("Real", ov.GetProperty("source").GetString());
        }
        finally { listener.Stop(); await accepting; }
    }

    [Fact]
    public async Task Overview_uses_the_saved_connection_reveals_no_secret_and_calls_nothing()
    {
        await using var rig = await ArrangeAsync();
        var ov = await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/overview");
        var d = Data(ov.Root);
        Assert.Equal("127.0.0.1", d.GetProperty("host").GetString()); Assert.Equal(rig.Rcs.Port, d.GetProperty("port").GetInt32()); Assert.True(d.GetProperty("configured").GetBoolean()); Assert.True(d.GetProperty("enabled").GetBoolean());
        Assert.DoesNotContain("password", ov.Raw, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("tokenCode", ov.Raw);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("lastApiCheck").ValueKind);
        Assert.Equal(0, rig.Rcs.Count);
    }

    // ============================================================================ H9/H10: history, audit, export, masking
    [Fact]
    public async Task Every_execution_is_in_the_history_and_the_audit_log_with_who_when_api_request_id_and_result_and_the_filters_work()
    {
        await using var rig = await ArrangeAsync();
        await Exec(rig.Admin, "queryAgvStatus", new { });
        rig.Rcs.Respond = _ => (200, """{"code":"1","message":"nope","reqCode":"x"}""", 0);
        var failed = Data((await Exec(rig.Admin, "lockPosition", new { positionCode = "p1", indBind = "0" })).Root);

        var all = Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history")).Root);
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        var first = all.GetProperty("items")[0];   // newest first
        Assert.Equal("lockPosition", first.GetProperty("api").GetString()); Assert.Equal("admin1", first.GetProperty("user").GetString()); Assert.Equal(failed.GetProperty("requestId").GetString(), first.GetProperty("requestId").GetString());
        Assert.False(first.GetProperty("success").GetBoolean()); Assert.Equal("1", first.GetProperty("rcsCode").GetString()); Assert.NotEqual(default, first.GetProperty("executedAt").GetDateTimeOffset());

        Assert.Equal(1, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?api=queryAgvStatus")).Root).GetProperty("total").GetInt32());
        Assert.Equal(1, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?status=success")).Root).GetProperty("total").GetInt32());
        Assert.Equal(1, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?status=failure")).Root).GetProperty("total").GetInt32());
        Assert.Equal(1, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?status=RCS_CODE_NOT_SUCCESS")).Root).GetProperty("total").GetInt32());
        Assert.Equal(2, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?user=admin1")).Root).GetProperty("total").GetInt32());
        Assert.Equal(0, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?user=nobody")).Root).GetProperty("total").GetInt32());
        Assert.Equal(0, Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history?from=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O")))).Root).GetProperty("total").GetInt32());
        var one = await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history/" + first.GetProperty("id").GetGuid());
        Assert.Equal(200, one.Status);

        await rig.H.Db(async db =>
        {
            var audit = await db.AuditLog.AsNoTracking().Where(a => a.Action == "RCS_DIAG_EXECUTE").OrderBy(a => a.OccurredAt).ToListAsync();
            Assert.Equal(2, audit.Count); Assert.All(audit, a => Assert.Equal("admin1", a.Actor));
            Assert.Equal([AuditResult.Success, AuditResult.Failed], audit.Select(a => a.Result).ToArray());
            Assert.Contains(failed.GetProperty("requestId").GetString()!, audit[1].Detail);
            return 0;
        });
        var export = await rig.Admin.GetAsync("/api/rcs-diagnostics/history/export");
        Assert.Equal(200, (int)export.StatusCode); Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, JsonDocument.Parse(await export.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Token_password_and_secret_fields_are_masked_in_the_inspector_the_history_the_export_and_the_audit()
    {
        await using var rig = await ArrangeAsync();
        rig.Rcs.Respond = _ => (200, """{"code":"0","message":"successful","reqCode":"x","token":"RESP-SECRET-9","data":{"password":"RESP-PWD"}}""", 0);
        var r = await Exec(rig.Admin, "queryAgvStatus", new { tokenCode = "TOK-SECRET-123" });
        Assert.Contains("TOK-SECRET-123", rig.Rcs.Requests[0].Body);   // the Admin chose to send it: the RCS gets it
        Assert.DoesNotContain("TOK-SECRET-123", r.Raw); Assert.DoesNotContain("RESP-SECRET-9", r.Raw); Assert.DoesNotContain("RESP-PWD", r.Raw);
        var hist = await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/history");
        Assert.DoesNotContain("TOK-SECRET-123", hist.Raw); Assert.DoesNotContain("RESP-SECRET-9", hist.Raw); Assert.DoesNotContain("RESP-PWD", hist.Raw);
        Assert.DoesNotContain("TOK-SECRET-123", await (await rig.Admin.GetAsync("/api/rcs-diagnostics/history/export")).Content.ReadAsStringAsync());
        var pv = await rig.Admin.SendJsonAsync(HttpMethod.Post, "/api/rcs-diagnostics/preview", new { api = "queryAgvStatus", body = new { tokenCode = "TOK-SECRET-123" } });
        Assert.DoesNotContain("TOK-SECRET-123", pv.Raw);
        await rig.H.Db(async db => { Assert.DoesNotContain(await db.AuditLog.AsNoTracking().Select(a => a.Detail + a.BeforeJson + a.AfterJson).ToListAsync(), x => x != null && x.Contains("TOK-SECRET-123")); return 0; });
    }

    // ============================================================================ H11/H12: not operational; no simulator; no auto-run
    [Fact]
    public async Task A_manual_call_changes_no_operational_state_and_the_fleet_snapshot_is_untouched()
    {
        await using var rig = await ArrangeAsync(new() { ["Rcs:Fleet:Source"] = "Simulator" });   // even with the simulator configured as the fleet source, diagnostics goes to the real saved RCS
        rig.Rcs.Respond = _ => (200, """{"code":"0","message":"successful","reqCode":"x","data":[{"robotCode":"1001","battery":"80","posX":"1","posY":"2"}]}""", 0);
        await Exec(rig.Admin, "queryAgvStatus", new { });
        await Exec(rig.Admin, "bindPodAndBerth", new { podCode = "P1", positionCode = "L1", indBind = "1" });
        Assert.Equal(2, rig.Rcs.Count);
        await rig.H.Db(async db =>
        {
            Assert.Equal(0, await db.RcsAmrSnapshots.CountAsync()); Assert.Equal(0, await db.RcsFleetStates.CountAsync());
            Assert.Equal(0, await db.Tasks.CountAsync()); Assert.Equal(0, await db.Racks.CountAsync());
            return 0;
        });
    }

    [Fact]
    public async Task With_no_saved_RCS_connection_nothing_is_sent_and_no_simulator_answer_is_ever_returned()
    {
        await using var rig = await ArrangeAsync(saveRcs: false);
        var r = Data((await Exec(rig.Admin, "queryAgvStatus", new { })).Root);
        Assert.Equal("NOT_CONFIGURED", r.GetProperty("outcome").GetString()); Assert.False(r.GetProperty("success").GetBoolean()); Assert.Equal(JsonValueKind.Null, r.GetProperty("httpStatus").ValueKind);
        Assert.Equal(0, rig.Rcs.Count);
        Assert.DoesNotContain("SIM-", r.GetRawText());
    }

    [Fact]
    public async Task Opening_the_module_sends_no_command_and_no_RCS_request_at_all()
    {
        await using var rig = await ArrangeAsync();
        foreach (var url in new[] { "overview", "catalog", "history", "callbacks" }) Assert.Equal(200, (await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/" + url)).Status);
        Assert.Equal(200, (await rig.Admin.SendJsonAsync(HttpMethod.Post, "/api/rcs-diagnostics/preview", new { api = "stopRobot", body = new { robotCount = "-1", mapShortName = "M" } })).Status);   // a preview never sends
        Assert.Equal(0, rig.Rcs.Count);
    }

    // ============================================================================ H13: no arbitrary destination
    [Theory]
    [InlineData("http://evil.example/x"), InlineData("../../etc/passwd"), InlineData("queryAgvStatus/../cancelTask"), InlineData("QueryAgvStatus"), InlineData("agvCallback"), InlineData("")]
    public async Task Only_catalogued_APIs_can_be_called_no_url_no_path_no_unknown_name(string api)
    {
        await using var rig = await ArrangeAsync();
        var r = await Exec(rig.Admin, api, new { });
        Assert.Equal(400, r.Status); Assert.Equal("RCS_DIAG_API_NOT_IN_CATALOG", r.Root.ErrorCode()); Assert.Equal(0, rig.Rcs.Count);
    }

    [Theory]
    [InlineData("host"), InlineData("port"), InlineData("url"), InlineData("baseUrl"), InlineData("endpoint"), InlineData("Address")]
    public async Task A_host_port_or_url_in_the_body_is_refused_and_the_request_goes_only_to_the_saved_RCS(string key)
    {
        await using var rig = await ArrangeAsync();
        var r = await Exec(rig.Admin, "queryAgvStatus", new Dictionary<string, object> { [key] = "10.9.9.9:1" });
        Assert.Equal(400, r.Status); Assert.Equal("RCS_DIAG_VALIDATION_FAILED", r.Root.ErrorCode()); Assert.Equal(0, rig.Rcs.Count);
    }

    // ============================================================================ H14: concurrency limit and the 5 s polling
    [Fact]
    public async Task Manual_requests_are_limited_and_the_surplus_is_refused_not_queued()
    {
        await using var rig = await ArrangeAsync(new() { ["Rcs:Diagnostics:MaxConcurrent"] = "1" });
        rig.Rcs.Respond = _ => (200, """{"code":"0","message":"successful","reqCode":"x"}""", 1500);
        var slow = Exec(rig.Admin, "queryAgvStatus", new { });
        await Task.Delay(400);
        var second = await Exec(rig.Admin, "queryAgvStatus", new { });
        Assert.Equal(409, second.Status); Assert.Equal("RCS_DIAG_BUSY", second.Root.ErrorCode());
        Assert.Equal(200, (await slow).Status);
        Assert.Equal(1, rig.Rcs.Count);
        Assert.Equal(200, (await Exec(rig.Admin, "queryAgvStatus", new { })).Status);   // the slot is free again
    }

    [Fact]
    public async Task A_manual_call_does_not_use_or_block_the_fleet_polling_path()
    {
        // the diagnostics module owns its HTTP path and its gate: while a slow manual call is running, the fleet service (the 5 s poll) still runs its own refresh at once and keeps its own snapshot
        await using var rcs = await MockRcs.StartAsync();
        await using var h = await Harness.CreateAsync(fixture, s => s.Configure<RcsDiagnosticsOptions>(o => o.MaxConcurrent = 1), new Dictionary<string, string?> { ["Rcs:Fleet:Source"] = "Real" });
        await h.Scoped(async sp => { await sp.GetRequiredService<ConnectionConfigService>().UpdateRcsAsync(new UpdateRcsCommand(true, new RcsSettings("RCS", "127.0.0.1", rcs.Port, null, ConnectionOptions.AuthNone, null, 5000), null, false, null, false, "test")); return 0; });
        var calls = 0;
        rcs.Respond = _ => (200, """{"code":"0","message":"successful","reqCode":"x","data":[{"robotCode":"A1"}]}""", Interlocked.Increment(ref calls) == 1 ? 1500 : 0);
        var admin = new WCS.Application.Auth.ActorInfo(Guid.NewGuid(), "admin", UserRole.Admin);
        var slow = h.Scoped(sp => sp.GetRequiredService<RcsDiagnosticsService>().ExecuteAsync(admin, "queryAgvStatus", JsonDocument.Parse("{}").RootElement));
        await Task.Delay(300);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await h.Scoped(sp => sp.GetRequiredService<WCS.Application.Fleet.FleetService>().RefreshAsync(CancellationToken.None, force: true));
        Assert.True(watch.ElapsedMilliseconds < 1200, $"the fleet refresh waited {watch.ElapsedMilliseconds} ms behind the manual call");
        Assert.True((await slow).Success);
        Assert.Equal(2, rcs.Count);
        // the fleet poll stored its own snapshot (AMR A1) and the manual call stored nothing there
        Assert.Equal(1, await h.Db(db => db.RcsAmrSnapshots.CountAsync()));
        Assert.Equal(1, await h.Db(db => db.RcsApiTests.CountAsync()));
    }

    // ============================================================================ callback monitor
    [Fact]
    public async Task Callback_monitor_says_honestly_that_no_receiver_exists_and_shows_nothing_invented()
    {
        await using var rig = await ArrangeAsync();
        var d = Data((await rig.Admin.SendJsonAsync(HttpMethod.Get, "/api/rcs-diagnostics/callbacks")).Root);
        Assert.Equal(5, d.GetProperty("endpoints").GetArrayLength());
        Assert.All(d.GetProperty("endpoints").EnumerateArray(), e => Assert.Equal("NotProvided", e.GetProperty("provided").GetString()));
        Assert.Equal(0, d.GetProperty("received").GetArrayLength());
    }
}
