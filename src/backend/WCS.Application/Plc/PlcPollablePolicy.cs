using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>
/// CR-012: which IO points of one PLC may be read (polled). A point is pollable when it has an address AND that address is valid under the existing
/// <see cref="AddressValidator"/> (format, type/device compatibility, no overlap with another point of the same PLC, STRING length/encoding).
/// Approval is NOT required. This decides READING only: write permission, the handshake and the connection guards (Enabled, AllowLiveConnection,
/// RealReadOnly) are separate and unchanged.
/// </summary>
public static class PlcPollablePolicy
{
    /// <summary>Ids of the points of <paramref name="pointsOfOnePlc"/> that may be polled.</summary>
    public static HashSet<Guid> Pollable(IReadOnlyList<StationIoPoint> pointsOfOnePlc, AddressBases bases)
    {
        var withAddress = pointsOfOnePlc.Where(p => p.IsPollable).ToList();
        var specs = withAddress.Select(PointSpec.Of).ToList();
        var blocked = new HashSet<Guid>();
        foreach (var i in AddressValidator.Validate(specs, bases)) { blocked.Add(i.PointId); if (i.ConflictWith is { } other) blocked.Add(other); }   // both sides of an overlap
        foreach (var p in withAddress.Where(p => p.DataType == PlcDataType.String && string.IsNullOrWhiteSpace(p.Encoding))) blocked.Add(p.Id);
        return withAddress.Where(p => !blocked.Contains(p.Id)).Select(p => p.Id).ToHashSet();
    }
}
