using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Services;
using WCS.Infrastructure.Seeding;

namespace WCS.Runtime;

/// <summary>Background loop: station request intake, then one PER_AGV scheduler pass. Errors never stop the loop.</summary>
public sealed class SchedulerHostedService(IServiceScopeFactory scopes, IOptions<SchedulerOptions> options, ILogger<SchedulerHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(100, options.Value.TickIntervalMs));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<StationRequestProcessor>().RunOnceAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<ITaskScheduler>().RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class AgvStatusHostedService(IServiceScopeFactory scopes, ILogger<AgvStatusHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgvStatusService>().RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AGV status refresh failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Runs the additive seed at host start (not during design-time tooling, which never starts the host).</summary>
public sealed class DatabaseSeedHostedService(IServiceScopeFactory scopes, ILogger<DatabaseSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WcsDataSeeder>().SeedAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<WCS.Infrastructure.Auth.AdminBootstrapper>().RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Seeding failed. Has the database migration been applied (dotnet ef database update)?");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
