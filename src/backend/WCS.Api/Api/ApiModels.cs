using System.ComponentModel.DataAnnotations;
using WCS.Domain.Common;

namespace WCS.Api.Api;

/// <summary>Unified response envelope for every endpoint.</summary>
public sealed record ApiError(string Code, string Message, object? Details = null);

public sealed record ApiResponse<T>(bool Success, T? Data, ApiError? Error, string TraceId);

public sealed record CreateTaskRequest(
    [property: Required, StringLength(200)] string RequestKey,
    [property: Required, StringLength(20)] string Flow,
    [property: Required, StringLength(50)] string StationCode,
    [property: StringLength(100)] string? RequestedBy);

public sealed record CancelTaskRequest(
    [property: Required, StringLength(500)] string Reason,
    [property: StringLength(100)] string? User);

public sealed record ReconcileTaskRequest(
    [property: Required, StringLength(50)] string ActualLocationCode,
    [property: Required, StringLength(100)] string User,
    [property: StringLength(1000)] string? Note);

public sealed record InitialStockRequest(
    [property: Required, StringLength(50)] string LocationCode,
    RackKind RackKind,
    [property: Range(1, 1000)] int Quantity,
    [property: Required, StringLength(100)] string User);

public sealed record StockAdjustmentRequest(
    [property: Required, StringLength(50)] string LocationCode,
    RackKind RackKind,
    [property: Range(0, 1000)] int TargetQuantity,
    [property: Required, StringLength(100)] string User,
    [property: StringLength(1000)] string? Note);

public sealed record SimulatedStationRequest(
    [property: Required, StringLength(50)] string StationCode,
    [property: Required, StringLength(20)] string Flow,
    [property: Required, StringLength(200)] string RequestKey);

// ---- connection / operational configuration (secrets are WRITE-ONLY: they appear in requests, never in responses) ----
public sealed record PlcSettingsRequest(bool Enabled, string? DeviceName, string? IpAddress, int? Port, string? Protocol,
    int? NetworkNumber, int? PcNumber, int? StationNumber, int? TimeoutMs, int? PollingIntervalMs, string? UpdatedBy);

public sealed record RcsSettingsRequest(bool Enabled, string? SystemName, string? Host, int? Port, string? BaseUrl, string? AuthMode,
    string? Username, int? TimeoutMs, string? Password, bool ClearPassword, string? Token, bool ClearToken, string? UpdatedBy);

public sealed record AgvConfigRequest(string? RcsRobotCode, bool Enabled);

public sealed record LocationConfigRequest(string Code, string Name, LocationType Type, int RackCapacity, AllowedRackKind AllowedRackKind,
    bool Enabled, bool RequiresPlcPermission);

// ---- authentication / accounts (passwords appear in requests only; they are never returned or logged) ----
public sealed record LoginRequest(
    [property: Required, StringLength(100)] string Username,
    [property: Required, StringLength(200)] string Password);

public sealed record ChangePasswordRequest(
    [property: Required, StringLength(200)] string CurrentPassword,
    [property: Required, StringLength(200)] string NewPassword);

public sealed record CreateUserRequest(
    [property: Required, StringLength(50)] string Username,
    [property: Required, StringLength(100)] string DisplayName,
    WCS.Domain.Users.UserRole Role,
    [property: Required, StringLength(200)] string Password);

public sealed record UpdateUserRequest(
    [property: Required, StringLength(100)] string DisplayName,
    WCS.Domain.Users.UserRole Role,
    bool IsActive);

public sealed record ResetPasswordRequest([property: Required, StringLength(200)] string NewPassword);

/// <summary>ExpectedState is the state the screen showed; a mismatch makes the server answer 409 (reload). It never grants permission.</summary>
public sealed record UpdateTaskRequest(
    [property: Required, StringLength(50)] string StationCode,
    [property: StringLength(40)] string? ExpectedState);
