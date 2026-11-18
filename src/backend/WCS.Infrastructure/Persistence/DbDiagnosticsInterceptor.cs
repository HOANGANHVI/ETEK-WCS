using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WCS.Infrastructure.Persistence;

/// <summary>
/// CR-021 batch C: records database problems that matter for diagnosis, and only those: a command slower than the threshold (<c>Diagnostics:SlowQueryMs</c>, default 1000) and a command that fails,
/// classified by the PostgreSQL error code (deadlock, constraint violation, timeout/cancel, connection, other). It logs the command text with its PARAMETER MARKERS (never parameter values) and, for
/// a failure, only the SQL state, table, column and constraint names - never the server's detail text, which can contain row values. It never changes a command, retries or throws.
/// A failing log sink cannot loop back: the system log ignores this assembly's database driver output and the sink is best effort.
/// </summary>
public sealed class DbDiagnosticsInterceptor(ILogger<DbDiagnosticsInterceptor> logger, int slowMs) : DbCommandInterceptor
{
    public const string SlowCode = "DB_SLOW_QUERY";

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) { Slow(command, eventData.Duration); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default) { Slow(command, eventData.Duration); return new(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) { Slow(command, eventData.Duration); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default) { Slow(command, eventData.Duration); return new(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) { Slow(command, eventData.Duration); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default) { Slow(command, eventData.Duration); return new(result); }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Failed(command, eventData);
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken ct = default) { Failed(command, eventData); return Task.CompletedTask; }

    private void Slow(DbCommand command, TimeSpan duration)
    {
        if (slowMs <= 0 || duration.TotalMilliseconds < slowMs) return;
        try { logger.LogWarning("Slow database command: {DurationMs} ms (threshold {ThresholdMs} ms): {CommandText}. {EventCode}", (long)duration.TotalMilliseconds, slowMs, Shorten(command.CommandText), SlowCode); }
        catch (Exception) { /* diagnostics must never disturb a command */ }
    }

    private void Failed(DbCommand command, CommandErrorEventData e)
    {
        try
        {
            var (code, what) = Classify(e.Exception);
            var pg = Find(e.Exception);
            logger.LogError("Database command failed ({Reason}) after {DurationMs} ms: SQL state {SqlState}, table {Table}, column {Column}, constraint {Constraint}; command {CommandText}. {EventCode}",
                what, (long)e.Duration.TotalMilliseconds, pg?.SqlState ?? "-", pg?.TableName ?? "-", pg?.ColumnName ?? "-", pg?.ConstraintName ?? "-", Shorten(command.CommandText), code);
        }
        catch (Exception) { /* diagnostics must never disturb a command */ }
    }

    private static PostgresException? Find(Exception? ex)
    {
        for (var i = 0; ex is not null && i < 5; i++, ex = ex.InnerException) if (ex is PostgresException p) return p;
        return null;
    }

    /// <summary>PostgreSQL error class -> stable event code. 40P01 deadlock, 40001 serialization, 23xxx integrity (unique/foreign/not-null/check), 57014 cancelled (statement timeout), 08xxx connection.</summary>
    public static (string Code, string Reason) Classify(Exception ex)
    {
        var pg = Find(ex);
        if (pg?.SqlState is { } s)
        {
            if (s == "40P01") return ("DB_DEADLOCK", "deadlock");
            if (s == "40001") return ("DB_SERIALIZATION_FAILURE", "serialization failure");
            if (s.StartsWith("23", StringComparison.Ordinal)) return ("DB_CONSTRAINT_VIOLATION", "constraint violation");
            if (s == "57014") return ("DB_TIMEOUT", "cancelled / statement timeout");
            if (s.StartsWith("08", StringComparison.Ordinal)) return ("DB_CONNECTION_FAILURE", "connection failure");
            return ("DB_COMMAND_FAILED", "server error " + s);
        }
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is TimeoutException) return ("DB_TIMEOUT", "timeout");
            if (e is NpgsqlException { IsTransient: true }) return ("DB_CONNECTION_FAILURE", "transient connection error");
        }
        return ("DB_COMMAND_FAILED", ex.GetType().Name);
    }

    private static string Shorten(string sql)
    {
        var one = string.Join(' ', sql.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= 300 ? one : one[..300] + "…";
    }
}
