using WCS.Application.Common;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>Outcome of one IO point in a bulk read-approval (see <see cref="PlcConfigService.PreviewBulkApprovalAsync"/>).</summary>
public static class BulkOutcome
{
    public const string Eligible = "ELIGIBLE";
    public const string Approved = "APPROVED";
    public const string AlreadyApproved = "ALREADY_APPROVED";
    public const string NotEligible = "NOT_ELIGIBLE";
    public const string Failed = "FAILED";
}

public sealed record BulkApprovalItem(Guid PointId, string StationId, string SignalName, string DataType, string? Address, string Outcome, string? Code, string? Reason);

public sealed record BulkApprovalPreview(string PlcId, int Total, int WithAddress, int Eligible, int NotEligible, int AlreadyApproved, IReadOnlyList<BulkApprovalItem> Items);

public sealed record BulkApprovalResult(string PlcId, int Total, int Approved, int AlreadyApproved, int NotEligible, int Failed, int VerifiedPollable,
    IReadOnlyList<BulkApprovalItem> Items);

/// <summary>
/// Which PLCs may use the bulk read-approval. Default: PLC-04 only (the PLC under commissioning). Configuration <c>Plc:BulkApproval:AllowedPlcIds</c> overrides it;
/// an empty list disables the feature. The approval itself is the existing one (<see cref="StationIoPoint.Approve"/>): read permission only, never write.
/// </summary>
public static class BulkApprovalPolicy
{
    public const string ConfigKey = "Plc:BulkApproval:AllowedPlcIds";
    public static readonly IReadOnlyList<string> Default = ["PLC-04"];
    public static bool IsAllowed(IReadOnlyList<string> allowed, string plcId) => allowed.Contains(plcId, StringComparer.OrdinalIgnoreCase);
}
