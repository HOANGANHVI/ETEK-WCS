using WCS.Application.Common;
using WCS.Application.Diagnostics;

namespace WCS.Api.Api;

/// <summary>
/// PLC Diagnostic Export (Admin only). Read-only: it never changes a setting, an approval or a PLC, and a failed export never affects the PLC runtime.
/// <c>GET /api/diagnostics/plc/export?plcId=PLC-01&amp;quick=1h</c> (or <c>from</c>/<c>to</c> in ISO 8601) returns a ZIP.
/// </summary>
public static class DiagnosticEndpoints
{
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
    }
}
