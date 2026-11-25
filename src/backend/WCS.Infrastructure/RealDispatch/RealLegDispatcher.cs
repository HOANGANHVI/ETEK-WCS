using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.RealDispatch;
using WCS.Application.Routing;

namespace WCS.Infrastructure.RealDispatch;

/// <summary>
/// Routing legs with the REAL RCS (CR-030 / CR-035). No simulator is ever used here. A leg is sent through <see cref="HikRcsClient"/> ONLY when a leg template is configured
/// (<c>Rcs:RealDispatch:LegTemplate</c>, owned by the integration team; never generated). Without it the leg is Blocked and waits (nothing is lost, nothing invented).
/// The template is NOT the Q1PICK template: its XML, the Pick/Drop triggers (Seq2/Seq6, needTrigger/triggerTyp) and the trigger-driven continueTask are not implemented yet.
/// </summary>
internal sealed class RealLegDispatcher(HikRcsClient rcs, IOptions<RealDispatchOptions> options) : ILegDispatcher
{
    public const string Reason = "LEG_DISPATCH_NOT_AVAILABLE_FOR_REAL_RCS";

    public const string StateUnknown = "RCS_STATE_UNKNOWN_NOT_RESENT";

    /// <summary>
    /// Never sends blindly: after a timeout / lost answer the outcome of the previous send is UNKNOWN, so the RCS is asked first (queryTaskStatus by the WCS request code).
    /// Task known to the RCS -> adopted (no second task is created); RCS answers "unknown task" -> sent; RCS cannot be asked -> NOT sent (Blocked), the engine tries again later.
    /// The location / rack / destination data of the leg are used as they are: the template only supplies the task type and the shape of the path.
    /// </summary>
    public async Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default)
    {
        var t = options.Value.LegTemplate;
        if (t is null || string.IsNullOrWhiteSpace(t.TaskTyp)) return new LegDispatchResult(LegDispatchOutcome.Blocked, null, Reason);
        if (string.IsNullOrWhiteSpace(request.SourceLocationCode) || string.IsNullOrWhiteSpace(request.DestinationLocationCode))
            return new LegDispatchResult(LegDispatchOutcome.Blocked, null, "LEG_LOCATIONS_INCOMPLETE");   // source rack position and reserved destination must both be known
        var known = await rcs.QueryTaskAsync(request.RequestCode, ct);
        switch (known.Status)
        {
            case RcsTaskStatus.NotFound: break;                                                           // the RCS does not know the task: safe to send
            case RcsTaskStatus.Unknown: return new LegDispatchResult(LegDispatchOutcome.Blocked, null, StateUnknown);
            default: return new LegDispatchResult(LegDispatchOutcome.Accepted, request.RequestCode, null);   // already created by an earlier send whose answer was lost
        }
        return Map(await rcs.CreateLegTaskAsync(request.RequestCode, request.RobotCode, request.SourceLocationCode, request.DestinationLocationCode, ct));
    }

    public async Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default)
    {
        var t = options.Value.LegTemplate;
        if (t is null || string.IsNullOrWhiteSpace(t.TaskTyp)) return new LegDispatchResult(LegDispatchOutcome.Blocked, null, Reason);
        return Map(await rcs.ContinueTaskAsync(requestCode, agvTaskCode, destinationLocationCode, ct));
    }

    public Task<RcsTaskInfo> QueryAsync(string requestCode, CancellationToken ct = default) => rcs.QueryTaskAsync(requestCode, ct);

    private static LegDispatchResult Map(RcsCallResult r) => r.Outcome switch
    {
        RcsCallOutcome.Accepted => new LegDispatchResult(LegDispatchOutcome.Accepted, r.RcsTaskCode, null),
        RcsCallOutcome.Rejected => new LegDispatchResult(LegDispatchOutcome.Rejected, null, r.Message),
        _ => new LegDispatchResult(LegDispatchOutcome.Unknown, null, r.Message),
    };
}
