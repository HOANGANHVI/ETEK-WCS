namespace WCS.Application.RcsDiagnostics;

/// <summary>
/// A deployment-wide parameter of the RCS APIs that the Request Builder can pre-fill. <c>Status</c>: Confirmed (a default value validated by the project), UserInput (the project has not given a value: the Admin
/// enters it and may save it). <c>Fields</c> are the REQUEST field names it applies to, exactly as the API document names them; nothing else is touched (the map ALIAS <c>mapShortName</c> in most requests; <c>mapCode</c> is a request field of queryAgvStatus only).
/// </summary>
public sealed record RcsParamDef(string Key, string Status, IReadOnlyList<string> Fields, int MaxLength);

public static class RcsParameterCatalog
{
    public const string Confirmed = "Confirmed", UserInput = "UserInput";

    public static readonly IReadOnlyList<RcsParamDef> All =
    [
        // MAP ID of this installation (value "TT", Rcs:Diagnostics:MapId). It pre-fills mapShortName ("Alias of the map") wherever an API has it, and mapCode, which only queryAgvStatus has (project decision).
        new("mapId", Confirmed, ["mapShortName", "mapCode"], 32),
        new("clientCode", UserInput, ["clientCode"], 16),
        new("agvTyp", UserInput, ["agvTyp"], 16),
        new("wbCode", UserInput, ["wbCode"], 32),
        new("ctnrTyp", UserInput, ["ctnrTyp"], 16),
        new("roadWayCode", UserInput, ["roadWayCode"], 16),
        new("areaCode", UserInput, ["matterArea", "targetArea", "areaCode"], 16),
    ];

    /// <summary>Request fields whose meaning or rule the document does not give (or whose value the project has not defined). They stay optional free inputs, marked "not determined – to be configured / confirmed"; no rule is invented.</summary>
    public static readonly IReadOnlyList<string> UndeterminedFields =
    [
        "genAgvSchedulingTask.data", "genAgvSchedulingTask.podTyp", "genAgvSchedulingTask.materialType", "genAgvSchedulingTask.materialLot", "genAgvSchedulingTask.groupId",
        "bindPodAndBerth.characterValue", "bindCtnrAndBin.characterValue", "bindCtnrAndBin.binName",
        "genPreScheduleTask.useableLayers", "genPreScheduleTask.cacheCount", "genPreScheduleTask.waitTime", "genPreScheduleTask.preTaskQty",
        "returnPod.returnPodStrategy",
    ];

    public static RcsParamDef? Find(string? key) => All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    public static string? ConfiguredDefault(RcsParamDef d, RcsDiagnosticsOptions o) => d.Key == "mapId" && !string.IsNullOrWhiteSpace(o.MapId) ? o.MapId.Trim() : null;
}
