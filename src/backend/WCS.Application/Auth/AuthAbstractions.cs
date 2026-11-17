using WCS.Domain.Users;

namespace WCS.Application.Auth;

public interface IUserRepository
{
    Task<AppUser?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Lookup by normalized (lower-case) username.</summary>
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> ListAsync(string? search, CancellationToken ct = default);
    Task<IReadOnlyList<AppUser>> ListActiveAdminsAsync(CancellationToken ct = default);
    Task<bool> AnyAdminAsync(CancellationToken ct = default);
    void Add(AppUser user);
    void Remove(AppUser user);
}

/// <summary>Password hashing (salted, slow hash). Implemented in infrastructure; the application never sees plain hashes' internals.</summary>
public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

/// <summary>What gets written to the audit trail. Before/After are non-secret snapshots; secret-looking string fields are redacted anyway.</summary>
public sealed record AuditRecord(string Action, string? Actor, string? ActorRole, string? TargetType, string? TargetId, AuditResult Result,
    string? Detail = null, object? Before = null, object? After = null, string? IpAddress = null);

public interface IAuditLog
{
    /// <summary>Appends an entry in its own transaction. Never throws to the caller: a failing audit write is logged, not propagated.</summary>
    Task WriteAsync(AuditRecord record, CancellationToken ct = default);
    /// <summary>
    /// Adds the entry to the CURRENT unit of work, so it is committed atomically with the change it describes (used when the audited
    /// object itself is destroyed, e.g. hard-deleting a task). The entry has no foreign key to the object and survives its deletion.
    /// </summary>
    void Enlist(AuditRecord record);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct = default);
    /// <summary>Number of audit entries made by (or about a sign-in attempt of) this username. Used to protect traceability when deleting accounts.</summary>
    Task<int> CountByActorAsync(string username, CancellationToken ct = default);
}

public sealed record AuditQuery(string? Actor, string? Action, AuditResult? Result, DateTimeOffset? From, DateTimeOffset? To, int Skip, int Take);

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    /// <summary>Session ends after this much inactivity (sliding).</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;
    /// <summary>Session ends this long after sign-in regardless of activity.</summary>
    public int AbsoluteTimeoutHours { get; set; } = 12;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 5;
    /// <summary>
    /// Cookie "Secure" policy: "Always" or "SameAsRequest". Not set = the built-in rule (SameAsRequest in Development, Always elsewhere).
    /// SameAsRequest outside Development is only for a deliberate plain-HTTP deployment on a trusted test LAN; use Always with HTTPS.
    /// </summary>
    public string? CookieSecure { get; set; }
}

/// <summary>The authenticated caller, taken from the validated session (never from the request body).</summary>
public sealed record ActorInfo(Guid Id, string Username, UserRole Role, string? IpAddress = null)
{
    public bool IsAdmin => Role == UserRole.Admin;
}

public sealed record AuthenticatedUser(Guid Id, string Username, string DisplayName, UserRole Role, bool MustChangePassword, Guid SecurityStamp);

public sealed record UserDto(Guid Id, string Username, string DisplayName, UserRole Role, bool IsActive, bool MustChangePassword,
    DateTimeOffset? LastLoginAt, DateTimeOffset? LockedUntil, DateTimeOffset CreatedAt);

/// <summary>The caller's own interface preferences.</summary>
public sealed record UserPreferencesDto(string Theme, bool SidebarCollapsed);

public sealed record CreateUserCommand(string Username, string DisplayName, UserRole Role, string Password, bool IsActive = true);
public sealed record UpdateUserCommand(string DisplayName, UserRole Role, bool IsActive);
