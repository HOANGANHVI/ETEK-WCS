using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Routing;
using WCS.Domain.Common;
using WCS.Domain.Routing;
using WCS.Domain.Users;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Seeding;

/// <summary>What one seed run did.</summary>
public sealed record ProcessSeedResult(IReadOnlyList<string> Published, IReadOnlyList<string> LeftDraft, IReadOnlyList<string> Skipped);

/// <summary>
/// CR-039: publishes the default transport processes of the embedded catalog (<c>reference-processes.json</c>, items with <c>defaultSinceVersion</c>) ONCE per catalog version.
/// Everything is data: no process code or location name is known here. Rules:
///   * insert only: a process code that already exists is never touched; a default an Admin disables or changes is never restored (its version is marked);
///   * publication runs the normal Publication Gate (<see cref="ProcessValidator"/>); a process that fails it, or whose trigger another Published process already answers,
///     stays a Draft with the reason;
///   * every default requires Operational Readiness (D-11): publishing never lets a request through; an Admin sets Ready, which re-checks the device conditions;
///   * processes, audit rows and the version marker are saved in ONE transaction under an advisory lock.
/// </summary>
public sealed class DefaultProcessSeeder(WcsDbContext db, IRoutingRepository routing, ProcessValidator validator, IClock clock, IOptions<SeedOptions> options, ILogger<DefaultProcessSeeder> logger)
{
    public const string CatalogAppliedAction = "SEED_PROCESS_CATALOG_APPLIED";
    public const string CatalogTargetType = "DefaultProcessCatalog";
    public const string Actor = "SEED";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<ProcessSeedResult> SeedAsync(CancellationToken ct = default, IReadOnlyList<ReferenceProcess>? catalogOverride = null)
    {
        var published = new List<string>(); var drafts = new List<string>(); var skipped = new List<string>();
        if (!options.Value.DefaultProcesses) return new(published, drafts, skipped);
        var items = (catalogOverride ?? ReferenceProcessCatalog.Load()).Where(x => x.DefaultSinceVersion is not null).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7390039)", ct);
        var applied = (await db.AuditLog.AsNoTracking().Where(a => a.Action == CatalogAppliedAction && a.TargetType == CatalogTargetType).Select(a => a.TargetId).ToListAsync(ct)).ToHashSet();

        foreach (var version in items.Select(x => x.DefaultSinceVersion!.Value).Distinct().Order())
        {
            if (applied.Contains(version.ToString())) continue;
            foreach (var r in items.Where(x => x.DefaultSinceVersion == version))
            {
                var code = r.Code.Trim().ToUpperInvariant();
                if (await routing.GetProcessByCodeAsync(code, ct) is not null) { skipped.Add(code); logger.LogInformation("Default process {Code} already exists: left as it is", code); continue; }

                var now = clock.UtcNow;
                var p = new BusinessProcess(code, r.Name, r.Description, DefinitionMapping.ParseRequestType(r.RequestType), DefinitionMapping.ParseRackKind(r.RackKind), r.TriggerLocationCodes, now);
                p.MarkReview(r.ReviewBlocked, r.ReviewReason, r.SourceReference, now);
                p.RequireReadiness(now);
                routing.Add(p);
                var v = new BusinessProcessVersion(p.Id, 1, Actor, $"Default catalog v{version}", now);
                v.ReplaceDefinition(r.Definition.FirstStepKey, r.Definition.Steps.Select(s => s.ToStep()).ToList(), r.Definition.Note, now);
                routing.Add(v);
                await db.SaveChangesAsync(ct);

                var problems = new List<string>();
                if (!r.DefaultPublish) problems.Add(r.DraftReason ?? "NOT_FOR_DEFAULT_PUBLICATION");
                else
                {
                    var outcome = await validator.ValidateAsync(p, v, ct);
                    problems.AddRange(outcome.Issues.Where(i => i.Severity == IssueSeverity.Error || i.Code == "AMBIGUOUS_TRIGGER").Select(i => i.Code).Distinct());
                    var issues = JsonSerializer.Serialize(outcome.Issues, Json);
                    if (problems.Count == 0) { v.MarkValidated(issues, clock.UtcNow); v.Publish(Actor, clock.UtcNow); }
                    else v.RecordFailedValidation(issues, clock.UtcNow);
                }
                if (problems.Count == 0) published.Add(code);
                else
                {
                    drafts.Add(code);
                    logger.LogWarning("Default process {Code} was left as a Draft: {Problems}", code, string.Join(", ", problems));
                }
                db.AuditLog.Add(new AuditEntry(clock.UtcNow, Actor, "System", problems.Count == 0 ? "SEED_PROCESS_PUBLISH" : "SEED_PROCESS_DRAFT", "BusinessProcess", code, AuditResult.Success,
                    problems.Count == 0 ? $"Default process {code} published by default process catalog v{version}; it accepts no request until Ready" : $"Default process {code} kept as Draft: {string.Join(", ", problems)}",
                    null, null, null));
                await db.SaveChangesAsync(ct);
            }
            db.AuditLog.Add(new AuditEntry(clock.UtcNow, Actor, "System", CatalogAppliedAction, CatalogTargetType, version.ToString(), AuditResult.Success,
                $"Default process catalog version {version} applied", null, null, null));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (published.Count > 0) logger.LogInformation("Default processes published (not Ready): {Codes}", string.Join(", ", published));
        return new(published, drafts, skipped);
    }
}
