using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Domain.Plc;

namespace WCS.Application.Plc;

/// <summary>
/// One APPROVED handshake rule for a Software -> PLC bit: set it while the "set" condition holds, clear it when the "reset" condition holds.
/// The workbook's Handshake Definition sheet is still empty/PENDING, so NO rule is shipped: a deployment adds a rule only after the PLC
/// engineer has approved it (Approved = true plus the approval reference). Unapproved rules are ignored.
/// </summary>
public sealed class HandshakeDefinition
{
    /// <summary>Station id, or empty for every station.</summary>
    public string Station { get; set; } = "";
    /// <summary>The Software -> PLC BOOL signal this rule drives (e.g. Request_Ack).</summary>
    public string Signal { get; set; } = "";
    public string SetWhenSignal { get; set; } = "";
    public bool SetWhenValue { get; set; } = true;
    public string ResetWhenSignal { get; set; } = "";
    public bool ResetWhenValue { get; set; }
    /// <summary>Optional: log a warning when the bit stays set longer than this (no automatic action: the retry policy is not approved).</summary>
    public int? TimeoutMs { get; set; }
    public bool Approved { get; set; }
    public string? ApprovalReference { get; set; }
}

/// <summary>
/// Purely OBSERVATIONAL phase derived from the PLC -> Software bits of a station. It is a technical readout for monitoring and carries no
/// business meaning: nothing is triggered by a phase.
/// </summary>
public enum HandshakePhase { Unknown = 0, Offline = 1, Fault = 2, Idle = 3, PickupRequested = 4, DeliveryRequested = 5, ConflictingRequests = 6, TransferComplete = 7, NotImplemented = 8 }

public sealed record ActiveHandshake(string Signal, DateTimeOffset Since, bool TimedOut);

public sealed record StationHandshakeState(string PlcId, string StationId, string Approval, string Phase, DateTimeOffset PhaseSince, string? Reason,
    int ApprovedRules, int SignalsAwaitingConfirmation, IReadOnlyList<ActiveHandshake> Active, string? LastAction, string? LastError);

/// <summary>
/// Per-Station handshake coordinator (framework). State lives in the runtime. With no approved rules it only observes; it never writes.
/// With approved rules it sets/clears the named bit through the normal write path, so the write policy (mode, allow-list, blocked signals)
/// still applies. It is driven by a periodic tick (<see cref="StepAsync"/>).
/// </summary>
public sealed class HandshakeCoordinator(IPlcRuntime runtime, IOptions<PlcRuntimeOptions> options, IPlcEvents events, IClock clock)
{
    private sealed class Mem
    {
        public HandshakePhase Phase; public DateTimeOffset Since; public string? Reason;
        public Dictionary<string, DateTimeOffset> Active = [];
        public HashSet<string> TimedOut = [];
        public string? LastAction, LastError;
    }

    private readonly ConcurrentDictionary<string, Mem> _mem = new();

    private static bool Truthy((PointValue? Value, PointQuality Quality)? r, bool expected) =>
        r is { Quality: PointQuality.Good or PointQuality.Simulated, Value: { Type: PlcDataType.Bool } v } && (bool)v.Value == expected;

    private static bool Live((PointValue? Value, PointQuality Quality)? r) => r is { Quality: PointQuality.Good or PointQuality.Simulated, Value: not null };

    /// <summary>Observational phase of a station from its inbound bits (needs live data for every bit it looks at).</summary>
    public static (HandshakePhase Phase, string? Reason) Observe(StationIoHandler h)
    {
        if (new[] { "Station_Online", "Station_Fault", "Pickup_Request", "Delivery_Request", "Transfer_Complete" }.All(n => h.Find(n) is null))
            return (HandshakePhase.NotImplemented, "[HANDSHAKE_NOT_IMPLEMENTED] No handshake is implemented for the signals of this station (IO map not confirmed).");   // CR-046
        var online = h.Read("Station_Online"); var fault = h.Read("Station_Fault"); var pick = h.Read("Pickup_Request");
        var del = h.Read("Delivery_Request"); var done = h.Read("Transfer_Complete");
        var missing = new[] { ("Station_Online", online), ("Station_Fault", fault), ("Pickup_Request", pick), ("Delivery_Request", del), ("Transfer_Complete", done) }
            .Where(x => !Live(x.Item2)).Select(x => x.Item1).ToList();
        if (missing.Count > 0) return (HandshakePhase.Unknown, "No valid data: " + string.Join(", ", missing));
        if (Truthy(fault, true)) return (HandshakePhase.Fault, null);
        if (Truthy(online, false)) return (HandshakePhase.Offline, null);
        var (p, d) = (Truthy(pick, true), Truthy(del, true));
        if (p && d) return (HandshakePhase.ConflictingRequests, "Pickup_Request and Delivery_Request are both on");
        if (p) return (HandshakePhase.PickupRequested, null);
        if (d) return (HandshakePhase.DeliveryRequested, null);
        if (Truthy(done, true)) return (HandshakePhase.TransferComplete, null);
        return (HandshakePhase.Idle, null);
    }

