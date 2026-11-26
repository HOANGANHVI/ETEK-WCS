using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>
/// CR-044 (OW-2): one explicit declaration that a real PLC may be written at one point. All of PLC, station, signal and address must match the point; for a STRING the length in words
/// must match too, because it decides how far the write reaches. Nothing is a wildcard.
/// </summary>
public sealed class PlcWriteDeclaration
{
    public string PlcId { get; set; } = "";
    public string StationId { get; set; } = "";
    public string Signal { get; set; } = "";
    public string Address { get; set; } = "";
    public int? Words { get; set; }
}

/// <summary>
/// CR-044 (OW-2): the deny-by-default layer of real writes, on top of the mode, the deployment keys and the Admin write approval of a point. A real write also needs a declaration in
/// <c>Plc:Write:AllowList</c> (empty by default) and must not reach a register of <c>Plc:Write:ReservedAddresses</c>. A reserved register is refused even when it is declared.
/// It never concerns reading or the simulator. Addresses come from configuration only.
/// </summary>
public static class PlcWriteGuard
{
    public const string NotDeclared = "WRITE_NOT_DECLARED";
    public const string Reserved = "WRITE_ADDRESS_RESERVED";
    public const string ReservedConfigInvalid = "WRITE_RESERVED_CONFIG_INVALID";
    public const string AuditUnavailable = "WRITE_AUDIT_UNAVAILABLE";
    public const string UnknownPoint = "WRITE_POINT_UNKNOWN";

    private readonly record struct Range(PlcDevice Device, int From, int To);

    /// <summary>One entry is a single M or D address or an inclusive range of one device ("D1600", "D1600-D1601"). NULL = at least one entry cannot be read (nothing may be written then).</summary>
    private static List<Range>? ReservedRanges(IEnumerable<string> entries)
    {
        var result = new List<Range>();
        foreach (var entry in entries)
        {
            var parts = (entry ?? "").Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length is < 1 or > 2 || !DeviceAddress.TryParse(parts[0], out var from) || !DeviceAddress.TryParse(parts[^1], out var to) || from.Device != to.Device || to.Number < from.Number) return null;
            result.Add(new Range(from.Device, from.Number, to.Number));
        }
        return result;
    }

    /// <summary>Whether the registers the point occupies include a reserved one. NULL = the reserved list is unreadable.</summary>
    public static bool? OverlapsReserved(PlcRuntimeOptions o, IoPointRef p)
    {
        if (ReservedRanges(o.Write.ReservedAddresses) is not { } ranges) return null;
        var text = p.Address?.Trim().ToUpperInvariant() ?? "";
        if (!DeviceAddress.TryParse(text, out var a)) return text.StartsWith('M') || text.StartsWith('D') ? true : false;   // an M/D address that cannot be read is treated as reserved; reserved entries are M/D only
        var last = a.Number + Math.Max(1, p.DataType == PlcDataType.String ? p.WordLength ?? 1 : 1) - 1;
        return ranges.Any(r => r.Device == a.Device && a.Number <= r.To && last >= r.From);
    }

    public static bool IsDeclared(PlcRuntimeOptions o, IoPointRef p)
    {
        if (string.IsNullOrWhiteSpace(p.Address)) return false;
        var address = DeviceAddress.Normalize(p.Address);
        return o.Write.AllowList.Any(d => Same(d.PlcId, p.PlcId) && Same(d.StationId, p.StationId) && Same(d.Signal, p.SignalName)
            && !string.IsNullOrWhiteSpace(d.Address) && DeviceAddress.Normalize(d.Address) == address
            && (p.DataType != PlcDataType.String || (d.Words is { } w && w == p.WordLength)));
        static bool Same(string declared, string actual) => string.Equals(declared?.Trim(), actual, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The guard of a REAL write. NULL = passed.</summary>
    public static (string Code, string Reason)? Check(PlcRuntimeOptions o, IoPointRef p) => OverlapsReserved(o, p) switch
    {
        null => (ReservedConfigInvalid, "The reserved register list (Plc:Write:ReservedAddresses) cannot be read: nothing is written to a real PLC."),
        true => (Reserved, $"Address {p.Address} reaches a register reserved for the unconfirmed interface: writing is blocked."),
        _ => IsDeclared(o, p) ? null : (NotDeclared, "The point is not declared in Plc:Write:AllowList (PLC, station, signal, address): writing is blocked."),
    };
}

/// <summary>CR-044: the audit record of one write decision towards a PLC in a real mode.</summary>
/// <param name="Action">PLC_WRITE_DENIED, PLC_WRITE_AUTHORIZED (recorded BEFORE anything is sent) or PLC_WRITE_RESULT.</param>
public sealed record PlcWriteAuditRecord(string Action, bool Denied, bool Success, string Actor, string PlcId, string StationId, string Signal, string? Address, string Value, string Mode, string? Code, Guid? PointId);

public interface IPlcWriteAudit
{
    /// <summary>Stores the record durably. Throws when it cannot: an authorization that cannot be recorded means the write is not sent.</summary>
    Task RecordAsync(PlcWriteAuditRecord record, CancellationToken ct = default);
}
