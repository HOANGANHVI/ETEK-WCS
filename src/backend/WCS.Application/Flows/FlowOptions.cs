using Microsoft.Extensions.Options;
using WCS.Domain.Common;

namespace WCS.Application.Flows;

/// <summary>
/// Configuration of the approved flows (BP-01, 02, 03, 05, 06). Location groups are listed in the ORDER in which
/// process-flows.md presents them. That order is NOT a confirmed priority policy: NEEDS_CONFIRMATION
/// (CLAUDE.md 2: source/destination priorities must be configured, never hard-coded).
/// </summary>
public sealed class ProcessFlowOptions
{
    public const string SectionName = "ProcessFlows";

    /// <summary>Key = ProcessFlow name (Bp01, Bp02, Bp03, Bp05, Bp06). A Bp04 key is rejected at startup (REVIEW).</summary>
    public Dictionary<string, FlowRuleOptions> Flows { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What to do when no source rack can be found: Fail (default) or Wait (stay VALIDATING). NEEDS_CONFIRMATION (BP-02 note).</summary>
    public string NoSourceBehavior { get; set; } = "Fail";
}

public sealed class FlowRuleOptions
{
    /// <summary>Full or Empty. Determines the AGV role (Full-&gt;AGV1 role, Empty-&gt;AGV2 role).</summary>
    public string RackKind { get; set; } = "";
    public string[] AllowedRequestStations { get; set; } = [];
    /// <summary>Request = the requesting station is the source; Search = search SourceGroups.</summary>
    public string SourceMode { get; set; } = "Request";
    public List<string[]> SourceGroups { get; set; } = [];
    /// <summary>Request = the requesting station is the destination; Search = search DestinationGroups.</summary>
    public string DestinationMode { get; set; } = "Search";
    public List<string[]> DestinationGroups { get; set; } = [];
    /// <summary>Drop locations documented as "Không bind rack" for this flow (inventory-management.md 4.6).</summary>
    public string[] NoBindDropLocations { get; set; } = [];
}

public enum LocationSelectionMode { Request, Search }

public sealed record FlowRule(
    ProcessFlow Flow,
    RackKind RackKind,
    IReadOnlySet<string> AllowedRequestStations,
    LocationSelectionMode SourceMode,
    IReadOnlyList<IReadOnlyList<string>> SourceGroups,
    LocationSelectionMode DestinationMode,
    IReadOnlyList<IReadOnlyList<string>> DestinationGroups,
    IReadOnlySet<string> NoBindDropLocations)
{
    public AgvRole AgvRole => RackKind == RackKind.Full ? AgvRole.FullRack : AgvRole.EmptyRack;
    public bool IsNoBind(string locationCode) => NoBindDropLocations.Contains(locationCode);
}

public interface IFlowCatalog
{
    FlowRule Get(ProcessFlow flow);
    bool TryParseFlow(string text, out ProcessFlow flow);
    IReadOnlyCollection<FlowRule> All { get; }
}

public sealed class FlowCatalog : IFlowCatalog
{
    private readonly Dictionary<ProcessFlow, FlowRule> _rules = [];

    public FlowCatalog(IOptions<ProcessFlowOptions> options)
    {
        foreach (var (key, o) in options.Value.Flows)
        {
            if (!Enum.TryParse<ProcessFlow>(key, true, out var flow) || !Enum.IsDefined(flow))
                throw new InvalidOperationException(
                    $"Process flow '{key}' is not an approved flow. BP-04 is REVIEW (BR-02); A3/A4 and D1/D2 are UNCHANGED (BR-04).");
            if (!Enum.TryParse<RackKind>(o.RackKind, true, out var kind))
                throw new InvalidOperationException($"Flow {key}: RackKind must be Full or Empty.");
            _rules[flow] = new FlowRule(flow, kind,
                new HashSet<string>(o.AllowedRequestStations, StringComparer.OrdinalIgnoreCase),
                ParseMode(key, o.SourceMode), o.SourceGroups.Select(g => (IReadOnlyList<string>)g).ToList(),
                ParseMode(key, o.DestinationMode), o.DestinationGroups.Select(g => (IReadOnlyList<string>)g).ToList(),
                new HashSet<string>(o.NoBindDropLocations, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static LocationSelectionMode ParseMode(string key, string text) =>
        Enum.TryParse<LocationSelectionMode>(text, true, out var m) ? m
            : throw new InvalidOperationException($"Flow {key}: mode must be Request or Search (was '{text}').");

    public IReadOnlyCollection<FlowRule> All => _rules.Values;

    public FlowRule Get(ProcessFlow flow) => _rules.TryGetValue(flow, out var r)
        ? r : throw new Common.AppException(Common.ErrorKind.Rejected, "FLOW_NOT_CONFIGURED", $"Flow {flow} is not configured.");

    public bool TryParseFlow(string text, out ProcessFlow flow)
    {
        flow = default;
        var normalized = (text ?? "").Replace("-", "").Replace("_", "").Trim();
        return Enum.TryParse(normalized, true, out flow) && Enum.IsDefined(flow);
    }
}
