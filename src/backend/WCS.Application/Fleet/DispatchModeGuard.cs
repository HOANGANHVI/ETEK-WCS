using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Routing;

namespace WCS.Application.Fleet;

/// <summary>Result of the mode-switch check: what is still active in the OTHER mode.</summary>
public sealed record DispatchModeCheck(bool Allowed, string Mode, string OtherMode, int ActiveTasks, int ActiveLegs, string? Message);

/// <summary>Raised at start when the configured Rcs:Dispatch:Mode differs from the mode that still has active work. WCS refuses to start instead of mixing two ways of choosing the AMR.</summary>
public sealed class DispatchModeChangeRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// CR-028: a mode (Legacy / RcsManaged) may be changed only when no task or leg of the OLD mode is still active. Active tasks: not finished tasks of that mode; active legs: legs that are
/// on their way (Legacy: an AGV chosen by WCS; RcsManaged: sent to the RCS with no AGV). Nothing is converted, assigned or deleted by this check.
/// </summary>
public sealed class DispatchModeGuard(IUnitOfWork uow, ITransportTaskRepository tasks, IRoutingRepository routing)
{
    public Task<DispatchModeCheck> CheckAsync(string configuredMode, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var managed = string.Equals(configuredMode, DispatchOptions.RcsManagedMode, StringComparison.OrdinalIgnoreCase);
        var mode = managed ? DispatchOptions.RcsManagedMode : DispatchOptions.Legacy;
        var other = managed ? DispatchOptions.Legacy : DispatchOptions.RcsManagedMode;
        var oldTasks = await tasks.CountActiveByModeAsync(!managed, t);
        var oldLegs = await routing.CountInFlightLegsByModeAsync(!managed, t);
        if (oldTasks == 0 && oldLegs == 0) return new DispatchModeCheck(true, mode, other, 0, 0, null);
        return new DispatchModeCheck(false, mode, other, oldTasks, oldLegs,
            $"DISPATCH_MODE_CHANGE_REFUSED: Rcs:Dispatch:Mode is '{mode}' but {oldTasks} unfinished task(s) and {oldLegs} leg(s) on their way still belong to '{other}'. " +
            $"Let them finish (or cancel them in WCS and the RCS), then change the mode and restart. No task was converted, reassigned or deleted.");
    }, ct);
}

/// <summary>
/// The mode WCS is running in (read once at start; IOptions never changes while the process runs) next to the mode that is configured now. A configuration that was edited while WCS runs does NOT
/// take effect until a restart: this state makes that visible (log + <c>FleetView.DispatchModePending</c>) so nobody believes the new mode is active.
/// </summary>
public sealed class DispatchModeState : IDisposable
{
    private readonly IDisposable? _subscription;
    private readonly ILogger<DispatchModeState> _logger;
    private volatile string _configured;

    public DispatchModeState(IOptions<DispatchOptions> effective, IOptionsMonitor<DispatchOptions> monitor, ILogger<DispatchModeState> logger)
    {
        _logger = logger;
        EffectiveMode = Normalize(effective.Value.Mode);
        _configured = Normalize(monitor.CurrentValue.Mode);
        _subscription = monitor.OnChange(o =>
        {
            _configured = Normalize(o.Mode);
            if (RestartPending)
                _logger.LogError("Rcs:Dispatch:Mode was changed to '{Configured}' but WCS keeps running in '{Effective}' until it is restarted. The change is not active.", _configured, EffectiveMode);
        });
    }

    public string EffectiveMode { get; }
    public string ConfiguredMode => _configured;
    public bool RestartPending => !string.Equals(EffectiveMode, _configured, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? mode) =>
        string.Equals(mode, DispatchOptions.RcsManagedMode, StringComparison.OrdinalIgnoreCase) ? DispatchOptions.RcsManagedMode
        : string.IsNullOrWhiteSpace(mode) || string.Equals(mode, DispatchOptions.Legacy, StringComparison.OrdinalIgnoreCase) ? DispatchOptions.Legacy : mode.Trim();

    public void Dispose() => _subscription?.Dispose();
}
