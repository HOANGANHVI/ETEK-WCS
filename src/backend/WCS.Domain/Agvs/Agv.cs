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

    public void SetEnabled(bool enabled) => Enabled = enabled;
    public void SetRobotCode(string? code) => RcsRobotCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}
