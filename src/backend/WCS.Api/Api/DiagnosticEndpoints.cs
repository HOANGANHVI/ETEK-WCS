using WCS.Application.Common;
using WCS.Application.Diagnostics;
using WCS.Application.Abstractions;

namespace WCS.Api.Api;

/// <summary>
/// PLC Diagnostic Export (Admin only). Read-only: it never changes a setting, an approval or a PLC, and a failed export never affects the PLC runtime.
/// <c>GET /api/diagnostics/plc/export?plcId=PLC-01&amp;quick=1h</c> (or <c>from</c>/<c>to</c> in ISO 8601) returns a ZIP.
/// </summary>
public sealed record SystemExportBody(string? Mode, DateTimeOffset? From, DateTimeOffset? To, List<string>? Modules, string? MinLevel, string? PlcId, string? Device, string? CorrelationId, string? TaskId);

public static class DiagnosticEndpoints
{
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>The job as the API shows it: no file path, the per-source report included.</summary>
    private static object View(DiagJob j) => new
    {
        j.Id, state = j.State.ToString(), j.Progress, j.Stage, j.RequestedBy, j.CreatedAt, j.StartedAt, j.FinishedAt, j.ExpiresAt, j.FileName, j.Bytes, j.Completeness, j.Events, j.Errors, j.Warnings,
        request = new { j.Request.Mode, j.Request.From, j.Request.To, j.Request.Scope.Modules, j.Request.Scope.MinLevel, j.Request.Scope.PlcId, j.Request.Scope.Device, j.Request.Scope.CorrelationId, j.Request.Scope.TaskId },
        sources = j.Sources, j.Notes, j.ErrorCode,
    };

    public static void MapDiagnosticApi(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/diagnostics").WithTags("Diagnostics").RequireAuthorization(AuthSetup.AdminPolicy);

        g.MapGet("plc/export", async (HttpContext c, DiagnosticExportService svc, TimeProvider time, string? plcId, string? quick, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            DateTimeOffset f, t;
            switch ((quick ?? "").Trim().ToLowerInvariant())
            {
                case "1h": f = now.AddHours(-1); t = now; break;
                case "24h": f = now.AddHours(-24); t = now; break;
                case "": t = to ?? now; f = from ?? t.AddHours(-24); break;
                default: throw AppException.Validation("DIAGNOSTIC_RANGE_INVALID", "quick must be 1h or 24h.");
            }
            var scope = string.IsNullOrWhiteSpace(plcId) ? null : plcId.Trim();
            var package = await Audited.RunAsync(c, "DIAGNOSTIC_EXPORT", "Diagnostics", scope ?? "ALL", null,
                () => svc.ExportPlcAsync(new DiagnosticRequest(scope, f, t), ct), p => new { p.FileName, files = p.Files.Count, bytes = p.Content.Length, warnings = p.Warnings.Count },
                detail: $"{f:O} .. {t:O}");
            c.Response.Headers["Cache-Control"] = "no-store";
            return Results.File(package.Content, "application/zip", package.FileName);
        });

        // CR-021: whole-system diagnostic export as a background job. POST creates it (202), GET shows its state and the per-source report, the download is Admin-only and audited.
        g.MapPost("exports", async (HttpContext c, DiagnosticExportJobs jobs, SystemExportBody b, CancellationToken ct) =>
        {
            var scope = new DiagScope { Modules = b.Modules ?? [], MinLevel = Blank(b.MinLevel), PlcId = Blank(b.PlcId), Device = Blank(b.Device), CorrelationId = Blank(b.CorrelationId), TaskId = Blank(b.TaskId) };
            var req = new SystemExportRequest((b.Mode ?? "24h").Trim().ToLowerInvariant(), b.From, b.To, scope);
            var job = await Audited.RunAsync(c, "DIAGNOSTIC_EXPORT_SYSTEM", "Diagnostics", scope.IsWholeSystem ? "ALL" : "SCOPED", null, () => Task.FromResult(jobs.Start(req, c.Actor().Username)),
                j => new { j.Id, req.Mode, req.From, req.To, scope.Modules, scope.MinLevel, scope.PlcId, scope.Device, scope.CorrelationId, scope.TaskId }, detail: $"mode={req.Mode}");
            return Results.Json(new ApiResponse<object>(true, View(job), null, c.TraceIdentifier), statusCode: 202);
        });
        g.MapGet("exports", (HttpContext c, DiagnosticExportJobs jobs) => ApiResults.Ok(c, jobs.List().Select(View).ToList()));
        g.MapGet("exports/{id:guid}", (HttpContext c, Guid id, DiagnosticExportJobs jobs) =>
            jobs.Get(id) is { } j ? ApiResults.Ok(c, View(j)) : ApiResults.Fail(c, 404, "DIAGNOSTIC_JOB_NOT_FOUND", "Diagnostic export not found (it may have expired)."));
        g.MapGet("exports/{id:guid}/download", async (HttpContext c, Guid id, DiagnosticExportJobs jobs) =>
        {
            if (jobs.Get(id) is null) return ApiResults.Fail(c, 404, "DIAGNOSTIC_JOB_NOT_FOUND", "Diagnostic export not found (it may have expired).");
            if (jobs.OpenDownload(id) is not { } f) return ApiResults.Fail(c, 409, "DIAGNOSTIC_JOB_NOT_READY", "The export is not finished, failed, or its file has expired.");
            await Audited.WriteAsync(c, "DIAGNOSTIC_EXPORT_DOWNLOAD", "Diagnostics", id.ToString(), f.FileName);
            c.Response.Headers["Cache-Control"] = "no-store";
            return Results.File(new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan), "application/zip", f.FileName);
        });
        g.MapGet("export-options", (HttpContext c) => ApiResults.Ok(c, new { modules = DiagModules.All, levels = DiagLevels.All, modes = new[] { "1h", "24h", "all", "custom" } }));
    }
}
