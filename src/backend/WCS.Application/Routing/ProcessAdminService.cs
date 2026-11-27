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
        if (await routing.GetPublishedVersionAsync(p.Id, t) is not null
            && !SameHeader(p, DefinitionMapping.ParseRequestType(h.RequestType), DefinitionMapping.ParseRackKind(h.RackKind), h.TriggerLocationCodes ?? []))
            throw HeaderLocked(p);   // CR-042 (OQ-42-01)
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
        EnsureApprovedForUse(p);   // CR-042 (OQ-42-02)
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
        if (routeOptions.Value.NewPublicationsRequireReadiness || outcome.NeedsReadiness) p.RequireReadiness(clock.UtcNow);   // CR-040 (D-12)   // CR-039: published with an unmet device condition => no request until Ready
        return ToVersionDto(p, v);
    }, ct);

    public Task<VersionDto> DisableAsync(Guid versionId, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        v.Disable(clock.UtcNow, byAdmin: true);   // CR-042 (OQ-45)
        return ToVersionDto(await ProcessOf(v, t), v);
    }, ct);

    /// <summary>CR-035 (D-11): sets Operational Readiness of a version. Independent of Publication: the version state is never touched. Reason is required (audit).</summary>
    public Task<ReadinessDto> SetReadinessAsync(Guid versionId, string target, string? reason, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (!Enum.TryParse<OperationalReadiness>(target, true, out var r)) throw AppException.Validation("READINESS_INVALID", "Readiness must be NotReady, Ready or Suspended.");
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required for a Readiness change.");
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        var p = await ProcessOf(v, t);
        if (r == OperationalReadiness.Ready) EnsureApprovedForUse(p);   // CR-042
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
            list.Add(new StartableProcessDto(p.Code, p.Name, p.RequestType.ToString(), p.RackKind.ToString(), p.TriggerLocationCodes, v.Readiness.ToString(), blocked is null, blocked, detail,
                p.RequiresReadiness, p.QueueScope));
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

    /// <summary>
    /// CR-040 (D-13): declares (or removes) the Queue Ordering Scope of a process. The scope is a decision of the Admin, never derived; the answer lists processes of ANOTHER scope that
    /// use the same locations so that the Admin sees where the declaration may be incomplete. Requests that already carry a scope keep it.
    /// </summary>
    public Task<QueueScopeDto> SetQueueScopeAsync(Guid processId, string? scope, string? reason, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required for a queue scope change.");
        var p = await routing.GetProcessAsync(processId, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        try { p.SetQueueScope(scope, clock.UtcNow); }
        catch (DomainException e) { throw AppException.Rejected(e.Code, e.Message); }
        var mine = await UsedLocationsAsync(p, t);
        var shared = new List<string>();
        foreach (var other in (await routing.ListProcessesAsync(t)).Where(x => x.Id != p.Id && x.QueueScope != p.QueueScope))
        {
            if (await routing.GetPublishedVersionAsync(other.Id, t) is null) continue;
            var common = (await UsedLocationsAsync(other, t)).Intersect(mine, StringComparer.OrdinalIgnoreCase).Order().ToList();
            if (common.Count > 0) shared.Add($"{other.Code} (scope {other.QueueScope ?? "not declared"}): {string.Join(", ", common)}");
        }
        return new QueueScopeDto(p.Id, p.Code, p.QueueScope, p.RequiresReadiness, shared);
    }, ct);

    private async Task<IReadOnlyList<string>> UsedLocationsAsync(BusinessProcess p, CancellationToken ct)
    {
        var published = await routing.GetPublishedVersionAsync(p.Id, ct);
        return p.TriggerLocationCodes.Concat(published?.Steps.SelectMany(s => s.Candidates.Select(c => c.LocationCode)) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

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

    // ------------------------------------------------------------------ CR-055: removal (owner decisions of 2026-11-27)

    /// <summary>What "remove" would do now. A protected reference process is never removed; a process with open orders is left alone; one with finished orders is never deleted (its history
    /// stays) and can only be switched off; one that never had an order is deleted, after it was switched off if it is published.</summary>
    public Task<ProcessRemovalDto> RemovalAsync(Guid id, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
        await PlanRemovalAsync(await routing.GetProcessAsync(id, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found."), await routing.ListVersionsAsync(id, t), t), ct);

    public Task<ProcessRemovalResult> RemoveAsync(Guid id, string? reason, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        RequireReason(reason);
        var p = await routing.GetProcessAsync(id, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        var versions = await routing.ListVersionsAsync(p.Id, t);
        var plan = await PlanRemovalAsync(p, versions, t);
        if (plan.Action == ProcessRemovalActions.None) throw AppException.Rejected(plan.BlockCode!, plan.BlockMessage!);
        if (plan.Action == ProcessRemovalActions.Disable)
            p.Update(p.Name, p.Description, p.RequestType, p.RackKind, p.TriggerLocationCodes, false, clock.UtcNow);
        else
        {   // steps and candidates go with their version (database cascade); the database also refuses the delete if an order appeared meanwhile
            foreach (var v in versions) routing.Remove(v);
            routing.Remove(p);
        }
        return new ProcessRemovalResult(p.Id, p.Code, plan.Action == ProcessRemovalActions.Disable ? "Disabled" : "Deleted", versions.Count, plan.Orders);
    }, ct);

    /// <summary>Deletes ONE version that was never published and that no order uses. The only version of a process is not deleted (remove the process instead).</summary>
    public Task<VersionRemovalResult> DeleteVersionAsync(Guid versionId, string? reason, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        RequireReason(reason);
        var v = await routing.GetVersionAsync(versionId, t) ?? throw AppException.NotFound("VERSION_NOT_FOUND", "Process version not found.");
        var p = await ProcessOf(v, t);
        if (!v.IsEditable) throw AppException.Rejected("VERSION_NOT_DELETABLE", "Only a draft or validated version can be deleted; a published or disabled version is kept.");
        if (await routing.IsVersionUsedByOrderAsync(v.Id, t)) throw AppException.Rejected("VERSION_IN_USE", "A transport order uses this version; it is kept.");
        if ((await routing.ListVersionsAsync(p.Id, t)).Count <= 1) throw AppException.Rejected("VERSION_ONLY_ONE", "This is the only version of the process; remove the process instead.");
        routing.Remove(v);
        return new VersionRemovalResult(p.Id, p.Code, v.Number);
    }, ct);

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required.");
        if (reason.Length > 500) throw AppException.Validation("REASON_TOO_LONG", "The reason has at most 500 characters.");
    }

    private async Task<ProcessRemovalDto> PlanRemovalAsync(BusinessProcess p, IReadOnlyList<BusinessProcessVersion> versions, CancellationToken ct)
    {
        var orders = await routing.CountOrdersOfProcessAsync(p.Id, false, ct);
        var open = orders == 0 ? 0 : await routing.CountOrdersOfProcessAsync(p.Id, true, ct);
        ProcessRemovalDto Blocked(string code, string message) => new(ProcessRemovalActions.None, code, message, orders, open);
        if (ReferenceProcessCatalog.Load().Any(r => r.Protected && string.Equals(r.Code, p.Code, StringComparison.OrdinalIgnoreCase)))
            return Blocked("PROCESS_PROTECTED", $"{p.Code} is an approved reference process: it is not removed. It can be switched off.");
        if (open > 0) return Blocked("PROCESS_HAS_OPEN_ORDERS", $"{p.Code} has {open} unfinished transport order(s): finish or cancel them first.");
        if (orders > 0)
            return p.Enabled ? new(ProcessRemovalActions.Disable, null, null, orders, 0)
                : Blocked("PROCESS_ALREADY_DISABLED", $"{p.Code} has {orders} transport order(s), so it is kept; it is already switched off.");
        if (p.Enabled && versions.Any(v => v.State == ProcessConfigState.Published))
            return Blocked("PROCESS_DISABLE_FIRST", $"{p.Code} is published and switched on: switch it off first, then remove it.");
        return new(ProcessRemovalActions.Delete, null, null, 0, 0);
    }

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

    /// <summary>
    /// CR-041: brings an EXISTING default process (imported earlier and therefore never touched by the seeder) to the confirmed catalog definition: a new Draft version with the catalog
    /// steps, and the review flag as the catalog states it. Only processes of the default catalog; nothing is validated, published or set Ready here; no version is removed.
    /// </summary>
    public Task<VersionDto> ApplyCatalogAsync(Guid processId, IReadOnlyList<ReferenceProcess> catalog, string? reason, string? user, CancellationToken ct = default) => uow.ExecuteAsync(async t =>
    {
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Validation("REASON_REQUIRED", "A reason is required.");
        var p = await routing.GetProcessAsync(processId, t) ?? throw AppException.NotFound("PROCESS_NOT_FOUND", "Process not found.");
        var item = DefaultItem(catalog, p.Code) ?? throw AppException.Rejected("PROCESS_NOT_IN_CATALOG", $"{p.Code} is not a default process of the confirmed catalog.");
        if (await CatalogStatusAsync(p, catalog, t) == CatalogStatuses.Matches) throw AppException.Rejected("CATALOG_ALREADY_APPLIED", $"{p.Code} already matches the catalog.");
        var all = await routing.ListVersionsAsync(p.Id, t);
        if (all.Any(x => x.State is ProcessConfigState.Draft or ProcessConfigState.Validated && x.Note == CatalogNote))
            throw AppException.Conflict("CATALOG_DRAFT_EXISTS", "A draft made from the catalog already exists: validate and publish it, or edit it.");
        var published = await routing.GetPublishedVersionAsync(p.Id, t) is not null;
        if (published)
        {   // CR-042 (OQ-42-01): a draft must not change what the published version does - header and review flag stay as they are, or nothing is created
            if (p.ReviewBlocked != item.ReviewBlocked || !SameHeader(p, DefinitionMapping.ParseRequestType(item.RequestType), DefinitionMapping.ParseRackKind(item.RackKind), item.TriggerLocationCodes))
                throw HeaderLocked(p);
        }
        else
        {
            p.MarkReview(item.ReviewBlocked, item.ReviewReason, item.SourceReference, clock.UtcNow);
            p.Update(item.Name, item.Description, DefinitionMapping.ParseRequestType(item.RequestType), DefinitionMapping.ParseRackKind(item.RackKind), item.TriggerLocationCodes, p.Enabled, clock.UtcNow);
        }
        var v = new BusinessProcessVersion(p.Id, all.Count == 0 ? 1 : all.Max(x => x.Number) + 1, user, CatalogNote, clock.UtcNow);
        v.ReplaceDefinition(item.Definition.FirstStepKey, item.Definition.Steps.Select(s => s.ToStep()).ToList(), CatalogNote, clock.UtcNow);
        routing.Add(v);
        return ToVersionDto(p, v);
    }, ct);

    private const string CatalogNote = "Draft from the confirmed default catalog";

    private static bool SameHeader(BusinessProcess p, RequestType type, RackKind kind, IEnumerable<string> triggers) =>
        p.RequestType == type && p.RackKind == kind && p.TriggerLocationCodes.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(triggers.Select(x => x.Trim()).Where(x => x.Length > 0));

    private static AppException HeaderLocked(BusinessProcess p) => AppException.Rejected("PROCESS_HEADER_LOCKED",
        $"Process {p.Code} has a Published version: its request type, rack kind, trigger locations and review flag cannot be changed. Disable the published version first, or create a new process.");

    /// <summary>CR-042 (OQ-42-02): a process the confirmed default catalog marks as not for publication cannot be published or set Ready until the catalog allows it.</summary>
    private static void EnsureApprovedForUse(BusinessProcess p)
    {
        if (DefaultItem(ReferenceProcessCatalog.Load(), p.Code) is { DefaultPublish: false } item)
            throw AppException.Rejected("PROCESS_NOT_APPROVED_FOR_PUBLICATION", $"Process {p.Code} is not approved for publication in the default catalog. {item.DraftReason}");
    }

    private static ReferenceProcess? DefaultItem(IReadOnlyList<ReferenceProcess> catalog, string code) =>
        catalog.FirstOrDefault(x => x.DefaultSinceVersion is not null && string.Equals(x.Code.Trim(), code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Compares the version that would run (the Published one, else the newest) with the catalog: steps, header and review flag. Names and notes are not compared.</summary>
    private async Task<string> CatalogStatusAsync(BusinessProcess p, IReadOnlyList<ReferenceProcess> catalog, CancellationToken ct)
    {
        if (DefaultItem(catalog, p.Code) is not { } item) return CatalogStatuses.NotDefault;
        var versions = await routing.ListVersionsAsync(p.Id, ct);
        var current = versions.FirstOrDefault(v => v.State == ProcessConfigState.Published) ?? versions.OrderByDescending(v => v.Number).FirstOrDefault();
        if (current is null || p.ReviewBlocked != item.ReviewBlocked || p.RequestType != DefinitionMapping.ParseRequestType(item.RequestType) || p.RackKind != DefinitionMapping.ParseRackKind(item.RackKind)
            || !p.TriggerLocationCodes.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(item.TriggerLocationCodes.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            return CatalogStatuses.Differs;
        var full = await routing.GetVersionAsync(current.Id, ct) ?? current;
        var expected = new BusinessProcessVersion(p.Id, 1, null, null, clock.UtcNow);
        expected.ReplaceDefinition(item.Definition.FirstStepKey, item.Definition.Steps.Select(s => s.ToStep()).ToList(), null, clock.UtcNow);
        static string Shape(VersionDefinition d) => JsonSerializer.Serialize(new VersionDefinition(d.FirstStepKey, d.Steps.OrderBy(s => s.Order).ToList(), null), Json);
        return Shape(full.ToDefinition()) == Shape(expected.ToDefinition()) ? CatalogStatuses.Matches : CatalogStatuses.Differs;
    }

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
                v.PublishedBy, v.DisabledAt, v.Note, v.Steps.Count, v.Readiness.ToString())).ToList(), p.ProductModelId, p.RequiresReadiness, p.QueueScope,
            await CatalogStatusAsync(p, ReferenceProcessCatalog.Load(), ct));
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
