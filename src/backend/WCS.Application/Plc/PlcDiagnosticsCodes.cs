using System.Net.Sockets;
using WCS.Application.Logging;

namespace WCS.Application.Plc;

/// <summary>Standard PLC diagnostic codes. Stable identifiers: logs, the export ZIP and the UI search use them (never translated).</summary>
public static class PlcDiagCodes
{
    public const string LiveConnectionDisabled = "LIVE_CONNECTION_DISABLED";
    public const string ConnectionNotEnabled = "CONNECTION_NOT_ENABLED";
    public const string ConfigurationMissing = "PLC_CONFIGURATION_MISSING";
    public const string ConnectFailed = "PLC_CONNECT_FAILED";
    public const string Timeout = "PLC_TIMEOUT";
    public const string NetworkUnreachable = "PLC_NETWORK_UNREACHABLE";
    public const string ConnectionRefused = "PLC_CONNECTION_REFUSED";
    public const string ConnectionLost = "PLC_CONNECTION_LOST";
    public const string ProtocolError = "PLC_PROTOCOL_ERROR";
    public const string EndCodeError = "PLC_END_CODE_ERROR";
    public const string ReadFailed = "PLC_READ_FAILED";
    public const string WriteBlocked = "PLC_WRITE_BLOCKED";
    public const string WriteFailed = "PLC_WRITE_FAILED";
    public const string IoPointNotApproved = "IO_POINT_NOT_APPROVED";
    public const string ReconnectFailed = "PLC_RECONNECT_FAILED";
    public const string WorkerCrashed = "PLC_WORKER_CRASHED";

    /// <summary>What the code means in plain English (kept with the code so an exported log is self-explanatory).</summary>
    public static readonly IReadOnlyDictionary<string, string> Explanations = new Dictionary<string, string>
    {
        [LiveConnectionDisabled] = "Plc:AllowLiveConnection is false: WCS deliberately opens no socket to a physical PLC.",
        [ConnectionNotEnabled] = "The Admin has not enabled this PLC connection: WCS does not connect to it.",
        [ConfigurationMissing] = "Required connection parameters are missing (see 'missing'): WCS cannot start the connection and never guesses a value.",
        [ConnectFailed] = "The TCP connection could not be established (no more specific cause was reported).",
        [Timeout] = "No answer within the configured timeout (TCP connect or MC response).",
        [NetworkUnreachable] = "The PLC address cannot be reached from this host (no route / host or network unreachable / unknown host).",
        [ConnectionRefused] = "The host answered but nothing accepts connections on that TCP port (wrong port, PLC Ethernet module not listening, or a firewall rejecting it).",
        [ConnectionLost] = "An established connection was closed or reset (by the PLC, a network device or a cable/power failure).",
        [ProtocolError] = "The reply was not a valid MC 3E response (framing, subheader or routing mismatch): wrong protocol/frame type or another device answered.",
        [EndCodeError] = "The PLC answered with an MC end code other than 0: the request reached the PLC and was refused (device/range/route/command not allowed).",
        [ReadFailed] = "Reading IO points failed.",
        [WriteBlocked] = "A write was refused by the write policy before anything was sent to the PLC.",
        [WriteFailed] = "A write command failed or its result is undetermined.",
        [IoPointNotApproved] = "IO points are not polled because they have no address, or an address that is invalid or overlaps another point of the same PLC (approval is not needed for reading): no read traffic is sent for them.",
        [ReconnectFailed] = "A reconnect attempt after a lost connection failed.",
        [WorkerCrashed] = "The PLC worker stopped because of an unexpected error.",
    };

    public static string Explain(string? code) => code is not null && Explanations.TryGetValue(code, out var e) ? e : "";
}

/// <summary>Where in the PLC path an event happened (searchable column <c>plc_comm_log.Stage</c>).</summary>
public static class PlcDiagStages
{
    public const string Config = "CONFIG";
    public const string State = "STATE";
    public const string Tcp = "TCP";
    public const string Mc = "MC";
    public const string Read = "READ";
    public const string Write = "WRITE";
    public const string Retry = "RETRY";
    public const string Recovery = "RECOVERY";
}

