using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WCS.Tests.Support;

/// <summary>
/// CR-030 MOCK of the HIKROBOT RCS-2000 REST API V3.3 (genAgvSchedulingTask 5.2.1, continueTask 5.2.2, cancelTask 5.2.3, queryTaskStatus 5.2.15). It answers exactly the way the API document describes
/// (code "0" succeeded, "1" failed, "6" same reqCode / task not completed, "99" unknown error; queryTaskStatus data[] with taskStatus and agvCode "exists when the task has been assigned") so the
/// real client can be verified WITHOUT a real RCS. A passing test against this mock is a MOCK test: it proves nothing about a real RCS-2000.
/// </summary>
public sealed class MockRcsServer : IAsyncDisposable
{
    public sealed class MockTask(string taskCode, string reqCode, string taskTyp)
    {
        public string TaskCode { get; } = taskCode; public string ReqCode { get; } = reqCode; public string TaskTyp { get; } = taskTyp;
        public string Status { get; set; } = "1"; public string? AgvCode { get; set; }
    }

    private readonly WebApplication _app;
    private readonly object _gate = new();
    private readonly List<(string Api, JsonDocument Body)> _requests = [];
    private readonly Dictionary<string, MockTask> _tasks = [];
    private int _rr;

    public int Port { get; }
    /// <summary>When not empty every request must carry this "tokenCode", otherwise the RCS answers code "1".</summary>
    public string Token { get; set; } = "";
    /// <summary>The AMRs of the mock fleet. With <see cref="AutoAssign"/> a created task gets the next one at once; without, it stays unassigned until <see cref="Assign"/>.</summary>
    public string[] Fleet { get; set; } = ["R001", "R002", "R003"];
    public bool AutoAssign { get; set; } = true;
    /// <summary>The next create call is answered only after this delay (after the task WAS stored): a timeout after creation.</summary>
    public TimeSpan DelayNextCreate { get; set; }
    /// <summary>The next create call fails with HTTP 500 BEFORE the task is stored: a failure before creation.</summary>
    public bool FailNextCreateBeforeStoring { get; set; }
    /// <summary>The next answer carries this code instead of the real result (for example "99").</summary>
    public string? ForceNextCode { get; set; }
    public bool AnswerNotJson { get; set; }

    private MockRcsServer(WebApplication app, int port) { _app = app; Port = port; }

    public static async Task<MockRcsServer> StartAsync(string token = "")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        MockRcsServer? self = null;
        app.MapPost("/rcms/services/rest/hikRpcService/{api}", (HttpContext c, string api) => self!.HandleAsync(c, api));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        self = new MockRcsServer(app, new Uri(address).Port) { Token = token };
        return self;
    }

    public IReadOnlyList<JsonDocument> Bodies(string api) { lock (_gate) return _requests.Where(r => r.Api == api).Select(r => r.Body).ToList(); }
    public int Count(string api) { lock (_gate) return _requests.Count(r => r.Api == api); }
    public int TaskCount { get { lock (_gate) return _tasks.Count; } }
    public MockTask? TaskOf(string taskCode) { lock (_gate) return _tasks.GetValueOrDefault(taskCode); }
    public void Assign(string taskCode, string? agv) { lock (_gate) if (_tasks.TryGetValue(taskCode, out var t)) t.AgvCode = agv; }
    public void SetStatus(string taskCode, string status) { lock (_gate) if (_tasks.TryGetValue(taskCode, out var t)) t.Status = status; }
    public Task StopAsync() => _app.StopAsync();

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    private async Task<IResult> HandleAsync(HttpContext c, string api)
    {
        using var reader = new StreamReader(c.Request.Body);
        var text = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(text);
        lock (_gate) _requests.Add((api, doc));
        var root = doc.RootElement; var reqCode = Str(root, "reqCode");

        if (AnswerNotJson) return Results.Text("<html>not json</html>", "text/html");
        if (Token.Length > 0 && Str(root, "tokenCode") != Token) return Answer("1", "invalid token", reqCode);
        if (ForceNextCode is { } forced) { ForceNextCode = null; return Answer(forced, forced == "99" ? "unknown error, try again" : "failed", reqCode); }

        switch (api)
        {
            case "genAgvSchedulingTask":
            {
                if (FailNextCreateBeforeStoring) { FailNextCreateBeforeStoring = false; return Results.StatusCode(500); }
                var taskCode = Str(root, "taskCode"); var taskTyp = Str(root, "taskTyp");
                if (taskTyp.Length == 0) return Answer("1", "taskTyp is required", reqCode);
                if (!root.TryGetProperty("positionCodePath", out var path) || path.ValueKind != JsonValueKind.Array || path.GetArrayLength() == 0) return Answer("1", "wbCode or positionCodePath is required", reqCode);
                bool duplicate;
                lock (_gate)
                {
                    duplicate = _tasks.ContainsKey(taskCode);
                    if (!duplicate)
                    {
                        var t = new MockTask(taskCode, reqCode, taskTyp);
                        var agv = Str(root, "agvCode");
                        t.AgvCode = agv.Length > 0 ? agv : AutoAssign && Fleet.Length > 0 ? Fleet[_rr++ % Fleet.Length] : null;
                        _tasks[taskCode] = t;
                    }
                }
                if (DelayNextCreate > TimeSpan.Zero) { var d = DelayNextCreate; DelayNextCreate = TimeSpan.Zero; await System.Threading.Tasks.Task.Delay(d); }
                return duplicate ? Answer("6", "the task of the same reqCode is not completed", reqCode) : Answer("0", "successful", reqCode, taskCode);
            }
            case "continueTask":
            {
                var taskCode = Str(root, "taskCode");
                lock (_gate) { if (!_tasks.TryGetValue(taskCode, out var t)) return Answer("1", "task not found", reqCode); t.Status = "2"; }
                return Answer("0", "successful", reqCode);
            }
            case "cancelTask":
            {
                var taskCode = Str(root, "taskCode");
                lock (_gate) { if (!_tasks.TryGetValue(taskCode, out var t)) return Answer("1", "task not found", reqCode); t.Status = "5"; }
                return Answer("0", "successful", reqCode);
            }
            case "queryTaskStatus":
            {
                var list = new List<object>();
                if (root.TryGetProperty("taskCodes", out var codes))
                    lock (_gate)
                        foreach (var code in codes.EnumerateArray().Select(x => x.GetString() ?? ""))
                            if (_tasks.TryGetValue(code, out var t)) list.Add(new { taskCode = t.TaskCode, taskStatus = t.Status, agvCode = t.AgvCode ?? "", taskTyp = t.TaskTyp });
                return Results.Json(new { code = "0", message = "successful", reqCode, data = list });
            }
            default: return Answer("1", "unknown api", reqCode);
        }
    }

    private static IResult Answer(string code, string message, string reqCode, string? data = null) => Results.Json(new { code, message, reqCode, data });

    public async ValueTask DisposeAsync() { try { await _app.StopAsync(); } catch { /* already stopped */ } await _app.DisposeAsync(); foreach (var r in _requests) r.Body.Dispose(); }
}
