using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using WCS.Application.Common;
using WCS.Application.ConfigTransfer;

namespace WCS.Api.Api;

public sealed record ExportRequest(IReadOnlyList<string>? Groups);

/// <summary>CR-026: configuration export / import. Admin only; the whole group answers 404 until <c>ConfigTransfer:Enabled</c> is switched on.</summary>
internal static class ConfigTransferEndpoints
{
    public static void MapConfigTransfer(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/config-transfer").WithTags("ConfigTransfer").RequireAuthorization(AuthSetup.AdminPolicy);
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var o = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<ConfigTransferOptions>>().Value;
            return o.Enabled ? await next(ctx) : ApiResults.Fail(ctx.HttpContext, 404, "CONFIG_TRANSFER_DISABLED", "Configuration transfer is not enabled (ConfigTransfer:Enabled).");
        });

        g.MapGet("status", (HttpContext c, ConfigBackupService backup, IOptions<ConfigTransferOptions> o) =>
            ApiResults.Ok(c, new { enabled = o.Value.Enabled, backupReady = backup.Problem() is null, backupProblem = backup.Problem(), maxPackageMb = o.Value.MaxPackageMb }));
        g.MapGet("groups", async (HttpContext c, ConfigTransferService s, CancellationToken ct) => ApiResults.Ok(c, await s.ListGroupsAsync(ct)));

        g.MapPost("exports/check", (HttpContext c, ExportRequest r, ConfigTransferService s) => ApiResults.Ok(c, s.CheckExport(r.Groups ?? [])));
        g.MapPost("exports", async (HttpContext c, ExportRequest r, ConfigTransferService s, CancellationToken ct) =>
        {
            var (name, data, _) = await s.ExportAsync(r.Groups ?? [], c.Actor(), ct);
            return Results.File(data, "application/zip", name);
        });

        g.MapPost("imports", async (HttpContext c, ConfigTransferService s, IOptions<ConfigTransferOptions> o, CancellationToken ct) =>
        {
            var max = (long)o.Value.MaxPackageMb * 1024 * 1024;
            if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } f) f.MaxRequestBodySize = max + 1024 * 1024;
            if (!c.Request.HasFormContentType) throw AppException.Validation("FILE_REQUIRED", "Send the package as multipart/form-data (field 'file').");
            var form = await c.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw AppException.Validation("FILE_REQUIRED", "Attach the package as the form field 'file'.");
            if (file.Length is 0 or > int.MaxValue || file.Length > max) throw AppException.Validation("PACKAGE_INVALID", $"The package must be between 1 byte and {o.Value.MaxPackageMb} MB.");
            using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms, ct);
            return ApiResults.Ok(c, await s.UploadAsync(ms.ToArray(), c.Actor(), ct), StatusCodes.Status201Created);
        });
        g.MapPost("imports/{id:guid}/preview", async (HttpContext c, Guid id, Selection? selection, ConfigTransferService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.PreviewAsync(id, selection, c.Actor(), ct)));
        g.MapPost("imports/{id:guid}/apply", async (HttpContext c, Guid id, ApplyRequest r, ConfigTransferService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ApplyAsync(id, r, c.Actor(), ct)));
        g.MapGet("imports/{id:guid}", async (HttpContext c, Guid id, ConfigTransferService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(id, ct)));
    }
}
