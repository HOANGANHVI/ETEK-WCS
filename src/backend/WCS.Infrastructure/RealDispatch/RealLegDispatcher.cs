using WCS.Application.Routing;

namespace WCS.Infrastructure.RealDispatch;

/// <summary>
/// CR-030: routing legs with the REAL RCS. There is no confirmed mapping of a configurable leg (Business Process step) to an RCS task type (open question Q-MLEG-1 / vendor question 8), so a leg is NOT
/// sent and NOT "accepted": it is Blocked and waits (nothing is lost, nothing is invented). Tasks of the approved flows (Bp01-Bp06) use <see cref="HikRcsClient"/>.
/// </summary>
internal sealed class RealLegDispatcher : ILegDispatcher
{
    public const string Reason = "LEG_DISPATCH_NOT_AVAILABLE_FOR_REAL_RCS";
    public Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default) => Task.FromResult(new LegDispatchResult(LegDispatchOutcome.Blocked, null, Reason));
    public Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default) => Task.FromResult(new LegDispatchResult(LegDispatchOutcome.Blocked, null, Reason));
}
