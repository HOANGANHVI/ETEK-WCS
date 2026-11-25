using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Application.Routing;
using WCS.Domain.Rcs;
using WCS.Infrastructure.RealDispatch;
using WCS.Infrastructure.Simulation;

namespace WCS.Infrastructure.ModeControl;

internal static class ModeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    public static string Of(object o) => JsonSerializer.Serialize(o, Options);
    public const string SimulatorProtocol = "SIMULATOR (in-process, no network)";
    public const string LegacyCode = "LEGACY_TASK_MODE_UNKNOWN";
    public const string LegacyMessage = LegacyCode + ": this task / order was created before CR-038 and has no execution mode. Nothing is sent for it (neither to the simulator nor to the RCS) until an Admin adopts it as Simulator, or it is finished / reconciled by hand.";
}

/// <summary>
/// CR-038: THE <see cref="IRcsClient"/> of the system. It owns no logic of its own: for every call it asks which mode the task was created in and hands the call to the matching adapter.
/// Simulator: the call goes to <see cref="RcsSimulator"/> and the REAL request and the REAL answer of that call are recorded (marked SIMULATED). Real: the call goes to
/// <see cref="HikRcsClient"/>, which records the HTTP exchange itself. A task created before CR-038 (no pinned mode) is sent NOWHERE: the answer is "unknown outcome", never a guess.
/// Nothing here can send anything to the RCS: only <see cref="HikRcsClient"/> does, and only for a Real task.
/// </summary>
public sealed class RoutedRcsClient(RcsSimulator simulator, HikRcsClient real, IExecutionModeResolver resolver, IRcsMessageRecorder recorder, ILogger<RoutedRcsClient> logger) : IRcsClient
{
    private readonly ConcurrentDictionary<string, string> _adapterOf = new();
    private readonly ConcurrentDictionary<string, string> _lastQuery = new();

    /// <summary>The adapter that took the last call of this request code (for the call log); "RoutedRcsClient" when unknown.</summary>
    public string AdapterFor(string requestCode) => _adapterOf.TryGetValue(requestCode, out var n) ? n : nameof(RoutedRcsClient);

    /// <summary>null = a task created before CR-038: nothing may be sent for it.</summary>
    private async Task<IRcsClient?> PickAsync(string? requestCode, CancellationToken ct)
    {
        var mode = await resolver.ForRequestCodeAsync(requestCode, ct);
        IRcsClient? target = mode switch { WCS.Domain.Rcs.ExecutionMode.Real => real, WCS.Domain.Rcs.ExecutionMode.Simulator => simulator, _ => null };
        if (requestCode is not null) { if (_adapterOf.Count > 2000) _adapterOf.Clear(); _adapterOf[requestCode] = target?.GetType().Name ?? "None(legacy)"; }
        return target;
    }

    private RcsCallResult Legacy(string operation, string requestCode)
    {
        logger.LogWarning("RCS {Operation} {RequestCode} NOT sent: the task was created before CR-038 and has no execution mode. {EventCode}", operation, requestCode, "RCS_LEGACY_TASK_NOT_SENT");
        return new RcsCallResult(RcsCallOutcome.Unknown, null, ModeJson.LegacyMessage);
    }

    public async Task<RcsCallResult> CreateTaskAsync(RcsCreateTaskRequest request, CancellationToken ct = default) =>
        await Call("Create Task", request.RequestCode, ModeJson.Of(request), ct, c => c.CreateTaskAsync(request, ct));

    public async Task<RcsCallResult> ContinueTaskAsync(string requestCode, string? rcsTaskCode, string destinationLocationCode, CancellationToken ct = default) =>
        await Call("Continue Task", requestCode, ModeJson.Of(new { requestCode, rcsTaskCode, destinationLocationCode }), ct, c => c.ContinueTaskAsync(requestCode, rcsTaskCode, destinationLocationCode, ct));

    public async Task<RcsCallResult> CancelTaskAsync(string requestCode, string? rcsTaskCode, CancellationToken ct = default) =>
        await Call("Cancel Task", requestCode, ModeJson.Of(new { requestCode, rcsTaskCode }), ct, c => c.CancelTaskAsync(requestCode, rcsTaskCode, ct));

