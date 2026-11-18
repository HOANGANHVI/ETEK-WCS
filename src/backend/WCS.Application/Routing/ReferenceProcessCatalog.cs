using System.Text.Json;

namespace WCS.Application.Routing;

/// <summary>
/// Reference processes derived from the working matrix and the process slides. They are DATA for the Admin import (as Draft versions): the engine never reads
/// this file, so changing a route means editing a process in the database, not this resource.
/// </summary>
public static class ReferenceProcessCatalog
{
    public static IReadOnlyList<ReferenceProcess> Load()
    {
        var asm = typeof(ReferenceProcessCatalog).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("reference-processes.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<List<ReferenceProcess>>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
