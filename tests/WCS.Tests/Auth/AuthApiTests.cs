using Microsoft.Extensions.DependencyInjection;
using WCS.Domain.Users;
using WCS.Tests.Config;
using WCS.Tests.Support;

namespace WCS.Tests.Auth;

/// <summary>
/// HTTP-level authentication/authorization checks through the REAL cookie pipeline, policies and request guards.
/// Only persistence is replaced by in-memory fakes, so no PostgreSQL is needed. Each test gets its own host/users.
/// </summary>
public class AuthApiTests
{
    private static HttpClient NewClient(ConfigApiFactory f) => f.CreateClient().WithClientHeader();

    private static async Task<HttpClient> As(ConfigApiFactory f, string user)
    {
        var c = NewClient(f);
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    // ---------------------------------------------------------------- 401 / 403
    [Theory]
    [InlineData("GET", "/api/tasks")]
    [InlineData("GET", "/api/agvs")]
    [InlineData("GET", "/api/inventory/racks")]
    [InlineData("GET", "/api/config/plc")]
    [InlineData("PUT", "/api/config/plc")]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/audit")]
    [InlineData("POST", "/api/simulator/tasks/00000000-0000-0000-0000-000000000001/advance")]
    [InlineData("GET", "/api/system/health")]
    [InlineData("GET", "/api/auth/me")]
    public async Task Without_a_session_every_protected_endpoint_returns_401(string method, string url)
    {
        await using var f = new ConfigApiFactory();
        var (status, _, _) = await NewClient(f).SendJsonAsync(new HttpMethod(method), url, method == "GET" ? null : new { });
        Assert.Equal(401, status);
    }

    [Theory]
    [InlineData("GET", "/api/config/plc")]
    [InlineData("PUT", "/api/config/plc")]
    [InlineData("POST", "/api/config/plc/test")]
    [InlineData("GET", "/api/config/rcs")]
    [InlineData("PUT", "/api/config/rcs")]
    [InlineData("GET", "/api/config/operations")]
    [InlineData("PUT", "/api/config/agvs/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/config/locations")]
    [InlineData("POST", "/api/simulator/tasks/00000000-0000-0000-0000-000000000001/advance")]
    [InlineData("POST", "/api/simulator/tasks/00000000-0000-0000-0000-000000000001/run")]
    [InlineData("POST", "/api/simulator/station-requests")]
    [InlineData("GET", "/api/users")]
    [InlineData("POST", "/api/users")]
    [InlineData("PUT", "/api/users/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/users/00000000-0000-0000-0000-000000000001/reset-password")]
    [InlineData("GET", "/api/audit")]
    [InlineData("POST", "/api/inventory/initial-stock")]
    [InlineData("POST", "/api/inventory/adjustments")]
    [InlineData("POST", "/api/tasks/00000000-0000-0000-0000-000000000001/cancel")]
    [InlineData("POST", "/api/tasks/00000000-0000-0000-0000-000000000001/reconcile")]
    public async Task An_operator_gets_403_on_admin_only_endpoints(string method, string url)
    {
        await using var f = new ConfigApiFactory();
        var c = await As(f, "op1");
        var (status, root, _) = await c.SendJsonAsync(new HttpMethod(method), url, method == "GET" ? null : new { });
        Assert.Equal(403, status);
        Assert.Equal(0, f.Repo.Rows.Count); // nothing was changed
        _ = root;
    }

    [Fact]
    public async Task An_admin_passes_the_admin_policy_and_an_operator_session_is_a_valid_session()
    {
        await using var f = new ConfigApiFactory();
        var admin = await As(f, "admin1");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Get, "/api/config/plc")).Status);

        var op = await As(f, "op1");
        var me = await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me");
        Assert.Equal(200, me.Status);
        Assert.Equal("Operator", me.Root.GetProperty("data").GetProperty("role").GetString());
        Assert.Equal("op1", me.Root.GetProperty("data").GetProperty("username").GetString());
    }

    [Fact]
    public async Task State_changing_calls_without_the_client_header_are_rejected()
    {
        await using var f = new ConfigApiFactory();
        var c = f.CreateClient(); // no X-WCS-Client header
        var (status, root, _) = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "admin1", password = TestUsers.Password });
        Assert.Equal(403, status);
        Assert.Equal("CLIENT_HEADER_MISSING", root.ErrorCode());
    }

    // ---------------------------------------------------------------- login
    [Fact]
    public async Task Wrong_password_unknown_user_and_disabled_account_all_get_the_same_generic_401()
    {
        await using var f = new ConfigApiFactory();
        f.Users.Users.Add(TestUsers.Make("gone", UserRole.Operator, active: false));
        var c = NewClient(f);

        var wrong = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "op1", password = "bad-password-1" });
        var unknown = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "nobody", password = "bad-password-1" });
        var disabled = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "gone", password = TestUsers.Password });

        foreach (var r in new[] { wrong, unknown, disabled })
        {
            Assert.Equal(401, r.Status);
            Assert.Equal("INVALID_CREDENTIALS", r.Root.ErrorCode());
        }
        Assert.Equal(wrong.Root.GetProperty("error").GetProperty("message").GetString(), disabled.Root.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(401, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status); // a disabled account got no session
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_for_the_correct_password()
    {
        await using var f = new ConfigApiFactory();
        var c = NewClient(f);
        for (var i = 0; i < 5; i++) await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "op1", password = "bad-password-1" });

        var locked = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "op1", password = TestUsers.Password });
        Assert.Equal(401, locked.Status);
        Assert.Equal("ACCOUNT_LOCKED", locked.Root.ErrorCode());
    }

    [Fact]
    public async Task Login_responses_and_stored_hashes_never_contain_the_password()
    {
        await using var f = new ConfigApiFactory();
        var c = NewClient(f);
        var r = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "op1", password = TestUsers.Password });
        Assert.DoesNotContain(TestUsers.Password, r.Raw);
        Assert.DoesNotContain("passwordHash", r.Raw, StringComparison.OrdinalIgnoreCase);
        Assert.All(f.Users.Users, u => Assert.DoesNotContain(TestUsers.Password, u.PasswordHash));
        Assert.All(f.Audit.Records, a => Assert.DoesNotContain(TestUsers.Password, a.Detail ?? ""));
    }

    // ---------------------------------------------------------------- session lifecycle
    [Fact]
    public async Task Logout_ends_the_session()
    {
        await using var f = new ConfigApiFactory();
        var c = await As(f, "op1");
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Post, "/api/auth/logout")).Status);
        Assert.Equal(401, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
    }

    [Fact]
    public async Task The_session_expires_after_the_idle_timeout_and_is_extended_by_activity()
    {
        await using var f = new ConfigApiFactory();
        var c = await As(f, "op1");

        f.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);   // activity slides the window
        f.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);   // 40 min after login, still valid
        f.Time.Advance(TimeSpan.FromMinutes(45));                                            // idle longer than the 30 min default
        Assert.Equal(401, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
    }

    [Fact]
    public async Task The_session_has_an_absolute_lifetime_even_with_constant_activity()
    {
        await using var f = new ConfigApiFactory();
        var c = await As(f, "op1");
        for (var i = 0; i < 12; i++)   // 12 x 25 min = 5 h, always active
        {
            f.Time.Advance(TimeSpan.FromMinutes(25));
            Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
        }
        for (var i = 0; i < 20 && (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status == 200; i++)
            f.Time.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(401, (await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
        Assert.True(f.Time.GetUtcNow() - DateTimeOffset.UtcNow >= TimeSpan.FromHours(12) - TimeSpan.FromMinutes(26)); // ended around the 12 h mark
    }

    [Fact]
    public async Task Disabling_an_account_or_changing_its_role_ends_its_open_session_immediately()
    {
        await using var f = new ConfigApiFactory();
        var op = await As(f, "op1");
        var admin = await As(f, "admin1");
        var opId = f.Users.Users.Single(u => u.Username == "op1").Id;

        var res = await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}", new { displayName = "OP1", role = "Operator", isActive = false });
        Assert.Equal(200, res.Status);
        Assert.Equal(401, (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
        Assert.Equal(401, await NewClient(f).LoginAsync("op1"));

        // role change (re-enable first): the old session of an operator promoted/demoted must not keep the old role
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}", new { displayName = "OP1", role = "Operator", isActive = true });
        var op2 = await As(f, "op1");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}", new { displayName = "OP1", role = "Admin", isActive = true });
        Assert.Equal(401, (await op2.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
    }

    // ---------------------------------------------------------------- forced password change
    [Fact]
    public async Task A_session_that_must_change_the_password_can_only_use_the_auth_endpoints()
    {
        await using var f = new ConfigApiFactory();
        f.Users.Users.Add(TestUsers.Make("newbie", UserRole.Admin, mustChange: true));
        var c = await As(f, "newbie");

        var blocked = await c.SendJsonAsync(HttpMethod.Get, "/api/config/plc");
        Assert.Equal(403, blocked.Status);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", blocked.Root.ErrorCode());
        var me = await c.SendJsonAsync(HttpMethod.Get, "/api/auth/me");
        Assert.True(me.Root.GetProperty("data").GetProperty("mustChangePassword").GetBoolean());

        var weak = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = TestUsers.Password, newPassword = "short1" });
        Assert.Equal(400, weak.Status);
        var wrongCurrent = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = "not-the-current-1", newPassword = "Another-Pass-77" });
        Assert.Equal("CURRENT_PASSWORD_INCORRECT", wrongCurrent.Root.ErrorCode());

        var ok = await c.SendJsonAsync(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = TestUsers.Password, newPassword = "Another-Pass-77" });
        Assert.Equal(200, ok.Status);
        Assert.Equal(200, (await c.SendJsonAsync(HttpMethod.Get, "/api/config/plc")).Status);   // same browser session continues
        Assert.Equal(401, await NewClient(f).LoginAsync("newbie"));                              // old password no longer works
        Assert.Equal(200, await NewClient(f).LoginAsync("newbie", "Another-Pass-77"));
        Assert.DoesNotContain("Another-Pass-77", string.Join('\n', f.Audit.Records.Select(a => a.Detail)));
    }

    // ---------------------------------------------------------------- account management rules
    [Fact]
    public async Task Admin_creates_accounts_with_a_hashed_password_and_the_user_must_change_it()
    {
        await using var f = new ConfigApiFactory();
        var admin = await As(f, "admin1");

        var weak = await admin.SendJsonAsync(HttpMethod.Post, "/api/users", new { username = "newop", displayName = "New", role = "Operator", password = "123" });
        Assert.Equal(400, weak.Status);
        var created = await admin.SendJsonAsync(HttpMethod.Post, "/api/users", new { username = "NewOp", displayName = "Người mới", role = "Operator", password = "Initial-Pass-55" });
        Assert.Equal(201, created.Status);
        Assert.DoesNotContain("Initial-Pass-55", created.Raw);
        var dup = await admin.SendJsonAsync(HttpMethod.Post, "/api/users", new { username = "newop", displayName = "X", role = "Operator", password = "Initial-Pass-55" });
        Assert.Equal(400, dup.Status);

        var stored = f.Users.Users.Single(u => u.Username == "newop");
        Assert.NotEqual("Initial-Pass-55", stored.PasswordHash);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(200, await NewClient(f).LoginAsync("newop", "Initial-Pass-55"));
    }

    [Fact]
    public async Task Nobody_can_change_their_own_role_or_disable_themselves()
    {
        await using var f = new ConfigApiFactory();
        var admin = await As(f, "admin1");
        var id = f.Users.Users.Single(u => u.Username == "admin1").Id;

        var role = await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{id}", new { displayName = "A", role = "Operator", isActive = true });
        Assert.Equal(422, role.Status); Assert.Equal("CANNOT_CHANGE_OWN_ROLE", role.Root.ErrorCode());
        var self = await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{id}", new { displayName = "A", role = "Admin", isActive = false });
        Assert.Equal(422, self.Status); Assert.Equal("CANNOT_DISABLE_SELF", self.Root.ErrorCode());
        Assert.Equal(UserRole.Admin, f.Users.Users.Single(u => u.Username == "admin1").Role);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_disabled_or_demoted()
    {
        await using var f = new ConfigApiFactory();
        var admin1 = await As(f, "admin1");
        var id2 = f.Users.Users.Single(u => u.Username == "admin2").Id;
        var id1 = f.Users.Users.Single(u => u.Username == "admin1").Id;

        // two admins: admin1 may demote admin2 ...
        Assert.Equal(200, (await admin1.SendJsonAsync(HttpMethod.Put, $"/api/users/{id2}", new { displayName = "A2", role = "Operator", isActive = true })).Status);
        // ... but now admin1 is the only one. Another admin session cannot exist, so promote op1 to test from a second admin:
        var op1Id = f.Users.Users.Single(u => u.Username == "op1").Id;
        await admin1.SendJsonAsync(HttpMethod.Put, $"/api/users/{op1Id}", new { displayName = "OP1", role = "Admin", isActive = true });
        var promoted = await As(f, "op1");
        // op1 (admin) tries to disable admin1 -> allowed (2 admins). Then admin1 is disabled, op1 is the last one:
        Assert.Equal(200, (await promoted.SendJsonAsync(HttpMethod.Put, $"/api/users/{id1}", new { displayName = "A1", role = "Admin", isActive = false })).Status);
        // the only remaining admin (op1) cannot be demoted by itself (own role) - and nobody else can act on it
        var attempt = await promoted.SendJsonAsync(HttpMethod.Put, $"/api/users/{op1Id}", new { displayName = "OP1", role = "Operator", isActive = true });
        Assert.Equal(422, attempt.Status);
        Assert.Single(f.Users.Users, u => u is { Role: UserRole.Admin, IsActive: true });
    }

    [Fact]
    public async Task Demoting_the_last_admin_through_another_admin_is_blocked_by_the_service_rule()
    {
        await using var f = new ConfigApiFactory();
        f.Users.Users.RemoveAll(u => u.Username == "admin2");
        // exactly one active admin; a second ADMIN-role caller is impossible, so exercise the rule at service level with a foreign actor id
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<WCS.Application.Auth.UserAdminService>();
        var target = f.Users.Users.Single(u => u.Username == "admin1");
        var ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => svc.UpdateAsync(
            new WCS.Application.Auth.ActorInfo(Guid.NewGuid(), "ghost", UserRole.Admin), target.Id,
            new WCS.Application.Auth.UpdateUserCommand("A1", UserRole.Operator, true)));
        Assert.Equal("LAST_ADMIN", ex.Code);
        ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => svc.UpdateAsync(
            new WCS.Application.Auth.ActorInfo(Guid.NewGuid(), "ghost", UserRole.Admin), target.Id,
            new WCS.Application.Auth.UpdateUserCommand("A1", UserRole.Admin, false)));
        Assert.Equal("LAST_ADMIN", ex.Code);
    }

    [Fact]
    public async Task An_operator_cannot_manage_accounts_even_through_the_service()
    {
        await using var f = new ConfigApiFactory();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<WCS.Application.Auth.UserAdminService>();
        var op = f.Users.Users.Single(u => u.Username == "op1");
        var actor = new WCS.Application.Auth.ActorInfo(op.Id, op.Username, UserRole.Operator);
        var ex = await Assert.ThrowsAsync<WCS.Application.Common.AppException>(() => svc.UpdateAsync(actor, op.Id,
            new WCS.Application.Auth.UpdateUserCommand("OP1", UserRole.Admin, true)));
        Assert.Equal("FORBIDDEN", ex.Code);
        Assert.Equal(UserRole.Operator, op.Role);
    }

    [Fact]
    public async Task Admin_can_search_and_reset_a_password_and_the_old_one_stops_working()
    {
        await using var f = new ConfigApiFactory();
        var admin = await As(f, "admin1");
        var found = await admin.SendJsonAsync(HttpMethod.Get, "/api/users?search=op");
        Assert.Equal(1, found.Root.GetProperty("data").GetArrayLength());

        var id = f.Users.Users.Single(u => u.Username == "op1").Id;
        var open = await As(f, "op1");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/users/{id}/reset-password", new { newPassword = "Reset-Pass-2026" })).Status);
        Assert.Equal(401, (await open.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);       // old session ended
        Assert.Equal(401, await NewClient(f).LoginAsync("op1"));                                       // old password rejected
        Assert.Equal(200, await NewClient(f).LoginAsync("op1", "Reset-Pass-2026"));
        Assert.True(f.Users.Users.Single(u => u.Username == "op1").MustChangePassword);
    }

    // ---------------------------------------------------------------- audit trail
    [Fact]
    public async Task Important_actions_are_audited_without_secrets()
    {
        await using var f = new ConfigApiFactory();
        var c = NewClient(f);
        await c.SendJsonAsync(HttpMethod.Post, "/api/auth/login", new { username = "op1", password = "wrong-password-9" });
        Assert.Equal(200, await c.LoginAsync("admin1"));

        await c.SendJsonAsync(HttpMethod.Put, "/api/config/rcs", new
        {
            enabled = true, systemName = "RCS", host = "rcs.example.test", port = 8182, authMode = "UsernamePassword", username = "svc",
            timeoutMs = 5000, password = "Audit-Secret-123", clearPassword = false, clearToken = false,
        });
        await c.SendJsonAsync(HttpMethod.Post, "/api/users", new { username = "audited", displayName = "A", role = "Operator", password = "Initial-Pass-55" });
        await c.SendJsonAsync(HttpMethod.Post, "/api/auth/logout");

        var actions = f.Audit.Records.Select(r => (r.Action, r.Result)).ToList();
        Assert.Contains(("AUTH_LOGIN", AuditResult.Failed), actions);
        Assert.Contains(("AUTH_LOGIN", AuditResult.Success), actions);
        Assert.Contains(("CONFIG_RCS_UPDATE", AuditResult.Success), actions);
        Assert.Contains(("USER_CREATE", AuditResult.Success), actions);
        Assert.Contains(("AUTH_LOGOUT", AuditResult.Success), actions);

        var rcs = f.Audit.Records.Single(r => r.Action == "CONFIG_RCS_UPDATE");
        Assert.Equal("admin1", rcs.Actor);
        Assert.Equal("Admin", rcs.ActorRole);
        Assert.NotNull(rcs.Before); Assert.NotNull(rcs.After);
        Assert.Equal("password replaced", rcs.Detail);   // the fact, not the value

        var everything = System.Text.Json.JsonSerializer.Serialize(f.Audit.Records);
        foreach (var secret in new[] { "Audit-Secret-123", "Initial-Pass-55", "wrong-password-9", TestUsers.Password })
            Assert.DoesNotContain(secret, everything);
    }
}
