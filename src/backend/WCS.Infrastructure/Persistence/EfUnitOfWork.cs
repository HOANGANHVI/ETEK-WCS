using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;

namespace WCS.Infrastructure.Persistence;

/// <summary>
/// One transaction per unit of work. Optimistic-concurrency failures (xmin), unique-index violations (PER_AGV, rack lock,
/// request idempotency) and serialization/deadlock errors make the whole unit re-run against fresh data.
/// CR-026: <see cref="ExecuteBatchAsync{T}"/> opens one transaction that the <see cref="ExecuteAsync{T}"/> calls made inside it join.
/// </summary>
internal sealed class EfUnitOfWork(WcsDbContext db, ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    private const int MaxAttempts = 12;
    private bool _inside;
    private bool _batch;

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => RunAsync(work, batch: false, ct);

    public Task<T> ExecuteBatchAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) => RunAsync(work, batch: true, ct);

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, bool batch, CancellationToken ct)
    {
        if (_inside)
        {
            // a unit of work inside a batch joins the open transaction: flushed (so later reads in the same batch see it) but committed only with the batch
            if (_batch && !batch)
            {
                var joined = await work(ct);
                await db.SaveChangesAsync(ct);
                return joined;
            }
            throw new InvalidOperationException(batch ? "A batch cannot be started inside another unit of work." : "Nested units of work are not supported.");
        }
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear(); // always start from fresh database state
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            _inside = true; _batch = batch;
            var effects = batch ? BatchEffects.Begin() : null;
            try
            {
                var result = await work(ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (effects is not null) await RunEffectsAsync(effects);
                return result;
            }
            catch (Exception ex) when (IsConflict(ex))
            {
                await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                if (attempt >= MaxAttempts)
                    throw new DataConflictException("Operation failed after repeated concurrent modification.", ex);
                logger.LogDebug(ex, "Concurrency conflict, retrying unit of work (attempt {Attempt})", attempt);
                await Task.Delay(Random.Shared.Next(5, 15 * attempt), CancellationToken.None); // jitter to break lock-step retries
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw;
            }
            finally { _inside = false; _batch = false; if (batch) BatchEffects.End(); }
        }
    }

    /// <summary>Realtime events held during a batch. The data is already committed, so a failing client notification is logged and never turned into a failure.</summary>
    private async Task RunEffectsAsync(List<Func<Task>> effects)
    {
        BatchEffects.End();   // effects run after the commit and must not be deferred again
        foreach (var e in effects)
        {
            try { await e(); }
            catch (Exception ex) { logger.LogWarning(ex, "A realtime notification held during a batch could not be published"); }
        }
    }

    private static bool IsConflict(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => true,
        DbUpdateException { InnerException: PostgresException pg } =>
            pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.ForeignKeyViolation,
        PostgresException p => p.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected,
        _ => false,
    };
}
