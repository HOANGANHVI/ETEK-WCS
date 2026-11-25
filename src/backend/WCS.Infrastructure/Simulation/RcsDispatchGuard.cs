using Microsoft.Extensions.Hosting;
using WCS.Application.Abstractions;
using WCS.Domain.Rcs;

namespace WCS.Infrastructure.Simulation;

/// <summary>
/// CR-008A (AUDIT-001) + CR-038. One place that says whether a NEW task may be created and dispatched.
///
/// Without an <see cref="IExecutionModeProvider"/> (the original form, kept for tests and tooling): in the Production environment the RCS simulator must not receive tasks unless the
/// deployment opts in (<c>Rcs:AllowSimulatorInProduction=true</c>); the check looks through decorators at the adapter.
///
/// With the provider (the running system): the mode is a persisted, audited choice.
///   - RECOVERY_REQUIRED (a restart found unresolved Real dispatches): nothing new is dispatched until an Admin reconciled them.
///   - Simulator: in Production only after an Admin CHOSE it (a fresh Production database starts with the mode "not chosen" and keeps refusing tasks, exactly as before).
///   - Real: allowed when the deployment enabled the real adapter (<c>Rcs:RealDispatch:Enabled</c>); missing configuration still makes every call fail closed in the adapter.
/// </summary>
public sealed class RcsDispatchGuard(IRcsClient rcs, IHostEnvironment? environment, bool allowSimulatorInProduction, IExecutionModeProvider? provider = null, bool realDispatchEnabled = false) : IRcsDispatchGuard
{
    public const string NotConnected = "RCS_NOT_CONNECTED";

    public RcsDispatchStatus Check()
    {
        if (provider is null) return Legacy();
        var s = provider.Current;
        if (s.RecoveryRequired)
            return new RcsDispatchStatus(false, ExecutionModeCodes.RecoveryRequired,
                "Execution mode RECOVERY_REQUIRED: unresolved RCS dispatches were found after a restart. Reconcile them and acknowledge the recovery in Operations before any new task.");
        if (s.Mode == ExecutionMode.Real)
            return realDispatchEnabled
                ? new RcsDispatchStatus(true, "OK", "RCS dispatch allowed (Real AGV mode).")
                : new RcsDispatchStatus(false, ExecutionModeCodes.RealNotReady, "Real AGV mode is selected but Rcs:RealDispatch:Enabled is false in the deployment configuration.");
        if (environment?.IsProduction() == true && !s.IsExplicit && !allowSimulatorInProduction)
            return new RcsDispatchStatus(false, NotConnected,
                "No operating mode has been chosen: choose Simulator or Real AGV in Operations (Admin). Until then the Production environment does not send tasks anywhere.");
        return new RcsDispatchStatus(true, "OK", "RCS dispatch allowed (Simulator mode).");
    }

    private RcsDispatchStatus Legacy()
    {
        if (environment?.IsProduction() == true && IRcsClientDecorator.Unwrap(rcs) is RcsSimulator && !allowSimulatorInProduction)
            return new RcsDispatchStatus(false, NotConnected,
                "RCS is not connected: the Production environment has no real RCS adapter, so tasks are not sent to the AGV. (Rcs:AllowSimulatorInProduction is for supervised testing only.)");
        return new RcsDispatchStatus(true, "OK", "RCS dispatch allowed.");
    }
}
