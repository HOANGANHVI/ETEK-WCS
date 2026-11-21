using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WCS.Application.Fleet;

namespace WCS.Runtime;

/// <summary>
/// CR-028: at start, refuses to run in a dispatch mode while tasks of the OTHER mode are still active (the host does not start; the message says why and what to do). It also creates the
/// <see cref="DispatchModeState"/> that watches the configuration afterwards. A database that cannot be read (migration not applied yet) is logged and skipped - the seed service reports that.
/// </summary>
public sealed class DispatchModeStartupCheck(IServiceScopeFactory scopes, DispatchModeState state, ILogger<DispatchModeStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        DispatchModeCheck check;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            check = await scope.ServiceProvider.GetRequiredService<DispatchModeGuard>().CheckAsync(state.EffectiveMode, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The dispatch mode could not be checked against the database (is the migration applied?). Mode in use: {Mode}", state.EffectiveMode);
            return;
        }
        if (!check.Allowed)
        {
            logger.LogCritical("{Message}", check.Message);
            throw new DispatchModeChangeRefusedException(check.Message!);
        }
        logger.LogInformation("Rcs:Dispatch:Mode = {Mode}", state.EffectiveMode);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
