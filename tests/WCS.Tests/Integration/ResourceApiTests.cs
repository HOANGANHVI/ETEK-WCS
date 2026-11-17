using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Monitoring;
using WCS.Domain.Users;
using WCS.Infrastructure.Monitoring;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>GET /api/system/resources (Admin only) and the extended /api/system/health (application health only).</summary>
[Collection("Database")]
public class ResourceApiTests(DatabaseFixture fixture)
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

    [Fact]
    public async Task Only_an_administrator_reads_the_server_resources()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        Assert.Equal(401, (await f.CreateClient().WithClientHeader().SendJsonAsync(HttpMethod.Get, "/api/system/resources")).Status);
        Assert.Equal(403, (await (await As(f, "op1")).SendJsonAsync(HttpMethod.Get, "/api/system/resources")).Status);
        Assert.Equal(200, (await (await As(f, "admin1")).SendJsonAsync(HttpMethod.Get, "/api/system/resources")).Status);
    }

    [Fact]
    public async Task The_snapshot_separates_container_and_host_metrics_and_every_metric_carries_unit_source_status_and_time()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        f.Services.GetRequiredService<ResourceMonitor>().Tick();
        var admin = await As(f, "admin1");
        var d = (await admin.SendJsonAsync(HttpMethod.Get, "/api/system/resources")).Root.GetProperty("data");
        foreach (var group in new[] { "container", "host" })
            foreach (var metric in d.GetProperty(group).EnumerateObject())
            {
                var m = metric.Value;
                Assert.True(m.TryGetProperty("unit", out _) && m.TryGetProperty("source", out _) && m.TryGetProperty("status", out _) && m.TryGetProperty("lastUpdatedAt", out _), $"{group}.{metric.Name}");
                if (m.GetProperty("status").GetString() == "Unavailable") { Assert.Equal(JsonValueKind.Null, m.GetProperty("value").ValueKind); Assert.False(string.IsNullOrEmpty(m.GetProperty("reason").GetString()), $"{group}.{metric.Name} needs a reason"); }
                else Assert.True(m.GetProperty("value").ValueKind == JsonValueKind.Number);
            }
        Assert.All(d.GetProperty("container").EnumerateObject(), x => Assert.Equal("Container", x.Value.GetProperty("source").GetString()));
        Assert.All(d.GetProperty("host").EnumerateObject(), x => Assert.NotEqual("Container", x.Value.GetProperty("source").GetString()));     // the container is never presented as the host
        Assert.True(d.GetProperty("intervalSeconds").GetInt32() >= 2);
        Assert.True(d.TryGetProperty("environment", out var env) && env.TryGetProperty("hostScope", out _));
        Assert.Equal(JsonValueKind.Array, d.GetProperty("history").ValueKind);
        // The test process is not in a container, so the container metrics must be unavailable with that reason (not host numbers dressed up as container numbers)
        if (!MonitoringSupportProbe.InContainer())
            Assert.All(d.GetProperty("container").EnumerateObject(), x => Assert.Equal("NOT_IN_CONTAINER", x.Value.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Health_reports_application_state_only_and_gains_backend_uptime_and_database_latency()
    {
        var (h, f) = await ArrangeAsync(); await using var _h = h; await using var _f = f;
        var admin = await As(f, "admin1");
        var d = (await admin.SendJsonAsync(HttpMethod.Get, "/api/system/health")).Root.GetProperty("data");
        Assert.True(d.GetProperty("backend").GetProperty("uptimeSeconds").GetDouble() >= 0);
        Assert.True(d.GetProperty("database").GetProperty("connected").GetBoolean());
        Assert.True(d.GetProperty("database").GetProperty("latencyMs").GetDouble() >= 0);
        Assert.True(d.TryGetProperty("integration", out _) && d.TryGetProperty("scheduler", out _));      // the existing fields are unchanged
        var raw = d.GetRawText().ToLowerInvariant();
        Assert.DoesNotContain("cpu", raw); Assert.DoesNotContain("memory", raw);                            // resources live in their own endpoint
        // an Operator still reads health (the header and dashboard use it)
        Assert.Equal(200, (await (await As(f, "op1")).SendJsonAsync(HttpMethod.Get, "/api/system/health")).Status);
    }
}

internal static class MonitoringSupportProbe
{
    public static bool InContainer() => string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) || File.Exists("/.dockerenv");
}
