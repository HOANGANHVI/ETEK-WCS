using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Auth;
using WCS.Application.Common;
using WCS.Domain.Common;
using WCS.Domain.Users;

namespace WCS.Application.ConfigTransfer;

public sealed record ApplyRequest(string PreviewToken, bool ConfirmHeaderChanges = false, bool SkipConflicts = false);

/// <summary>
/// CR-026: Export and Import of configuration. Every write goes through the existing business services inside ONE batch transaction (<see cref="IUnitOfWork.ExecuteBatchAsync{T}"/>):
/// no direct database write, no PLC call, no PLC runtime reload, no Publish, no deletion, and Maintenance is never switched off here.
/// </summary>
public sealed class ConfigTransferService(ConfigGroupCatalog catalog, IUnitOfWork uow, IMaintenanceGate maintenance, ActiveWorkGate activeWork, ConfigBackupService backup,
    IConfigTransferStore store, IAuditLog audit, IClock clock, IOptions<ConfigTransferOptions> options, IConfiguration configuration, ILogger<ConfigTransferService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static readonly string[] FileConfigSections = ["ProcessFlows", "RouteEngine", "Scheduler"];
    private ConfigTransferOptions O => options.Value;

    // ================================================================================================ groups and export
    public async Task<IReadOnlyList<GroupInfo>> ListGroupsAsync(CancellationToken ct = default)
    {
        var list = new List<GroupInfo>();
        foreach (var id in catalog.Order(catalog.All.Select(g => g.Id)))
        {
            var g = catalog.Get(id);
            list.Add(new GroupInfo(g.Id, g.File, g.DependsOn, g.UpdateOnly, (await g.ReadAsync(ct)).Count));
        }
        return list;
    }

    public ExportDependencyReport CheckExport(IReadOnlyList<string> groupIds)
    {
        Require(groupIds);
        return catalog.Dependencies(groupIds);
    }

    public async Task<(string FileName, byte[] Data, PackageManifest Manifest)> ExportAsync(IReadOnlyList<string> groupIds, ActorInfo actor, CancellationToken ct = default)
    {
        Require(groupIds);
        var order = catalog.Order(groupIds);
        var report = catalog.Dependencies(groupIds);
        var groups = new List<(string, string, IReadOnlyList<ConfigRecord>)>();
        // one consistent read: the groups are read inside one unit of work
        await uow.ExecuteSnapshotAsync(async c =>
        {
            foreach (var id in order) groups.Add((id, catalog.Get(id).File, await catalog.Get(id).ReadAsync(c)));
            return 0;
        }, ct);
        var warnings = new List<string>
        {
            "Approval state (Approved / WriteAllowed / WriteApproval), passwords, tokens and RCS secrets are never part of a package.",
            "Racks, tasks, orders, station transactions, logs and user accounts are not part of a package.",
        };
        var unresolved = report.Missing.Select(m => $"{m} (needed by {string.Join(", ", report.Reasons[m])}) is not in this package").ToList();
        var manifest = await NewManifestAsync(actor.Username, warnings, unresolved, ct);
        var (data, written) = ConfigPackage.WriteWithManifest(manifest, groups);
        manifest = written;
        await audit.WriteAsync(new AuditRecord("CONFIG_EXPORT", actor.Username, actor.Role.ToString(), "ConfigPackage", null, AuditResult.Success,
            $"groups: {string.Join(",", order)}; records: {groups.Sum(g => g.Item3.Count)}; unresolved dependencies: {unresolved.Count}", IpAddress: actor.IpAddress), ct);
        return ($"wcs-config-{clock.UtcNow:yyyyMMdd-HHmmss}.zip", data, manifest);
    }

    private async Task<PackageManifest> NewManifestAsync(string by, IReadOnlyList<string> warnings, IReadOnlyList<string> unresolved, CancellationToken ct) =>
        new(ConfigPackage.FormatVersion, clock.UtcNow, by, Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown", await store.LatestMigrationAsync(ct), O.SourceSite ?? "",
            [], [
                new ManifestExcluded("Rack, reservation, inventory transaction", "Inventory is owned by WCS and is operational data."),
                new ManifestExcluded("Task, order, leg, station transaction, station request", "Operational data."),
                new ManifestExcluded("Logs, audit, PLC communication log", "History."),
                new ManifestExcluded("User accounts", "Credentials and personal data (CR-026 D4)."),
                new ManifestExcluded("PLC connections, IO points, RCS settings", "Not in Phase 2 (CR-026)."),
                new ManifestExcluded("appsettings (ProcessFlows, RouteEngine, Scheduler)", "File configuration is outside the import scope (CR-026 D3); only its fingerprint is recorded."),
            ], warnings, unresolved, FileConfigFingerprint());

    private FileConfigInfo FileConfigFingerprint()
    {
        var lines = new List<string>();
        foreach (var s in FileConfigSections)
            foreach (var kv in configuration.GetSection(s).AsEnumerable(true).Where(x => x.Value is not null).OrderBy(x => x.Key, StringComparer.Ordinal)) lines.Add($"{s}:{kv.Key}={kv.Value}");
        return new FileConfigInfo(ConfigPackage.Sha256(string.Join('\n', lines)), FileConfigSections);
    }

    private void Require(IReadOnlyList<string> groupIds)
    {
        if (groupIds is null || groupIds.Count == 0) throw AppException.Validation("GROUPS_REQUIRED", "Select at least one configuration group.");
        foreach (var id in groupIds) catalog.Get(id);
    }

    // ================================================================================================ upload
    public async Task<UploadResult> UploadAsync(byte[] data, ActorInfo actor, CancellationToken ct = default)
    {
        var problem = backup.Problem();
        if (problem is not null) throw AppException.Rejected("BACKUP_NOT_READY", problem);
        var package = Parse(data);
        var withFile = new ConfigTransferSession(package.PackageSha256, actor.Username, clock.UtcNow);
        backup.SaveIncoming(withFile.PackageFile, data);
        await uow.ExecuteAsync(t => { store.Add(withFile); return Task.FromResult(0); }, ct);
        await audit.WriteAsync(new AuditRecord("CONFIG_IMPORT_UPLOAD", actor.Username, actor.Role.ToString(), "ConfigImport", withFile.Id.ToString("D"), AuditResult.Success,
            $"sha256 {package.PackageSha256[..16]}; groups: {string.Join(",", package.Groups.Keys)}", IpAddress: actor.IpAddress), ct);
        return new UploadResult(withFile.Id, package.PackageSha256, package.Manifest, await PackageWarningsAsync(package, ct));
    }

    private PackageContent Parse(byte[] data) => ConfigPackage.Read(data, catalog.Files, O, (group, rec) => catalog.Has(group) ? catalog.Get(group).Validate(rec) : [$"unknown group {group}"]);

    private async Task<IReadOnlyList<string>> PackageWarningsAsync(PackageContent package, CancellationToken ct)
    {
        var w = new List<string>(package.Manifest.Warnings ?? []);
        if (!string.Equals(package.Manifest.FileConfig?.Sha256, FileConfigFingerprint().Sha256, StringComparison.OrdinalIgnoreCase))
            w.Add("FILE_CONFIG_DIFFERS: appsettings ProcessFlows / RouteEngine / Scheduler differ between the source system and this one. They are not imported and are never edited here.");
        var mine = await store.LatestMigrationAsync(ct);
        if (string.CompareOrdinal(package.Manifest.SchemaMigration, mine) > 0)
            w.Add($"SCHEMA_NEWER: the package comes from a newer database schema ({package.Manifest.SchemaMigration}) than this system ({mine}).");
        foreach (var u in package.Manifest.UnresolvedReferences ?? []) w.Add("UNRESOLVED_AT_EXPORT: " + u);
        return w;
    }

    // ================================================================================================ plan / preview
    private sealed record Built(IReadOnlyList<string> Order, IReadOnlyList<PlanItem> Items, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ConfigRecord>> Target,
        IReadOnlyDictionary<string, ConfigRecord> PackageByKey, IReadOnlyList<string> Warnings, string Hash);

    private async Task<Built> BuildAsync(PackageContent package, Selection selection, CancellationToken ct)
    {
        var selected = (selection.Groups ?? package.Groups.Keys.ToList()).Where(g => package.Groups.ContainsKey(g)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var g in selected) catalog.Get(g);
        var order = catalog.Order(selected);
        var excluded = new HashSet<(string, string)>((selection.Exclude ?? []).Select(r => (r.Group, r.Key)));

        var target = new Dictionary<string, IReadOnlyDictionary<string, ConfigRecord>>();
        foreach (var g in catalog.All) target[g.Id] = (await g.ReadAsync(ct)).ToDictionary(r => r.Key, StringComparer.Ordinal);
        var chosen = new Dictionary<string, IReadOnlyDictionary<string, ConfigRecord>>();
        foreach (var g in catalog.All)
            chosen[g.Id] = order.Contains(g.Id) ? package.Groups[g.Id].Where(r => !excluded.Contains((g.Id, r.Key))).ToDictionary(r => r.Key, StringComparer.Ordinal) : new Dictionary<string, ConfigRecord>();
        var ctx = new PlanContext(target, chosen);

        var items = new List<PlanItem>(); var byKey = new Dictionary<string, ConfigRecord>();
        foreach (var id in order)
        {
            var g = catalog.Get(id);
            foreach (var rec in package.Groups[id].OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                byKey[$"{id}/{rec.Key}"] = rec;
                if (excluded.Contains((id, rec.Key))) { items.Add(new PlanItem(id, rec.Key, PlanAction.Skip, [], null, "Left out by the user.")); continue; }
                var tgt = target[id].GetValueOrDefault(rec.Key);
                IReadOnlyList<FieldChange> changes = [];
                PlanAction action;
                if (tgt is null) action = PlanAction.Create;
                else { changes = g.Diff(rec, tgt); action = changes.Count == 0 ? PlanAction.Unchanged : PlanAction.Update; }
                if (action == PlanAction.Create && !g.UpdateOnly) changes = rec.Fields.Select(kv => new FieldChange(kv.Key, null, kv.Value is null ? null : ConfigPackage.Render(kv.Value))).Where(c => c.Field != "definition" && c.Field != "definitionSource").ToList();
                (string Code, string Message)? conflict = null;
                if (action != PlanAction.Unchanged) conflict = await g.CheckAsync(rec, tgt, ctx, ct);
                if (conflict is { } c) { items.Add(new PlanItem(id, rec.Key, PlanAction.Conflict, changes, c.Code, c.Message)); continue; }
                var header = id == ConfigGroupIds.BusinessProcess && action == PlanAction.Update && changes.Any(x => BusinessProcessConfigGroup.HeaderFields.Contains(x.Field));
                items.Add(new PlanItem(id, rec.Key, action, changes, null, null, header));
            }
        }
        var warnings = new List<string>();
        if (!string.Equals(package.Manifest.FileConfig?.Sha256, FileConfigFingerprint().Sha256, StringComparison.OrdinalIgnoreCase))
            warnings.Add("FILE_CONFIG_DIFFERS: appsettings ProcessFlows / RouteEngine / Scheduler differ between the source system and this one. They are not imported and are never edited here.");
        warnings.AddRange(package.Manifest.Warnings ?? []);

        // the hash binds the package, the selection, every decision and the exact current state of every record the import would touch
        var basis = JsonSerializer.Serialize(new
        {
            package = package.PackageSha256, selection = selection with { Groups = selected },
            items = items.Select(i => new { i.Group, i.Key, i.Action, i.ConflictCode, changes = i.Changes.Select(c => new { c.Field, c.From, c.To }),
                target = i.Action is PlanAction.Update or PlanAction.Create or PlanAction.Conflict ? target[i.Group].GetValueOrDefault(i.Key)?.Fields.ToJsonString() : null }),
        }, Json);
        return new Built(order, items, target, byKey, warnings, ConfigPackage.Sha256(basis));
    }

    public async Task<ImportPlan> PreviewAsync(Guid importId, Selection? selection, ActorInfo actor, CancellationToken ct = default)
    {
        selection ??= new Selection();
        var session = await LoadAsync(importId, ct);
        if (session.Status == ConfigImportStatus.Applied) throw AppException.Conflict("IMPORT_ALREADY_APPLIED", "This import was already applied.");
        var package = LoadPackage(session);
        var built = await BuildAsync(package, selection, ct);
        var token = Guid.NewGuid().ToString("N"); var expires = clock.UtcNow.AddMinutes(O.PreviewMinutes);
        await uow.ExecuteAsync(async t =>
        {
            var s = await store.GetAsync(importId, t) ?? throw AppException.NotFound("IMPORT_NOT_FOUND", "Import not found.");
            s.MarkPreviewed(JsonSerializer.Serialize(selection, Json), built.Hash, token, expires, clock.UtcNow);
            return 0;
        }, ct);
        var plan = new ImportPlan(importId, built.Order, built.Items, Summarize(built.Items), built.Warnings, await ReadinessAsync(ct), built.Hash, token, expires);
        await audit.WriteAsync(new AuditRecord("CONFIG_IMPORT_PREVIEW", actor.Username, actor.Role.ToString(), "ConfigImport", importId.ToString("D"), AuditResult.Success,
            $"create {plan.Summary.Create}, update {plan.Summary.Update}, unchanged {plan.Summary.Unchanged}, skip {plan.Summary.Skip}, conflict {plan.Summary.Conflict}", IpAddress: actor.IpAddress), ct);
        return plan;
    }

    private static PlanSummary Summarize(IReadOnlyList<PlanItem> items) => new(
        items.Count(i => i.Action == PlanAction.Create), items.Count(i => i.Action == PlanAction.Update), items.Count(i => i.Action == PlanAction.Unchanged),
        items.Count(i => i.Action == PlanAction.Skip), items.Count(i => i.Action == PlanAction.Conflict));

    private async Task<ApplyReadiness> ReadinessAsync(CancellationToken ct)
    {
        var on = await maintenance.IsEnabledAsync(ct);
        var problem = backup.Problem();
        var blockers = (await activeWork.CheckAsync(ct)).ToList();
        if (!on) blockers.Insert(0, new Blocker("MAINTENANCE_REQUIRED", "Maintenance mode is off: switch it on before Apply (new work must be stopped first).", 1, []));
        if (problem is not null) blockers.Add(new Blocker("BACKUP_NOT_READY", problem, 1, []));
        return new ApplyReadiness(on, problem is null, problem, blockers, blockers.Count == 0);
    }

    // ================================================================================================ apply
    public async Task<ApplyResult> ApplyAsync(Guid importId, ApplyRequest request, ActorInfo actor, CancellationToken ct = default)
    {
        var first = await LoadAsync(importId, ct);
        if (first.Status == ConfigImportStatus.Applied) return StoredResult(first);                       // idempotent: the same import is never applied twice
        if (first.Status != ConfigImportStatus.Previewed || first.PreviewToken is null) throw AppException.Conflict("IMPORT_NOT_PREVIEWED", "Preview the import before applying it.");
        if (!string.Equals(first.PreviewToken, request.PreviewToken, StringComparison.Ordinal)) throw AppException.Rejected("PREVIEW_TOKEN_INVALID", "The preview token does not match this import. Preview again.");
        if (first.PreviewExpiresAt is { } exp && clock.UtcNow > exp) throw AppException.Rejected("PREVIEW_EXPIRED", "The preview has expired. Preview again.");
        var problem = backup.Problem();
        if (problem is not null) throw AppException.Rejected("BACKUP_NOT_READY", problem);
        var package = LoadPackage(first);
        var selection = JsonSerializer.Deserialize<Selection>(first.SelectionJson ?? "{}", Json) ?? new Selection();

        var writing = false;
        try
        {
            return await uow.ExecuteBatchAsync(async c =>
            {
                writing = false;
                var session = await store.GetAsync(importId, c) ?? throw AppException.NotFound("IMPORT_NOT_FOUND", "Import not found.");
                if (session.Status == ConfigImportStatus.Applied) return StoredResult(session);           // a concurrent Apply of the same import already finished

                // 1. Maintenance must be ON, read with the row lock: it also keeps an Admin from switching it off while this transaction runs
                if (!await maintenance.HoldNewWorkAsync(c))
                    throw new ConfigTransferException(ErrorKind.Rejected, "MAINTENANCE_REQUIRED", "Switch Maintenance on before applying a configuration import.",
                        new[] { new Blocker("MAINTENANCE_REQUIRED", "Maintenance mode is off.", 1, []) });
                // 2. nothing may be running that the change could affect (system-wide); nothing is cancelled to pass this check
                var blockers = await activeWork.CheckAsync(c);
                if (blockers.Count > 0)
                    throw new ConfigTransferException(ErrorKind.Conflict, "APPLY_BLOCKED", "Apply refused: " + string.Join("; ", blockers.Select(b => $"{b.Message} ({b.Count})")), blockers);
                // 3. the Preview token is bound to the package AND to the current database state
                var built = await BuildAsync(package, selection, c);
                if (!string.Equals(built.Hash, session.PlanHash, StringComparison.Ordinal))
                    throw AppException.Conflict("PLAN_STALE", "The configuration changed since the Preview. Preview again.");
                var conflicts = built.Items.Where(i => i.Action == PlanAction.Conflict).ToList();
                if (conflicts.Count > 0 && !request.SkipConflicts)
                    throw new ConfigTransferException(ErrorKind.Rejected, "PLAN_HAS_CONFLICTS", $"{conflicts.Count} record(s) are in conflict. Exclude them or confirm that they are skipped.", conflicts);
                var header = built.Items.Where(i => i.HeaderChange).ToList();
                if (header.Count > 0 && !request.ConfirmHeaderChanges)
                    throw new ConfigTransferException(ErrorKind.Rejected, "HEADER_CHANGES_NOT_CONFIRMED", "Business Process header changes (name, description, trigger locations) must be confirmed.", header);

                var toApply = built.Items.Where(i => i.Action is PlanAction.Create or PlanAction.Update).ToList();
                var backupFile = "";
                if (toApply.Count > 0)
                {
                    // 4. backup of what is about to change, verified before the first configuration write
                    var snapshot = built.Order.Select(g => (g, catalog.Get(g).File, (IReadOnlyList<ConfigRecord>)toApply.Where(i => i.Group == g && i.Action == PlanAction.Update)
                        .Select(i => built.Target[g][i.Key]).ToList())).Where(x => x.Item3.Count > 0).ToList();
                    var created = toApply.Where(i => i.Action == PlanAction.Create).Select(i => $"{i.Group}/{i.Key}").ToList();
                    var manifest = await NewManifestAsync(actor.Username, [$"Backup taken before import {importId:D}.", .. created.Select(k => "CREATED_BY_IMPORT: " + k)], [], c);
                    backupFile = backup.WriteAndVerify(importId, manifest, snapshot, catalog.Files);
                }
                // 5. write through the existing services, group by group in dependency order (one transaction: any failure rolls everything back)
                writing = true;
                var ctx = new ApplyContext(importId, actor);
                foreach (var g in built.Order)
                {
                    var rows = toApply.Where(i => i.Group == g).Select(i => (i, built.PackageByKey[$"{g}/{i.Key}"], built.Target[g].GetValueOrDefault(i.Key))).ToList();
                    if (rows.Count > 0) await catalog.Get(g).ApplyAsync(rows, ctx, c);
                }
                var result = new ApplyResult(importId, false, toApply.Count(i => i.Action == PlanAction.Create), toApply.Count(i => i.Action == PlanAction.Update),
                    built.Items.Count(i => i.Action == PlanAction.Unchanged), built.Items.Count(i => i.Action == PlanAction.Skip), conflicts.Count, backupFile, toApply, true);
                audit.Enlist(new AuditRecord("CONFIG_IMPORT_APPLY", actor.Username, actor.Role.ToString(), "ConfigImport", importId.ToString("D"), AuditResult.Success,
                    $"created {result.Created}, updated {result.Updated}, unchanged {result.Unchanged}, skipped {result.Skipped}, conflicts skipped {result.ConflictsSkipped}; backup {backupFile}", IpAddress: actor.IpAddress));
                session.MarkApplied(JsonSerializer.Serialize(result with { Applied = toApply.Select(i => i with { Changes = [] }).ToList() }, Json), backupFile, clock.UtcNow);
                return result;
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var code = ex switch { AppException a => a.Code, DomainException d => d.Code, _ => "ERROR" };
            logger.LogWarning("Configuration import {ImportId} was not applied: {Code} {Message}. {EventCode}", importId, code, ex.Message, "CONFIG_IMPORT_NOT_APPLIED");
            // The transaction was rolled back: the configuration is exactly as it was. A refusal before the first write keeps the Preview usable; a failure while writing needs a new Preview.
            if (writing) await Safe(() => uow.ExecuteAsync(async t => { (await store.GetAsync(importId, t))?.MarkFailed(code, clock.UtcNow); return 0; }, CancellationToken.None));
            await audit.WriteAsync(new AuditRecord("CONFIG_IMPORT_APPLY", actor.Username, actor.Role.ToString(), "ConfigImport", importId.ToString("D"), AuditResult.Failed,
                $"{code}: {ex.Message}", IpAddress: actor.IpAddress), CancellationToken.None);
            throw;
        }
    }

    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Recording the failed import did not succeed"); }
    }

    private static ApplyResult StoredResult(ConfigTransferSession s) =>
        (JsonSerializer.Deserialize<ApplyResult>(s.ResultJson ?? "{}", Json) ?? throw new InvalidOperationException("The stored import result is missing.")) with { AlreadyApplied = true };

    // ================================================================================================ read
    public async Task<ImportSession> GetAsync(Guid importId, CancellationToken ct = default)
    {
        var s = await LoadAsync(importId, ct);
        return new ImportSession(s.Id, s.PackageSha256, s.Status.ToString(), s.Actor, s.CreatedAt, s.PlanHash, null, s.PreviewExpiresAt, s.AppliedAt, s.ResultJson, s.BackupFile, s.ErrorCode);
    }

    private Task<ConfigTransferSession> LoadAsync(Guid id, CancellationToken ct) => uow.ExecuteAsync(async t =>
        await store.GetAsync(id, t) ?? throw AppException.NotFound("IMPORT_NOT_FOUND", "Import not found."), ct);

    private PackageContent LoadPackage(ConfigTransferSession s)
    {
        var problem = backup.Problem();
        if (problem is not null) throw AppException.Rejected("BACKUP_NOT_READY", problem);
        var bytes = backup.ReadIncoming(s.PackageFile);
        if (!string.Equals(ConfigPackage.Sha256(bytes), s.PackageSha256, StringComparison.OrdinalIgnoreCase)) throw AppException.Conflict("PACKAGE_CHANGED", "The stored package no longer matches the uploaded one.");
        return Parse(bytes);
    }
}
