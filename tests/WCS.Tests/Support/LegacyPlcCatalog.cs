using System.Runtime.CompilerServices;
using System.Text.Json;
using WCS.Infrastructure.Plc;

namespace WCS.Tests.Support;

/// <summary>
/// CR-046. The product ships the catalog of the IO map of 22/11/2026. The tests of the PLC mechanics (polling, writes, approvals, handshake engine, request intake) were written against a richer
/// signal set (bits, strings, requests), kept here as a TEST FIXTURE only. Tests of the product catalog load it explicitly with <see cref="PlcCatalog.LoadEmbedded"/>.
/// </summary>
public static class LegacyPlcCatalog
{
    private static readonly Lazy<PlcCatalog> Fixture = new(Load);
    public static PlcCatalog Instance => Fixture.Value;

    [ModuleInitializer]
    internal static void Use() => PlcCatalog.UseForTests(Instance);

    private static PlcCatalog Load()
    {
        var asm = typeof(LegacyPlcCatalog).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("legacy-plc-io-catalog.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<PlcCatalog>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
