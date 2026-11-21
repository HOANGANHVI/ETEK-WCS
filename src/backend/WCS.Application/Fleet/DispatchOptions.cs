using WCS.Application.Abstractions;
using WCS.Domain.Fleet;

namespace WCS.Application.Fleet;

/// <summary>
/// CR-028: who chooses the AMR of a task. <c>Legacy</c> (default, exactly as before CR-028): WCS chooses the business AGV by role at creation (PER_AGV, per-AGV queue, DispatchHold).
/// <c>RcsManaged</c>: WCS sends the task WITHOUT an AMR code and the RCS chooses; the task has no AGV until the RCS reports one. Real RCS adapter: not implemented (no confirmed mapping);
/// RcsManaged is exercised on the RCS simulator.
/// </summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Rcs:Dispatch";
    public const string Legacy = "Legacy";
    public const string RcsManagedMode = "RcsManaged";

    public string Mode { get; set; } = Legacy;
    /// <summary>RcsManaged only: how many tasks may be in flight (sent to the RCS and not finished) at the same time, for the whole fleet. 0 = no limit set by WCS (the RCS decides what it accepts).</summary>
    public int MaxInFlightTasks { get; set; }
    /// <summary>Seconds between two "which AMR?" queries of a task that has no AMR yet.</summary>
    public int AssignmentCheckSeconds { get; set; } = 5;
    /// <summary>Seconds between two re-checks of a task that already has an AMR (detects a change of robot).</summary>
    public int AssignedRecheckSeconds { get; set; } = 60;
    /// <summary>RcsManaged legs: how many CONSECUTIVE queries must report the same different AMR (while the rack is on the old AMR) before the Order goes to ReconciliationRequired. At least 2: one query alone never does it. Technical proposal, not a business rule.</summary>
    public int RobotChangeConfirmChecks { get; set; } = 2;

    public bool IsRcsManaged => string.Equals(Mode, RcsManagedMode, StringComparison.OrdinalIgnoreCase);
    public static bool IsValidMode(string? mode) => string.Equals(mode, Legacy, StringComparison.OrdinalIgnoreCase) || string.Equals(mode, RcsManagedMode, StringComparison.OrdinalIgnoreCase);
}

/// <summary>CR-028: finds out whether an AMR code reported by the RCS is known: as a business AGV row (<c>agvs.RcsRobotCode</c>, Legacy link) and in the fleet read model of the CURRENT source.</summary>
public sealed class RcsRobotMapper(IAgvRepository agvs, IRcsFleetRepository fleet, IRcsFleetQuery query)
{
    public sealed record Mapping(Guid? AgvId, string? AgvCode, bool InFleet);

    public async Task<Mapping> MapAsync(string? robotCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(robotCode)) return new Mapping(null, null, false);
        var code = robotCode.Trim();
        var agv = (await agvs.ListAsync(ct)).FirstOrDefault(a => string.Equals(a.RcsRobotCode, code, StringComparison.Ordinal));
        // only the rows of the source that is configured now: simulator data never vouches for a real robot (and the other way round)
        var inFleet = (await fleet.ListAsync(query.Source, ct)).Any(s => string.Equals(s.RobotCode, code, StringComparison.Ordinal));
        return new Mapping(agv?.Id, agv?.Code, inFleet);
    }
}
