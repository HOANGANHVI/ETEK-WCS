using WCS.Application.Logging;

namespace WCS.Api.Api;

/// <summary>
/// CR-007 Logging &amp; Diagnostics API. EVERY endpoint is Admin-only: the log contains stack traces, device addresses and operator activity that Operators must not see.
/// Read-only: there is no endpoint that writes or deletes log entries (deployment events arrive through a read-only file mount, not through the network).
/// </summary>
public static class LogEndpoints
{
    public static void MapLogApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/logs").WithTags("Logs").RequireAuthorization(AuthSetup.AdminPolicy);

        g.MapGet("stats", async (HttpContext c, ILogQueryService s, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            ApiResults.Ok(c, await s.StatsAsync(from, to, ct)));

        g.MapGet("", async (HttpContext c, ILogQueryService s, DateTimeOffset? from, DateTimeOffset? to, string? category, string? level, string? minLevel, string? device,
            string? source, string? eventCode, string? keyword, int? page, int? pageSize, CancellationToken ct) =>
            ApiResults.Ok(c, await s.SearchAsync(new LogSearchQuery(from, to, Blank(category), Blank(level), Blank(minLevel), device, source, eventCode, keyword, page ?? 1, pageSize ?? 50), ct)));

        g.MapGet("{id}", async (HttpContext c, ILogQueryService s, string id, CancellationToken ct) =>
            await s.DetailAsync(id, ct) is { } d ? ApiResults.Ok(c, d) : ApiResults.Fail(c, StatusCodes.Status404NotFound, "LOG_NOT_FOUND", "Không tìm thấy bản ghi log."));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
