using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Plc;
using WCS.Domain.Plc;
using WCS.Infrastructure.Persistence;

namespace WCS.Infrastructure.Plc;

/// <summary>
/// Stores the latest MC request/response pairs of every PLC in PostgreSQL (<c>plc_exchange_log</c>, at most <see cref="PlcRuntimeOptions.ExchangeHistoryPerPlc"/> rows per PLC, older rows pruned).
/// It is a side channel of the PLC path: <see cref="Record"/> only enqueues (never blocks, never throws); a background loop writes in batches with its own database context.
/// If the database refuses the write, the entries of that batch are dropped, one warning per minute is logged, and the PLC worker is not affected.
/// Failures and every WRITE are always kept; successful READ exchanges are sampled (one per PLC per <see cref="PlcRuntimeOptions.ExchangeOkSampleSeconds"/>) so routine traffic cannot push a failure out of the window.
/// </summary>
public sealed class PlcExchangeRecorder : IPlcExchangeSink, IDisposable
{
    private readonly Channel<PlcExchange> _channel = Channel.CreateBounded<PlcExchange>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastOk = new();
    private readonly IServiceProvider _sp;
    private readonly IOptions<PlcRuntimeOptions> _options;
    private readonly ILogger<PlcExchangeRecorder> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private long _failedBatches, _lostEntries, _stored;
    private int _sincePrune;
    private DateTimeOffset _lastWarning = DateTimeOffset.MinValue, _lastPrune = DateTimeOffset.MinValue;

    public PlcExchangeRecorder(IServiceProvider sp, IOptions<PlcRuntimeOptions> options, ILogger<PlcExchangeRecorder> logger)
    {
        _sp = sp; _options = options; _logger = logger;
        _loop = Task.Run(RunAsync);
    }

    public long FailedBatches => Interlocked.Read(ref _failedBatches);
    public long LostEntries => Interlocked.Read(ref _lostEntries);
    public long StoredEntries => Interlocked.Read(ref _stored);

    public void Record(PlcExchange e)
    {
        try
        {
            var gap = _options.Value.ExchangeOkSampleSeconds;
            if (e.Outcome == "OK" && e.Operation != "WRITE" && gap > 0)      // CR-015: every write exchange is kept (rare and the one to audit); only routine successful READS are sampled
            {
                if (_lastOk.TryGetValue(e.PlcId, out var last) && e.At - last < TimeSpan.FromSeconds(gap)) return;
                _lastOk[e.PlcId] = e.At;
            }
            _channel.Writer.TryWrite(e);
        }
        catch (Exception) { /* the history is a side channel: never disturb the PLC path */ }
    }

    private async Task RunAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                await Task.Delay(500, _cts.Token);            // let a few exchanges accumulate: one insert per batch, not per frame
                var batch = new List<PlcExchange>();
                while (batch.Count < 200 && reader.TryRead(out var x)) batch.Add(x);
                if (batch.Count == 0) continue;
                try
                {
                    await using var scope = _sp.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
                    db.PlcExchangeLog.AddRange(batch.Select(x => new PlcExchangeLogEntry(x.At, x.PlcId, x.Operation, x.Command, x.Device, x.StartAddress, x.Points, x.DurationMs,
                        x.EndCode, x.Outcome, x.Error, x.RequestHex, x.ResponseHex, x.IsAmbiguous)));
                    await db.SaveChangesAsync(_cts.Token);
                    Interlocked.Add(ref _stored, batch.Count);
                    _sincePrune += batch.Count;
                    if (_sincePrune >= 50 || DateTimeOffset.UtcNow - _lastPrune > TimeSpan.FromSeconds(20))
                    {
                        _lastPrune = DateTimeOffset.UtcNow; _sincePrune = 0;
                        await PruneAsync(db, batch.Select(x => x.PlcId).Distinct().ToList());
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref _failedBatches); Interlocked.Add(ref _lostEntries, batch.Count);
                    if (DateTimeOffset.UtcNow - _lastWarning > TimeSpan.FromMinutes(1))
                    {
                        _lastWarning = DateTimeOffset.UtcNow;
                        _logger.LogWarning(ex, "MC exchange history could not be stored: {Lost} entries were dropped (PLC communication is not affected). {EventCode}", batch.Count, "PLC_EXCHANGE_LOG_FAILED");
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
    }

    /// <summary>Keeps the newest N rows per PLC (N = ExchangeHistoryPerPlc).</summary>
    public async Task PruneAsync(WcsDbContext db, IReadOnlyList<string> plcIds)
    {
        var keep = Math.Max(1, _options.Value.ExchangeHistoryPerPlc);
        foreach (var plc in plcIds)
            await db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM plc_exchange_log WHERE ""PlcId"" = {plc} AND ""Id"" IN
                (SELECT ""Id"" FROM plc_exchange_log WHERE ""PlcId"" = {plc} ORDER BY ""OccurredAt"" DESC, ""Id"" OFFSET {keep})", _cts.Token);
    }

    /// <summary>Test hook: waits until everything queued so far has been written (or dropped).</summary>
    public async Task FlushAsync(int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until && _channel.Reader.Count > 0) await Task.Delay(50);
        await Task.Delay(1200);
    }

    private int _disposed;

    /// <summary>Idempotent: the container may dispose the instance through both of its registrations.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel.Writer.TryComplete(); try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        try { _loop.Wait(1000); } catch { /* stopping */ }
        _cts.Dispose();
    }
}
