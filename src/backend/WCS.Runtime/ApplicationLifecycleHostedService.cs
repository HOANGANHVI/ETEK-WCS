using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WCS.Runtime;

/// <summary>
/// CR-021 batch C: records the life of the application and of its background workers: which workers are registered when it has started, and when it is asked to stop. Per-worker faults are already
/// logged by each worker; this gives the missing frame around them (a restart shows as STOPPING / STARTED with the build label). Logging only.
/// </summary>
public sealed class ApplicationLifecycleHostedService(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<ApplicationLifecycleHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => Safe(() =>
            logger.LogInformation("WCS application started (build {Build}); background workers: {Workers}. {EventCode}",
                Environment.GetEnvironmentVariable("WCS_BUILD") ?? "(not set)", string.Join(", ", Names()), "APP_STARTED")));
        lifetime.ApplicationStopping.Register(() => Safe(() => logger.LogInformation("WCS application is stopping; workers stopping: {Workers}. {EventCode}", string.Join(", ", Names()), "APP_STOPPING")));
        lifetime.ApplicationStopped.Register(() => Safe(() => logger.LogInformation("WCS application stopped. {EventCode}", "APP_STOPPED")));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // the workers are resolved when the event fires, NOT in the constructor: asking for every IHostedService while one of them is being built is a circular dependency
    private IEnumerable<string> Names() => services.GetServices<IHostedService>().Select(w => w.GetType().Name).Where(n => n != nameof(ApplicationLifecycleHostedService)).OrderBy(n => n);

    private static void Safe(Action a) { try { a(); } catch (Exception) { /* logging must never disturb start-up or shutdown */ } }
}
