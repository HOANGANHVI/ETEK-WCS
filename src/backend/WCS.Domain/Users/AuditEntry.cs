namespace WCS.Domain.Users;

public enum AuditResult { Success = 1, Denied = 2, Failed = 3 }

/// <summary>
/// Append-only record of an important action. Never contains passwords, tokens or other secrets
/// (callers pass only non-secret before/after snapshots).
/// </summary>
public class AuditEntry
{
    private AuditEntry() { }

    public AuditEntry(DateTimeOffset occurredAt, string? actor, string? actorRole, string action, string? targetType, string? targetId,
        AuditResult result, string? detail, string? beforeJson, string? afterJson, string? ipAddress)
    {
        Id = Guid.NewGuid();
        OccurredAt = occurredAt; Actor = actor; ActorRole = actorRole; Action = action;
        TargetType = targetType; TargetId = targetId; Result = result; Detail = detail;
        BeforeJson = beforeJson; AfterJson = afterJson; IpAddress = ipAddress;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    /// <summary>Username of the actor; null/attempted name for failed logins.</summary>
    public string? Actor { get; private set; }
    public string? ActorRole { get; private set; }
    /// <summary>Stable action code, e.g. AUTH_LOGIN, TASK_CREATE, CONFIG_PLC_UPDATE.</summary>
    public string Action { get; private set; } = default!;
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }
    public AuditResult Result { get; private set; }
    public string? Detail { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? IpAddress { get; private set; }
}
