using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;

namespace WCS.Infrastructure.Simulation;

/// <summary>
/// CR-021 batch C: one place that records every call WCS makes to the RCS adapter - operation, request code, outcome, duration - without changing what the call does or returns. It wraps whatever
/// <see cref="IRcsClient"/> is registered (today only the simulator; a real adapter will be logged the same way). The log says which adapter answered, so simulated results are never mistaken for
/// a real device. Only identifiers and outcomes are logged: no request payload, no credentials. AGV status queries are not logged per call (the AGV status service records CHANGES).
/// </summary>
public sealed class LoggingRcsClient(IRcsClient inner, ILogger<LoggingRcsClient> logger) : IRcsClient, IRcsClientDecorator
{
    public IRcsClient Inner => inner;
    private string Adapter => IRcsClientDecorator.Unwrap(inner).GetType().Name;

    public Task<RcsCallResult> CreateTaskAsync(RcsCreateTaskRequest request, CancellationToken ct = default) => Call("CREATE", request.RequestCode, null, () => inner.CreateTaskAsync(request, ct));

    public Task<RcsCallResult> ContinueTaskAsync(string requestCode, string? rcsTaskCode, string destinationLocationCode, CancellationToken ct = default) =>
        Call("CONTINUE", requestCode, rcsTaskCode, () => inner.ContinueTaskAsync(requestCode, rcsTaskCode, destinationLocationCode, ct));

    public Task<RcsCallResult> CancelTaskAsync(string requestCode, string? rcsTaskCode, CancellationToken ct = default) => Call("CANCEL", requestCode, rcsTaskCode, () => inner.CancelTaskAsync(requestCode, rcsTaskCode, ct));

    public async Task<(RcsTaskStatus Status, string? RcsTaskCode)> QueryTaskStatusAsync(string requestCode, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await inner.QueryTaskStatusAsync(requestCode, ct);
            logger.LogInformation("RCS QUERY {RequestCode} -> {RcsStatus} (RCS task {RcsTaskCode}) in {DurationMs} ms, adapter {Adapter}. {EventCode}", requestCode, r.Status, r.RcsTaskCode, sw.ElapsedMilliseconds, Adapter, "RCS_CALL");
            return r;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RCS QUERY {RequestCode} threw after {DurationMs} ms, adapter {Adapter}. {EventCode}", requestCode, sw.ElapsedMilliseconds, Adapter, "RCS_CALL_EXCEPTION");
            throw;
        }
    }

    public Task<RcsAgvStatus?> QueryAgvStatusAsync(string robotCode, CancellationToken ct = default) => inner.QueryAgvStatusAsync(robotCode, ct);

    private async Task<RcsCallResult> Call(string operation, string requestCode, string? rcsTaskCode, Func<Task<RcsCallResult>> call)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await call();
            if (r.Outcome == RcsCallOutcome.Accepted)
                logger.LogInformation("RCS {Operation} {RequestCode} accepted (RCS task {RcsTaskCode}) in {DurationMs} ms, adapter {Adapter}. {EventCode}", operation, requestCode, r.RcsTaskCode ?? rcsTaskCode, sw.ElapsedMilliseconds, Adapter, "RCS_CALL");
            else
                logger.LogWarning("RCS {Operation} {RequestCode} {Outcome} in {DurationMs} ms, adapter {Adapter}: {Detail}. {EventCode}", operation, requestCode, r.Outcome, sw.ElapsedMilliseconds, Adapter, r.Message ?? "no detail", "RCS_CALL_FAILED");
            return r;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RCS {Operation} {RequestCode} threw after {DurationMs} ms, adapter {Adapter}. {EventCode}", operation, requestCode, sw.ElapsedMilliseconds, Adapter, "RCS_CALL_EXCEPTION");
            throw;
        }
    }
}
