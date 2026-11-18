using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Domain.Users;

namespace WCS.Application.Auth;

/// <summary>
/// The caller's OWN profile: full name, e-mail and avatar. The account is always the authenticated session (<see cref="ActorInfo"/>); no method takes a
/// user id from a request. Position, department, role and activation are administrator-managed (<see cref="UserAdminService"/>) and cannot be changed here;
/// the interface preferences are not touched by any of these operations.
/// </summary>
public sealed class ProfileService(IUserRepository users, IUnitOfWork uow, IAuditLog audit, IClock clock)
{
    public sealed record AvatarFile(string ContentType, byte[] Data);

    private async Task<ProfileDto> ToDtoAsync(AppUser u, CancellationToken ct)
    {
        var stamp = await users.GetAvatarStampAsync(u.Id, ct);
        return new ProfileDto(u.Username, u.DisplayName, u.Role, u.Email, u.Position, u.Department, stamp is not null,
            stamp?.ToUnixTimeMilliseconds() ?? 0, u.LastLoginAt);
    }

    private static object Snapshot(AppUser u) => new { u.Username, u.DisplayName, u.Email };

    public Task<ProfileDto> GetAsync(ActorInfo actor, CancellationToken ct = default) =>
        uow.ExecuteAsync(async t => await ToDtoAsync(await users.GetAsync(actor.Id, t) ?? throw NotFound(), t), ct);

    /// <summary>Updates the caller's full name and e-mail. Returns the refreshed identity so the API can re-issue the session (the name is a session claim).</summary>
    public async Task<(ProfileDto Profile, AuthenticatedUser Identity)> UpdateOwnAsync(ActorInfo actor, string? displayName, string? email, CancellationToken ct = default)
    {
        var errors = new List<FieldError>();
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name)) errors.Add(new FieldError("displayName", "Cần nhập họ tên hiển thị.", "DISPLAY_NAME_REQUIRED"));
        else if (name.Length > AppUser.DisplayNameMax) errors.Add(new FieldError("displayName", "Họ tên không được dài quá 100 ký tự.", "DISPLAY_NAME_MAX"));
        var mail = AppUser.NormalizeOptional(email);
        if (mail is not null && !AppUser.IsValidEmail(mail)) errors.Add(new FieldError("email", "Địa chỉ email không hợp lệ.", "EMAIL_INVALID"));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        object? before = null;
        var result = await uow.ExecuteAsync(async t =>
        {
            var user = await users.GetAsync(actor.Id, t) ?? throw NotFound();
            if (!user.IsActive) throw AppException.Unauthorized("ACCOUNT_DISABLED", "Tài khoản đã bị vô hiệu hóa.");
            before = Snapshot(user);
            user.UpdateOwnProfile(name!, mail, clock.UtcNow);
            return (Profile: await ToDtoAsync(user, t), Identity: new AuthenticatedUser(user.Id, user.Username, user.DisplayName, user.Role, user.MustChangePassword, user.SecurityStamp), After: Snapshot(user));
        }, ct);
        await Audit(actor, "USER_PROFILE_UPDATE", before, result.After, ct);
        return (result.Profile, result.Identity);
    }

    /// <summary>Sets or replaces the caller's avatar. The bytes are validated here (content sniffing, size, dimensions); nothing about the file name or path is kept.</summary>
    public async Task<ProfileDto> SetAvatarAsync(ActorInfo actor, byte[] data, CancellationToken ct = default)
    {
        var image = AvatarImage.Validate(data);
        await uow.ExecuteAsync(async t =>
        {
            var user = await users.GetAsync(actor.Id, t) ?? throw NotFound();
            if (!user.IsActive) throw AppException.Unauthorized("ACCOUNT_DISABLED", "Tài khoản đã bị vô hiệu hóa.");
            var existing = await users.GetAvatarAsync(actor.Id, t);
            if (existing is null) users.AddAvatar(UserAvatar.Create(actor.Id, image.ContentType, data, clock.UtcNow));
            else existing.Replace(image.ContentType, data, clock.UtcNow);
            return 0;
        }, ct);
        await Audit(actor, "USER_AVATAR_UPDATE", null, new { contentType = image.ContentType, bytes = data.Length, image.Width, image.Height }, ct);
        return await GetAsync(actor, ct);
    }

    public async Task<ProfileDto> RemoveAvatarAsync(ActorInfo actor, CancellationToken ct = default)
    {
        var removed = await uow.ExecuteAsync(async t =>
        {
            var existing = await users.GetAvatarAsync(actor.Id, t);
            if (existing is null) return false;
            users.RemoveAvatar(existing);
            return true;
        }, ct);
        if (removed) await Audit(actor, "USER_AVATAR_DELETE", null, null, ct);
        return await GetAsync(actor, ct);
    }

    /// <summary>The caller's own avatar image; null when none has been set.</summary>
    public Task<AvatarFile?> GetAvatarAsync(ActorInfo actor, CancellationToken ct = default) =>
        uow.ExecuteAsync<AvatarFile?>(async t =>
        {
            var a = await users.GetAvatarAsync(actor.Id, t);
            return a is null ? null : new AvatarFile(a.ContentType, a.Data);
        }, ct);

    private static AppException NotFound() => AppException.NotFound("USER_NOT_FOUND", "Không tìm thấy tài khoản.");

    private Task Audit(ActorInfo actor, string action, object? before, object? after, CancellationToken ct) =>
        audit.WriteAsync(new AuditRecord(action, actor.Username, actor.Role.ToString(), "User", actor.Id.ToString(), AuditResult.Success,
            Before: before, After: after, IpAddress: actor.IpAddress), ct);
}
