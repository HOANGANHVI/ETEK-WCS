using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Application.Config;
using WCS.Domain.Common;
using WCS.Domain.Users;

namespace WCS.Application.Auth;

/// <summary>Account management. Callers must already have been authorized as Admin; the rules below are enforced again here.</summary>
public sealed class UserAdminService(IUserRepository users, IPasswordService passwords, IUnitOfWork uow, IAuditLog audit, IClock clock)
{
    private UserDto ToDto(AppUser u) => new(u.Id, u.Username, u.DisplayName, u.Role, u.IsActive, u.MustChangePassword, u.LastLoginAt,
        u.IsLockedOut(clock.UtcNow) ? u.LockoutEnd : null, u.CreatedAt);

    private static object Snapshot(AppUser u) => new { u.Username, u.DisplayName, role = u.Role.ToString(), u.IsActive };

    private static void RequireAdmin(ActorInfo actor)
    {
        if (!actor.IsAdmin) throw AppException.Forbidden("FORBIDDEN", "Chỉ Admin được quản lý tài khoản.");
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(ActorInfo actor, string? search, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return (await users.ListAsync(search?.Trim(), ct)).Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateAsync(ActorInfo actor, CreateUserCommand cmd, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var name = AppUser.NormalizeUsername(cmd.Username);
        var errors = new List<FieldError>();
        if (!AppUser.IsValidUsername(name)) errors.Add(new FieldError("username", "Tên đăng nhập gồm 3-50 ký tự: chữ thường, số, dấu chấm, gạch dưới hoặc gạch ngang."));
        if (string.IsNullOrWhiteSpace(cmd.DisplayName)) errors.Add(new FieldError("displayName", "Cần nhập họ tên hiển thị."));
        else if (cmd.DisplayName.Trim().Length > 100) errors.Add(new FieldError("displayName", "Họ tên không được dài quá 100 ký tự."));
        if (!Enum.IsDefined(cmd.Role)) errors.Add(new FieldError("role", "Vai trò không hợp lệ."));
        errors.AddRange(PasswordPolicy.Check(cmd.Password, name));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        var dto = await uow.ExecuteAsync(async t =>
        {
            if (await users.GetByUsernameAsync(name, t) is not null)
                throw new ValidationFailedException([new FieldError("username", "Tên đăng nhập đã tồn tại.")]);
            // A new account must change the password handed over by the administrator at first login.
            var user = AppUser.Create(name, cmd.DisplayName, cmd.Role, passwords.Hash(cmd.Password), mustChangePassword: true, clock.UtcNow);
            users.Add(user);
            return ToDto(user);
        }, ct);
        await Audit(actor, "USER_CREATE", dto.Id, null, new { dto.Username, dto.DisplayName, role = dto.Role.ToString(), dto.IsActive }, ct);
        return dto;
    }

    public async Task<UserDto> UpdateAsync(ActorInfo actor, Guid id, UpdateUserCommand cmd, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        if (string.IsNullOrWhiteSpace(cmd.DisplayName) || cmd.DisplayName.Trim().Length > 100)
            throw new ValidationFailedException([new FieldError("displayName", "Họ tên phải có 1-100 ký tự.")]);
        if (!Enum.IsDefined(cmd.Role)) throw new ValidationFailedException([new FieldError("role", "Vai trò không hợp lệ.")]);

        object? before = null;
        var dto = await uow.ExecuteAsync(async t =>
        {
            var user = await users.GetAsync(id, t) ?? throw AppException.NotFound("USER_NOT_FOUND", "Không tìm thấy tài khoản.");
            before = Snapshot(user);
            var self = user.Id == actor.Id;
            if (self && cmd.Role != user.Role) throw AppException.Rejected("CANNOT_CHANGE_OWN_ROLE", "Không thể tự thay đổi vai trò của chính mình.");
            if (self && !cmd.IsActive) throw AppException.Rejected("CANNOT_DISABLE_SELF", "Không thể tự vô hiệu hóa tài khoản của chính mình.");

            var losesAdmin = user is { Role: UserRole.Admin, IsActive: true } && (cmd.Role != UserRole.Admin || !cmd.IsActive);
            if (losesAdmin)
            {
                var admins = await users.ListActiveAdminsAsync(t);
                if (admins.Count <= 1) throw AppException.Rejected("LAST_ADMIN", "Không thể vô hiệu hóa hoặc hạ quyền Admin đang hoạt động cuối cùng.");
                // Touch every active admin: two concurrent demotions then conflict on the row version and re-evaluate the count.
                foreach (var a in admins) a.Touch(clock.UtcNow);
            }

            user.UpdateProfile(cmd.DisplayName, clock.UtcNow);
            user.ChangeRole(cmd.Role, clock.UtcNow);
            user.SetActive(cmd.IsActive, clock.UtcNow);
            return ToDto(user);
        }, ct);
        await Audit(actor, "USER_UPDATE", id, before, new { dto.Username, dto.DisplayName, role = dto.Role.ToString(), dto.IsActive }, ct);
        return dto;
    }

    public async Task ResetPasswordAsync(ActorInfo actor, Guid id, string? newPassword, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        string username = "";
        await uow.ExecuteAsync(async t =>
        {
            var user = await users.GetAsync(id, t) ?? throw AppException.NotFound("USER_NOT_FOUND", "Không tìm thấy tài khoản.");
            username = user.Username;
            PasswordPolicy.Require(newPassword, user.Username, "newPassword");
            user.SetPassword(passwords.Hash(newPassword!), mustChange: true, clock.UtcNow);
            return 0;
        }, ct);
        // The audit entry records THAT the password was reset, never the password itself.
        await Audit(actor, "USER_PASSWORD_RESET", id, null, new { username, mustChangePassword = true }, ct);
    }

    private Task Audit(ActorInfo actor, string action, Guid targetId, object? before, object? after, CancellationToken ct) =>
        audit.WriteAsync(new AuditRecord(action, actor.Username, actor.Role.ToString(), "User", targetId.ToString(), AuditResult.Success,
            Before: before, After: after, IpAddress: actor.IpAddress), ct);
}
