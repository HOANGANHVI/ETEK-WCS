using WCS.Application.Abstractions;
using WCS.Application.Plc;
using WCS.Application.Routing;

namespace WCS.Infrastructure.Simulation;

/// <summary>
/// Leg dispatch for the simulator only. A real RCS adapter for configurable legs does not exist (the task template for them is not confirmed, Q-MLEG-1), so
/// this adapter refuses to act where <see cref="IRcsDispatchGuard"/> says no AGV link is allowed (Production without the explicit simulator opt-in).
/// It is idempotent: the same request code always returns the same task code, and it never touches the network.
/// </summary>
internal sealed class SimulatedLegDispatcher(IRcsDispatchGuard guard, RcsSimulator rcs) : ILegDispatcher
{
    public Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default)
    {
        var link = guard.Check();
        // CR-028: a leg sent WITHOUT a robot code (RcsManaged) is registered with the simulated RCS, which chooses the AMR; a Legacy leg (robot code given) is exactly as before
        return Task.FromResult(link.Allowed
            ? new LegDispatchResult(LegDispatchOutcome.Accepted, request.RobotCode is null ? rcs.CreateLegTask(request.RequestCode) : $"SIM-{request.RequestCode}", null)
            : new LegDispatchResult(LegDispatchOutcome.Blocked, null, link.Code));
    }

    public Task<WCS.Application.Abstractions.RcsTaskInfo> QueryAsync(string requestCode, CancellationToken ct = default) => rcs.QueryTaskAsync(requestCode, ct);

    public Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default)
    {
        var link = guard.Check();
        return Task.FromResult(link.Allowed
            ? new LegDispatchResult(LegDispatchOutcome.Accepted, agvTaskCode ?? $"SIM-{requestCode}", null)
            : new LegDispatchResult(LegDispatchOutcome.Blocked, null, link.Code));
    }
}

/// <summary>Reads the connection state of each PLC from the PLC runtime (one worker per PLC). A PLC without a worker is unknown (treated as offline by the engine).</summary>
internal sealed class PlcRuntimeLinkStatus(IPlcRuntime runtime) : IPlcLinkStatus
{
    public bool? IsOnline(string plcId)
    {
        var w = runtime.Workers(false).FirstOrDefault(x => x.PlcId == plcId);
        return w is null ? null : string.Equals(w.State, nameof(EndpointState.Connected), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Placeholder until the PLC engineer approves the handshake mapping: there is no confirmed address of the 9-step signals, so nothing can be read.
/// "No data" keeps suspended transactions suspended - WCS never guesses a physical state.
/// </summary>
internal sealed class UnavailablePlcObservationSource : IPlcObservationSource
{
    public Task<PlcHandshakeObservation?> ReadAsync(string plcId, string portId, CancellationToken ct = default) => Task.FromResult<PlcHandshakeObservation?>(null);
}
