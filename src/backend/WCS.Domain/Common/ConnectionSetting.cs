namespace WCS.Domain.Common;

public enum ConnectionKind { Plc = 1, Rcs = 2 }

/// <summary>Outcome of the last connectivity check. Simulated / NotExecuted never mean that a real device was reached.</summary>
public enum ConnectionTestOutcome { Success = 1, Failed = 2, Simulated = 3, NotExecuted = 4 }

/// <summary>
/// Persistent connection configuration of one external system (one row per <see cref="ConnectionKind"/>).
/// Non-secret fields live in <see cref="SettingsJson"/>; passwords/tokens exist only as <see cref="ProtectedSecrets"/> (encrypted) and are
/// never exposed. No value is pre-filled: a row is created only when an operator saves it.
/// </summary>
public class ConnectionSetting
{
    private ConnectionSetting() { }

    public ConnectionSetting(ConnectionKind kind, DateTimeOffset now)
    {
        Kind = kind;
        SettingsJson = "{}";
        UpdatedAt = now;
    }

    public ConnectionKind Kind { get; private set; }
    public bool Enabled { get; private set; }
    public string SettingsJson { get; private set; } = "{}";
    /// <summary>Encrypted blob with the secrets (opaque to the domain).</summary>
    public string? ProtectedSecrets { get; private set; }

    public DateTimeOffset? LastTestAt { get; private set; }
    public ConnectionTestOutcome? LastTestOutcome { get; private set; }
    public string? LastTestMode { get; private set; }
    public string? LastTestMessage { get; private set; }
    public string? LastError { get; private set; }
    public long? LastLatencyMs { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public void Update(bool enabled, string settingsJson, string? protectedSecrets, string? updatedBy, DateTimeOffset now)
    {
        Enabled = enabled;
        SettingsJson = settingsJson;
        ProtectedSecrets = protectedSecrets;
        UpdatedAt = now;
        UpdatedBy = updatedBy;
        // The previous test belongs to the previous configuration: it must not look valid for the new one.
        LastTestAt = null; LastTestOutcome = null; LastTestMode = null; LastTestMessage = null; LastError = null; LastLatencyMs = null;
    }

    public void RecordTest(ConnectionTestOutcome outcome, string mode, string message, string? error, long? latencyMs, DateTimeOffset now)
    {
        LastTestAt = now;
        LastTestOutcome = outcome;
        LastTestMode = mode;
        LastTestMessage = message;
        LastError = error;
        LastLatencyMs = latencyMs;
    }
}
