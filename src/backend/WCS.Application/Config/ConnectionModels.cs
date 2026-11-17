using WCS.Application.Common;
using WCS.Domain.Common;

namespace WCS.Application.Config;

/// <summary>One invalid field. <c>Message</c> is a human-readable fallback; <c>Code</c> (+ <c>Args</c>) is the stable key the UI translates (bilingual UI).</summary>
public sealed record FieldError(string Field, string Message, string? Code = null, IReadOnlyDictionary<string, object?>? Args = null);

/// <summary>Builds the <c>Args</c> dictionary of a <see cref="FieldError"/>.</summary>
public static class FieldArgs
{
    public static IReadOnlyDictionary<string, object?> Of(params (string Key, object? Value)[] items) => items.ToDictionary(x => x.Key, x => x.Value);
}

/// <summary>Input validation failure with per-field messages (mapped to HTTP 400 with details).</summary>
public sealed class ValidationFailedException(IReadOnlyList<FieldError> errors)
    : AppException(ErrorKind.Validation, "VALIDATION_FAILED", "Dữ liệu cấu hình không hợp lệ.")
{
    public IReadOnlyList<FieldError> Errors { get; } = errors;
}

/// <summary>
/// PLC connection fields. Which of them the final Mitsubishi adapter really needs is NOT confirmed (NEEDS_CONFIRMATION with the PLC
/// engineer): every field is optional and nothing is pre-filled.
/// </summary>
public sealed record PlcSettings(
    string? DeviceName, string? IpAddress, int? Port, string? Protocol,
    int? NetworkNumber, int? PcNumber, int? StationNumber, int? TimeoutMs, int? PollingIntervalMs);

/// <summary>RCS connection fields. No endpoint path or payload is defined here (the RCS API contract is not confirmed).</summary>
public sealed record RcsSettings(
    string? SystemName, string? Host, int? Port, string? BaseUrl, string AuthMode, string? Username, int? TimeoutMs);

public sealed record SecretState(bool HasPassword, bool HasToken);

public sealed record LastTestView(ConnectionTestOutcome Outcome, string Mode, string Message, string? Error, long? LatencyMs, DateTimeOffset TestedAt);

public sealed record PlcConfigView(bool Configured, bool Enabled, PlcSettings Settings, LastTestView? LastTest, DateTimeOffset? UpdatedAt, string? UpdatedBy);

/// <summary>Never contains a password or token – only whether one is stored.</summary>
public sealed record RcsConfigView(bool Configured, bool Enabled, RcsSettings Settings, SecretState Secrets, LastTestView? LastTest,
    DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record UpdatePlcCommand(bool Enabled, PlcSettings Settings, string? UpdatedBy);

/// <summary>Secrets are write-only. null = keep the stored value; a value = replace it; Clear* = remove it.</summary>
public sealed record UpdateRcsCommand(bool Enabled, RcsSettings Settings, string? Password, bool ClearPassword, string? Token, bool ClearToken, string? UpdatedBy);

public sealed record ConnectionTestResult(ConnectionTestOutcome Outcome, string Mode, string Message, string? Error, long? LatencyMs, DateTimeOffset TestedAt);

public static class ConnectionOptions
{
    /// <summary>Only the protocol named in integration-requirements.md 5.2 (expected, to be confirmed with the PLC engineer).</summary>
    public const string PlcMcProtocol3EBinaryTcp = "McProtocol3EBinaryTcp";
    public static readonly string[] PlcProtocols = [PlcMcProtocol3EBinaryTcp];

    public const string AuthNone = "None";
    public const string AuthToken = "Token";
    public const string AuthUserPassword = "UsernamePassword";
    public static readonly string[] RcsAuthModes = [AuthNone, AuthToken, AuthUserPassword];
}
