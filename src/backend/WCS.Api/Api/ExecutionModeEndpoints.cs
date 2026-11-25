using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Rcs;

namespace WCS.Api.Api;

public sealed record ModeTransitionBody(string RequestId, string Target, string Reason, bool ConfirmRealAgv);
public sealed record RecoveryAckBody(string RequestId, string Reason);
public sealed record AdoptLegacyBody(string Reason);

/// <summary>
/// CR-038: the operating mode (Simulator / Real AGV) and the RCS message log. Reading the mode and a task's messages is open to Admin and Operator (payloads are stored masked);
/// changing the mode, its readiness check, its history and the recovery acknowledgement are Admin only. The frontend never chooses a dispatcher: it asks WCS to change the mode, and WCS decides.
/// </summary>
internal static class ExecutionModeEndpoints
{
    public static void MapExecutionMode(this RouteGroupBuilder api)
    {
        var admin = AuthSetup.AdminPolicy;
        var g = api.MapGroup("/execution-mode").WithTags("ExecutionMode");
        g.MapGet("", async (HttpContext c, IExecutionModeService s, CancellationToken ct) => ApiResults.Ok(c, await s.GetAsync(ct)));
        g.MapGet("readiness", async (HttpContext c, string? target, IExecutionModeService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.ReadinessAsync(ParseTarget(target), ct))).RequireAuthorization(admin);
        g.MapGet("history", async (HttpContext c, int? take, IExecutionModeService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.HistoryAsync(take ?? 50, ct))).RequireAuthorization(admin);
        g.MapPost("transition", async (HttpContext c, ModeTransitionBody b, IExecutionModeService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.TransitionAsync(new ModeTransitionRequest(b.RequestId, ParseTarget(b.Target), b.Reason, b.ConfirmRealAgv), c.Actor().Username, ct))).RequireAuthorization(admin);
        g.MapPost("recovery/acknowledge", async (HttpContext c, RecoveryAckBody b, IExecutionModeService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.AcknowledgeRecoveryAsync(b.RequestId, b.Reason, c.Actor().Username, ct))).RequireAuthorization(admin);

        g.MapPost("legacy/adopt-simulator", async (HttpContext c, AdoptLegacyBody b, IExecutionModeService s, CancellationToken ct) =>
            ApiResults.Ok(c, await s.AdoptLegacyAsSimulatorAsync(b.Reason, c.Actor().Username, ct))).RequireAuthorization(admin);
        g.MapGet("unlinked-messages", async (HttpContext c, int? take, IRcsMessageQuery q, CancellationToken ct) => ApiResults.Ok(c, await q.UnlinkedAsync(take ?? 50, ct))).RequireAuthorization(admin);
        api.MapGet("/tasks/{id:guid}/rcs-messages", async (HttpContext c, Guid id, IRcsMessageQuery q, CancellationToken ct) =>
            await q.ForTaskAsync(id, ct) is { } rows ? ApiResults.Ok(c, rows) : throw AppException.NotFound("TASK_NOT_FOUND", "Task not found.")).WithTags("ExecutionMode");
        api.MapGet("/rcs-messages/{id:guid}", async (HttpContext c, Guid id, IRcsMessageQuery q, CancellationToken ct) =>
            await q.GetAsync(id, ct) is { } row ? ApiResults.Ok(c, row) : throw AppException.NotFound("RCS_MESSAGE_NOT_FOUND", "Message not found.")).WithTags("ExecutionMode");
    }

    private static ExecutionMode ParseTarget(string? text) =>
        Enum.TryParse<ExecutionMode>(text, true, out var m) && Enum.IsDefined(m) ? m : throw AppException.Validation("MODE_INVALID", "Target must be Simulator or Real.");
}
