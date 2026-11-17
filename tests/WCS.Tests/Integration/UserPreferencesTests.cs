using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>Personal interface preferences (theme, sidebar): stored per account, owned by the user, independent between accounts.</summary>
[Collection("Database")]
public class UserPreferencesTests(DatabaseFixture fixture)
{
    private async Task<(Harness H, DbApiFactory F)> ArrangeAsync()
    {
        var h = await Harness.CreateAsync(fixture);
        var f = new DbApiFactory(fixture);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
        await db.SaveChangesAsync();
        return (h, f);
    }

    private static async Task<HttpClient> As(DbApiFactory f, string user)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static async Task<(string Theme, bool Collapsed)> Get(HttpClient c)
    {
        var r = await c.SendJsonAsync(HttpMethod.Get, "/api/auth/preferences");
        Assert.Equal(200, r.Status);
        var d = r.Root.GetProperty("data");
        return (d.GetProperty("theme").GetString()!, d.GetProperty("sidebarCollapsed").GetBoolean());
    }

    [Fact]
    public async Task A_user_without_stored_preferences_gets_light_and_expanded()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        Assert.Equal(("light", false), await Get(await As(f, "admin1")));
        Assert.Equal(("light", false), await Get(await As(f, "op1")));
    }

    [Fact]
    public async Task Each_account_keeps_its_own_preferences_across_logins()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true })).Status);
        var op = await As(f, "op1");
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "light", sidebarCollapsed = false })).Status);

        // sign out and back in: every account gets its own choice back, from the server
        var admin2 = await As(f, "admin1"); var op2 = await As(f, "op1");
        Assert.Equal(("dark", true), await Get(admin2));
        Assert.Equal(("light", false), await Get(op2));
        var stored = await h.Db(db => db.Users.Where(u => u.Username == "admin1").Select(u => new { u.Theme, u.SidebarCollapsed }).SingleAsync());
        Assert.Equal(("dark", true), (stored.Theme, stored.SidebarCollapsed));
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("")]
    [InlineData("DARK")]
    public async Task An_invalid_theme_is_rejected_and_nothing_changes(string theme)
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        var r = await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme, sidebarCollapsed = true });
        Assert.Equal(400, r.Status);
        Assert.Equal(("light", false), await Get(op));
    }

    [Fact]
    public async Task Preferences_require_a_signed_in_user()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var anonymous = f.CreateClient().WithClientHeader();
        Assert.Equal(401, (await anonymous.SendJsonAsync(HttpMethod.Get, "/api/auth/preferences")).Status);
        Assert.Equal(401, (await anonymous.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true })).Status);
    }

    [Fact]
    public async Task The_request_cannot_choose_whose_preferences_change()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var adminId = await h.Db(db => db.Users.Where(u => u.Username == "admin1").Select(u => u.Id).SingleAsync());
        var op = await As(f, "op1");
        // a forged user id in the body is ignored: only the signed-in account (op1) changes
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true, userId = adminId, username = "admin1" })).Status);
        Assert.Equal(("light", false), await Get(await As(f, "admin1")));
        Assert.Equal(("dark", true), await Get(await As(f, "op1")));
        // and there is no route that addresses another user
        Assert.NotEqual(200, (await op.SendJsonAsync(HttpMethod.Put, $"/api/auth/preferences/{adminId}", new { theme = "dark", sidebarCollapsed = true })).Status);
    }

    [Fact]
    public async Task An_administrator_editing_a_profile_does_not_touch_the_users_preferences()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var op = await As(f, "op1");
        await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true });
        var opId = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => u.Id).SingleAsync());

        var admin = await As(f, "admin1");   // the administrator's own preferences stay light/expanded
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, $"/api/users/{opId}", new { displayName = "Operator renamed", role = "Operator", isActive = true })).Status);
        Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, $"/api/users/{opId}/reset-password", new { newPassword = "ResetPass2026x" })).Status);

        var row = await h.Db(db => db.Users.Where(u => u.Id == opId).Select(u => new { u.DisplayName, u.Theme, u.SidebarCollapsed }).SingleAsync());
        Assert.Equal("Operator renamed", row.DisplayName);
        Assert.Equal(("dark", true), (row.Theme, row.SidebarCollapsed));
    }

    [Fact]
    public async Task Changing_preferences_neither_ends_the_session_nor_changes_the_security_stamp()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var stampBefore = await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => u.SecurityStamp).SingleAsync());
        var op = await As(f, "op1");
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Put, "/api/auth/preferences", new { theme = "dark", sidebarCollapsed = true })).Status);
        Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/auth/me")).Status);
        Assert.Equal(stampBefore, await h.Db(db => db.Users.Where(u => u.Username == "op1").Select(u => u.SecurityStamp).SingleAsync()));
    }
}