    private async Task<RcsCallResult> Call(string type, string requestCode, string payload, CancellationToken ct, Func<IRcsClient, Task<RcsCallResult>> call)
    {
        var target = await PickAsync(requestCode, ct);
        if (target is null) return Legacy(type, requestCode);
        if (target is not RcsSimulator) return await call(target);   // Real: HikRcsClient records its own HTTP exchange
        await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.Out, type, ModeJson.SimulatorProtocol, requestCode, Payload: payload, Result: "Sent"), CancellationToken.None);
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await call(target);
            await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.In, type + " Response", ModeJson.SimulatorProtocol, requestCode,
                Payload: ModeJson.Of(new { outcome = r.Outcome.ToString(), rcsTaskCode = r.RcsTaskCode, message = r.Message }), Result: r.Outcome.ToString(), ErrorMessage: r.Outcome == RcsCallOutcome.Accepted ? null : r.Message,
                RcsTaskCode: r.RcsTaskCode, DurationMs: sw.ElapsedMilliseconds), CancellationToken.None);
            return r;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.In, type + " Response", ModeJson.SimulatorProtocol, requestCode,
                Result: "Error", ErrorCode: ex.GetType().Name, ErrorMessage: ex.Message, DurationMs: sw.ElapsedMilliseconds), CancellationToken.None);
            throw;
        }
    }

    // ---- queries: polled continuously, so a pair (OUT / IN) is recorded only when the answer CHANGED since the last recorded one for this request code
    public async Task<(RcsTaskStatus Status, string? RcsTaskCode)> QueryTaskStatusAsync(string requestCode, CancellationToken ct = default)
    {
        var info = await QueryTaskAsync(requestCode, ct);
        return (info.Status, info.RcsTaskCode);
    }

    public async Task<RcsTaskInfo> QueryTaskAsync(string requestCode, CancellationToken ct = default)
    {
        var target = await PickAsync(requestCode, ct);
        if (target is null) return new RcsTaskInfo(RcsTaskStatus.Unknown, null, null);   // inconclusive, never "not found": nothing is sent
        var info = await target.QueryTaskAsync(requestCode, ct);
        if (target is RcsSimulator)
        {
            var key = $"{info.Status}|{info.RcsTaskCode}|{info.AgvCode}";
            if (!_lastQuery.TryGetValue(requestCode, out var prev) || prev != key)
            {
                if (_lastQuery.Count > 2000) _lastQuery.Clear();
                _lastQuery[requestCode] = key;
                await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.Out, "Query Task Status", ModeJson.SimulatorProtocol, requestCode, Payload: ModeJson.Of(new { requestCode }), Result: "Sent"), CancellationToken.None);
                await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.In, "Query Task Status Response", ModeJson.SimulatorProtocol, requestCode,
                    Payload: ModeJson.Of(new { status = info.Status.ToString(), rcsTaskCode = info.RcsTaskCode, robotCode = info.AgvCode }), Result: info.Status == RcsTaskStatus.Unknown ? "Unknown" : "Received", RcsTaskCode: info.RcsTaskCode), CancellationToken.None);
            }
        }
        return info;
    }

    // no request code: the CURRENT mode decides (Real: the real client, which has no per-robot read and no fleet list, exactly as before CR-038; fleet monitoring has its own read-only client)
    public async Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default) => await (await PickAsync(null, ct) ?? simulator).QueryAgvStatusAsync(robotCode, ct);
    public async Task<WCS.Application.Fleet.RcsFleetResult> QueryFleetAsync(CancellationToken ct = default) => await (await PickAsync(null, ct) ?? simulator).QueryFleetAsync(ct);
}

/// <summary>CR-038: the <see cref="ILegDispatcher"/> of the system: one place that sends a leg to the simulator or to the real adapter according to the mode pinned on the order. A leg of an order created before CR-038 is Blocked (it waits, nothing is sent).</summary>
internal sealed class RoutedLegDispatcher(SimulatedLegDispatcher simulated, RealLegDispatcher real, IExecutionModeResolver resolver, IRcsMessageRecorder recorder) : ILegDispatcher
{
    private static readonly LegDispatchResult LegacyBlocked = new(LegDispatchOutcome.Blocked, null, ModeJson.LegacyCode);

    public async Task<LegDispatchResult> DispatchAsync(LegDispatchRequest request, CancellationToken ct = default)
    {
        var mode = await resolver.ForRequestCodeAsync(request.RequestCode, ct);
        if (mode is null) return LegacyBlocked;
        if (mode == WCS.Domain.Rcs.ExecutionMode.Real) return await real.DispatchAsync(request, ct);
        return await Simulate("Create Leg Task", request.RequestCode, ModeJson.Of(request), () => simulated.DispatchAsync(request, ct));
    }

    public async Task<LegDispatchResult> ContinueAsync(string requestCode, string? agvTaskCode, string destinationLocationCode, CancellationToken ct = default)
    {
        var mode = await resolver.ForRequestCodeAsync(requestCode, ct);
        if (mode is null) return LegacyBlocked;
        if (mode == WCS.Domain.Rcs.ExecutionMode.Real) return await real.ContinueAsync(requestCode, agvTaskCode, destinationLocationCode, ct);
        return await Simulate("Continue Leg Task", requestCode, ModeJson.Of(new { requestCode, agvTaskCode, destinationLocationCode }), () => simulated.ContinueAsync(requestCode, agvTaskCode, destinationLocationCode, ct));
    }

    public async Task<RcsTaskInfo> QueryAsync(string requestCode, CancellationToken ct = default) =>
        await resolver.ForRequestCodeAsync(requestCode, ct) switch
        {
            WCS.Domain.Rcs.ExecutionMode.Real => await real.QueryAsync(requestCode, ct),
            WCS.Domain.Rcs.ExecutionMode.Simulator => await simulated.QueryAsync(requestCode, ct),
            _ => new RcsTaskInfo(RcsTaskStatus.Unknown, null, null),   // legacy: inconclusive
        };

    private async Task<LegDispatchResult> Simulate(string type, string requestCode, string payload, Func<Task<LegDispatchResult>> call)
    {
        await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.Out, type, ModeJson.SimulatorProtocol, requestCode, Payload: payload, Result: "Sent"), CancellationToken.None);
        var sw = Stopwatch.StartNew();
        var r = await call();
        await recorder.RecordAsync(new RcsMessageEntry(WCS.Domain.Rcs.ExecutionMode.Simulator, RcsMessageDirection.In, type + " Response", ModeJson.SimulatorProtocol, requestCode,
            Payload: ModeJson.Of(new { outcome = r.Outcome.ToString(), agvTaskCode = r.AgvTaskCode, reason = r.Reason }), Result: r.Outcome.ToString(), ErrorMessage: r.Outcome == LegDispatchOutcome.Accepted ? null : r.Reason,
            RcsTaskCode: r.AgvTaskCode, DurationMs: sw.ElapsedMilliseconds), CancellationToken.None);
        return r;
    }
}
