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
