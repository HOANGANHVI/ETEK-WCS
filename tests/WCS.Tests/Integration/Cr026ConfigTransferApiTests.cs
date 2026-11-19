using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WCS.Application.ConfigTransfer;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-026 adjustment: Config Transfer is ON by default and only an Administrator may use any of it - checked on the real HTTP API, not on the menu.</summary>
[Collection("Database")]
public class Cr026ConfigTransferApiTests(DatabaseFixture fixture)
{
    /// <summary>The application exactly as deployed: NO ConfigTransfer:Enabled setting at all. Only the persistent folder is given (the Docker deployment sets /config-backups).</summary>
    private sealed class Factory(DatabaseFixture db, string? backupPath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WcsDatabase"] = db.ConnectionString, ["Database:Password"] = null, ["Logging:SystemLog:Enabled"] = "false",
                ["ConfigTransfer:BackupPath"] = backupPath, ["ConfigTransfer:MinFreeMb"] = "1",
            }));
            builder.ConfigureServices(s => s.RemoveAll<IHostedService>());
        }
    }

    private async Task<(Harness H, Factory F, string Dir)> ArrangeAsync(bool withBackupPath = true)
    {
        var h = await Harness.CreateAsync(fixture);
        var dir = Path.Combine(AppContext.BaseDirectory, "cfgapi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var f = new Factory(fixture, withBackupPath ? dir : null);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
        db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); db.Users.Add(TestUsers.Make("op1", UserRole.Operator));
        await db.SaveChangesAsync();
        return (h, f, dir);
    }

    private static async Task<HttpClient> As(Factory f, string? user)
    {
        var c = f.CreateClient().WithClientHeader();
        if (user is not null) Assert.Equal(200, await c.LoginAsync(user));
        return c;
    }

    private static MultipartFormDataContent Upload(byte[] zip) => new() { { new ByteArrayContent(zip), "file", "package.zip" } };

    [Fact]
    public async Task The_module_is_on_by_default_without_any_setting()
    {
        Assert.True(new ConfigTransferOptions().Enabled);
        // the deployed composition (no ConfigTransfer:Enabled anywhere) resolves to Enabled = true
        var (h, f, dir) = await ArrangeAsync();
        try { Assert.True(f.Services.GetRequiredService<IOptions<ConfigTransferOptions>>().Value.Enabled); }
        finally { f.Dispose(); await h.DisposeAsync(); Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Admin_uses_every_endpoint_and_gets_no_not_enabled_answer()
    {
        var (h, f, dir) = await ArrangeAsync(); await using var _h = h; using var _f = f;
        try
        {
            var admin = await As(f, "admin1");
            var status = await admin.SendJsonAsync(HttpMethod.Get, "/api/config-transfer/status");
            Assert.Equal(200, status.Status);
            Assert.True(status.Root.GetProperty("data").GetProperty("enabled").GetBoolean());
            Assert.True(status.Root.GetProperty("data").GetProperty("backupReady").GetBoolean());
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Get, "/api/config-transfer/groups")).Status);
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Post, "/api/config-transfer/exports/check", new { groups = new[] { "BusinessProcess" } })).Status);

            var export = await admin.PostAsJsonAsync("/api/config-transfer/exports", new { groups = new[] { "Location", "Agv", "PlcStation", "BusinessProcess" } });
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal("application/zip", export.Content.Headers.ContentType?.MediaType);
            var zip = await export.Content.ReadAsByteArrayAsync();

            var up = await admin.PostAsync("/api/config-transfer/imports", Upload(zip));
            Assert.Equal(HttpStatusCode.Created, up.StatusCode);
            var id = (await up.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("data").GetProperty("importId").GetGuid();
            var prev = await admin.SendJsonAsync(HttpMethod.Post, $"/api/config-transfer/imports/{id}/preview", new { });
            Assert.Equal(200, prev.Status);
            var token = prev.Root.GetProperty("data").GetProperty("previewToken").GetString();
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Get, $"/api/config-transfer/imports/{id}")).Status);

            // the safety conditions are unchanged: no Apply without Maintenance
            var noMaint = await admin.SendJsonAsync(HttpMethod.Post, $"/api/config-transfer/imports/{id}/apply", new { previewToken = token, confirmHeaderChanges = true, skipConflicts = false });
            Assert.Equal(422, noMaint.Status);
            Assert.Equal("MAINTENANCE_REQUIRED", noMaint.Root.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(200, (await admin.SendJsonAsync(HttpMethod.Put, "/api/system/maintenance", new { enabled = true, reason = "api test" })).Status);
            var applied = await admin.SendJsonAsync(HttpMethod.Post, $"/api/config-transfer/imports/{id}/apply", new { previewToken = token, confirmHeaderChanges = true, skipConflicts = false });
            Assert.Equal(200, applied.Status);
            Assert.True(applied.Root.GetProperty("data").GetProperty("maintenanceStillOn").GetBoolean());
            Assert.True((await admin.SendJsonAsync(HttpMethod.Get, "/api/system/maintenance")).Root.GetProperty("data").GetProperty("enabled").GetBoolean());   // never switched off by the import
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task An_operator_gets_403_on_every_endpoint_including_upload_preview_and_apply_and_an_anonymous_caller_401()
    {
        var (h, f, dir) = await ArrangeAsync(); await using var _h = h; using var _f = f;
        try
        {
            var op = await As(f, "op1"); var anon = await As(f, null); var any = Guid.NewGuid();
            var calls = new (HttpMethod M, string Url, object? Body)[]
            {
                (HttpMethod.Get, "/api/config-transfer/status", null),
                (HttpMethod.Get, "/api/config-transfer/groups", null),
                (HttpMethod.Post, "/api/config-transfer/exports/check", new { groups = new[] { "Location" } }),
                (HttpMethod.Post, "/api/config-transfer/exports", new { groups = new[] { "Location" } }),
                (HttpMethod.Post, $"/api/config-transfer/imports/{any}/preview", new { }),
                (HttpMethod.Post, $"/api/config-transfer/imports/{any}/apply", new { previewToken = "x", confirmHeaderChanges = true, skipConflicts = true }),
                (HttpMethod.Get, $"/api/config-transfer/imports/{any}", null),
                (HttpMethod.Put, "/api/system/maintenance", new { enabled = true, reason = "x" }),
            };
            foreach (var (m, url, body) in calls)
            {
                Assert.Equal(403, (await op.SendJsonAsync(m, url, body)).Status);
                Assert.Equal(401, (await anon.SendJsonAsync(m, url, body)).Status);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsync("/api/config-transfer/imports", Upload([0x50, 0x4B, 3, 4]))).StatusCode);   // upload
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/config-transfer/imports", Upload([0x50, 0x4B, 3, 4]))).StatusCode);
            Assert.Equal(200, (await op.SendJsonAsync(HttpMethod.Get, "/api/system/maintenance")).Status);   // reading the flag (the banner) stays open to every signed-in user
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Without_a_usable_backup_folder_admin_still_reaches_the_api_but_upload_is_refused()
    {
        var (h, f, dir) = await ArrangeAsync(withBackupPath: false); await using var _h = h; using var _f = f;
        try
        {
            var admin = await As(f, "admin1");
            var status = await admin.SendJsonAsync(HttpMethod.Get, "/api/config-transfer/status");
            Assert.Equal(200, status.Status);
            Assert.False(status.Root.GetProperty("data").GetProperty("backupReady").GetBoolean());
            var export = await admin.PostAsJsonAsync("/api/config-transfer/exports", new { groups = new[] { "Location" } });
            var zip = await export.Content.ReadAsByteArrayAsync();
            var up = await admin.PostAsync("/api/config-transfer/imports", Upload(zip));
            Assert.Equal(422, (int)up.StatusCode);
            Assert.Contains("BACKUP_NOT_READY", await up.Content.ReadAsStringAsync());
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
