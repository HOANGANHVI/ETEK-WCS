using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Users;

namespace WCS.Application.Auth;

/// <summary>Credential check, lockout and own-password change. Does not know about cookies/HTTP.</summary>
public sealed class AuthService(IUserRepository users, IPasswordService passwords, IUnitOfWork uow, IAuditLog audit, IClock clock,
    IOptions<AuthOptions> options)
{
    private const string Generic = "Tên đăng nhập hoặc mật khẩu không đúng.";
    private const string DummyHashSource = "dummy-password-for-constant-time-comparison-1";
    private static string? _dummyHash;

    private enum Outcome { Ok, Invalid, Locked }
    private sealed record LoginAttempt(Outcome Outcome, AuthenticatedUser? User, string? Reason);

    public async Task<AuthenticatedUser> LoginAsync(string? username, string? password, string? ip, CancellationToken ct = default)
    {
        var name = AppUser.NormalizeUsername(username);
        var o = options.Value;
        var attempt = await uow.ExecuteAsync(async t =>
        {
            var user = name.Length == 0 ? null : await users.GetByUsernameAsync(name, t);
            var now = clock.UtcNow;
            if (user is null)
            {
                // Same amount of hashing work for unknown accounts, so response time does not reveal whether the name exists.
                passwords.Verify(_dummyHash ??= passwords.Hash(DummyHashSource), password ?? "");
                return new LoginAttempt(Outcome.Invalid, null, "unknown account");
            }
            if (user.IsLockedOut(now)) return new LoginAttempt(Outcome.Locked, null, "locked");
            var ok = passwords.Verify(user.PasswordHash, password ?? "");
            if (!ok)
            {
                user.RegisterFailedLogin(now, o.MaxFailedLogins, TimeSpan.FromMinutes(o.LockoutMinutes));
                return new LoginAttempt(Outcome.Invalid, null, "wrong password");
            }
            if (!user.IsActive) return new LoginAttempt(Outcome.Invalid, null, "disabled account");
            user.RegisterSuccessfulLogin(now);
            return new LoginAttempt(Outcome.Ok,
                new AuthenticatedUser(user.Id, user.Username, user.DisplayName, user.Role, user.MustChangePassword, user.SecurityStamp), null);
        }, ct);

        if (attempt.Outcome == Outcome.Ok)
        {
            var u = attempt.User!;
            await audit.WriteAsync(new AuditRecord("AUTH_LOGIN", u.Username, u.Role.ToString(), "User", u.Id.ToString(), AuditResult.Success, IpAddress: ip), ct);
            return u;
        }
        // The attempted name is user-supplied text: it is recorded as the actor but never the password.
        await audit.WriteAsync(new AuditRecord("AUTH_LOGIN", Truncate(name), null, "User", null, AuditResult.Failed, attempt.Reason, IpAddress: ip), ct);
        throw attempt.Outcome == Outcome.Locked
            ? AppException.Unauthorized("ACCOUNT_LOCKED", "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau.")
            : AppException.Unauthorized("INVALID_CREDENTIALS", Generic);
    }

    public Task LogoutAsync(ActorInfo actor, CancellationToken ct = default) =>
        audit.WriteAsync(new AuditRecord("AUTH_LOGOUT", actor.Username, actor.Role.ToString(), "User", actor.Id.ToString(), AuditResult.Success, IpAddress: actor.IpAddress), ct);

    /// <summary>Changes the caller's own password; returns the refreshed identity so the API can re-issue the session.</summary>
    public async Task<AuthenticatedUser> ChangeOwnPasswordAsync(ActorInfo actor, string? current, string? next, CancellationToken ct = default)
    {
        PasswordPolicy.Require(next, actor.Username, "newPassword");
        var result = await uow.ExecuteAsync(async t =>
        {
            var user = await users.GetAsync(actor.Id, t) ?? throw AppException.NotFound("USER_NOT_FOUND", "Không tìm thấy tài khoản.");
            if (!user.IsActive) throw AppException.Unauthorized("ACCOUNT_DISABLED", "Tài khoản đã bị vô hiệu hóa.");
            if (!passwords.Verify(user.PasswordHash, current ?? ""))
                return (User: (AuthenticatedUser?)null, Wrong: true);
            if (passwords.Verify(user.PasswordHash, next!))
                throw AppException.Validation("PASSWORD_UNCHANGED", "Mật khẩu mới phải khác mật khẩu hiện tại.");
            user.SetPassword(passwords.Hash(next!), mustChange: false, clock.UtcNow);
            return (User: new AuthenticatedUser(user.Id, user.Username, user.DisplayName, user.Role, false, user.SecurityStamp), Wrong: false);
        }, ct);

        if (result.Wrong)
        {
            await audit.WriteAsync(new AuditRecord("AUTH_PASSWORD_CHANGE", actor.Username, actor.Role.ToString(), "User", actor.Id.ToString(),
                AuditResult.Failed, "current password incorrect", IpAddress: actor.IpAddress), ct);
            throw AppException.Validation("CURRENT_PASSWORD_INCORRECT", "Mật khẩu hiện tại không đúng.");
        }
        await audit.WriteAsync(new AuditRecord("AUTH_PASSWORD_CHANGE", actor.Username, actor.Role.ToString(), "User", actor.Id.ToString(),
            AuditResult.Success, IpAddress: actor.IpAddress), ct);
        return result.User!;
    }

    private static string Truncate(string s) => s.Length <= 50 ? s : s[..50];
}
