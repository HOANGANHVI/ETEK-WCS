using System.Text.Json;
using System.Text.Json.Serialization;
using WCS.Domain.Common;

namespace WCS.Domain.Routing;

/// <summary>One controlled condition of a candidate or of a Condition/Waiting step. <see cref="LocationCode"/> null = the candidate's own location.</summary>
public sealed record ConditionSpec(ConditionType Type, string? Parameter = null, string? LocationCode = null);

public static class ConditionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static string Write(IReadOnlyList<ConditionSpec> conditions) => JsonSerializer.Serialize(conditions, Options);
    public static IReadOnlyList<ConditionSpec> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<ConditionSpec>>(json, Options) ?? []; }
        catch (JsonException) { throw new DomainException("CONDITIONS_INVALID", "Conditions JSON is not valid (only the supported condition types are accepted)."); }
    }
}

/// <summary>
/// A Business Process (identity of a configurable route). The route itself lives in <see cref="BusinessProcessVersion"/>.
/// <see cref="ReviewBlocked"/> marks a process whose source flow is REVIEW (BP-04): it can be drafted but never validated for publishing.
/// </summary>
public class BusinessProcess
{
    private BusinessProcess() { }

    public BusinessProcess(string code, string name, string? description, RequestType requestType, RackKind rackKind,
        IEnumerable<string> triggerLocationCodes, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("PROCESS_CODE_REQUIRED", "Process code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("PROCESS_NAME_REQUIRED", "Process name is required.");
        Id = Guid.NewGuid(); Code = code.Trim().ToUpperInvariant(); Name = name.Trim(); Description = description;
        RequestType = requestType; RackKind = rackKind;
        TriggerLocationCodesCsv = Csv(triggerLocationCodes);
        CreatedAt = now; UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public RequestType RequestType { get; private set; }
    public RackKind RackKind { get; private set; }
    /// <summary>Locations (ports) whose request can start this process, comma separated (data, never code).</summary>
    public string TriggerLocationCodesCsv { get; private set; } = "";
    public bool ReviewBlocked { get; private set; }
    public string? ReviewReason { get; private set; }
    /// <summary>Source reference (document, page) for traceability. Free text.</summary>
    public string? SourceReference { get; private set; }
    public bool Enabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }
    /// <summary>CR-036: product model of the process. NULL = legacy / no model constraint (every existing process).</summary>
    public Guid? ProductModelId { get; private set; }

    public void SetProductModel(Guid? modelId, DateTimeOffset now) { ProductModelId = modelId; UpdatedAt = now; }

    /// <summary>
    /// CR-039 (D-11): a request is accepted only while the Published version is Operational Readiness = Ready, whatever `RouteEngine:EnforceReadiness` says. Set for the default
    /// catalog and for a process that was published although a device condition (PLC port / IO mapping) was not met. ONE WAY: nothing clears it.
    /// </summary>
    public bool RequiresReadiness { get; private set; }

    public void RequireReadiness(DateTimeOffset now) { if (!RequiresReadiness) { RequiresReadiness = true; UpdatedAt = now; } }

    public IReadOnlyList<string> TriggerLocationCodes => TriggerLocationCodesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Update(string name, string? description, RequestType requestType, RackKind rackKind, IEnumerable<string> triggers, bool enabled, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("PROCESS_NAME_REQUIRED", "Process name is required.");
        Name = name.Trim(); Description = description; RequestType = requestType; RackKind = rackKind;
        TriggerLocationCodesCsv = Csv(triggers); Enabled = enabled; UpdatedAt = now;
    }

    public void MarkReview(bool blocked, string? reason, string? sourceReference, DateTimeOffset now)
    { ReviewBlocked = blocked; ReviewReason = reason; SourceReference = sourceReference; UpdatedAt = now; }

    private static string Csv(IEnumerable<string> codes) => string.Join(',', codes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}

/// <summary>One numbered version of a process: Draft -> Validated -> Published -> Disabled. A Published version is immutable.</summary>
public class BusinessProcessVersion
{
    private BusinessProcessVersion() { }

    public BusinessProcessVersion(Guid processId, int number, string? createdBy, string? note, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); ProcessId = processId; Number = number; State = ProcessConfigState.Draft;
        CreatedBy = createdBy; Note = note; CreatedAt = now; UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProcessId { get; private set; }
    public int Number { get; private set; }
    public ProcessConfigState State { get; private set; }
    public string? Note { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ValidatedAt { get; private set; }
    public string? PublishedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }
    /// <summary>Result of the last validation (JSON array of issues) so the Admin can see why a publish was refused.</summary>
    public string? ValidationJson { get; private set; }
    public uint Version { get; private set; }

    public string FirstStepKey { get; private set; } = "";
    /// <summary>CR-035 (D-11): Operational Readiness, a second axis independent of <see cref="State"/>. A new version starts NotReady; changing it never changes State.</summary>
    public OperationalReadiness Readiness { get; private set; } = OperationalReadiness.NotReady;
    public string? ReadinessReason { get; private set; }
    public string? ReadinessChangedBy { get; private set; }
    public DateTimeOffset? ReadinessChangedAt { get; private set; }

    public ICollection<RouteStep> Steps { get; private set; } = new List<RouteStep>();

    public bool IsEditable => State is ProcessConfigState.Draft or ProcessConfigState.Validated;

    /// <summary>Replaces the whole definition. Allowed only while editable; the version returns to Draft because it has to be validated again.</summary>
    public void ReplaceDefinition(string firstStepKey, IEnumerable<RouteStep> steps, string? note, DateTimeOffset now)
    {
        if (!IsEditable) throw new DomainException("VERSION_NOT_EDITABLE", $"A {State} version cannot be edited; create a new version.");
        Steps.Clear();
        foreach (var s in steps) Steps.Add(s);
        FirstStepKey = firstStepKey ?? "";
        Note = note ?? Note; State = ProcessConfigState.Draft; ValidatedAt = null; ValidationJson = null; UpdatedAt = now;
    }

    public void MarkValidated(string issuesJson, DateTimeOffset now)
    {
        if (State is ProcessConfigState.Published or ProcessConfigState.Disabled) throw new DomainException("VERSION_NOT_EDITABLE", $"A {State} version cannot be validated.");
        State = ProcessConfigState.Validated; ValidatedAt = now; ValidationJson = issuesJson; UpdatedAt = now;
    }

    public void RecordFailedValidation(string issuesJson, DateTimeOffset now)
    {
        if (!IsEditable) return;
        State = ProcessConfigState.Draft; ValidatedAt = null; ValidationJson = issuesJson; UpdatedAt = now;
    }

    public void Publish(string? by, DateTimeOffset now)
    {
        if (State != ProcessConfigState.Validated) throw new DomainException("VERSION_NOT_VALIDATED", "Only a validated version can be published.");
        State = ProcessConfigState.Published; PublishedBy = by; PublishedAt = now; UpdatedAt = now;
    }

    /// <summary>CR-035 (D-11/OQ-51): only NotReady->Ready, Ready->Suspended and Suspended->Ready are decided. Other moves are refused until OQ-51 is answered. Never changes State.</summary>
    public void SetReadiness(OperationalReadiness target, string? by, string? reason, DateTimeOffset now)
    {
        if (target == Readiness) return;
        var allowed = (Readiness, target) is (OperationalReadiness.NotReady, OperationalReadiness.Ready) or (OperationalReadiness.Ready, OperationalReadiness.Suspended) or (OperationalReadiness.Suspended, OperationalReadiness.Ready);
        if (!allowed) throw new DomainException("READINESS_TRANSITION_NOT_DECIDED", $"{Readiness} -> {target} is not a decided transition (open question OQ-51).");
        Readiness = target; ReadinessReason = reason; ReadinessChangedBy = by; ReadinessChangedAt = now; UpdatedAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        if (State == ProcessConfigState.Disabled) return;
        State = ProcessConfigState.Disabled; DisabledAt = now; UpdatedAt = now;
    }
}

/// <summary>One step of a version. Movement steps (Pickup/Delivery/BufferStorage/BufferRetrieval) create a Leg.</summary>
public class RouteStep
{
    private RouteStep() { }

    public RouteStep(string stepKey, int order, RouteStepType type)
    {
        if (string.IsNullOrWhiteSpace(stepKey)) throw new DomainException("STEP_KEY_REQUIRED", "Step key is required.");
        Id = Guid.NewGuid(); StepKey = stepKey.Trim(); Order = order; Type = type;
    }

    public Guid Id { get; private set; }
    public Guid VersionId { get; private set; }
    public string StepKey { get; private set; } = default!;
    public int Order { get; private set; }
    public RouteStepType Type { get; private set; }
    public SourceMode SourceMode { get; private set; }
    public DestinationMode DestinationMode { get; private set; }
    public DestinationResolution Resolution { get; private set; }
    /// <summary>Next step key (null on ProcessCompletion). Alternative next is used by Condition steps and by WaitBehavior.Alternative.</summary>
    public string? NextStepKey { get; private set; }
    public string? AlternativeNextStepKey { get; private set; }
    public WaitBehavior WaitBehavior { get; private set; }
    public ErrorBehavior ErrorBehavior { get; private set; }
    /// <summary>Waiting / Condition steps: conditions that must all hold (JSON of <see cref="ConditionSpec"/>).</summary>
    public string? ConditionsJson { get; private set; }
    /// <summary>Optional intermediate waiting position (an AMR waits there while it still holds the rack). NOT a storage location: inventory is not touched.</summary>
    public string? WaitingPositionCode { get; private set; }
    public string? Description { get; private set; }
    public ICollection<RouteCandidate> Candidates { get; private set; } = new List<RouteCandidate>();

    public RouteStep Configure(SourceMode source, DestinationMode destination, DestinationResolution resolution, string? next, string? alternative,
        WaitBehavior wait, ErrorBehavior error, IReadOnlyList<ConditionSpec>? conditions, string? waitingPosition, string? description)
    {
        SourceMode = source; DestinationMode = destination; Resolution = resolution; NextStepKey = Blank(next); AlternativeNextStepKey = Blank(alternative);
        WaitBehavior = wait; ErrorBehavior = error;
        ConditionsJson = conditions is { Count: > 0 } ? ConditionJson.Write(conditions) : null;
        WaitingPositionCode = Blank(waitingPosition); Description = description;
        return this;
    }

    public RouteStep AddCandidate(RouteCandidate c) { Candidates.Add(c); return this; }

    public IReadOnlyList<ConditionSpec> Conditions => ConditionJson.Read(ConditionsJson);
    public bool IsMovement => Type is RouteStepType.Pickup or RouteStepType.Delivery or RouteStepType.BufferStorage or RouteStepType.BufferRetrieval;

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>A location that a step may use as source or destination, with its priority (1 = first) and its own conditions.</summary>
public class RouteCandidate
{
    private RouteCandidate() { }

    public RouteCandidate(CandidateRole role, int priority, int order, string locationCode, IReadOnlyList<ConditionSpec>? conditions, bool noBind = false, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(locationCode)) throw new DomainException("CANDIDATE_LOCATION_REQUIRED", "Candidate location code is required.");
        Id = Guid.NewGuid(); Role = role; Priority = priority; Order = order; LocationCode = locationCode.Trim();
        ConditionsJson = conditions is { Count: > 0 } ? ConditionJson.Write(conditions) : null;
        NoBind = noBind; Note = note;
    }

    public Guid Id { get; private set; }
    public Guid StepId { get; private set; }
    public CandidateRole Role { get; private set; }
    /// <summary>1 = tried first. Candidates with the same priority are tried in <see cref="Order"/> (a tie-break that the source documents do not fix: NEEDS_CONFIRMATION).</summary>
    public int Priority { get; private set; }
    public int Order { get; private set; }
    public string LocationCode { get; private set; } = default!;
    public string? ConditionsJson { get; private set; }
    /// <summary>The source process states "Không bind rack" for a drop here: the rack is not bound to an external id (inventory-management.md 4.6).</summary>
    public bool NoBind { get; private set; }
    public string? Note { get; private set; }

    public IReadOnlyList<ConditionSpec> Conditions => ConditionJson.Read(ConditionsJson);
}
