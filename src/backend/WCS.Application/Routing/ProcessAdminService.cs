using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>
/// Admin operations on Business Process configuration: create, edit a Draft definition, validate, publish, disable, new version, version history.
/// Publishing is refused unless <see cref="ProcessValidator"/> passes at that very moment. A Published version is never edited: a change is a new version,
/// and running Transport Tasks keep their own snapshot.
/// </summary>
public sealed class ProcessAdminService(IUnitOfWork uow, IRoutingRepository routing, ProcessValidator validator, IClock clock, IProductModelRepository productModels, IOptions<RouteEngineOptions> routeOptions)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // ------------------------------------------------------------------ read

    public Task<IReadOnlyList<ProcessDto>> ListAsync(CancellationToken ct = default) => uow.ExecuteAsync<IReadOnlyList<ProcessDto>>(async t =>
    {
        var list = new List<ProcessDto>();
        foreach (var p in await routing.ListProcessesAsync(t)) list.Add(await ToDtoAsync(p, t));
        return list;
    }, ct);

    public Task<ProcessDto> GetAsync(Guid id, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
        await ToDtoAsync(await routing.GetProcessAsync(id, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found."), t), ct);

    public Task<VersionDto> GetVersionAsync(Guid versionId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        return ToVersionDto(await ProcessOf(v, t), v);
    }, ct);

    // ------------------------------------------------------------------ write

    public Task<ProcessDto> CreateAsync(ProcessHeader h, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var code = (h.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 60) throw AppException.Validation("PROCESS_CODE_INVALID", "Process code is required (max 60 characters).");
        if (await routing.GetProcessByCodeAsync(code, t) is not null) throw AppException.Conflict("PROCESS_CODE_EXISTS", $"Process code {code} already exists.");
        var p = new BusinessProcess(code, h.Name, h.Description, DefinitionMapping.ParseRequestType(h.RequestType), DefinitionMapping.ParseRackKind(h.RackKind), h.TriggerLocationCodes ?? [], clock.UtcNow);
        if (h.ProductModelId is { } pm0) { await EnsureModelAsync(pm0, t); p.SetProductModel(pm0, clock.UtcNow); }   // CR-036
        routing.Add(p);
        routing.Add(new BusinessProcessVersion(p.Id, 1, user, "Initial draft", clock.UtcNow));
        await routing.FlushAsync(t);   // the returned view reads the version list from the database
        return await ToDtoAsync(p, t);
    }, ct);

    public Task<ProcessDto> UpdateAsync(Guid id, ProcessHeader h, bool enabled, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var p = await routing.GetProcessAsync(id, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        p.Update(h.Name, h.Description, DefinitionMapping.ParseRequestType(h.RequestType), DefinitionMapping.ParseRackKind(h.RackKind), h.TriggerLocationCodes ?? [], enabled, clock.UtcNow);
        if (h.ProductModelId is { } pm1 && pm1 != p.ProductModelId)
        {   // CR-036: null = unchanged. A model is not changed under a Published version (its snapshot, validation and running orders were made for the old one).
            await EnsureModelAsync(pm1, t);
            if ((await routing.ListVersionsAsync(p.Id, t)).Any(v => v.State == ProcessConfigState.Published)) throw AppException.Conflict("PROCESS_MODEL_LOCKED", "The product model of a process with a Published version cannot be changed; create a new process.");
            p.SetProductModel(pm1, clock.UtcNow);
        }
        return await ToDtoAsync(p, t);
    }, ct);

    public Task<VersionDto> SaveDefinitionAsync(Guid versionId, VersionDefinition def, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        if (!v.IsEditable) throw AppException.Rejected("VERSION_NOT_EDITABLE", $"A {v.State} version cannot be edited; create a new version.");
        var steps = def.Steps.Select(s => s.ToStep()).ToList();
        v.ReplaceDefinition(def.FirstStepKey, steps, def.Note, clock.UtcNow);
        return ToVersionDto(await ProcessOf(v, t), v);
    }, ct);

    /// <summary>Runs the publish checks and stores the outcome. A valid version becomes Validated; an invalid one stays Draft and keeps the issue list.</summary>
    public Task<VersionDto> ValidateAsync(Guid versionId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        var p = await ProcessOf(v, t);
        if (!v.IsEditable) return ToVersionDto(p, v);
        var outcome = await validator.ValidateAsync(p, v, t);
        var json = JsonSerializer.Serialize(outcome.Issues, Json);
        if (outcome.IsValid) v.MarkValidated(json, clock.UtcNow); else v.RecordFailedValidation(json, clock.UtcNow);
        return ToVersionDto(p, v);
    }, ct);

    public Task<VersionDto> PublishAsync(Guid versionId, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        var p = await ProcessOf(v, t);
        if (v.State == ProcessConfigState.Published) return ToVersionDto(p, v);   // idempotent
        if (v.State != ProcessConfigState.Validated) throw AppException.Rejected("VERSION_NOT_VALIDATED", "Validate the version before publishing it.");
        // the checks run again NOW: locations, ports and PLC approvals may have changed since the last validation
        var outcome = await validator.ValidateAsync(p, v, t);
        if (!outcome.IsValid)
        {
            v.RecordFailedValidation(JsonSerializer.Serialize(outcome.Issues, Json), clock.UtcNow);
            throw new ProcessPublishRefusedException(outcome.Issues);
        }
        if (await routing.GetPublishedVersionAsync(p.Id, t) is { } current && current.Id != v.Id)
        {
            current.Disable(clock.UtcNow);
            await routing.FlushAsync(t);   // the old Published row must reach the database first (one Published version per process)
        }
        v.Publish(user, clock.UtcNow);
        if (outcome.NeedsReadiness) p.RequireReadiness(clock.UtcNow);   // CR-039: published with an unmet device condition => no request until Ready
        return ToVersionDto(p, v);
    }, ct);

    public Task<VersionDto> DisableAsync(Guid versionId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        v.Disable(clock.UtcNow);
        return ToVersionDto(await ProcessOf(v, t), v);
    }, ct);

    /// <summary>CR-035 (D-11): sets Operational Readiness of a version. Independent of Publication: the version state is never touched. Reason is required (audit).</summary>
    public Task<ReadinessDto> SetReadinessAsync(Guid versionId, string target, string? reason, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (!Enum.TryParse<OperationalReadiness>(target, true, out var r)) throw AppException.Validation("READINESS_INVALID", "Readiness must be NotReady, Ready or Suspended.");
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required for a Readiness change.");
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        var p = await ProcessOf(v, t);
        if (r == OperationalReadiness.Ready && p.RequiresReadiness && await validator.DeviceConditionsAsync(p, v, t) is { Count: > 0 } unmet)
            throw AppException.Rejected("READINESS_CONDITIONS_UNMET", "The version cannot be set Ready: " + string.Join(" ", unmet.Select(i => i.Message).Take(5)) + (unmet.Count > 5 ? $" (+{unmet.Count - 5} more)" : ""));
        try { v.SetReadiness(r, user, reason.Trim(), clock.UtcNow); }
        catch (DomainException e) { throw AppException.Rejected(e.Code, e.Message); }
        return new ReadinessDto(v.Id, v.State.ToString(), v.Readiness.ToString(), v.ReadinessReason, v.ReadinessChangedBy, v.ReadinessChangedAt);
    }, ct);

    public Task<ReadinessDto> GetReadinessAsync(Guid versionId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        return new ReadinessDto(v.Id, v.State.ToString(), v.Readiness.ToString(), v.ReadinessReason, v.ReadinessChangedBy, v.ReadinessChangedAt);
    }, ct);

    /// <summary>
    /// CR-039: EVERY process with a Published version (enabled, not review-blocked) with what a request would be told now. Nothing is filtered by location, process code or
    /// product group: a process an Admin creates appears here as soon as it is published. A process that accepts no request is listed with the reason, never hidden.
    /// </summary>
    public Task<IReadOnlyList<StartableProcessDto>> StartableAsync(CancellationToken ct = default) => uow.ExecuteAsync<IReadOnlyList<StartableProcessDto>>(async t =>
    {
        var list = new List<StartableProcessDto>();
        foreach (var p in (await routing.ListProcessesAsync(t)).Where(x => x.Enabled && !x.ReviewBlocked).OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            if (await routing.GetPublishedVersionAsync(p.Id, t) is not { } published) continue;
            var v = await routing.GetVersionAsync(published.Id, t) ?? published;
            string? blocked = null; string? detail = null;
            if ((routeOptions.Value.EnforceReadiness || p.RequiresReadiness) && v.Readiness != OperationalReadiness.Ready) blocked = "PROCESS_NOT_READY";
            if (p.RequiresReadiness && await validator.DeviceConditionsAsync(p, v, t) is { Count: > 0 } unmet) { blocked = "PROCESS_NOT_READY"; detail = unmet[0].Message; }
            list.Add(new StartableProcessDto(p.Code, p.Name, p.RequestType.ToString(), p.RackKind.ToString(), p.TriggerLocationCodes, v.Readiness.ToString(), blocked is null, blocked, detail));
        }
        return list;
    }, ct);

    /// <summary>CR-039: marks a process as accepting requests only when Ready (one way). Used by the configuration import.</summary>
    public Task<bool> RequireReadinessAsync(Guid processId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var p = await routing.GetProcessAsync(processId, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        p.RequireReadiness(clock.UtcNow);
        return true;
    }, ct);

    /// <summary>New Draft version copied from <paramref name="fromVersionId"/> (or the latest version). Published versions stay untouched.</summary>
    public Task<VersionDto> NewVersionAsync(Guid processId, Guid? fromVersionId, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var p = await routing.GetProcessAsync(processId, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        var all = await routing.ListVersionsAsync(p.Id, t);
        var from = fromVersionId is { } fid ? all.FirstOrDefault(x => x.Id == fid) : all.OrderByDescending(x => x.Number).FirstOrDefault();
        if (from is null) throw AppException.NotFound("VERSION_NOT_FOUND", "Source version not found.");
        var number = all.Max(x => x.Number) + 1;
        var nv = new BusinessProcessVersion(p.Id, number, user, $"Copy of version {from.Number}", clock.UtcNow);
        var full = (await routing.GetVersionAsync(from.Id, t))!;
        nv.ReplaceDefinition(full.FirstStepKey, full.ToDefinition().Steps.Select(s => s.ToStep()).ToList(), null, clock.UtcNow);
        routing.Add(nv);
        return ToVersionDto(p, nv);
    }, ct);

    /// <summary>
    /// Loads the reference processes derived from the working matrix (Process in detail, Phase 2) as DRAFT versions. Idempotent by process code; never publishes.
    /// A REVIEW flow (BP-04) is marked review-blocked so it can never be published (PH2-04 was approved by D-09).
    /// </summary>
    public Task<ImportResult> ImportReferenceAsync(IReadOnlyList<ReferenceProcess> items, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var imported = new List<string>(); var skipped = 0;
        foreach (var r in items)
        {
            if (await routing.GetProcessByCodeAsync(r.Code.ToUpperInvariant(), t) is not null) { skipped++; continue; }
            var p = new BusinessProcess(r.Code, r.Name, r.Description, DefinitionMapping.ParseRequestType(r.RequestType), DefinitionMapping.ParseRackKind(r.RackKind), r.TriggerLocationCodes, clock.UtcNow);
            p.MarkReview(r.ReviewBlocked, r.ReviewReason, r.SourceReference, clock.UtcNow);
            routing.Add(p);
            var v = new BusinessProcessVersion(p.Id, 1, user, "Imported reference draft (not validated)", clock.UtcNow);
            v.ReplaceDefinition(r.Definition.FirstStepKey, r.Definition.Steps.Select(s => s.ToStep()).ToList(), r.Definition.Note ?? "Imported reference draft (not validated)", clock.UtcNow);
            routing.Add(v);
            imported.Add(p.Code);
        }
        return new ImportResult(imported.Count, skipped, imported);
    }, ct);

    // ------------------------------------------------------------------ helpers

    private async Task<BusinessProcess> ProcessOf(BusinessProcessVersion v, CancellationToken ct) =>
        await routing.GetProcessAsync(v.ProcessId, ct) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");

    private async Task EnsureModelAsync(Guid modelId, CancellationToken ct)
    {
        var m = await productModels.GetAsync(modelId, ct) ?? throw AppException.NotFound("MODEL_NOT_FOUND", "Product model not found.");
        if (!m.Enabled) throw AppException.Rejected("MODEL_DISABLED", $"Product model {m.Code} is disabled.");
    }

    private async Task<ProcessDto> ToDtoAsync(BusinessProcess p, CancellationToken ct)
    {
        var versions = await routing.ListVersionsAsync(p.Id, ct);
        return new ProcessDto(p.Id, p.Code, p.Name, p.Description, p.RequestType.ToString(), p.RackKind.ToString(), p.TriggerLocationCodes, p.Enabled, p.ReviewBlocked, p.ReviewReason,
            p.SourceReference, versions.FirstOrDefault(v => v.State == ProcessConfigState.Published)?.Number, versions.Count == 0 ? 0 : versions.Max(v => v.Number),
            versions.OrderByDescending(v => v.Number).Select(v => new VersionSummaryDto(v.Id, v.Number, v.State.ToString(), v.CreatedAt, v.CreatedBy, v.ValidatedAt, v.PublishedAt,
                v.PublishedBy, v.DisabledAt, v.Note, v.Steps.Count)).ToList(), p.ProductModelId);
    }

    private static VersionDto ToVersionDto(BusinessProcess p, BusinessProcessVersion v)
    {
        IReadOnlyList<ProcessIssue> issues = [];
        if (!string.IsNullOrWhiteSpace(v.ValidationJson)) { try { issues = JsonSerializer.Deserialize<List<ProcessIssue>>(v.ValidationJson, Json) ?? []; } catch (JsonException) { } }
        return new VersionDto(v.Id, p.Id, p.Code, v.Number, v.State.ToString(), v.IsEditable, v.Note, v.ToDefinition(), issues, v.ValidatedAt);
    }
}

/// <summary>Publishing failed because the version no longer passes validation; <see cref="Issues"/> tell the Admin what to fix.</summary>
public sealed class ProcessPublishRefusedException(IReadOnlyList<ProcessIssue> issues)
    : AppException(ErrorKind.Rejected, "PUBLISH_REFUSED", "The version cannot be published: " + string.Join("; ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Code).Distinct()))
{
    public IReadOnlyList<ProcessIssue> Issues { get; } = issues;
}
