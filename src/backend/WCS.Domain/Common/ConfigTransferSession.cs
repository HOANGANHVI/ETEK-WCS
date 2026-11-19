namespace WCS.Domain.Common;

public enum ConfigImportStatus { Uploaded = 1, Previewed = 2, Applied = 3, Failed = 4 }

/// <summary>
/// CR-026: bookkeeping of one configuration import (upload, preview, apply). It stores hashes, the preview token, counts and file names - NEVER the content of the
/// package. The package itself and the backup are files in the configured persistent folder.
/// </summary>
public class ConfigTransferSession
{
    private ConfigTransferSession() { }

    public ConfigTransferSession(string packageSha256, string actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); PackageSha256 = packageSha256; PackageFile = $"incoming/{Id:N}.zip"; Actor = actor; CreatedAt = now; UpdatedAt = now; Status = ConfigImportStatus.Uploaded;
    }

    public Guid Id { get; private set; }
    public string PackageSha256 { get; private set; } = default!;
    /// <summary>File name below the persistent folder (incoming/&lt;id&gt;.zip).</summary>
    public string PackageFile { get; private set; } = default!;
    public ConfigImportStatus Status { get; private set; }
    public string Actor { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? SelectionJson { get; private set; }
    public string? PlanHash { get; private set; }
    public string? PreviewToken { get; private set; }
    public DateTimeOffset? PreviewExpiresAt { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    /// <summary>Counts and the applied keys (no field values).</summary>
    public string? ResultJson { get; private set; }
    public string? BackupFile { get; private set; }
    public string? ErrorCode { get; private set; }
    public uint Version { get; private set; }

    public void MarkPreviewed(string selectionJson, string planHash, string token, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (Status == ConfigImportStatus.Applied) throw new DomainException("IMPORT_ALREADY_APPLIED", "This import was already applied.");
        Status = ConfigImportStatus.Previewed; SelectionJson = selectionJson; PlanHash = planHash; PreviewToken = token; PreviewExpiresAt = expiresAt; ErrorCode = null; UpdatedAt = now;
    }

    public void MarkApplied(string resultJson, string backupFile, DateTimeOffset now)
    {
        Status = ConfigImportStatus.Applied; ResultJson = resultJson; BackupFile = backupFile; AppliedAt = now; PreviewToken = null; ErrorCode = null; UpdatedAt = now;
    }

    public void MarkFailed(string errorCode, DateTimeOffset now)
    {
        if (Status == ConfigImportStatus.Applied) return;
        Status = ConfigImportStatus.Failed; ErrorCode = errorCode.Length > 100 ? errorCode[..100] : errorCode; PreviewToken = null; UpdatedAt = now;
    }
}