    private IEnumerable<HandshakeDefinition> RulesFor(string station) =>
        options.Value.Handshake.Definitions.Where(d => d.Approved && !string.IsNullOrWhiteSpace(d.ApprovalReference) && (d.Station == "" || d.Station == station));

    public IReadOnlyList<StationHandshakeState> States() =>
        runtime.Stations(false).Select(s => State(s.PlcId, s.StationId)).ToList();

    public StationHandshakeState State(string plcId, string stationId)
    {
        var m = _mem.GetOrAdd(stationId, _ => new Mem { Since = clock.UtcNow });
        var rules = RulesFor(stationId).Count();
        var signals = runtime.Handler(stationId)?.Points.Count ?? 0;
        lock (m)
            return new StationHandshakeState(plcId, stationId, rules == 0 ? "NEEDS_CONFIRMATION" : "PARTIALLY_APPROVED", m.Phase.ToString(), m.Since, m.Reason, rules,
                Math.Max(0, signals - rules), m.Active.Select(a => new ActiveHandshake(a.Key, a.Value, m.TimedOut.Contains(a.Key))).ToList(), m.LastAction, m.LastError);
    }

    /// <summary>One coordination pass over every station. Returns the number of writes that were requested.</summary>
    public async Task<int> StepAsync(CancellationToken ct = default)
    {
        var writes = 0;
        foreach (var snap in runtime.Stations(false))
        {
            var handler = runtime.Handler(snap.StationId);
            if (handler is null) continue;
            var m = _mem.GetOrAdd(snap.StationId, _ => new Mem { Since = clock.UtcNow });
            var (phase, reason) = Observe(handler);
            lock (m) { if (m.Phase != phase) { m.Phase = phase; m.Since = clock.UtcNow; m.Reason = reason; events.Publish("HandshakePhaseChanged", new { plcId = snap.PlcId, stationId = snap.StationId, phase = phase.ToString(), reason }); } else m.Reason = reason; }

            foreach (var rule in RulesFor(snap.StationId))
            {
                var target = handler.Find(rule.Signal);
                if (target is null || target.Direction != PlcSignalDirection.SoftwareToPlc || target.DataType != PlcDataType.Bool)
                { lock (m) m.LastError = $"[HANDSHAKE_RULE_INVALID] Rule {rule.Signal}: không phải tín hiệu BOOL chiều Phần mềm → PLC."; continue; }
                var current = handler.Read(rule.Signal);
                var setNow = Truthy(handler.Read(rule.SetWhenSignal), rule.SetWhenValue);
                var resetNow = !string.IsNullOrEmpty(rule.ResetWhenSignal) && Truthy(handler.Read(rule.ResetWhenSignal), rule.ResetWhenValue);
                if (!Live(current)) continue;                                   // never act on data that is not live
                var isSet = Truthy(current, true);

                bool? want = null;
                if (!isSet && setNow && !resetNow) want = true;
                else if (isSet && resetNow) want = false;
                if (want is not null)
                {
                    var r = await runtime.WriteAsync(snap.PlcId, snap.StationId, rule.Signal, PointValue.OfBool(want.Value), "handshake", ct, handshake: true);
                    lock (m)
                    {
                        m.LastAction = $"{rule.Signal} = {(want.Value ? 1 : 0)}"; m.LastError = r.Success ? null : $"{r.Code}: {r.Message}";
                        if (r.Success) { if (want.Value) m.Active[rule.Signal] = clock.UtcNow; else { m.Active.Remove(rule.Signal); m.TimedOut.Remove(rule.Signal); } }
                    }
                    if (!r.Success) events.Log(PlcLogLevel.Warning, snap.PlcId, snap.StationId, "HANDSHAKE_WRITE_REFUSED", $"{rule.Signal}: {r.Code}", WCS.Application.Logging.LogArgs.Of(("signal", rule.Signal), ("code", r.Code)));
                    else { writes++; events.Log(PlcLogLevel.Info, snap.PlcId, snap.StationId, "HANDSHAKE", $"{rule.Signal} = {(want.Value ? 1 : 0)}", WCS.Application.Logging.LogArgs.Of(("signal", rule.Signal), ("value", want.Value ? 1 : 0))); }
                }
                else if (isSet && rule.TimeoutMs is { } t)
                {
                    lock (m)
                    {
                        m.Active.TryAdd(rule.Signal, clock.UtcNow);
                        if (clock.UtcNow - m.Active[rule.Signal] > TimeSpan.FromMilliseconds(t) && m.TimedOut.Add(rule.Signal))
                            events.Log(PlcLogLevel.Warning, snap.PlcId, snap.StationId, "HANDSHAKE_TIMEOUT", $"{rule.Signal} still on after {t} ms (retry policy not approved: no automatic handling).", WCS.Application.Logging.LogArgs.Of(("signal", rule.Signal), ("ms", t)));
                    }
                }
                else if (!isSet) lock (m) { m.Active.Remove(rule.Signal); m.TimedOut.Remove(rule.Signal); }
            }
        }
        return writes;
    }
}

public sealed class PlcHandshakeOptions
{
    /// <summary>Approved rules only. Empty by default: the framework observes and never writes.</summary>
    public List<HandshakeDefinition> Definitions { get; set; } = [];
}
