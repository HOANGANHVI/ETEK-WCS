using WCS.Domain.Common;

namespace WCS.Domain.Users;

public enum UserRole { Admin = 1, Operator = 2 }

/// <summary>Valid values of the per-user interface preferences.</summary>
public static class UserPreferences
{
    public const string DefaultTheme = "light";
    public static readonly IReadOnlyList<string> Themes = ["light", "dark"];
    public static bool IsValidTheme(string? theme) => theme is not null && Themes.Contains(theme);
}

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
    /// <summary>Personal interface preference (<see cref="UserPreferences.Themes"/>): owned by the user, never changed by an administrator editing the profile.</summary>
    public string Theme { get; private set; } = UserPreferences.DefaultTheme;
    /// <summary>Personal interface preference: the navigation sidebar is collapsed to icons.</summary>
    public bool SidebarCollapsed { get; private set; }
    /// <summary>Contact e-mail. Owned by the user (self-service). Null = not provided yet; never defaulted or guessed.</summary>
    public string? Email { get; private set; }
    /// <summary>Job title. Managed by an administrator. Null = not provided yet.</summary>
    public string? Position { get; private set; }
    /// <summary>Department. Managed by an administrator. Null = not provided yet.</summary>
    public string? Department { get; private set; }
    public uint Version { get; private set; }

    public const int DisplayNameMax = 100;
    public const int EmailMax = 254;
    public const int OrganizationMax = 100;

    /// <summary>Trims; empty becomes null ("not provided").</summary>
    public static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Pragmatic e-mail shape check (one '@', a dotted domain, no spaces/control characters, length limit). It does not prove the address exists.</summary>
    public static bool IsValidEmail(string email)
    {
        if (email.Length is 0 or > EmailMax || email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1) return false;
        var local = email[..at]; var domain = email[(at + 1)..];
        if (local.Length > 64 || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return false;
        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(l => l.Length is > 0 and <= 63 && !l.StartsWith('-') && !l.EndsWith('-') && l.All(ch => char.IsLetterOrDigit(ch) || ch == '-'));
    }

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

    /// <summary>Stores the user's own interface preferences. It is not a security change (sessions stay valid) and not a profile edit (<see cref="UpdatedAt"/> is untouched).</summary>
    public void SetPreferences(string theme, bool sidebarCollapsed)
    {
        if (!UserPreferences.IsValidTheme(theme)) throw new DomainException("PREFERENCE_INVALID", "Invalid theme.");
        Theme = theme; SidebarCollapsed = sidebarCollapsed;
    }

    /// <summary>Bumps the concurrency token (xmin) so two concurrent admin changes serialize (last-admin protection).</summary>
    public void Touch(DateTimeOffset now) { Revision++; UpdatedAt = now; }

    public void UpdateProfile(string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("DISPLAY_NAME_REQUIRED", "Cần nhập họ tên hiển thị.");
        DisplayName = displayName.Trim(); UpdatedAt = now;
    }

    /// <summary>
    /// Self-service profile (full name and e-mail). Not a security change (sessions stay valid) and it never touches the interface preferences,
    /// the organization data or the role.
    /// </summary>
    public void UpdateOwnProfile(string displayName, string? email, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > DisplayNameMax) throw new DomainException("DISPLAY_NAME_LENGTH", "Full name must be 1-100 characters.");
        var mail = NormalizeOptional(email);
        if (mail is not null && !IsValidEmail(mail)) throw new DomainException("EMAIL_INVALID", "E-mail address is not valid.");
        DisplayName = displayName.Trim(); Email = mail; UpdatedAt = now;
    }

    /// <summary>Administrator-managed organization data. Null/blank = not provided. Does not touch preferences, role or security stamp.</summary>
    public void SetOrganization(string? position, string? department, DateTimeOffset now)
    {
        var p = NormalizeOptional(position); var d = NormalizeOptional(department);
        if ((p?.Length ?? 0) > OrganizationMax || (d?.Length ?? 0) > OrganizationMax) throw new DomainException("ORGANIZATION_TOO_LONG", "Position / department: at most 100 characters.");
        Position = p; Department = d; UpdatedAt = now;
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
