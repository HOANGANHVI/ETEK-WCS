using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WCS.Application.Abstractions;
using WCS.Infrastructure.Logging;
using WCS.Infrastructure.Persistence;

namespace WCS.Runtime;

/// <summary>
/// CR-007 housekeeping of the searchable log: imports deployment events (read-only file mount, see <see cref="DeploymentLogImporter"/>) and removes OLD system_log rows
/// (retention <c>Logging:SystemLog:RetentionDays</c>, default 90, minimum 7). It never touches audit_log or plc_comm_log, and a failure here never affects the rest of WCS.
/// </summary>
public sealed class LogMaintenanceHostedService(IServiceScopeFactory scopes, IConfiguration config, IClock clock, ILogger<LogMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPurge = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dir = config["DeploymentLogs:Directory"];
                if (!string.IsNullOrWhiteSpace(dir))
                    await scope.ServiceProvider.GetRequiredService<DeploymentLogImporter>().ImportAsync(dir, stoppingToken);

                if (clock.UtcNow - lastPurge > TimeSpan.FromHours(6))
                {
                    lastPurge = clock.UtcNow;
                    var days = Math.Max(7, config.GetValue("Logging:SystemLog:RetentionDays", 90));
                    var cutoff = clock.UtcNow.AddDays(-days);
                    var removed = await scope.ServiceProvider.GetRequiredService<WcsDbContext>().SystemLog.Where(x => x.OccurredAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                    if (removed > 0) logger.LogInformation("System log retention: removed {Removed} entries older than {Days} days. {EventCode}", removed, days, "LOG_RETENTION");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Log maintenance failed (will retry). {EventCode}", "LOG_MAINTENANCE_FAILED"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// CR-007: reports database state TRANSITIONS (lost / restored, pending migrations) instead of one error per failed query. Read-only probe every 15 seconds; it does not retry,
/// reconnect or change anything: the application keeps behaving exactly as before.
/// </summary>
public sealed class DbHealthMonitorHostedService(IServiceScopeFactory scopes, IClock clock, ILogger<DbHealthMonitorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool? up = null; DateTimeOffset? downSince = null; var checkedMigrations = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            bool ok; string? why = null;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WcsDbContext>();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); cts.CancelAfter(TimeSpan.FromSeconds(5));
                ok = await db.Database.CanConnectAsync(cts.Token);
                if (ok && !checkedMigrations)
                {
                    checkedMigrations = true;
                    var pending = (await db.Database.GetPendingMigrationsAsync(cts.Token)).ToList();
                    if (pending.Count > 0)
                        logger.LogWarning("Database has {Count} pending migration(s): {Migrations}. Run the migrator (deploy.ps1 does this). {EventCode}", pending.Count, string.Join(", ", pending), "DB_MIGRATIONS_PENDING");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { ok = false; why = ex.GetType().Name + ": " + ex.Message; }

            if (up != ok)
            {
                if (!ok) { downSince ??= clock.UtcNow; logger.LogWarning("Database connection lost{Reason}. {Device} {EventCode}", why is null ? "" : " (" + why + ")", "postgres", "DB_LOST"); }
                else if (up == false)
                {
                    var secs = downSince is { } d ? (clock.UtcNow - d).TotalSeconds : 0;
                    logger.LogInformation("Database connection restored after {Seconds:0} s. {Device} {EventCode}", secs, "postgres", "DB_RESTORED");
                    downSince = null; checkedMigrations = false;
                }
                up = ok;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
