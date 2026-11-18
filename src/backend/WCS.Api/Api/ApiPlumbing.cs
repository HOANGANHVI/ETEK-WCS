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
                ErrorKind.Unauthorized => 401,
                ErrorKind.Forbidden => 403,
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

/// <summary>SignalR hub. Only signed-in Admin/Operator sessions (same cookie as the REST API) may connect.</summary>
[Microsoft.AspNetCore.Authorization.Authorize(Policy = AuthSetup.OperationsPolicy)]
public sealed class WcsHub(ILogger<WcsHub> logger) : Hub
{
    public const string AdminGroup = "admins";

    /// <summary>Admins join a private group that receives configuration/diagnostic events (PLC logs, diagnostics). Operators never do.</summary>
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(nameof(WCS.Domain.Users.UserRole.Admin)) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        logger.LogInformation("Realtime client connected: user {User}, role {Role}. {EventCode}", Context.User?.Identity?.Name ?? "-", Context.User?.IsInRole(nameof(WCS.Domain.Users.UserRole.Admin)) == true ? "Admin" : "Operator", "SIGNALR_CONNECTED");   // CR-021
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // CR-021: a clean close is information; a close with an error (network drop, transport failure) is a warning
        if (exception is null) logger.LogInformation("Realtime client disconnected: user {User}. {EventCode}", Context.User?.Identity?.Name ?? "-", "SIGNALR_DISCONNECTED");
        else logger.LogWarning(exception, "Realtime client disconnected with an error: user {User}. {EventCode}", Context.User?.Identity?.Name ?? "-", "SIGNALR_DISCONNECTED_ERROR");
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class SignalRWcsNotifier(IHubContext<WcsHub> hub) : IWcsNotifier
{
    public async Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default)
    {
        foreach (var e in events)
            await (e.AdminOnly ? hub.Clients.Group(WcsHub.AdminGroup) : hub.Clients.All).SendAsync(e.Name, e.Payload, ct);
    }
}
