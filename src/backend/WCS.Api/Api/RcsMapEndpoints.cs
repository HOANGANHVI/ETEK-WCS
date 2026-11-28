using System.ComponentModel.DataAnnotations;
using WCS.Application.RcsMaps;
using WCS.Domain.Users;

namespace WCS.Api.Api;

public sealed record RcsMapImportRequest([property: Required, StringLength(260)] string FileName, [property: Required] string Content);

/// <summary>CR-065: the monitoring map. Every signed-in user reads it; an Admin imports it and sets the coordinate conversion.</summary>
public static class RcsMapEndpoints
{
    public static void MapRcsMapApi(this RouteGroupBuilder api, string adminPolicy)
    {
        api.MapGet("/rcs-map", async (HttpContext c, RcsMapService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(ct))).WithTags("RCS map");
        var cfg = api.MapGroup("/config/rcs-map").WithTags("RCS map").RequireAuthorization(adminPolicy);
        cfg.MapPost("", async (HttpContext c, RcsMapImportRequest r, RcsMapService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_RCS_MAP_IMPORT", "RcsMap", r.FileName, null,
                () => s.ImportAsync(r.FileName, r.Content, c.Actor().Username, ct), x => new { x.Info.MapId, x.Info.MapName, x.Info.Sha256, x.Info.PointCount, x.Info.EdgeCount, x.Replaced })));
        // CR-066: name and WCS location of one point; "removed" when both are empty
        cfg.MapPut("points/{pointId:long}", async (HttpContext c, long pointId, RcsMapPointInput r, RcsMapService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "RCS_MAP_POINT_SET", "RcsMapPoint", pointId.ToString(), null,
                () => s.SetPointAsync(pointId, r, c.Actor().Username, ct), _ => new { pointId, r.Name, r.LocationCode })));
        cfg.MapDelete("points/{pointId:long}", async (HttpContext c, long pointId, RcsMapService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "RCS_MAP_POINT_REMOVE", "RcsMapPoint", pointId.ToString(), null,
                () => s.RemovePointAsync(pointId, ct), _ => new { pointId })));
        cfg.MapPut("settings", async (HttpContext c, RcsMapSettings r, RcsMapService s, CancellationToken ct) =>
            ApiResults.Ok(c, await Audited.RunAsync(c, "CONFIG_RCS_MAP_SETTINGS", "RcsMap", "current", null,
                () => s.SaveSettingsAsync(r, c.Actor().Username, ct), x => new { x.MapId, x.MmPerUnit, x.YAxisUp, x.HeadingOffsetDeg, x.HeadingClockwise, x.RcsMapCodes, confirmed = x.ConfirmedAt != null })));
    }
}
