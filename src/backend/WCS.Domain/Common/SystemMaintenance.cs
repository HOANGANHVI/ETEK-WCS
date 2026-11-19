namespace WCS.Domain.Common;

/// <summary>
/// CR-026: the one system-wide Maintenance flag (a single row, Id = 1). While it is on, nothing NEW may start (no new Task/Order, no dispatch of work that has not
/// been sent to the RCS yet); work that already started keeps running. Only an Administrator switches it, and it never switches itself off.
/// </summary>
public class SystemMaintenance
{
    public const int SingletonId = 1;
    private SystemMaintenance() { }

    public int Id { get; private set; } = SingletonId;
    public bool Enabled { get; private set; }
    public string? Reason { get; private set; }
    public string? ChangedBy { get; private set; }
    /// <summary>When the flag was last switched (on or off).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public void Set(bool enabled, string? reason, string? by, DateTimeOffset now)
    {
        Enabled = enabled;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 300)];
        ChangedBy = by;
        UpdatedAt = now;
    }
}
