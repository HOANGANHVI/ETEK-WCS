using System.Text.Json;
using System.Text.Json.Serialization;
using WCS.Application.Abstractions;
using WCS.Application.Common;
using WCS.Domain.Routing;

namespace WCS.Application.Routing;

/// <summary>
/// Admin operations on Business Process configuration: create, edit a Draft definition, validate, publish, disable, new version, version history.
/// Publishing is refused unless <see cref="ProcessValidator"/> passes at that very moment. A Published version is never edited: a change is a new version,
/// and running Transport Tasks keep their own snapshot.
/// </summary>
public sealed class ProcessAdminService(IUnitOfWork uow, IRoutingRepository routing, ProcessValidator validator, IClock clock)
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
        routing.Add(p);
        routing.Add(new BusinessProcessVersion(p.Id, 1, user, "Initial draft", clock.UtcNow));
        await routing.FlushAsync(t);   // the returned view reads the version list from the database
        return await ToDtoAsync(p, t);
    }, ct);

    public Task<ProcessDto> UpdateAsync(Guid id, ProcessHeader h, bool enabled, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var p = await routing.GetProcessAsync(id, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        p.Update(h.Name, h.Description, DefinitionMapping.ParseRequestType(h.RequestType), DefinitionMapping.ParseRackKind(h.RackKind), h.TriggerLocationCodes ?? [], enabled, clock.UtcNow);
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
        return ToVersionDto(p, v);
    }, ct);

    public Task<VersionDto> DisableAsync(Guid versionId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        v.Disable(clock.UtcNow);
        return ToVersionDto(await ProcessOf(v, t), v);
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
    /// REVIEW flows (BP-04 and its Phase 2 counterpart) are marked review-blocked so they can never be published.
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

    private async Task<ProcessDto> ToDtoAsync(BusinessProcess p, CancellationToken ct)
    {
        var versions = await routing.ListVersionsAsync(p.Id, ct);
        return new ProcessDto(p.Id, p.Code, p.Name, p.Description, p.RequestType.ToString(), p.RackKind.ToString(), p.TriggerLocationCodes, p.Enabled, p.ReviewBlocked, p.ReviewReason,
            p.SourceReference, versions.FirstOrDefault(v => v.State == ProcessConfigState.Published)?.Number, versions.Count == 0 ? 0 : versions.Max(v => v.Number),
            versions.OrderByDescending(v => v.Number).Select(v => new VersionSummaryDto(v.Id, v.Number, v.State.ToString(), v.CreatedAt, v.CreatedBy, v.ValidatedAt, v.PublishedAt,
                v.PublishedBy, v.DisabledAt, v.Note, v.Steps.Count)).ToList());
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
