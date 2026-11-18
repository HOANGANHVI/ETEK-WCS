using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Plc;
using WCS.Tests.Support;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-017: the whole chain  .env -> docker-compose.yml substitution -> container environment variables -> ASP.NET configuration -> PlcRuntimeOptions -> effective mode / missingWriteKeys -> Runtime Info API.
/// The compose file of the repository is parsed and resolved exactly like Docker Compose does (a value set and non-empty wins over the default of <c>${NAME:-default}</c>); the resulting variables are
/// put into the REAL process environment so the real EnvironmentVariables provider reads them. Nothing is sent to a PLC. In the Database collection so no host starts while the variables are set.
/// </summary>
[Collection("Database")]
public class PlcWriteConfigBindingTests(DatabaseFixture fixture)
{
    private const string OldReference = "TEST-OLD-REF-0001";      // a made-up value of the REMOVED PLC_WRITE_APPROVAL_REFERENCE key (CR-019)

    private static string ComposePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deployment", "docker-compose.yml"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "deployment", "docker-compose.yml");
    }

    /// <summary>The Plc__* variables of the backend service, resolved against a simulated .env.</summary>
    private static Dictionary<string, string> ContainerPlcEnvironment(IReadOnlyDictionary<string, string> dotEnv)
    {
        var result = new Dictionary<string, string>();
        var inBackend = false;
        foreach (var line in File.ReadAllLines(ComposePath()))
        {
            if (Regex.IsMatch(line, @"^  backend:\s*$")) { inBackend = true; continue; }
            if (inBackend && Regex.IsMatch(line, @"^  [A-Za-z]")) break;                      // next service
            if (!inBackend) continue;
            var m = Regex.Match(line, @"^\s{6}(Plc__[A-Za-z_]+):\s*(.*?)\s*$");
            if (!m.Success) continue;
            var value = Regex.Replace(m.Groups[2].Value, @"\$\{([A-Z0-9_]+)(?::-([^}]*))?\}", x =>
                dotEnv.TryGetValue(x.Groups[1].Value, out var v) && v.Length > 0 ? v : x.Groups[2].Value);          // ${NAME:-default}: unset OR empty -> default
            result[m.Groups[1].Value] = value.Trim('"');
        }
        return result;
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _saved = [];
        public EnvScope(IReadOnlyDictionary<string, string> vars)
        {
            foreach (var kv in vars) { _saved[kv.Key] = Environment.GetEnvironmentVariable(kv.Key); Environment.SetEnvironmentVariable(kv.Key, kv.Value); }
        }
        public void Dispose() { foreach (var kv in _saved) Environment.SetEnvironmentVariable(kv.Key, kv.Value); }
    }

    private static PlcRuntimeOptions Bind() =>
        new ConfigurationBuilder().AddEnvironmentVariables().Build().GetSection("Plc").Get<PlcRuntimeOptions>() ?? new PlcRuntimeOptions();

    private static Dictionary<string, string> AllKeys() => new()
    {
        ["PLC_ALLOW_LIVE_CONNECTION"] = "true", ["PLC_WRITE_ENABLED"] = "true",
    };

    [Fact]
    public void The_compose_file_passes_every_write_condition_under_the_name_the_backend_binds()
    {
        var env = ContainerPlcEnvironment(AllKeys());
        foreach (var name in new[] { "Plc__Mode", "Plc__AllowLiveConnection", "Plc__Write__Enabled" })
            Assert.True(env.ContainsKey(name), $"docker-compose.yml does not pass {name} to the backend");
        using var _ = new EnvScope(env);
        var o = Bind();                                                                            // the real EnvironmentVariables provider and binder
        Assert.Equal((PlcRuntimeMode.RealReadWrite, true, true), (o.Mode, o.AllowLiveConnection, o.Write.Enabled));
        Assert.DoesNotContain(env.Keys, k => k.Contains("EngineerApproval") || k.Contains("HandshakeApproved"));      // CR-019: the removed keys are not passed any more
        Assert.Equal(PlcRuntimeMode.RealReadWrite, WritePolicy.EffectiveMode(o));
        Assert.Empty(WritePolicy.MissingWriteKeys(o));
    }

    [Theory]
    [InlineData("PLC_ALLOW_LIVE_CONNECTION", "PLC_ALLOW_LIVE_CONNECTION")]
    [InlineData("PLC_WRITE_ENABLED", "PLC_WRITE_ENABLED")]
    public void Each_missing_key_alone_keeps_the_runtime_read_only_and_is_the_only_one_reported(string absent, string expectedMissing)
    {
        var dotEnv = AllKeys(); dotEnv.Remove(absent);
        using var _ = new EnvScope(ContainerPlcEnvironment(dotEnv));
        var o = Bind();
        Assert.Equal(PlcRuntimeMode.RealReadOnly, WritePolicy.EffectiveMode(o));
        Assert.Equal([expectedMissing], WritePolicy.MissingWriteKeys(o));
    }

    [Theory]
    [InlineData("false")] [InlineData("")] [InlineData("False")]
    public void A_key_that_is_false_or_empty_counts_as_missing(string value)
    {
        var dotEnv = AllKeys(); dotEnv["PLC_WRITE_ENABLED"] = value;
        using var _ = new EnvScope(ContainerPlcEnvironment(dotEnv));
        Assert.Equal(["PLC_WRITE_ENABLED"], WritePolicy.MissingWriteKeys(Bind()));
    }

    [Theory]
    [InlineData("True")] [InlineData("TRUE")] [InlineData("true")]
    public void The_boolean_spelling_of_a_Windows_edited_env_file_is_accepted(string value)
    {
        var dotEnv = AllKeys(); dotEnv["PLC_WRITE_ENABLED"] = value; dotEnv["PLC_ALLOW_LIVE_CONNECTION"] = value;
        using var _ = new EnvScope(ContainerPlcEnvironment(dotEnv));
        Assert.Empty(WritePolicy.MissingWriteKeys(Bind()));
    }

    [Fact]
    public void An_old_env_file_with_PLC_MODE_RealReadOnly_keeps_the_runtime_read_only_even_with_every_key_and_the_mode_is_what_is_reported()
    {
        var dotEnv = AllKeys(); dotEnv["PLC_MODE"] = "RealReadOnly";                               // what the earlier README told the operator to set
        using var _ = new EnvScope(ContainerPlcEnvironment(dotEnv));
        var o = Bind();
        Assert.Equal(PlcRuntimeMode.RealReadOnly, o.Mode);
        Assert.Equal(PlcRuntimeMode.RealReadOnly, WritePolicy.EffectiveMode(o));
        Assert.Equal(["PLC_MODE"], WritePolicy.MissingWriteKeys(o));
    }

    [Fact]
    public void An_env_file_without_any_write_setting_reports_the_two_keys_and_not_the_mode()
    {
        using var _ = new EnvScope(ContainerPlcEnvironment(new Dictionary<string, string>()));        // the defaults of docker-compose.yml only
        var o = Bind();
        Assert.Equal(PlcRuntimeMode.RealReadWrite, o.Mode);                                          // CR-015 default
        Assert.False(o.AllowLiveConnection); Assert.False(o.Write.Enabled);
        Assert.Equal(PlcRuntimeMode.RealReadOnly, WritePolicy.EffectiveMode(o));
        Assert.Equal(["PLC_ALLOW_LIVE_CONNECTION", "PLC_WRITE_ENABLED"], WritePolicy.MissingWriteKeys(o));
    }

    [Theory]
    [InlineData("")] [InlineData("false")] [InlineData(OldReference)]
    public void The_removed_approval_reference_and_handshake_keys_are_ignored_whatever_their_value(string oldValue)
    {
        // an old .env still containing the two removed lines (CR-019): they are not even passed by compose, and if set as raw variables they are ignored by binding
        var dotEnv = AllKeys(); dotEnv["PLC_WRITE_APPROVAL_REFERENCE"] = oldValue; dotEnv["PLC_WRITE_HANDSHAKE_APPROVED"] = oldValue;
        var vars = ContainerPlcEnvironment(dotEnv);
        vars["Plc__Write__EngineerApprovalReference"] = oldValue; vars["Plc__Write__HandshakeApproved"] = oldValue;
        using var _ = new EnvScope(vars);
        var o = Bind();                                                                            // must not throw
        Assert.Equal(PlcRuntimeMode.RealReadWrite, WritePolicy.EffectiveMode(o));
        Assert.Empty(WritePolicy.MissingWriteKeys(o));
    }

    // ---------------------------------------------------------------- through a real host: environment -> configuration -> runtime -> Runtime Info API
    private async Task<(System.Text.Json.JsonElement Runtime, string Raw)> RuntimeInfoAsync(IReadOnlyDictionary<string, string> dotEnv)
    {
        var h = await Harness.CreateAsync(fixture);
        await using var _h = h;
        var f0 = new DbApiFactory(fixture);
        using (var scope = f0.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
            db.Users.Add(TestUsers.Make("admin1", UserRole.Admin)); await db.SaveChangesAsync();
        }
        await f0.DisposeAsync();
        using var env = new EnvScope(ContainerPlcEnvironment(dotEnv));
        using var f = new DbApiFactory(fixture).WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Plc:RuntimeEnabled"] = "false" })));   // no workers: nothing connects
        var admin = f.CreateClient().WithClientHeader();
        Assert.Equal(200, await admin.LoginAsync("admin1"));
        var r = await admin.SendJsonAsync(HttpMethod.Get, "/api/plc/overview");
        Assert.Equal(200, r.Status);
        return (r.Root.GetProperty("data").GetProperty("runtime").Clone(), r.Raw);
    }

    [Fact]
    public async Task The_Runtime_Info_API_reports_what_the_backend_really_received_without_the_removed_keys()
    {
        var (all, rawAll) = await RuntimeInfoAsync(AllKeys());
        Assert.Equal("RealReadWrite", all.GetProperty("effectiveMode").GetString());
        Assert.True(all.GetProperty("allowLiveConnection").GetBoolean() && all.GetProperty("writeEnabled").GetBoolean());
        Assert.Equal(0, all.GetProperty("missingWriteKeys").GetArrayLength());
        Assert.DoesNotContain("writeApprovalReferenceSet", rawAll); Assert.DoesNotContain("writeHandshakeApproved", rawAll);

        var partial = AllKeys(); partial.Remove("PLC_WRITE_ENABLED");
        var (some, _) = await RuntimeInfoAsync(partial);
        Assert.Equal("RealReadOnly", some.GetProperty("effectiveMode").GetString());
        Assert.True(some.GetProperty("allowLiveConnection").GetBoolean());                              // what the backend DID receive is not reported as missing
        Assert.Equal(["PLC_WRITE_ENABLED"], some.GetProperty("missingWriteKeys").EnumerateArray().Select(x => x.GetString()).ToArray());

        var (none, _) = await RuntimeInfoAsync(new Dictionary<string, string>());
        Assert.Equal("RealReadWrite", none.GetProperty("mode").GetString());
        Assert.Equal(2, none.GetProperty("missingWriteKeys").GetArrayLength());
    }
}
