using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WCS.Api.Api;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>CR-006: cookie policy flag, anonymous /healthz, Production start-up guard against the PLC simulator.</summary>
[Collection("Database")]
public class Cr006DeploymentTests(DatabaseFixture fixture)
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Cfg(params (string k, string v)[] kv) => new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(x => x.k, x => (string?)x.v)).Build();

    [Theory]
    [InlineData(null, false, CookieSecurePolicy.Always)]                 // unchanged default outside Development
    [InlineData(null, true, CookieSecurePolicy.SameAsRequest)]           // unchanged default in Development
    [InlineData("Always", true, CookieSecurePolicy.Always)]
    [InlineData("SameAsRequest", false, CookieSecurePolicy.SameAsRequest)]
    [InlineData(" sameasrequest ", false, CookieSecurePolicy.SameAsRequest)]
    public void Cookie_secure_policy_resolves_and_keeps_the_secure_default(string? configured, bool dev, CookieSecurePolicy expected) =>
        Assert.Equal(expected, AuthSetup.ResolveSecurePolicy(configured, dev));

    [Theory]
    [InlineData("None")]
    [InlineData("false")]
    [InlineData("insecure")]
    public void Cookie_secure_policy_rejects_values_that_would_disable_it(string configured) =>
        Assert.Throws<InvalidOperationException>(() => AuthSetup.ResolveSecurePolicy(configured, false));

    [Fact]
    public void Production_refuses_the_simulator_unless_explicitly_allowed_and_never_with_integration()
    {
        var prod = new Env("Production");
        Assert.Throws<InvalidOperationException>(() => ProductionSafety.Validate(Cfg(), prod));                                      // Plc:Mode unset = Simulator
        Assert.Throws<InvalidOperationException>(() => ProductionSafety.Validate(Cfg(("Plc:Mode", "Simulator")), prod));
        Assert.Throws<InvalidOperationException>(() => ProductionSafety.Validate(Cfg(("Plc:AllowSimulatorInProduction", "false")), prod));
        Assert.True(ProductionSafety.Validate(Cfg(("Plc:AllowSimulatorInProduction", "true")), prod));                               // explicit exception
        Assert.Throws<InvalidOperationException>(() => ProductionSafety.Validate(Cfg(("Plc:AllowSimulatorInProduction", "true"), ("Plc:Integration:Enabled", "true")), prod));
        Assert.False(ProductionSafety.Validate(Cfg(("Plc:Mode", "RealReadOnly")), prod));                                           // real mode: no exception needed
        Assert.False(ProductionSafety.Validate(Cfg(), new Env("Development")));                                                     // other environments untouched
        Assert.False(ProductionSafety.Validate(Cfg(), new Env("Testing")));
    }

    [Fact]
    public async Task Production_host_does_not_start_with_the_simulator_by_default_and_starts_with_the_explicit_exception()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var refused = new DbApiFactory(fixture).WithWebHostBuilder(b => b.UseEnvironment("Production"));
        Assert.ThrowsAny<Exception>(() => refused.CreateClient());
        await refused.DisposeAsync();

        await using var allowed = new DbApiFactory(fixture).WithWebHostBuilder(b => { b.UseEnvironment("Production"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { [ProductionSafety.AllowKey] = "true", [ProductionSafety.FleetAllowKey] = "true" })); });
        var anon = allowed.CreateClient();
        Assert.Equal(200, (int)(await anon.GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public void Forwarded_headers_are_trusted_only_from_the_configured_proxy_and_never_open_to_everybody()
    {
        Assert.Null(ForwardedHeadersSetup.Build(Cfg()));                                                                                      // off by default
        Assert.Null(ForwardedHeadersSetup.Build(Cfg(("Deployment:TrustForwardedHeaders", "false"), ("Deployment:TrustedProxyNetworks:0", "10.0.0.1/32"))));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(Cfg(("Deployment:TrustForwardedHeaders", "true"))));       // enabled without a proxy list: refused
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(Cfg(("Deployment:TrustForwardedHeaders", "true"), ("Deployment:TrustedProxyNetworks:0", "0.0.0.0/0"))));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(Cfg(("Deployment:TrustForwardedHeaders", "true"), ("Deployment:TrustedProxyNetworks:0", "::/0"))));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.Build(Cfg(("Deployment:TrustForwardedHeaders", "true"), ("Deployment:TrustedProxyNetworks:0", "not-an-ip"))));
        var o = ForwardedHeadersSetup.Build(Cfg(("Deployment:TrustForwardedHeaders", "true"), ("Deployment:TrustedProxyNetworks:0", "172.30.0.10/32"), ("Deployment:TrustedProxyNetworks:1", "10.1.1.1")))!;
        Assert.Single(o.KnownIPNetworks); Assert.Single(o.KnownProxies);
        Assert.Equal(1, o.ForwardLimit);
        Assert.Equal(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto, o.ForwardedHeaders);
    }

    [Fact]
    public async Task Healthz_is_anonymous_reveals_nothing_sensitive_and_reports_application_and_database_separately()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await using var f = new DbApiFactory(fixture);
        var res = await f.CreateClient().GetAsync("/healthz");            // no session, no client header
        Assert.Equal(200, (int)res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        Assert.Equal("""{"application":"Up","database":"Up"}""", json);
        Assert.Equal(401, (int)(await f.CreateClient().GetAsync("/api/system/health")).StatusCode);   // the detailed health still needs a session
    }
}
