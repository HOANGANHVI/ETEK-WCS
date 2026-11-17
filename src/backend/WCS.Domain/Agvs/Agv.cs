using WCS.Domain.Common;

namespace WCS.Domain.Agvs;

/// <summary>
/// AGV with a business role. "Busy" is NOT stored here: it is derived from the tasks that occupy the AGV
/// (PER_AGV, enforced in the database by a partial unique index on active tasks).
/// </summary>
public class Agv
{
    private Agv() { }

    public Agv(string code, AgvRole role, string? rcsRobotCode = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("AGV_CODE_REQUIRED", "AGV code is required.");
        Id = Guid.NewGuid();
        Code = code.Trim();
        Role = role;
        RcsRobotCode = rcsRobotCode;
        Enabled = true;
        ConnectionStatus = AgvConnectionStatus.Unknown;
    }

    public Guid Id { get; private set; }
    /// <summary>Business code, e.g. AGV1 / AGV2.</summary>
    public string Code { get; private set; } = default!;
    public AgvRole Role { get; private set; }
    /// <summary>RCS robot code mapping. Configuration/data, never hard-coded. NEEDS_CONFIRMATION with the RCS team.</summary>
    public string? RcsRobotCode { get; private set; }
    public bool Enabled { get; private set; }
    public AgvConnectionStatus ConnectionStatus { get; private set; }
    public string? StatusDetail { get; private set; }
    public DateTimeOffset? LastStatusAt { get; private set; }
    public uint Version { get; private set; }

    public RackKind CarriedRackKind => Role == AgvRole.FullRack ? RackKind.Full : RackKind.Empty;

    public void UpdateStatus(AgvConnectionStatus status, string? detail, DateTimeOffset now)
    {
        ConnectionStatus = status; StatusDetail = detail; LastStatusAt = now;
    }

    /// <summary>CR-008B-5 (D-7, P2): the AGV takes no new task while true. Independent of <see cref="Enabled"/>; only an Admin releases it (D-8).</summary>
    public bool DispatchHold { get; private set; }
    public string? DispatchHoldReason { get; private set; }
    public DateTimeOffset? DispatchHoldAt { get; private set; }
    /// <summary>The task whose uncertain outcome caused the hold (no foreign key: it is an audit pointer).</summary>
    public Guid? DispatchHoldTaskId { get; private set; }

    /// <summary>Idempotent: an AGV already on hold keeps the first reason / task (the later ones are in the task history).</summary>
    public bool PlaceDispatchHold(string reason, Guid taskId, DateTimeOffset now)
    {
        if (DispatchHold) return false;
        DispatchHold = true; DispatchHoldReason = reason.Length > 200 ? reason[..200] : reason; DispatchHoldAt = now; DispatchHoldTaskId = taskId;
        return true;
    }

    public void ReleaseDispatchHold()
    {
        DispatchHold = false; DispatchHoldReason = null; DispatchHoldAt = null; DispatchHoldTaskId = null;
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;
    public void SetRobotCode(string? code) => RcsRobotCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}
