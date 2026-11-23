using System.Text;
using System.Text.Json;
using WCS.Application.RcsDiagnostics;

namespace WCS.Api.Api;

/// <summary>Body of <c>POST /api/rcs-diagnostics/execute</c> and <c>/preview</c>: the catalogued API name and the request fields. There is NO url / host / port in it (such keys are refused).</summary>
public sealed record RcsExecuteBody(string? Api, JsonElement Body);

/// <summary>
/// CR-032: RCS Diagnostics &amp; Manual API Tester. EVERY endpoint is Admin-only (policy on the group, and the service checks again). Reading the catalog, the overview or the history never calls the RCS; only
/// <c>execute</c>, <c>tcp-check</c> and <c>api-check</c> do, and each of them is an explicit Admin action that is sent immediately (no approval step).
/// </summary>
public static class RcsDiagnosticsEndpoints
{
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static object CatalogView(RcsApiDef a) => new
    {
        a.Name, a.Group, a.Section, a.Method, a.Path, a.Function, a.Effect, a.Executable, fields = RcsApiCatalog.RequestFields(a), a.Rules, response = a.Response, a.Sample, a.Gaps,
        permission = "Admin",
    };

    public static void MapRcsDiagnosticsApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/rcs-diagnostics").WithTags("RcsDiagnostics").RequireAuthorization(AuthSetup.AdminPolicy);

        g.MapGet("overview", async (HttpContext c, RcsDiagnosticsService s, CancellationToken ct) => ApiResults.Ok(c, await s.OverviewAsync(c.Actor(), ct)));
        g.MapGet("catalog", (HttpContext c) => ApiResults.Ok(c, new { groups = RcsApiCatalog.Groups, apis = RcsApiCatalog.All.Select(CatalogView).ToList(), callbacks = RcsApiCatalog.Callbacks }));

        g.MapPost("preview", async (HttpContext c, RcsDiagnosticsService s, RcsExecuteBody b, CancellationToken ct) =>
            ApiResults.Ok(c, await s.PreviewAsync(c.Actor(), b.Api ?? "", b.Body, ct)));
        g.MapPost("execute", async (HttpContext c, RcsDiagnosticsService s, RcsExecuteBody b, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ExecuteAsync(c.Actor(), b.Api ?? "", b.Body, ct)));
        g.MapPost("tcp-check", async (HttpContext c, RcsDiagnosticsService s, CancellationToken ct) => ApiResults.Ok(c, await s.TcpCheckAsync(c.Actor(), ct)));
        g.MapPost("api-check", async (HttpContext c, RcsDiagnosticsService s, CancellationToken ct) => ApiResults.Ok(c, await s.ApiCheckAsync(c.Actor(), ct)));

        g.MapGet("history", async (HttpContext c, RcsDiagnosticsService s, DateTimeOffset? from, DateTimeOffset? to, string? api, string? user, string? status, int? skip, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, await s.HistoryAsync(c.Actor(), new RcsHistoryQuery(from, to, Blank(api), Blank(user), Blank(status), skip ?? 0, take ?? 50), ct)));
        g.MapGet("history/export", async (HttpContext c, RcsDiagnosticsService s, DateTimeOffset? from, DateTimeOffset? to, string? api, string? user, string? status, CancellationToken ct) =>
        {
            var rows = await s.ExportAsync(c.Actor(), new RcsHistoryQuery(from, to, Blank(api), Blank(user), Blank(status), 0, 1000), ct);
            c.Response.Headers["Cache-Control"] = "no-store";
            var json = JsonSerializer.Serialize(new { exportedAt = DateTimeOffset.UtcNow, count = rows.Count, note = "Requests and responses are masked; tokens, passwords and secrets are replaced by ***.", items = rows }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"rcs-diagnostics-history-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        });
        g.MapGet("history/{id:guid}", async (HttpContext c, RcsDiagnosticsService s, Guid id, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(c.Actor(), id, ct)));

        // Callback Monitor: the five calls the RCS makes TO a platform. On this build WCS provides no receiver for any of them, and says so; the list shows only callbacks that were really received.
        g.MapGet("callbacks", async (HttpContext c, RcsDiagnosticsService s, string? callback, int? skip, int? take, CancellationToken ct) =>
            ApiResults.Ok(c, new { endpoints = RcsApiCatalog.Callbacks, received = await s.CallbacksAsync(c.Actor(), Blank(callback), skip ?? 0, take ?? 50, ct) }));
    }
}
