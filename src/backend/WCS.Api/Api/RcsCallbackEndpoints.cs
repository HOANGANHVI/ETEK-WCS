using System.Text;
using System.Text.Json;
using WCS.Application.Abstractions;
using WCS.Application.RealDispatch;
using WCS.Domain.Rcs;

namespace WCS.Api.Api;

/// <summary>
/// CR-030: the RCS -> WCS callback endpoint (agvCallback, RCS-2000 API V3.3 5.3.1). NOT mapped at all unless Rcs:RealDispatch:Enabled and Rcs:RealDispatch:Callback:Enabled are both true. It is outside
/// the browser API (no cookie, no client header) and protected only by the WCS-side checks that are configured (shared-secret header and/or allowed source addresses); with none configured every call
/// is refused with 401. No detail about why is returned, the token is never logged. The answer follows the API document: { code, message, reqCode }.
/// </summary>
internal static class RcsCallbackEndpoints
{
    public static void MapRcsCallback(this WebApplication app)
    {
        var o = app.Configuration.GetSection(RealDispatchOptions.SectionName).Get<RealDispatchOptions>() ?? new RealDispatchOptions();
        if (!o.Enabled || !o.Callback.Enabled) return;
        var path = o.Callback.Path;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            app.Logger.LogError("Rcs:RealDispatch:Callback:Path '{Path}' is not usable (it must start with '/' and must not be under /api or /hubs): the RCS callback endpoint is NOT mapped. {EventCode}", path, "RCS_CALLBACK_PATH_INVALID");
            return;
        }
        if (!o.Callback.AuthenticationConfigured)
            app.Logger.LogWarning("Rcs:RealDispatch:Callback is enabled without SharedSecret and AllowedSources: every RCS callback will be refused (401). {EventCode}", "RCS_CALLBACK_AUTH_NOT_CONFIGURED");

        app.MapPost(path, async (HttpContext c, RcsCallbackService svc, ILogger<RcsCallbackService> log, CancellationToken ct) =>
        {
            var auth = svc.Authenticate(c.Request.Headers[svc.HeaderName].FirstOrDefault(), c.Connection.RemoteIpAddress);
            if (auth != RcsCallbackStatus.Applied)
            {
                log.LogWarning("RCS callback refused (not authenticated) from {Remote}. {EventCode}", c.Connection.RemoteIpAddress, "RCS_CALLBACK_REFUSED");
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }
            if (c.Request.ContentLength is { } len && len > svc.MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var ms = new MemoryStream();
            var buf = new byte[8192]; int n;
            while ((n = await c.Request.Body.ReadAsync(buf, ct)) > 0)
            {
                if (ms.Length + n > svc.MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                ms.Write(buf, 0, n);
            }
            var body = Encoding.UTF8.GetString(ms.ToArray());
            var r = await svc.HandleAsync(body, ct);
            // CR-038: the service records the authenticated callback (linked to its task, secrets masked)
            return Results.Json(new { code = r.Code, message = r.Message, reqCode = r.ReqCode ?? "" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }).AllowAnonymous().WithTags("RcsCallback");
    }
}