public sealed record PlcDiagResult(string Code, string Stage, string? SocketError, int? SocketErrorCode, string? Inner)
{
    public string Explanation => PlcDiagCodes.Explain(Code);
}

/// <summary>
/// Turns a transport/protocol exception into a standard code. It only READS the exception (SocketError, InnerException chain); it never changes behaviour.
/// MC exception types live in the infrastructure project, so they are recognised by type name.
/// </summary>
public static class PlcDiagClassifier
{
    public static PlcDiagResult Classify(Exception ex, string stage, string fallbackCode = PlcDiagCodes.ConnectFailed)
    {
        var socket = FindSocket(ex);
        var inner = ex.InnerException is { } i ? Trunc($"{i.GetType().Name}: {i.Message}") : null;
        string code; var st = stage;
        var typeName = ex.GetType().Name;
        if (typeName == "McEndCodeException") { code = PlcDiagCodes.EndCodeError; st = PlcDiagStages.Mc; }
        else if (typeName == "McProtocolException") { code = PlcDiagCodes.ProtocolError; st = PlcDiagStages.Mc; }
        else if (socket is not null)
        {
            code = socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => PlcDiagCodes.ConnectionRefused,
                SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.NetworkDown or SocketError.HostDown or SocketError.HostNotFound or SocketError.NoData
                    or SocketError.TryAgain => PlcDiagCodes.NetworkUnreachable,
                SocketError.TimedOut => PlcDiagCodes.Timeout,
                SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown or SocketError.Disconnecting => PlcDiagCodes.ConnectionLost,
                _ => fallbackCode,
            };
        }
        else if (ex is TimeoutException || ex.InnerException is TimeoutException) code = PlcDiagCodes.Timeout;
        else if (ex is System.IO.IOException) code = PlcDiagCodes.ConnectionLost;
        else code = fallbackCode;
        return new PlcDiagResult(code, st, socket?.SocketErrorCode.ToString(), socket?.ErrorCode, inner);
    }

    /// <summary>Best effort classification of an error text that is all that is known (for example the last error kept by the driver).</summary>
    public static string? ClassifyText(string? text, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var t = text.ToLowerInvariant();
        if (t.Contains("actively refused") || t.Contains("connection refused")) return PlcDiagCodes.ConnectionRefused;
        if (t.Contains("unreachable") || t.Contains("no such host") || t.Contains("host is down")) return PlcDiagCodes.NetworkUnreachable;
        if (t.Contains("end code")) return PlcDiagCodes.EndCodeError;
        if (t.Contains("no response within") || t.Contains("timed out") || t.Contains("timeout")) return PlcDiagCodes.Timeout;
        if (t.Contains("connection closed") || t.Contains("reset by") || t.Contains("forcibly closed") || t.Contains("broken pipe")) return PlcDiagCodes.ConnectionLost;
        if (t.Contains("subheader") || t.Contains("frame") || t.Contains("protocol")) return PlcDiagCodes.ProtocolError;
        return fallback;
    }

    private static SocketException? FindSocket(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException) if (e is SocketException s) return s;
        return null;
    }

    private static string Trunc(string s) => s.Length <= 300 ? s : s[..300];
}

/// <summary>Builds the structured <c>args</c> of a PLC event (stored in the details JSON; <c>diagCode</c> and <c>stage</c> also fill searchable columns).</summary>
public static class PlcDiag
{
    public const string CodeKey = "diagCode";
    public const string StageKey = "stage";

    public static IReadOnlyDictionary<string, object?> Args(string? code, string stage, params (string Key, object? Value)[] extra)
    {
        var d = new Dictionary<string, object?>();
        if (code is not null) { d[CodeKey] = code; var ex = PlcDiagCodes.Explain(code); if (ex.Length > 0) d["explanation"] = ex; }
        d[StageKey] = stage;
        foreach (var (k, v) in extra) if (v is not null) d[k] = v;
        return d;
    }

