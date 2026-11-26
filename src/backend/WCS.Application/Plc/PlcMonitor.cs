namespace WCS.Application.Plc;

/// <summary>Result of an on-demand read of one IO point (CR-020). <see cref="Value"/> is what the PLC answered, never a cached value.</summary>
public sealed record ReadResult(bool Success, string? Code = null, string? Message = null, PointValue? Value = null);

/// <summary>One row of the PLC Monitoring screen: configuration facts merged with the live value and a communication status (CR-020).</summary>
public sealed record MonitorPointView(Guid Id, string PlcId, string StationId, int Seq, string Group, string SignalName, string Description, string Direction, string DataType,
    object? Value, string Quality, string Status, DateTimeOffset? LastUpdated, string? Address, string? Error, string? WriteApproval, bool? WriteSupported, bool WriteBlocked, int? WordLength = null, string? Encoding = null,
    bool Confirmed = true, string? IoMapAddress = null, bool HasAddress = true);

/// <summary>Verification of a write by reading the point back (CR-020). A successful write request is NOT proof the PLC holds the value; only VERIFIED is.</summary>
public sealed record ReadBackResult(string Status, object? Value = null, string? Code = null, string? Message = null);

public static class PlcMonitor
{
    public const string Good = "Good", Bad = "Bad", Offline = "Offline", Stale = "Stale", Unknown = "Unknown", NotConfigured = "NotConfigured", Simulated = "Simulated";

    /// <summary>
    /// Communication status shown to the user. Good only when the last read succeeded AND is not older than <paramref name="staleAfter"/> (a value that stopped refreshing is Stale, never Good);
    /// Offline = PLC link down (last value kept but not live); Bad = the last read failed; Unknown = no valid read yet; Simulated is never presented as a real device value.
    /// </summary>
    public static string StatusOf(string quality, DateTimeOffset? lastUpdated, DateTimeOffset now, TimeSpan staleAfter) => quality switch
    {
        nameof(PointQuality.Good) => lastUpdated is { } u ? (now - u <= staleAfter ? Good : Stale) : Unknown,
        nameof(PointQuality.Bad) => Bad,
        nameof(PointQuality.Disconnected) => Offline,
        nameof(PointQuality.NotConfigured) => NotConfigured,
        nameof(PointQuality.Simulated) => Simulated,
        _ => Unknown,
    };

    /// <summary>Compares what was written with what the PLC returns. STRING ignores trailing NUL/space padding only; nothing else is coerced.</summary>
    public static ReadBackResult Verify(PointValue written, ReadResult read)
    {
        if (!read.Success || read.Value is null) return new ReadBackResult("UNAVAILABLE", null, read.Code, read.Message);
        static object Norm(PointValue v) => v.Value is string s ? s.TrimEnd('\0', ' ') : v.Value;
        return Equals(Norm(written), Norm(read.Value)) ? new ReadBackResult("VERIFIED", read.Value.Value) : new ReadBackResult("MISMATCH", read.Value.Value);
    }
}
