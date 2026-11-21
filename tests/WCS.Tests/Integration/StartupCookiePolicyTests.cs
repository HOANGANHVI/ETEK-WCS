using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WCS.Api.Api;
using WCS.Api.Logging;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// Regression of the start-up failure "String 'Always' was not recognized as a valid Boolean": the start-up logging read Auth:CookieSecure as a boolean while the valid values are the TEXT Always | SameAsRequest.
/// Logging now uses the resolver authentication uses; these tests pin that, and that the cookie policy itself is unchanged.
/// </summary>
[Collection("Database")]
public class StartupCookiePolicyTests(DatabaseFixture fixture)
{
    private sealed class Capture : ILoggerProvider
    {
        public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Props)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new L(this);
        public void Dispose() { }
        private sealed class L(Capture o) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f)
            {
                var d = new Dictionary<string, object?>();
                if (state is IEnumerable<KeyValuePair<string, object?>> kv) foreach (var p in kv) d[p.Key] = p.Value;
                lock (o.Entries) o.Entries.Add((level, f(state, ex), d));
            }
        }
    }

    private static (WebApplication App, Capture Log) Host(string environment, string? cookieSecure)
    {
        var cap = new Capture();
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        if (cookieSecure is not null) b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:CookieSecure"] = cookieSecure });
        b.Logging.ClearProviders(); b.Logging.AddProvider(cap);
        return (b.Build(), cap);
    }

    private static string SummaryPolicy(Capture cap) => cap.Entries.Single(e => Equals(e.Props.GetValueOrDefault("EventCode"), "STARTUP_SUMMARY")).Props["CookieSecure"]!.ToString()!;

    [Theory]
    [InlineData("Production", "Always", CookieSecurePolicy.Always)]
    [InlineData("Production", "SameAsRequest", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", " sameasrequest ", CookieSecurePolicy.SameAsRequest)]    // the resolver trims and ignores case; logging must agree
    [InlineData("Production", null, CookieSecurePolicy.Always)]                         // unset: the current secure default
    [InlineData("Development", null, CookieSecurePolicy.SameAsRequest)]                 // unset: the current development default
    [InlineData("Development", "Always", CookieSecurePolicy.Always)]
    public void Start_up_logging_accepts_every_valid_value_and_reports_the_policy_authentication_resolves(string environment, string? configured, CookieSecurePolicy expected)
    {
        var (app, cap) = Host(environment, configured);
        using var _ = app;
        LogSetup.LogStartupSummary(app);                                               // before the fix: FormatException for "Always"
        var logged = SummaryPolicy(cap);
        Assert.Equal(expected.ToString(), logged);
        Assert.Equal(AuthSetup.ResolveSecurePolicy(configured, environment == "Development"), Enum.Parse<CookieSecurePolicy>(logged));   // one resolver, one answer
    }

    [Theory]
    [InlineData("None")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("Sometimes")]
    public void An_invalid_value_fails_start_up_logging_exactly_like_authentication_and_is_never_turned_into_a_weaker_policy(string configured)
    {
        var (app, cap) = Host("Production", configured);
        using var _ = app;
        var fromAuth = Assert.Throws<InvalidOperationException>(() => AuthSetup.ResolveSecurePolicy(configured, false));
        var fromLogging = Assert.Throws<InvalidOperationException>(() => LogSetup.LogStartupSummary(app));          // not a FormatException, and not a silent fallback
        Assert.Equal(fromAuth.Message, fromLogging.Message);
        Assert.DoesNotContain(cap.Entries, e => Equals(e.Props.GetValueOrDefault("EventCode"), "STARTUP_SUMMARY"));      // nothing was logged as if the start-up were fine
    }

    // ---- the real host (the one the deployment runs): env var Auth__CookieSecure, as docker-compose sets it from WCS_COOKIE_SECURE
    private async Task<(string? SetCookie, string StartupPolicy)> LoginAsync(string cookieSecure, string scheme)
    {
        var prev = Environment.GetEnvironmentVariable("Auth__CookieSecure");
        Environment.SetEnvironmentVariable("Auth__CookieSecure", cookieSecure);
        try
        {
            var f0 = new DbApiFactory(fixture);
            using (var scope = f0.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
                if (!db.Users.Any(u => u.Username == "admin1")) { db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); await db.SaveChangesAsync(); }
            }
            await f0.DisposeAsync();
            using var f = new DbApiFactory(fixture).WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Rcs:Fleet:Source"] = "Real", ["Plc:RuntimeEnabled"] = "false", ["Plc:AllowSimulatorInProduction"] = "true", ["Rcs:AllowSimulatorInProduction"] = "true" })); });
            var client = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(scheme + "://localhost") }).WithClientHeader();   // building the host runs the start-up summary
            var res = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin1", password = TestUsers.Password });
            Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
            res.Headers.TryGetValues("Set-Cookie", out var cookies);
            return (cookies is null ? null : string.Join(" | ", cookies), AuthSetup.ResolveSecurePolicy(cookieSecure, false).ToString());
        }
        finally { Environment.SetEnvironmentVariable("Auth__CookieSecure", prev); }
    }

    [Fact]
    public async Task The_real_host_starts_with_WCS_COOKIE_SECURE_Always_and_the_session_cookie_stays_Secure()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var (cookie, policy) = await LoginAsync("Always", "https");
        Assert.Equal("Always", policy);
        Assert.NotNull(cookie); Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);                                 // the rest of the cookie policy is untouched
    }

    [Fact]
    public async Task The_real_host_starts_with_WCS_COOKIE_SECURE_SameAsRequest_and_keeps_that_policy()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var (cookieHttp, policy) = await LoginAsync("SameAsRequest", "http");
        Assert.Equal("SameAsRequest", policy);
        Assert.NotNull(cookieHttp); Assert.DoesNotContain("secure;", cookieHttp + ";", StringComparison.OrdinalIgnoreCase);   // plain HTTP request: no Secure flag (SameAsRequest)
        var (cookieHttps, _) = await LoginAsync("SameAsRequest", "https");
        Assert.Contains("secure", cookieHttps!, StringComparison.OrdinalIgnoreCase);                              // HTTPS request: Secure
    }
}
