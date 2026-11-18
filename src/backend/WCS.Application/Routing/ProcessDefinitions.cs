using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

// Transport shapes of a process definition (API and reference import). Enums travel as text; conditions are typed, never scripts.

public sealed record ConditionDefinition(string Type, string? Parameter = null, string? LocationCode = null);

public sealed record CandidateDefinition(string Role, int Priority, int Order, string LocationCode, IReadOnlyList<ConditionDefinition>? Conditions = null, bool NoBind = false, string? Note = null);

public sealed record StepDefinition(string StepKey, int Order, string Type, string? SourceMode, string? DestinationMode, string? Resolution, string? NextStepKey,
    string? AlternativeNextStepKey, string? WaitBehavior, string? ErrorBehavior, IReadOnlyList<ConditionDefinition>? Conditions, string? WaitingPositionCode,
    string? Description, IReadOnlyList<CandidateDefinition>? Candidates);

public sealed record VersionDefinition(string FirstStepKey, IReadOnlyList<StepDefinition> Steps, string? Note = null);

public sealed record ProcessHeader(string Code, string Name, string? Description, string RequestType, string RackKind, IReadOnlyList<string> TriggerLocationCodes);

public sealed record ReferenceProcess(string Code, string Name, string? Description, string RequestType, string RackKind, IReadOnlyList<string> TriggerLocationCodes,
    bool ReviewBlocked, string? ReviewReason, string? SourceReference, VersionDefinition Definition);

public sealed record VersionSummaryDto(Guid Id, int Number, string State, DateTimeOffset CreatedAt, string? CreatedBy, DateTimeOffset? ValidatedAt, DateTimeOffset? PublishedAt, string? PublishedBy,
    DateTimeOffset? DisabledAt, string? Note, int StepCount);

public sealed record ProcessDto(Guid Id, string Code, string Name, string? Description, string RequestType, string RackKind, IReadOnlyList<string> TriggerLocationCodes,
    bool Enabled, bool ReviewBlocked, string? ReviewReason, string? SourceReference, int? PublishedVersion, int LatestVersion, IReadOnlyList<VersionSummaryDto> Versions);

public sealed record VersionDto(Guid Id, Guid ProcessId, string ProcessCode, int Number, string State, bool Editable, string? Note, VersionDefinition Definition,
    IReadOnlyList<ProcessIssue> Issues, DateTimeOffset? ValidatedAt);

public sealed record ImportResult(int Imported, int Skipped, IReadOnlyList<string> ImportedCodes);

public static class DefinitionMapping
{
    private static T Parse<T>(string? text, T fallback, string what) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(text) ? fallback
        : Enum.TryParse<T>(text, true, out var v) && Enum.IsDefined(v) ? v
        : throw Common.AppException.Validation("DEFINITION_INVALID", $"'{text}' is not a valid {what}.");

    public static ConditionSpec ToSpec(this ConditionDefinition c) =>
        new(Parse(c.Type, default(ConditionType), "condition type") is var t && t != 0 ? t : throw Common.AppException.Validation("DEFINITION_INVALID", "Condition type is required."),
            string.IsNullOrWhiteSpace(c.Parameter) ? null : c.Parameter.Trim(), string.IsNullOrWhiteSpace(c.LocationCode) ? null : c.LocationCode.Trim());

    public static ConditionDefinition ToDefinition(this ConditionSpec c) => new(c.Type.ToString(), c.Parameter, c.LocationCode);

    public static RouteStep ToStep(this StepDefinition d)
    {
        var type = Parse(d.Type, default(RouteStepType), "step type");
        if (type == 0) throw Common.AppException.Validation("DEFINITION_INVALID", $"Step '{d.StepKey}' has no type.");
        var step = new RouteStep(d.StepKey, d.Order, type).Configure(
            Parse(d.SourceMode, SourceMode.Request, "source mode"), Parse(d.DestinationMode, DestinationMode.Request, "destination mode"),
            Parse(d.Resolution, DestinationResolution.BeforePick, "resolution"), d.NextStepKey, d.AlternativeNextStepKey,
            Parse(d.WaitBehavior, WaitBehavior.Wait, "wait behavior"), Parse(d.ErrorBehavior, ErrorBehavior.Fail, "error behavior"),
            d.Conditions?.Select(ToSpec).ToList(), d.WaitingPositionCode, d.Description);
        foreach (var c in d.Candidates ?? [])
        {
            var role = Parse(c.Role, default(CandidateRole), "candidate role");
            if (role == 0) throw Common.AppException.Validation("DEFINITION_INVALID", $"A candidate of step '{d.StepKey}' has no role.");
            if (c.Priority < 1) throw Common.AppException.Validation("DEFINITION_INVALID", $"Candidate priority of step '{d.StepKey}' must be 1 or more.");
            step.AddCandidate(new RouteCandidate(role, c.Priority, c.Order, c.LocationCode, c.Conditions?.Select(ToSpec).ToList(), c.NoBind, c.Note));
        }
        return step;
    }

    public static VersionDefinition ToDefinition(this BusinessProcessVersion v) => new(v.FirstStepKey,
        v.Steps.OrderBy(s => s.Order).Select(s => new StepDefinition(s.StepKey, s.Order, s.Type.ToString(), s.SourceMode.ToString(), s.DestinationMode.ToString(), s.Resolution.ToString(),
            s.NextStepKey, s.AlternativeNextStepKey, s.WaitBehavior.ToString(), s.ErrorBehavior.ToString(), s.Conditions.Select(c => c.ToDefinition()).ToList(),
            s.WaitingPositionCode, s.Description,
            s.Candidates.OrderBy(c => c.Role).ThenBy(c => c.Priority).ThenBy(c => c.Order)
                .Select(c => new CandidateDefinition(c.Role.ToString(), c.Priority, c.Order, c.LocationCode, c.Conditions.Select(x => x.ToDefinition()).ToList(), c.NoBind, c.Note)).ToList())).ToList(),
        v.Note);

    public static RequestType ParseRequestType(string text) => Parse(text, default(RequestType), "request type") is var t && t != 0 ? t : throw Common.AppException.Validation("DEFINITION_INVALID", "Request type is required.");
    public static RackKind ParseRackKind(string text) => Parse(text, default(RackKind), "rack kind") is var k && k != 0 ? k : throw Common.AppException.Validation("DEFINITION_INVALID", "Rack kind is required.");
}
