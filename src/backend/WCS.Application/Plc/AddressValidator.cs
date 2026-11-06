using WCS.Domain.Plc;

namespace WCS.Application.Plc;

public sealed record PointSpec(Guid Id, string StationId, string Signal, PlcDataType Type, string? Address, int? WordLength);

public sealed record AddressIssue(Guid PointId, string StationId, string Signal, string Code, string Message, Guid? ConflictWith = null);

/// <summary>
/// Checks the CONFIRMED addresses of ONE physical PLC. Addresses are independent namespaces per PLC (the same address may exist on
/// two PLCs); inside one PLC no two points may overlap. The workbook template addresses are NOT confirmed addresses and are never validated here.
/// </summary>
public static class AddressValidator
{
    public const int MaxStringWords = 960;

    /// <summary>Number of device units (bits for M, words for D) a point occupies, or null when it cannot be determined.</summary>
    public static int? Footprint(PlcDataType type, DeviceAddress a, int? wordLength) => (type, a.Device) switch
    {
        (PlcDataType.Bool, PlcDevice.M) => 1,
        (PlcDataType.Int, PlcDevice.D) => 1,
        (PlcDataType.String, PlcDevice.D) => wordLength is > 0 and <= MaxStringWords ? wordLength : null,
        _ => null,
    };

    /// <summary>Validates a single point on its own (format + type/device compatibility). Returns null when fine.</summary>
    public static AddressIssue? ValidateOne(PointSpec p)
    {
        if (string.IsNullOrWhiteSpace(p.Address)) return null;
        if (!DeviceAddress.TryParse(p.Address, out var a))
            return new(p.Id, p.StationId, p.Signal, "ADDRESS_FORMAT", $"Địa chỉ '{p.Address}' không hợp lệ (chỉ hỗ trợ thiết bị M hoặc D, ví dụ M100, D2000).");
        if (p.Type == PlcDataType.Bool && a.Device != PlcDevice.M)
            return new(p.Id, p.StationId, p.Signal, "TYPE_DEVICE", "Tín hiệu BOOL chỉ được cấu hình trên thiết bị bit M. Bit trong thanh ghi D chưa được xác nhận.");
        if (p.Type != PlcDataType.Bool && a.Device != PlcDevice.D)
            return new(p.Id, p.StationId, p.Signal, "TYPE_DEVICE", "Tín hiệu INT/STRING chỉ được cấu hình trên thanh ghi D.");
        if (p.Type == PlcDataType.String && Footprint(p.Type, a, p.WordLength) is null)
            return new(p.Id, p.StationId, p.Signal, "STRING_LENGTH", $"STRING cần độ dài (1–{MaxStringWords} word) và encoding được xác nhận.");
        return null;
    }

    /// <summary>Validates all points of one PLC: per-point rules plus overlaps between different points.</summary>
    public static IReadOnlyList<AddressIssue> Validate(IEnumerable<PointSpec> pointsOfOnePlc)
    {
        var issues = new List<AddressIssue>();
        var ranges = new List<(PlcDevice Device, int Start, int End, PointSpec Point)>();
        foreach (var p in pointsOfOnePlc.Where(x => !string.IsNullOrWhiteSpace(x.Address)))
        {
            if (ValidateOne(p) is { } one) { issues.Add(one); continue; }
            DeviceAddress.TryParse(p.Address, out var a);
            var len = Footprint(p.Type, a, p.WordLength)!.Value;
            ranges.Add((a.Device, a.Number, a.Number + len - 1, p));
        }
        foreach (var g in ranges.GroupBy(r => r.Device))
        {
            var sorted = g.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
            for (var i = 0; i < sorted.Count; i++)
                for (var j = i + 1; j < sorted.Count && sorted[j].Start <= sorted[i].End; j++)
                {
                    var (x, y) = (sorted[i].Point, sorted[j].Point);
                    issues.Add(new(y.Id, y.StationId, y.Signal, "ADDRESS_CONFLICT",
                        $"Địa chỉ {y.Address} trùng/chồng vùng nhớ với {x.StationId}/{x.Signal} ({x.Address}) trên cùng PLC.", x.Id));
                }
        }
        return issues;
    }
}
