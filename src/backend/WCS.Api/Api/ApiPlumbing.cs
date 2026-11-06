using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Domain.Common;

namespace WCS.Api.Api;

public static class ApiResults
{
    public static IResult Ok<T>(HttpContext c, T data, int status = StatusCodes.Status200OK) =>
        Results.Json(new ApiResponse<T>(true, data, null, c.TraceIdentifier), statusCode: status);

    public static IResult Fail(HttpContext c, int status, string code, string message, object? details = null) =>
        Results.Json(new ApiResponse<object>(false, null, new ApiError(code, message, details), c.TraceIdentifier), statusCode: status);
}

/// <summary>Validates DataAnnotations on request bodies and returns a structured 400.</summary>
public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        foreach (var arg in ctx.Arguments)
        {
            if (arg is null || arg.GetType().Namespace != typeof(ApiResponse<>).Namespace) continue;
            var results = new List<ValidationResult>();
            if (Validator.TryValidateObject(arg, new ValidationContext(arg), results, validateAllProperties: true)) continue;
            var details = results.Select(r => new { fields = r.MemberNames, message = r.ErrorMessage });
            return ApiResults.Fail(ctx.HttpContext, 400, "VALIDATION_FAILED", "Request validation failed.", details);
        }
        return await next(ctx);
    }
}

public static class ExceptionMapping
{
    public static void Configure(IApplicationBuilder app) => app.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WCS.Api.Errors");
        var (status, code, message) = ex switch
        {
            AppException a => (a.Kind switch
            {
                ErrorKind.Validation => 400,
                ErrorKind.NotFound => 404,
                ErrorKind.Conflict => 409,
                _ => 422,
            }, a.Code, a.Message),
            InvalidTaskTransitionException t => (409, t.Code, t.Message),
            DomainException d => (422, d.Code, d.Message),
            DataConflictException c => (409, "CONCURRENT_MODIFICATION", c.Message),
            BadHttpRequestException b => (400, "BAD_REQUEST", b.Message),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred."),
        };
        if (status >= 500) logger.LogError(ex, "Unhandled exception {TraceId}", context.TraceIdentifier);
        else logger.LogInformation("Request rejected {Status} {Code}: {Message}", status, code, message);

        object? details = ex is ValidationFailedException v ? v.Errors : null;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiResponse<object>(false, null, new ApiError(code, message, details), context.TraceIdentifier));
    });
}

/// <summary>SignalR hub. No authentication yet (no approved authorization requirements); [Authorize] can be added later.</summary>
public sealed class WcsHub : Hub
{
}

public sealed class SignalRWcsNotifier(IHubContext<WcsHub> hub) : IWcsNotifier
{
    public async Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default)
    {
        foreach (var e in events) await hub.Clients.All.SendAsync(e.Name, e.Payload, ct);
    }
}
