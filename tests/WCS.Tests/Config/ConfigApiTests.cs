using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Tests.Support;

namespace WCS.Tests.Config;

/// <summary>HTTP-level checks of the configuration API. Persistence, secret protection and the probe are replaced by in-memory fakes (no PostgreSQL, no network).</summary>
public sealed class ConfigApiFactory : WebApplicationFactory<Program>
{
    public FakeConnectionRepo Repo { get; } = new();
    public RecordingProbe Probe { get; } = new();
    public InMemoryUserRepository Users { get; } = new();
    public RecordingAuditLog Audit { get; } = new();
    public FakeTimeProvider Time { get; } = new();

    public ConfigApiFactory()
    {
        Users.Users.Add(TestUsers.Make("admin1", WCS.Domain.Users.UserRole.Admin));
        Users.Users.Add(TestUsers.Make("admin2", WCS.Domain.Users.UserRole.Admin));
        Users.Users.Add(TestUsers.Make("op1", WCS.Domain.Users.UserRole.Operator));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Password"] = "not-a-real-password",   // only so the DbContext can be constructed; it is never connected in these tests
            ["Runtime:EnableWorkers"] = "false",
        }));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IHostedService>();                    // no seeding, no scheduler
            s.RemoveAll<IUnitOfWork>(); s.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
            s.RemoveAll<IConnectionSettingRepository>(); s.AddSingleton<IConnectionSettingRepository>(Repo);
            s.RemoveAll<ISecretProtector>(); s.AddSingleton<ISecretProtector, FakeProtector>();
            s.RemoveAll<IConnectionProbe>(); s.AddSingleton<IConnectionProbe>(Probe);
            s.RemoveAll<WCS.Application.Auth.IUserRepository>(); s.AddSingleton<WCS.Application.Auth.IUserRepository>(Users);
            s.RemoveAll<WCS.Application.Auth.IAuditLog>(); s.AddSingleton<WCS.Application.Auth.IAuditLog>(Audit);
            s.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(WCS.Api.Api.AuthSetup.Scheme, o => o.TimeProvider = Time);
        });
    }
}

public class ConfigApiTests(ConfigApiFactory factory) : IClassFixture<ConfigApiFactory>
{
    private const string Secret = "Api-S3cret-Value-42";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<HttpClient> Admin(ConfigApiFactory f)
    {
        var c = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await c.LoginAsync("admin1"));
        return c;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Root, string Raw)> Call(HttpClient c, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: Web);
        var res = await c.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, JsonDocument.Parse(raw).RootElement, raw);
    }

    private static object ValidPlc(bool enabled = true, string ip = "192.0.2.10") => new
    {
        enabled, deviceName = "PLC-1", ipAddress = ip, port = 5007, protocol = ConnectionOptions.PlcMcProtocol3EBinaryTcp,
        networkNumber = 0, pcNumber = 255, stationNumber = 0, timeoutMs = 3000, pollingIntervalMs = 500, updatedBy = "api-test",
    };

    [Fact]
    public async Task Get_returns_an_empty_unconfigured_view_and_the_unified_envelope()
    {
        var c = await Admin(factory);
        var (status, root, _) = await Call(c, HttpMethod.Get, "/api/config/rcs");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(root.GetProperty("success").GetBoolean());
        var data = root.GetProperty("data");
        Assert.False(data.GetProperty("configured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("settings").GetProperty("host").ValueKind); // nothing pre-filled
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Put_then_get_round_trips_and_never_returns_the_secret()
    {
        var c = await Admin(factory);
        var body = new
        {
            enabled = true, systemName = "RCS", host = "rcs.example.test", port = 8182, baseUrl = (string?)null, authMode = ConnectionOptions.AuthUserPassword,
            username = "svc", timeoutMs = 5000, password = Secret, clearPassword = false, token = (string?)null, clearToken = false, updatedBy = "api-test",
        };

        var put = await Call(c, HttpMethod.Put, "/api/config/rcs", body);
        var get = await Call(c, HttpMethod.Get, "/api/config/rcs");

        Assert.Equal(HttpStatusCode.OK, put.Status);
        foreach (var raw in new[] { put.Raw, get.Raw })
        {
            Assert.DoesNotContain(Secret, raw);
            Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"token\"", raw, StringComparison.OrdinalIgnoreCase);
        }
        var data = get.Root.GetProperty("data");
        Assert.True(data.GetProperty("secrets").GetProperty("hasPassword").GetBoolean());
        Assert.Equal("rcs.example.test", data.GetProperty("settings").GetProperty("host").GetString());
        Assert.DoesNotContain(Secret, factory.Repo.Rows[WCS.Domain.Common.ConnectionKind.Rcs].ProtectedSecrets);
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_per_field_details_and_does_not_save()
    {
        var c = await Admin(factory);
        var bad = new { enabled = true, ipAddress = "300.1.1.1", port = 70000, protocol = "Modbus", timeoutMs = 5 };
        var (status, root, _) = await Call(c, HttpMethod.Put, "/api/config/plc", bad);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_FAILED", root.GetProperty("error").GetProperty("code").GetString());
        var fields = root.GetProperty("error").GetProperty("details").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("ipAddress", fields); Assert.Contains("port", fields); Assert.Contains("protocol", fields); Assert.Contains("timeoutMs", fields);
        Assert.False(factory.Repo.Rows.ContainsKey(WCS.Domain.Common.ConnectionKind.Plc));
    }

    [Fact]
    public async Task Check_without_saved_config_reports_not_executed_and_never_calls_the_probe()
    {
        var f = new ConfigApiFactory();
        var (status, root, _) = await Call(await Admin(f), HttpMethod.Post, "/api/config/plc/test");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("NotExecuted", root.GetProperty("data").GetProperty("outcome").GetString());
        Assert.Empty(f.Probe.Calls);
    }

    [Fact]
    public async Task Check_runs_only_after_saving_and_reports_the_simulated_mode()
    {
        var f = new ConfigApiFactory();
        var c = await Admin(f);
        await Call(c, HttpMethod.Put, "/api/config/plc", ValidPlc());

        var (_, root, _) = await Call(c, HttpMethod.Post, "/api/config/plc/test");
        var data = root.GetProperty("data");
        Assert.Equal("Simulated", data.GetProperty("outcome").GetString());
        Assert.Equal("Simulated", data.GetProperty("mode").GetString());
        Assert.Single(f.Probe.Calls);

        var view = (await Call(c, HttpMethod.Get, "/api/config/plc")).Root.GetProperty("data");
        Assert.Equal("Simulated", view.GetProperty("lastTest").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task The_production_default_probe_is_the_simulated_one()
    {
        // Without Integration:AllowNetworkProbe the real container must wire the probe that never touches the network.
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Password"] = "x", ["Runtime:EnableWorkers"] = "false" }));
            b.ConfigureServices(s => s.RemoveAll<IHostedService>());
        });
        using var scope = f.Services.CreateScope();
        Assert.IsType<WCS.Infrastructure.Config.SimulatedConnectionProbe>(scope.ServiceProvider.GetRequiredService<IConnectionProbe>());
    }
}
