using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Auth;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>User profile: own full name / e-mail / avatar (self-service), administrator-managed position and department, and strict isolation between accounts.</summary>
[Collection("Database")]
public class UserProfileTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator)); db.Users.Add(TestUsers.Make("op2", UserRole.Operator));
        await db.SaveChangesAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static async Task<JsonElement> Profile(HttpClient c)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, "/api/auth/profile");
        Assert.Equal(200, r.Status);
        return r.Root.GetProperty("data");
    }

    private static async Task<(int Status, JsonElement Root)> PutAvatar(HttpClient c, byte[] body, string contentType = "image/png")
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/auth/avatar") { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var res = await c.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        return ((int)res.StatusCode, string.IsNullOrWhiteSpace(raw) ? default : JsonDocument.Parse(raw).RootElement);
    }

    private static Task<Guid> IdOf(Harness h, string user) => h.Db(db => db.Users.Where(u => u.Username == user).Select(u => u.Id).SingleAsync());

    // ------------------------------------------------------------------ profile
    [Fact]
    public async Task A_new_account_has_no_email_position_department_or_avatar_and_nothing_is_invented()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var p = await Profile(await As(f, "op1"));
        Assert.Equal(JsonValueKind.Null, p.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("position").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("department").ValueKind);
        Assert.False(p.GetProperty("hasAvatar").GetBoolean());
        Assert.Equal("op1", p.GetProperty("username").GetString());
        Assert.Equal("Operator", p.GetProperty("role").GetString());
    }

    [Fact]
    public async Task The_profile_never_exposes_any_password_material()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var raw = (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/profile")).Raw.ToLowerInvariant();
        Assert.DoesNotContain("password", raw); Assert.DoesNotContain("hash", raw); Assert.DoesNotContain("securitystamp", raw);
        var admin = await As(f, "admin1");
        var list = (await admin.SendJsonAsync(HttpMethod.Get, "/api/users")).Raw.ToLowerInvariant();
        Assert.DoesNotContain("passwordhash", list); Assert.DoesNotContain("securitystamp", list);
    }

    [Fact]
    public async Task A_user_updates_their_own_full_name_and_email_and_the_session_shows_the_new_name()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var r = await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "  Nguyen Van A ", email = " op1@example.com " });
        Assert.Equal(200, r.Status);
        var p = await Profile(op);
        Assert.Equal("Nguyen Van A", p.GetProperty("displayName").GetString());
        Assert.Equal("op1@example.com", p.GetProperty("email").GetString());
        Assert.Equal("Nguyen Van A", (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Root.GetProperty("data").GetProperty("displayName").GetString());
        // still signed in with the same security stamp: other sessions are not ended by a profile edit
        var other = await As(f, "op1");
        Assert.Equal(200, (await other.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Nguyen Van A", email = (string?)null })).Status);
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
    }

    [Fact]
    public async Task A_blank_email_clears_it_and_is_stored_as_not_provided()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Op", email = "op1@example.com" });
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Op", email = "   " })).Status);
        Assert.Null(await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => u.Email).SingleAsync()));
    }

    [Theory]
    [InlineData("not-an-email", "EMAIL_INVALID")]
    [InlineData("a@b", "EMAIL_INVALID")]
    [InlineData("a b@example.com", "EMAIL_INVALID")]
    public async Task An_invalid_email_is_rejected_by_the_backend_and_nothing_changes(string email, string field)
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var r = await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Changed", email });
        Assert.Equal(400, r.Status);
        Assert.Contains(field, r.Raw);
        var row = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => new { u.DisplayName, u.Email }).SingleAsync());
        Assert.Equal(("OP1", (string?)null), (row.DisplayName, row.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_full_name_is_rejected(string name)
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        Assert.Equal(400, (await (await As(f, "op1")).SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = name, email = (string?)null })).Status);
    }

    [Fact]
    public async Task A_full_name_or_email_over_the_limit_is_rejected()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        Assert.Equal(400, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = new string('n', 101), email = (string?)null })).Status);
        Assert.Equal(400, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "ok", email = new string('a', 250) + "@example.com" })).Status);
    }

    [Fact]
    public async Task Profile_endpoints_need_a_signed_in_user()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var anon = f.CreateClient().WithClientHeader();
        Assert.Equal(401, (await anon.SendJsonAsync(HttpMethod.Get, "/api/auth/profile")).Status);
        Assert.Equal(401, (await anon.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "x", email = (string?)null })).Status);
        Assert.Equal(401, (await anon.SendJsonAsync(HttpMethod.Get, "/api/auth/avatar")).Status);
        Assert.Equal(401, (await PutAvatar(anon, AvatarAndProfileRulesTests.Png(64, 64))).Status);
        Assert.Equal(401, (await anon.SendJsonAsync(HttpMethod.Delete, "/api/auth/avatar")).Status);
    }

    // ------------------------------------------------------------------ who may change what
    [Fact]
    public async Task A_user_cannot_change_their_own_position_department_role_or_username_through_the_profile()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var r = await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Op One", email = "op1@example.com", position = "Director", department = "Board", role = "Admin", username = "root", isActive = false });
        Assert.Equal(200, r.Status);
        var row = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => new { u.Position, u.Department, u.Role, u.IsActive }).SingleAsync());
        Assert.Equal(((string?)null, (string?)null, UserRole.Operator, true), (row.Position, row.Department, row.Role, row.IsActive));
        Assert.Equal(1, await h.Db(db => db.Users.CountAsync(u => u.Username == "op1")));
        Assert.Equal(0, await h.Db(db => db.Users.CountAsync(u => u.Username == "root")));
    }

    [Fact]
    public async Task Only_an_administrator_sets_position_and_department_and_an_operator_is_refused()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op1Id = await IdOf(h, "op1"); var op2Id = await IdOf(h, "op2");
        var op = await As(f, "op1");
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, $"/api/users/{op1Id}/organization", new { position = "Lead", department = "Line 1" })).Status);   // own account
        Assert.Equal(403, (await op.SendJsonAsync(HttpMethod.Put, $"/api/users/{op2Id}/organization", new { position = "Lead", department = "Line 1" })).Status);   // someone else
        Assert.Equal(401, (await f.CreateClient().WithClientHeader().SendJsonAsync(HttpMethod.Put, $"/api/users/{op1Id}/organization", new { position = "x", department = "y" })).Status);
        Assert.Null(await h.Db(db => db.Users.Where(u => u.Id == op1Id).Select(u => u.Position).SingleAsync()));

        var admin = await As(f, "admin1");
        var r = await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{op1Id}/organization", new { position = "  Line lead ", department = "Assembly" });
        Assert.Equal(200, r.Status);
        var p = await Profile(await As(f, "op1"));
        Assert.Equal("Line lead", p.GetProperty("position").GetString()); Assert.Equal("Assembly", p.GetProperty("department").GetString());
        // the administrator can read it in the account list
        var row = (await admin.SendJsonAsync(HttpMethod.Get, "/api/users")).Root.GetProperty("data").EnumerateArray().Single(u => u.GetProperty("username").GetString() == "op1");
        Assert.Equal("Assembly", row.GetProperty("department").GetString());
    }

    [Fact]
    public async Task An_administrator_changing_organization_data_leaves_the_operators_preferences_and_personal_data_alone()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true });
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Op Own Name", email = "own@example.com" });
        var opId = await IdOf(h, "op1");
        var stamp = await h.Db(db => db.Users.Where(u => u.Id == opId).Select(u => u.SecurityStamp).SingleAsync());

        var admin = await As(f, "admin1");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}/organization", new { position = "Lead", department = "Assembly" })).Status);

        var row = await h.Db(db => db.Users.Where(u => u.Id == opId).Select(u => new { u.Theme, u.SidebarCollapsed, u.DisplayName, u.Email, u.SecurityStamp, u.Position }).SingleAsync());
        Assert.Equal(("dark", true, "Op Own Name", "own@example.com", stamp, "Lead"), (row.Theme, row.SidebarCollapsed, row.DisplayName, row.Email, row.SecurityStamp, row.Position));
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);   // the operator's session stays valid
        // the admin's own preferences were not changed either
        Assert.Equal("light", await h.Db(db => db.Users.Where(u => u.Username == "admin1").Select(u => u.Theme).SingleAsync()));
    }

    [Fact]
    public async Task The_existing_admin_edit_dialog_keeps_organization_email_and_preferences()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true });
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Op", email = "op@example.com" });
        var opId = await IdOf(h, "op1");
        var admin = await As(f, "admin1");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}/organization", new { position = "Lead", department = "Assembly" });
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}", new { displayName = "Renamed by admin", role = "Operator", isActive = true })).Status);
        var row = await h.Db(db => db.Users.Where(u => u.Id == opId).Select(u => new { u.DisplayName, u.Email, u.Position, u.Department, u.Theme, u.SidebarCollapsed }).SingleAsync());
        Assert.Equal(("Renamed by admin", "op@example.com", "Lead", "Assembly", "dark", true), (row.DisplayName, row.Email, row.Position, row.Department, row.Theme, row.SidebarCollapsed));
    }

    [Fact]
    public async Task Organization_values_over_100_characters_are_rejected_and_blank_means_not_provided()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var opId = await IdOf(h, "op1");
        var admin = await As(f, "admin1");
        Assert.Equal(400, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}/organization", new { position = new string('p', 101), department = "x" })).Status);
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}/organization", new { position = "Lead", department = "Assembly" });
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}/organization", new { position = "", department = (string?)null })).Status);
        var row = await h.Db(db => db.Users.Where(u => u.Id == opId).Select(u => new { u.Position, u.Department }).SingleAsync());
        Assert.Equal(((string?)null, (string?)null), (row.Position, row.Department));
        Assert.Equal(404, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{Guid.NewGuid()}/organization", new { position = "x", department = "y" })).Status);
    }

    [Fact]
    public async Task There_is_no_route_to_edit_another_users_profile_and_a_forged_id_is_ignored()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op2Id = await IdOf(h, "op2");
        var op = await As(f, "op1");
        Assert.NotEqual(200, (await op.SendJsonAsync(HttpMethod.Put, $"/api/auth/profile/{op2Id}", new { displayName = "Hacked", email = "h@example.com" })).Status);
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Mine", email = "mine@example.com", id = op2Id, userId = op2Id, username = "op2" })).Status);
        var rows = await h.Db(db => db.Users.Where(u => u.Username == "op1" || u.Username == "op2").Select(u => new { u.Username, u.DisplayName, u.Email }).ToListAsync());
        Assert.Equal("Mine", rows.Single(r => r.Username == "op1").DisplayName);
        Assert.Equal(("OP2", (string?)null), (rows.Single(r => r.Username == "op2").DisplayName, rows.Single(r => r.Username == "op2").Email));
    }

    [Fact]
    public async Task Profile_edits_are_audited_without_secrets()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/profile", new { displayName = "Op", email = "op@example.com" });
        await PutAvatar(op, AvatarAndProfileRulesTests.Png(64, 64));
        var admin = await As(f, "admin1");
        await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{await IdOf(h, "op1")}/organization", new { position = "Lead", department = "A" });
        var actions = await h.Db(db => db.AuditLog.Select(a => a.Action).ToListAsync());
        Assert.Contains("USER_PROFILE_UPDATE", actions); Assert.Contains("USER_AVATAR_UPDATE", actions); Assert.Contains("USER_ORG_UPDATE", actions);
    }

    // ------------------------------------------------------------------ avatar
    [Fact]
    public async Task A_user_uploads_replaces_reads_and_removes_their_own_avatar()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var png = AvatarAndProfileRulesTests.Png(64, 64);
        var up = await PutAvatar(op, png);
        Assert.Equal(200, up.Status);
        Assert.True(up.Root.GetProperty("data").GetProperty("hasAvatar").GetBoolean());
        Assert.True(up.Root.GetProperty("data").GetProperty("avatarVersion").GetInt64() > 0);

        var get = await op.GetAsync("/api/auth/avatar");
        Assert.Equal(200, (int)get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", get.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", get.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", get.Headers.CacheControl!.ToString());

        var jpeg = AvatarAndProfileRulesTests.Jpeg(90, 60);          // replacing keeps a single row
        Assert.Equal(200, (await PutAvatar(op, jpeg, "image/jpeg")).Status);
        var again = await op.GetAsync("/api/auth/avatar");
        Assert.Equal("image/jpeg", again.Content.Headers.ContentType!.MediaType);
        Assert.Equal(jpeg, await again.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await h.Db(db => db.UserAvatars.CountAsync(a => a.UserId == db.Users.Where(u => u.Username == "op1").Select(u => u.Id).First())));

        var del = await op.SendJsonAsync(HttpMethod.Delete, "/api/auth/avatar");
        Assert.Equal(200, del.Status);
        Assert.False(del.Root.GetProperty("data").GetProperty("hasAvatar").GetBoolean());
        Assert.Equal(404, (int)(await op.GetAsync("/api/auth/avatar")).StatusCode);
        Assert.Equal(0, await h.Db(db => db.UserAvatars.CountAsync()));
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Delete, "/api/auth/avatar")).Status);   // removing nothing is not an error
    }

    [Fact]
    public async Task The_stored_type_comes_from_the_content_not_from_what_the_client_claims()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        Assert.Equal(200, (await PutAvatar(op, AvatarAndProfileRulesTests.Png(64, 64), "text/html")).Status);
        Assert.Equal("image/png", (await op.GetAsync("/api/auth/avatar")).Content.Headers.ContentType!.MediaType);
        // the claimed type is irrelevant the other way round too: html declared as an image is still refused
        var r = await PutAvatar(op, System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "image/png");
        Assert.Equal(400, r.Status); Assert.Equal("AVATAR_UNSUPPORTED_TYPE", r.Root.ErrorCode());
    }

    [Fact]
    public async Task Invalid_uploads_are_refused_with_a_specific_code_and_nothing_is_stored()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        async Task Refused(byte[] body, string code) { var r = await PutAvatar(op, body); Assert.Equal(400, r.Status); Assert.Equal(code, r.Root.ErrorCode()); }
        await Refused([], "AVATAR_EMPTY");
        await Refused(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"), "AVATAR_UNSUPPORTED_TYPE");
        await Refused(System.Text.Encoding.ASCII.GetBytes("GIF89a......"), "AVATAR_UNSUPPORTED_TYPE");
        await Refused(AvatarAndProfileRulesTests.Png(64, 64, withIend: false), "AVATAR_INVALID_IMAGE");
        await Refused(AvatarAndProfileRulesTests.Png(8, 8), "AVATAR_DIMENSIONS");
        await Refused(AvatarAndProfileRulesTests.Png(9000, 64), "AVATAR_DIMENSIONS");
        var big = AvatarAndProfileRulesTests.Png(64, 64); var head = big[..^12]; var tail = big[^12..];
        await Refused([.. head, .. new byte[AvatarImage_Max + 10], .. tail], "AVATAR_TOO_LARGE");
        Assert.Equal(0, await h.Db(db => db.UserAvatars.CountAsync()));
    }

    private static int AvatarImage_Max => WCS.Application.Auth.AvatarImage.MaxBytes;

    [Fact]
    public async Task Avatars_are_private_to_their_owner_and_never_reachable_through_another_account()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1"); var op1 = await As(f, "op1"); var op2 = await As(f, "op2");
        var png = AvatarAndProfileRulesTests.Png(64, 64);
        await PutAvatar(admin, AvatarAndProfileRulesTests.Jpeg(64, 64), "image/jpeg");
        await PutAvatar(op1, png);
        // op2 has none: the endpoint answers for op2's own account only (404), it can never return op1's or the admin's image
        Assert.Equal(404, (int)(await op2.GetAsync("/api/auth/avatar")).StatusCode);
        Assert.Equal(png, await (await op1.GetAsync("/api/auth/avatar")).Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", (await admin.GetAsync("/api/auth/avatar")).Content.Headers.ContentType!.MediaType);
        // no route addresses another account's avatar, whatever the role
        var op1Id = await IdOf(h, "op1");
        foreach (var path in new[] { $"/api/auth/avatar/{op1Id}", $"/api/users/{op1Id}/avatar", $"/api/avatars/{op1Id}" })
        {
            Assert.NotEqual(200, (int)(await op2.GetAsync(path)).StatusCode);
            Assert.NotEqual(200, (int)(await admin.GetAsync(path)).StatusCode);
        }
        // operator 2 deleting/uploading touches only their own row
        await op2.SendJsonAsync(HttpMethod.Delete, "/api/auth/avatar");
        Assert.Equal(2, await h.Db(db => db.UserAvatars.CountAsync()));
        // the account list (admin) carries no image bytes
        var list = (await admin.SendJsonAsync(HttpMethod.Get, "/api/users")).Raw;
        Assert.DoesNotContain("iVBOR", list); Assert.DoesNotContain("\"data\":\"/9j/", list);
    }

    [Fact]
    public async Task Changing_the_avatar_does_not_touch_preferences_or_the_security_stamp()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true });
        var stamp = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => u.SecurityStamp).SingleAsync());
        Assert.Equal(200, (await PutAvatar(op, AvatarAndProfileRulesTests.Png(64, 64))).Status);
        var row = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => new { u.Theme, u.SidebarCollapsed, u.SecurityStamp }).SingleAsync());
        Assert.Equal(("dark", true, stamp), (row.Theme, row.SidebarCollapsed, row.SecurityStamp));
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_avatar_with_it()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op2");
        await PutAvatar(op, AvatarAndProfileRulesTests.Png(64, 64));
        Assert.Equal(1, await h.Db(db => db.UserAvatars.CountAsync()));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Remove(await db.Users.SingleAsync(u => u.Username == "op2"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, await h.Db(db => db.UserAvatars.CountAsync()));
    }

    // ------------------------------------------------------------------ password (existing API reused)
    [Fact]
    public async Task Own_password_change_enforces_the_policy_and_never_returns_a_password()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        async Task<(int Status, string Raw)> Change(string cur, string next) { var r = await op.SendJsonAsync(HttpMethod.Post, "/api/auth/change-password", new { currentPassword = cur, newPassword = next }); return (r.Status, r.Raw); }
        Assert.Equal(400, (await Change("wrong-current-99", "Brand-New-Pass-77")).Status);
        Assert.Equal(400, (await Change(TestUsers.Password, "short1")).Status);
        Assert.Equal(400, (await Change(TestUsers.Password, "onlyletterspassword")).Status);
        Assert.Equal(400, (await Change(TestUsers.Password, TestUsers.Password)).Status);
        var ok = await Change(TestUsers.Password, "Brand-New-Pass-77");
        Assert.Equal(200, ok.Status);
        Assert.DoesNotContain("Brand-New-Pass-77", ok.Raw);
        Assert.Equal(401, await f.CreateClient().WithClientHeader().LoginAsync("op1", TestUsers.Password));
        Assert.Equal(200, await f.CreateClient().WithClientHeader().LoginAsync("op1", "Brand-New-Pass-77"));
        var stored = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => u.PasswordHash).SingleAsync());
        Assert.DoesNotContain("Brand-New-Pass-77", stored);
    }

    [Fact]
    public async Task One_operator_cannot_change_another_accounts_password_or_see_it()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op1 = await As(f, "op1");
        var op2Id = await IdOf(h, "op2");
        Assert.Equal(403, (await op1.SendJsonAsync(HttpMethod.Post, $"/api/users/{op2Id}/reset-password", new { newPassword = "Hijacked-Pass-1" })).Status);
        Assert.Equal(200, await f.CreateClient().WithClientHeader().LoginAsync("op2", TestUsers.Password));
        Assert.Equal(403, (await op1.SendJsonAsync(HttpMethod.Get, "/api/users")).Status);
    }
}
