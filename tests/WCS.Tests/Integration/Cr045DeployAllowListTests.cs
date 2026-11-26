using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using WCS.Application.Plc;
using WCS.Domain.Plc;

namespace WCS.Tests.Integration;

/// <summary>
/// CR-045: the write allow list as one environment value, and the chain .env -> docker-compose.yml -> configuration -> write policy. Pure configuration: nothing is started or connected.
/// </summary>
public class Cr045DeployAllowListTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deployment", "docker-compose.yml"))) dir = dir.Parent;
        return Path.Combine([dir!.FullName, .. parts]);
    }

    /// <summary>The Plc__* variables the backend container receives for a simulated .env, resolved like Docker Compose does.</summary>
    private static Dictionary<string, string?> Container(params (string Name, string Value)[] dotEnv)
    {
        var env = dotEnv.ToDictionary(x => x.Name, x => x.Value);
        var result = new Dictionary<string, string?>();
        var inBackend = false;
        foreach (var line in File.ReadAllLines(RepoFile("deployment", "docker-compose.yml")))
        {
            if (Regex.IsMatch(line, @"^  backend:\s*$")) { inBackend = true; continue; }
            if (inBackend && Regex.IsMatch(line, @"^  [A-Za-z]")) break;
            if (!inBackend || Regex.Match(line, @"^\s{6}(Plc__[A-Za-z_]+):\s*(.*?)\s*$") is not { Success: true } m) continue;
            result[m.Groups[1].Value.Replace("__", ":")] = Regex.Replace(m.Groups[2].Value, @"\$\{([A-Z0-9_]+)(?::-([^}]*))?\}", x =>
                env.TryGetValue(x.Groups[1].Value, out var v) && v.Length > 0 ? v : x.Groups[2].Value);
        }
        return result;
    }

    /// <summary>The application settings that ship in the image, overridden by the container environment.</summary>
    private static PlcRuntimeOptions Bind(params (string Name, string Value)[] dotEnv) =>
        new ConfigurationBuilder().AddJsonFile("api.appsettings.json").AddInMemoryCollection(Container(dotEnv)).Build().GetSection("Plc").Get<PlcRuntimeOptions>()!;

    private static (string, string)[] Keys(string allowList = "") => [("PLC_ALLOW_LIVE_CONNECTION", "true"), ("PLC_WRITE_ENABLED", "true"), ("PLC_WRITE_ALLOWLIST", allowList)];

    private static IoPointRef Point(string address, string signal = "Request_Ack", string station = "A1", PlcDataType type = PlcDataType.Bool, int? words = null) =>
        new(Guid.NewGuid(), "PLC-01", station, signal, PlcSignalDirection.SoftwareToPlc, type, address, words, type == PlcDataType.String ? "ASCII" : null, true, "Command");

    [Fact]
    public void The_deployment_defaults_enable_no_write_and_declare_nothing()
    {
        var o = Bind();
        Assert.False(o.AllowLiveConnection); Assert.False(o.Write.Enabled);
        Assert.Empty(PlcWriteGuard.Declarations(o));
        Assert.NotEmpty(o.Write.ReservedAddresses);
        Assert.Equal("READ_ONLY", WritePolicy.Evaluate(o, Point("M101")).Code);
        var example = File.ReadAllLines(RepoFile("deployment", ".env.example"));
        Assert.Contains("PLC_WRITE_ALLOWLIST=", example); Assert.Contains("PLC_WRITE_ENABLED=false", example); Assert.Contains("PLC_ALLOW_LIVE_CONNECTION=false", example);
    }

    [Fact]
    public void With_both_keys_and_an_empty_list_a_real_write_is_still_refused()
    {
        var o = Bind(Keys());
        Assert.Equal(PlcRuntimeMode.RealReadWrite, WritePolicy.EffectiveMode(o));
        Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, Point("M101")).Code);
    }

    [Fact]
    public void The_value_of_the_env_file_declares_exactly_its_points()
    {
        var o = Bind(Keys(" PLC-01|A1|Request_Ack|M101 ; PLC-01|A2|Note|D100|4 ;"));
        Assert.Equal(2, PlcWriteGuard.Declarations(o).Count);
        Assert.True(WritePolicy.Evaluate(o, Point("M101")).Allowed);
        Assert.True(WritePolicy.Evaluate(o, Point("D100", "Note", "A2", PlcDataType.String, 4)).Allowed);
        Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, Point("M102")).Code);
        Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, Point("D100", "Note", "A2", PlcDataType.String, 8)).Code);
        Assert.Equal("WRITE_NOT_ALLOWED", WritePolicy.Evaluate(o, Point("M101") with { WriteAllowed = false }).Code);   // the Admin approval is still required
    }

    [Theory]
    [InlineData("D1600")] [InlineData("D1501")] [InlineData("M9200")]
    public void A_reserved_register_stays_refused_when_the_env_file_declares_it(string address)
    {
        var type = address[0] == 'M' ? PlcDataType.Bool : PlcDataType.Int;
        var o = Bind(Keys($"PLC-01|A1|Request_Ack|{address}"));
        Assert.Equal(PlcWriteGuard.Reserved, WritePolicy.Evaluate(o, Point(address, type: type)).Code);
    }

    [Theory]
    [InlineData("PLC-01|A1|Request_Ack")]
    [InlineData("PLC-01|A1|Request_Ack|M101;garbage")]
    [InlineData("PLC-01|A1|*|M101")]
    [InlineData("PLC-01||Request_Ack|M101")]
    [InlineData("PLC-01|A1|Request_Ack|M101|zero")]
    [InlineData("PLC-01,A1,Request_Ack,M101")]
    public void One_unreadable_entry_makes_the_whole_value_declare_nothing(string text)
    {
        var o = Bind(Keys(text));
        o.Write.AllowList.Add(new() { PlcId = "PLC-01", StationId = "A1", Signal = "Request_Ack", Address = "M101" });   // even with a valid structured entry
        Assert.Null(PlcWriteGuard.ParseAllowList(o.Write.AllowListText));
        Assert.Equal(PlcWriteGuard.NotDeclared, WritePolicy.Evaluate(o, Point("M101")).Code);
    }
}
