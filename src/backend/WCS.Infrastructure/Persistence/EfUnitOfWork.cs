using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Common;

namespace WCS.Infrastructure.Persistence;

/// <summary>
/// One transaction per unit of work. Optimistic-concurrency failures (xmin), unique-index violations (PER_AGV, rack lock,
/// request idempotency) and serialization/deadlock errors make the whole unit re-run against fresh data.
/// </summary>
internal sealed class EfUnitOfWork(WcsDbContext db, ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    private const int MaxAttempts = 12;
    private bool _inside;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (_inside) throw new InvalidOperationException("Nested units of work are not supported.");
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear(); // always start from fresh database state
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            _inside = true;
            try
            {
                var result = await work(ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
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
            finally { _inside = false; }
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
