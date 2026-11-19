using WCS.Application.Services;

namespace WCS.Api.Api;

public sealed record MaintenanceRequest(bool Enabled, string? Reason);

/// <summary>CR-026: the system-wide Maintenance flag. Everyone signed in may read it (the banner and the disabled "create" buttons); only an Administrator switches it.</summary>
internal static class MaintenanceEndpoints
{
    public static void MapMaintenance(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/system/maintenance").WithTags("System");
        g.MapGet("", async (HttpContext c, MaintenanceService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(ct)));
        g.MapPut("", async (HttpContext c, MaintenanceRequest r, MaintenanceService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.SetAsync(r.Enabled, r.Reason, c.Actor(), ct))).RequireAuthorization(AuthSetup.AdminPolicy);
    }
}
