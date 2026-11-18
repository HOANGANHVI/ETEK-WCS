using System.Text.Json;
using System.Text.Json.Serialization;
using WCS.Domain.Common;

namespace WCS.Domain.Routing;

/// <summary>
/// Immutable copy of a published process version, stored on every Transport Task when it is created. A running task always reads THIS copy,
/// never the live configuration, so publishing or editing a process cannot change a task that already started (CR-010 3.4).
/// </summary>
public sealed record ProcessSnapshot(Guid ProcessId, string ProcessCode, int VersionNumber, Guid VersionId, RequestType RequestType, RackKind RackKind,
    string FirstStepKey, IReadOnlyList<StepSnapshot> Steps)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public StepSnapshot? Step(string? key) => key is null ? null : Steps.FirstOrDefault(s => s.StepKey == key);
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static ProcessSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<ProcessSnapshot>(json, Options) ?? throw new DomainException("SNAPSHOT_INVALID", "Process snapshot cannot be read.");

    public static ProcessSnapshot Of(BusinessProcess process, BusinessProcessVersion version) => new(process.Id, process.Code, version.Number, version.Id,
        process.RequestType, process.RackKind, version.FirstStepKey,
        version.Steps.OrderBy(s => s.Order).Select(s => new StepSnapshot(s.StepKey, s.Order, s.Type, s.SourceMode, s.DestinationMode, s.Resolution, s.NextStepKey,
            s.AlternativeNextStepKey, s.WaitBehavior, s.ErrorBehavior, s.Conditions, s.WaitingPositionCode,
            s.Candidates.OrderBy(c => c.Priority).ThenBy(c => c.Order)
                .Select(c => new CandidateSnapshot(c.Role, c.Priority, c.Order, c.LocationCode, c.Conditions, c.NoBind)).ToList())).ToList());
}

public sealed record StepSnapshot(string StepKey, int Order, RouteStepType Type, SourceMode SourceMode, DestinationMode DestinationMode, DestinationResolution Resolution,
    string? NextStepKey, string? AlternativeNextStepKey, WaitBehavior WaitBehavior, ErrorBehavior ErrorBehavior, IReadOnlyList<ConditionSpec> Conditions,
    string? WaitingPositionCode, IReadOnlyList<CandidateSnapshot> Candidates)
{
    public bool IsMovement => Type is RouteStepType.Pickup or RouteStepType.Delivery or RouteStepType.BufferStorage or RouteStepType.BufferRetrieval;
    public IEnumerable<CandidateSnapshot> Sources => Candidates.Where(c => c.Role == CandidateRole.Source);
    public IEnumerable<CandidateSnapshot> Destinations => Candidates.Where(c => c.Role == CandidateRole.Destination);
}

public sealed record CandidateSnapshot(CandidateRole Role, int Priority, int Order, string LocationCode, IReadOnlyList<ConditionSpec> Conditions, bool NoBind);
