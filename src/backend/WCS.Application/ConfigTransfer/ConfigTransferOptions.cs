namespace WCS.Application.ConfigTransfer;

/// <summary>CR-026. The module is ON by default for Administrators (no setting needs to be changed); <see cref="Enabled"/> = false is only an emergency switch. <see cref="BackupPath"/> has NO default outside the Docker deployment (which sets /config-backups): Apply is refused until a persistent folder is configured (NEEDS_CONFIGURATION).</summary>
public sealed class ConfigTransferOptions
{
    public const string SectionName = "ConfigTransfer";
    public bool Enabled { get; set; } = true;
    /// <summary>Absolute path of a PERSISTENT folder (a Docker volume, never a temporary directory). Backups and uploaded packages are stored below it.</summary>
    public string? BackupPath { get; set; }
    public int MaxPackageMb { get; set; } = 20;
    public int MaxUncompressedMb { get; set; } = 50;
    public int PreviewMinutes { get; set; } = 30;
    public int MinFreeMb { get; set; } = 50;
    public string SourceSite { get; set; } = "";
}
