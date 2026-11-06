using WCS.Domain.Common;

namespace WCS.Domain.Users;

public enum UserRole { Admin = 1, Operator = 2 }

/// <summary>
/// Application account. The password is only ever stored as a salted hash (produced by the infrastructure hasher), never in plain text.
/// <see cref="SecurityStamp"/> changes whenever credentials, role or activation change, which invalidates existing sessions.
/// </summary>
public class AppUser
{
    private AppUser() { }

    public static AppUser Create(string username, string displayName, UserRole role, string passwordHash, bool mustChangePassword, DateTimeOffset now)
    {
        var name = NormalizeUsername(username);
        if (!IsValidUsername(name)) throw new DomainException("USERNAME_INVALID", "Tên đăng nhập chỉ gồm chữ thường, số, dấu chấm, gạch dưới hoặc gạch ngang (3-50 ký tự).");
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("DISPLAY_NAME_REQUIRED", "Cần nhập họ tên hiển thị.");
        return new AppUser
        {
            Id = Guid.NewGuid(),
            Username = name,
            DisplayName = displayName.Trim(),
            Role = role,
            PasswordHash = passwordHash,
            IsActive = true,
            MustChangePassword = mustChangePassword,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public Guid Id { get; private set; }
    /// <summary>Unique, stored lower-case.</summary>
    public string Username { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public string PasswordHash { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public bool MustChangePassword { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Incremented by <see cref="Touch"/> so a no-op "touch" is still a real UPDATE that moves the xmin token.</summary>
    public int Revision { get; private set; }
    public uint Version { get; private set; }

    public static string NormalizeUsername(string? username) => (username ?? "").Trim().ToLowerInvariant();

    public static bool IsValidUsername(string name) =>
        name.Length is >= 3 and <= 50 && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-');

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public void RegisterFailedLogin(DateTimeOffset now, int maxFailures, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxFailures) { LockoutEnd = now + lockoutDuration; FailedLoginCount = 0; }
        UpdatedAt = now;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0; LockoutEnd = null; LastLoginAt = now; UpdatedAt = now;
    }

    /// <summary>Bumps the concurrency token (xmin) so two concurrent admin changes serialize (last-admin protection).</summary>
    public void Touch(DateTimeOffset now) { Revision++; UpdatedAt = now; }

    public void UpdateProfile(string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("DISPLAY_NAME_REQUIRED", "Cần nhập họ tên hiển thị.");
        DisplayName = displayName.Trim(); UpdatedAt = now;
    }

    public void ChangeRole(UserRole role, DateTimeOffset now)
    {
        if (Role == role) return;
        Role = role; SecurityStamp = Guid.NewGuid(); UpdatedAt = now;
    }

    public void SetActive(bool active, DateTimeOffset now)
    {
        if (IsActive == active) return;
        IsActive = active; SecurityStamp = Guid.NewGuid(); UpdatedAt = now;
        if (active) { FailedLoginCount = 0; LockoutEnd = null; }
    }

    /// <summary>Sets a new password hash. An administrator reset (or the first bootstrap) forces a change at next login.</summary>
    public void SetPassword(string passwordHash, bool mustChange, DateTimeOffset now)
    {
        PasswordHash = passwordHash; MustChangePassword = mustChange; SecurityStamp = Guid.NewGuid();
        FailedLoginCount = 0; LockoutEnd = null; UpdatedAt = now;
    }
}