    public static IReadOnlyDictionary<string, object?> From(PlcDiagResult r, params (string Key, object? Value)[] extra)
    {
        var d = new Dictionary<string, object?>(Args(r.Code, r.Stage, extra));
        if (r.SocketError is not null) d["socketError"] = r.SocketError;
        if (r.SocketErrorCode is not null) d["socketErrorCode"] = r.SocketErrorCode;
        if (r.Inner is not null) d["inner"] = r.Inner;
        return d;
    }

    /// <summary>Code and stage for events that do not carry them (older events and the generic STATE line).</summary>
    public static (string? Code, string Stage) Infer(string eventName, string message)
    {
        var m = message ?? "";
        string? Bracket(params string[] names) => names.FirstOrDefault(n => m.Contains("[" + n + "]", StringComparison.Ordinal));
        switch (eventName)
        {
            case "DISABLED":
                return (Bracket(PlcDiagCodes.LiveConnectionDisabled, PlcDiagCodes.ConnectionNotEnabled), PlcDiagStages.Config);
            case "NEEDS_CONFIGURATION": case "CONFIG_MISSING":
                return (PlcDiagCodes.ConfigurationMissing, PlcDiagStages.Config);
            case "CONFIG_SNAPSHOT": case "RELOAD": case "RUNTIME_START":
                return (null, PlcDiagStages.Config);
            case "STATE":
                if (m.Contains("[PLC_PARAMS_MISSING]")) return (PlcDiagCodes.ConfigurationMissing, PlcDiagStages.Config);
                if (m.Contains("[PLC_CONNECT_FAILED]")) return (PlcDiagCodes.ConnectFailed, PlcDiagStages.Tcp);
                if (m.Contains("[PLC_LINK_LOST]")) return (PlcDiagCodes.ConnectionLost, PlcDiagStages.Tcp);
                if (m.Contains("[PLC_WORKER_CRASHED]")) return (PlcDiagCodes.WorkerCrashed, PlcDiagStages.State);
                return (null, PlcDiagStages.State);
            case "TCP_CONNECTED": return (null, PlcDiagStages.Tcp);
            case "CONNECT_FAILED": return (PlcDiagClassifier.ClassifyText(m, PlcDiagCodes.ConnectFailed), PlcDiagStages.Tcp);
            case "LINK_LOST": return (PlcDiagClassifier.ClassifyText(m, PlcDiagCodes.ConnectionLost), PlcDiagStages.Tcp);
            case "WORKER_CRASH": return (PlcDiagCodes.WorkerCrashed, PlcDiagStages.State);
            case "WRITE_DENIED": return (PlcDiagCodes.WriteBlocked, PlcDiagStages.Write);
            case "WRITE_FAILED": case "WRITE_AMBIGUOUS": return (PlcDiagCodes.WriteFailed, PlcDiagStages.Write);
            case "WRITE": return (null, PlcDiagStages.Write);
            case "IO_NOT_APPROVED": return (PlcDiagCodes.IoPointNotApproved, PlcDiagStages.Read);
        }
        if (eventName.StartsWith("READ_BAD", StringComparison.Ordinal) || eventName.StartsWith("STATION_ERROR", StringComparison.Ordinal))
            return (PlcDiagClassifier.ClassifyText(m) is { } c && c == PlcDiagCodes.EndCodeError ? c : PlcDiagCodes.ReadFailed, PlcDiagStages.Read);
        return (null, PlcDiagStages.State);
    }
}

/// <summary>Receives every MC request/response pair so it can be stored (never blocks and never throws into the PLC path).</summary>
public interface IPlcExchangeSink
{
    void Record(PlcExchange exchange);
}

public sealed class NullPlcExchangeSink : IPlcExchangeSink
{
    public void Record(PlcExchange exchange) { }
}
