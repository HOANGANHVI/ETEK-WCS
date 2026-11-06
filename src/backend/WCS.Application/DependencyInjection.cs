using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Application.Flows;
using WCS.Application.Services;

namespace WCS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWcsApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProcessFlowOptions>(configuration.GetSection(ProcessFlowOptions.SectionName));
        services.Configure<SchedulerOptions>(configuration.GetSection(SchedulerOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISchedulerStatus, SchedulerStatus>();
        services.AddSingleton<IFlowCatalog, FlowCatalog>();

        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ITaskOrchestrator, TaskOrchestrator>();
        services.AddScoped<ITaskScheduler, Services.TaskScheduler>();
        services.AddScoped<StationRequestProcessor>();
        services.AddScoped<WcsQueries>();
        services.AddScoped<AgvStatusService>();
        services.AddScoped<ConnectionConfigService>();
        services.AddScoped<OperationalConfigService>();

        services.Configure<Plc.PlcRuntimeOptions>(configuration.GetSection(Plc.PlcRuntimeOptions.SectionName));
        services.AddScoped<Plc.PlcConfigService>();
        services.AddSingleton<Plc.IPlcRuntime, Plc.PlcRuntime>();
        services.AddSingleton<Plc.HandshakeCoordinator>();
        services.AddSingleton<Plc.PlcStationSignalService>();

        services.Configure<Auth.AuthOptions>(configuration.GetSection(Auth.AuthOptions.SectionName));
        services.AddScoped<Auth.AuthService>();
        services.AddScoped<Auth.UserAdminService>();
        return services;
    }
}
