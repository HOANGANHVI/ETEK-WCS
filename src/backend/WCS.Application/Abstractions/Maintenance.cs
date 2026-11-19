using WCS.Application.Common;
using WCS.Domain.Common;

namespace WCS.Application.Abstractions;

/// <summary>Stable code returned (API) or recorded (logs) when the system is in Maintenance.</summary>
public static class MaintenanceCodes
{
    public const string Code = "SYSTEM_MAINTENANCE";
    public const string Message = "The system is in Maintenance: no new Task or Order can be created and no new work is dispatched.";
}

/// <summary>
/// CR-026 (OQ-1): the single place that decides whether NEW work may start.
/// The locked members must run INSIDE a unit of work: they read the flag with a row lock (<c>FOR SHARE</c>) that makes switching Maintenance on wait for every
/// unit that is about to start new work and makes later units see the new value - so a Task cannot slip in while the flag is being turned on.
/// </summary>
public interface IMaintenanceGate
{
    /// <summary>Cheap read without a lock (fast refusal / hold). Never use it as the only protection of a creating transaction.</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct = default);

    /// <summary>Inside a unit of work: throws <see cref="AppException"/> <c>SYSTEM_MAINTENANCE</c> when Maintenance is on (row locked).</summary>
    Task EnsureOpenAsync(CancellationToken ct = default);

    /// <summary>Inside a unit of work: true when Maintenance is on (row locked), so the caller leaves the work untouched.</summary>
    Task<bool> HoldNewWorkAsync(CancellationToken ct = default);
}

public sealed record MaintenanceView(bool Enabled, string? Reason, string? ChangedBy, DateTimeOffset? UpdatedAt);

/// <summary>Persistence of the flag (kept out of the gate so the switch and the gate share one implementation detail).</summary>
public interface IMaintenanceStore
{
    Task<SystemMaintenance> GetAsync(CancellationToken ct = default);
    /// <summary>Inside a unit of work: loads the row with an exclusive row lock, so the change waits for the units that hold the share lock.</summary>
    Task<SystemMaintenance> GetForUpdateAsync(CancellationToken ct = default);
}
