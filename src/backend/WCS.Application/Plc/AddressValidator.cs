using WCS.Application.Config;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <param name="Direction">Optional. When known, the validator also enforces that an X (input relay) point is never a Software -> PLC point.</param>
public sealed record PointSpec(Guid Id, string StationId, string Signal, PlcDataType Type, string? Address, int? WordLength, PlcSignalDirection? Direction = null);

public sealed record AddressIssue(Guid PointId, string StationId, string Signal, string Code, string Message, Guid? ConflictWith = null, IReadOnlyDictionary<string, object?>? Args = null);

/// <summary>
/// Checks the CONFIRMED addresses of ONE physical PLC. Addresses are independent namespaces per PLC (the same address may exist on
/// two PLCs); inside one PLC no two points may overlap. The workbook template addresses are NOT confirmed addresses and are never validated here.
/// X/Y/W need a confirmed numbering base (<see cref="AddressBases"/>); without it the address is refused instead of being guessed.
/// </summary>
public static class AddressValidator
{
    public const int MaxStringWords = 960;

    /// <summary>Number of device units (bits for M/X/Y, words for D/W) a point occupies, or null when it cannot be determined.</summary>
    public static int? Footprint(PlcDataType type, DeviceAddress a, int? wordLength) => (type, a.IsBitDevice) switch
    {
        (PlcDataType.Bool, true) => 1,
        (PlcDataType.Int, false) => 1,
        (PlcDataType.String, false) => wordLength is > 0 and <= MaxStringWords ? wordLength : null,
        _ => null,
    };

    /// <summary>Validates a single point on its own (format + type/device compatibility). Returns null when fine.</summary>
    public static AddressIssue? ValidateOne(PointSpec p, AddressBases? bases = null)
    {
        if (string.IsNullOrWhiteSpace(p.Address)) return null;
        var parsed = DeviceAddress.Parse(p.Address, bases);
        if (parsed.Address is not { } a)
            return new(p.Id, p.StationId, p.Signal, parsed.Problem == AddressProblem.BaseUnconfirmed ? "RADIX_UNCONFIRMED" : "ADDRESS_FORMAT",
                parsed.Problem == AddressProblem.BaseUnconfirmed ? parsed.Message! : $"Địa chỉ '{p.Address}' không hợp lệ (thiết bị hỗ trợ: M, D, W, X, Y; ví dụ M100, D2000).",
                Args: FieldArgs.Of(("address", p.Address.Trim()), ("kind", p.Address.Trim().ToUpperInvariant()[0] is 'X' or 'Y' ? "xy" : "w")));
        if (p.Type == PlcDataType.Bool && !a.IsBitDevice)
            return new(p.Id, p.StationId, p.Signal, "TYPE_DEVICE", "Tín hiệu BOOL chỉ được cấu hình trên thiết bị bit (M, X, Y). Bit trong thanh ghi D/W chưa được xác nhận.", Args: FieldArgs.Of(("kind", "bool")));
        if (p.Type != PlcDataType.Bool && !a.IsWordDevice)
            return new(p.Id, p.StationId, p.Signal, "TYPE_DEVICE", "Tín hiệu INT/STRING chỉ được cấu hình trên thanh ghi D hoặc W.", Args: FieldArgs.Of(("kind", "word")));
        if (a.Device == PlcDevice.X && p.Direction == PlcSignalDirection.SoftwareToPlc)
            return new(p.Id, p.StationId, p.Signal, "X_READ_ONLY", "Thiết bị X (Input Relay) là ngõ vào phần cứng của PLC, không được dùng cho tín hiệu Phần mềm → PLC.");
        if (p.Type == PlcDataType.String && Footprint(p.Type, a, p.WordLength) is null)
            return new(p.Id, p.StationId, p.Signal, "STRING_LENGTH", $"STRING cần độ dài (1–{MaxStringWords} word) và encoding được xác nhận.", Args: FieldArgs.Of(("max", MaxStringWords)));
        return null;
    }

    /// <summary>Validates all points of one PLC: per-point rules plus overlaps between different points.</summary>
    public static IReadOnlyList<AddressIssue> Validate(IEnumerable<PointSpec> pointsOfOnePlc, AddressBases? bases = null)
    {
        var issues = new List<AddressIssue>();
        var ranges = new List<(PlcDevice Device, int Start, int End, PointSpec Point)>();
        foreach (var p in pointsOfOnePlc.Where(x => !string.IsNullOrWhiteSpace(x.Address)))
        {
            if (ValidateOne(p, bases) is { } one) { issues.Add(one); continue; }
            DeviceAddress.TryParse(p.Address, bases, out var a);
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
                        $"Địa chỉ {y.Address} trùng/chồng vùng nhớ với {x.StationId}/{x.Signal} ({x.Address}) trên cùng PLC.", x.Id, FieldArgs.Of(("address", y.Address), ("other", $"{x.StationId}/{x.Signal}"), ("otherAddress", x.Address))));
                }
        }
        return issues;
    }
}
