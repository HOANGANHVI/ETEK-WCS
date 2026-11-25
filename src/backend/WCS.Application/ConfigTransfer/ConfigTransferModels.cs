using System.Text.Json.Nodes;
using WCS.Application.Common;

namespace WCS.Application.ConfigTransfer;

/// <summary>CR-026 – shapes shared by the package, the planner and the API. Records are identified by their NATURAL key (Code, StationId, …): the package never carries a Guid.</summary>
public static class ConfigGroupIds
{
    public const string Location = "Location";
    public const string Agv = "Agv";
    public const string PlcStation = "PlcStation";
    public const string BusinessProcess = "BusinessProcess";
    /// <summary>CR-036 (A.1): product models. Records are keyed by CODE (never by Guid).</summary>
    public const string ProductModel = "ProductModel";
}

/// <summary>One configuration record in its canonical form: natural key + configuration fields only (no Guid, no row version, no operational / approval / secret field).</summary>
public sealed record ConfigRecord(string Key, JsonObject Fields);

public sealed record GroupInfo(string Id, string File, IReadOnlyList<string> DependsOn, bool UpdateOnly, int TargetCount);

public sealed record ManifestGroup(string Group, string File, int Count, string Sha256);
public sealed record ManifestExcluded(string Name, string Reason);
public sealed record FileConfigInfo(string Sha256, IReadOnlyList<string> Sections);

public sealed record PackageManifest(string FormatVersion, DateTimeOffset ExportedAt, string ExportedBy, string AppVersion, string SchemaMigration, string SourceSite,
    IReadOnlyList<ManifestGroup> Groups, IReadOnlyList<ManifestExcluded> Excluded, IReadOnlyList<string> Warnings, IReadOnlyList<string> UnresolvedReferences,
    FileConfigInfo FileConfig);

/// <summary>A group of the package, parsed: the records exactly as stored.</summary>
public sealed record PackageContent(PackageManifest Manifest, IReadOnlyDictionary<string, IReadOnlyList<ConfigRecord>> Groups, string PackageSha256);

public enum PlanAction { Create, Update, Unchanged, Skip, Conflict }

public sealed record FieldChange(string Field, string? From, string? To);

/// <param name="HeaderChange">A Business Process whose Header (name, description, trigger locations) changes: the Admin must confirm before Apply.</param>
public sealed record PlanItem(string Group, string Key, PlanAction Action, IReadOnlyList<FieldChange> Changes, string? ConflictCode, string? ConflictMessage, bool HeaderChange = false);

public sealed record RecordRef(string Group, string Key);

/// <summary>Which part of the package is imported. <see cref="Groups"/> null = every group of the package; <see cref="Exclude"/> lists single records left out (they show as Skip).</summary>
public sealed record Selection(IReadOnlyList<string>? Groups = null, IReadOnlyList<RecordRef>? Exclude = null);

public sealed record Blocker(string Code, string Message, int Count, IReadOnlyList<string> Samples);

public sealed record ApplyReadiness(bool MaintenanceOn, bool BackupReady, string? BackupProblem, IReadOnlyList<Blocker> Blockers, bool CanApply);

public sealed record PlanSummary(int Create, int Update, int Unchanged, int Skip, int Conflict);

public sealed record ImportPlan(Guid ImportId, IReadOnlyList<string> GroupOrder, IReadOnlyList<PlanItem> Items, PlanSummary Summary, IReadOnlyList<string> Warnings,
    ApplyReadiness Readiness, string PlanHash, string? PreviewToken, DateTimeOffset? PreviewExpiresAt);

public sealed record ImportSession(Guid Id, string PackageSha256, string Status, string Actor, DateTimeOffset CreatedAt, string? PlanHash, string? PreviewToken,
    DateTimeOffset? PreviewExpiresAt, DateTimeOffset? AppliedAt, string? ResultJson, string? BackupFile, string? ErrorCode);

public sealed record UploadResult(Guid ImportId, string PackageSha256, PackageManifest Manifest, IReadOnlyList<string> Warnings);

public sealed record ApplyResult(Guid ImportId, bool AlreadyApplied, int Created, int Updated, int Unchanged, int Skipped, int ConflictsSkipped, string BackupFile,
    IReadOnlyList<PlanItem> Applied, bool MaintenanceStillOn);

public sealed record ExportDependencyReport(IReadOnlyList<string> Selected, IReadOnlyList<string> Missing, IReadOnlyDictionary<string, IReadOnlyList<string>> Reasons);

/// <summary>Errors that carry structured details to the API response (for example the list of blockers of an Apply).</summary>
public interface IHasErrorDetails { object? Details { get; } }

public sealed class ConfigTransferException(ErrorKind kind, string code, string message, object? details = null) : AppException(kind, code, message), IHasErrorDetails
{
    public object? Details { get; } = details;
}
