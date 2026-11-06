using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WCS.Application.Abstractions;
using WCS.Application.Plc;
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

/// <summary>
/// Starts the 4 PLC workers once the IO catalog exists in the database (the seed hosted service creates it), and stops them with the host.
/// Disabled by Plc:RuntimeEnabled=false. A database that is not migrated yet only delays the start (it never crashes the host).
/// </summary>
public sealed class PlcRuntimeHostedService(IPlcRuntime runtime, IServiceScopeFactory scopes, ILogger<PlcRuntimeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var any = (await scope.ServiceProvider.GetRequiredService<WCS.Application.Plc.IPlcConfigRepository>().ListConnectionsAsync(stoppingToken)).Count > 0;
                if (any) { await runtime.StartAsync(stoppingToken); break; }
                logger.LogInformation("PLC runtime is waiting for the PLC catalog to be seeded");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "PLC runtime could not start yet (is the database migrated?)"); }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await runtime.StopAsync();
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
            await scope.ServiceProvider.GetRequiredService<WCS.Infrastructure.Plc.PlcCatalogSeeder>().SeedAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<WCS.Infrastructure.Auth.AdminBootstrapper>().RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Seeding failed. Has the database migration been applied (dotnet ef database update)?");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
