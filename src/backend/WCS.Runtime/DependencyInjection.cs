using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WCS.Runtime;

public static class DependencyInjection
{
    /// <summary>Registers background workers. Set Runtime:EnableWorkers=false to run the API without the scheduler.</summary>
    public static IServiceCollection AddWcsRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<ApplicationLifecycleHostedService>();   // CR-021
        services.AddHostedService<DatabaseSeedHostedService>();
        if (configuration.GetValue("Runtime:EnableWorkers", true))
        {
            if (configuration.GetValue("Plc:RuntimeEnabled", true)) { services.AddHostedService<PlcRuntimeHostedService>(); services.AddHostedService<HandshakeHostedService>(); }
            services.AddHostedService<SchedulerHostedService>();
            services.AddHostedService<RoutingHostedService>();   // CR-010
            services.AddHostedService<AgvStatusHostedService>();
            services.AddHostedService<LogMaintenanceHostedService>();   // CR-007
            services.AddHostedService<DiagnosticExportCleanupHostedService>();   // CR-021
            services.AddHostedService<DbHealthMonitorHostedService>();  // CR-007
        }
        return services;
    }
}
