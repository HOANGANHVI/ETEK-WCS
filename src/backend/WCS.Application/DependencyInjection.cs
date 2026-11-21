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
        services.Configure<Diagnostics.DiagnosticExportOptions>(configuration.GetSection(Diagnostics.DiagnosticExportOptions.SectionName));
        services.Configure<Routing.RouteEngineOptions>(configuration.GetSection(Routing.RouteEngineOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISchedulerStatus, SchedulerStatus>();
        services.AddSingleton<IFlowCatalog, FlowCatalog>();

        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ITaskOrchestrator, TaskOrchestrator>();
        services.AddScoped<ITaskScheduler, Services.TaskScheduler>();
        services.AddScoped<StationRequestProcessor>();
        services.AddScoped<WcsQueries>();
        services.AddScoped<ReconciliationService>();   // CR-008B-3
        services.AddScoped<AgvHoldService>();   // CR-008B-5
        services.AddScoped<MaintenanceService>();   // CR-026
        services.Configure<Fleet.RcsFleetOptions>(configuration.GetSection(Fleet.RcsFleetOptions.SectionName));   // CR-027
        services.AddScoped<Fleet.FleetService>();
        // CR-028: Rcs:Dispatch:Mode = Legacy (default) | RcsManaged. A wrong value stops the start instead of silently choosing one.
        var dispatchMode = configuration[$"{Fleet.DispatchOptions.SectionName}:Mode"];
        if (dispatchMode is not null && !Fleet.DispatchOptions.IsValidMode(dispatchMode))
            throw new NotSupportedException($"Rcs:Dispatch:Mode '{dispatchMode}' is not valid. Use '{Fleet.DispatchOptions.Legacy}' or '{Fleet.DispatchOptions.RcsManagedMode}'.");
        services.Configure<Fleet.DispatchOptions>(configuration.GetSection(Fleet.DispatchOptions.SectionName));
        services.AddScoped<Fleet.RcsRobotMapper>();
        // CR-030: the REAL RCS dispatch adapter. OFF and EMPTY by default (Rcs:RealDispatch:Enabled=false, no mapping, no callback secret); the callback service is inert until enabled and authenticated.
        services.Configure<RealDispatch.RealDispatchOptions>(configuration.GetSection(RealDispatch.RealDispatchOptions.SectionName));
        services.AddScoped<RealDispatch.RcsCallbackService>();
        services.AddScoped<Fleet.DispatchModeGuard>();
        services.AddSingleton<Routing.LegAssignmentThrottle>();   // CR-028
        services.AddSingleton<Fleet.DispatchModeState>();   // CR-028: effective mode vs the mode configured now
        services.Configure<ConfigTransfer.ConfigTransferOptions>(configuration.GetSection(ConfigTransfer.ConfigTransferOptions.SectionName));
        services.AddScoped<ConfigTransfer.IConfigGroup, ConfigTransfer.LocationConfigGroup>();
        services.AddScoped<ConfigTransfer.IConfigGroup, ConfigTransfer.AgvConfigGroup>();
        services.AddScoped<ConfigTransfer.IConfigGroup, ConfigTransfer.PlcStationConfigGroup>();
        services.AddScoped<ConfigTransfer.IConfigGroup, ConfigTransfer.BusinessProcessConfigGroup>();
        services.AddScoped<ConfigTransfer.ConfigGroupCatalog>();
        services.AddScoped<ConfigTransfer.ActiveWorkGate>();
        services.AddScoped<ConfigTransfer.ConfigBackupService>();
        services.AddScoped<ConfigTransfer.ConfigTransferService>();
        services.AddScoped<AgvStatusService>();
        services.AddScoped<Diagnostics.DiagnosticExportService>();   // PLC diagnostic export (P1)
        // CR-021: whole-system diagnostic export (sources are registered by the infrastructure; jobs run in the background)
        services.Configure<Diagnostics.SystemDiagnosticOptions>(configuration.GetSection(Diagnostics.SystemDiagnosticOptions.SectionName));
        services.AddScoped<Diagnostics.SystemDiagnosticExporter>();
        services.AddSingleton<Diagnostics.DiagnosticExportJobs>();
        // CR-010
        services.AddScoped<Routing.RouteConditionEvaluator>();
        services.AddScoped<Routing.ProcessValidator>();
        services.AddScoped<Routing.ProcessAdminService>();
        services.AddScoped<Routing.StationTransactionService>();
        services.AddScoped<Routing.IRouteEngine, Routing.RouteEngine>();
        services.AddScoped<Routing.IPlcRequestService, Routing.PlcRequestService>();
        services.AddScoped<ConnectionConfigService>();
        services.AddScoped<OperationalConfigService>();

        services.Configure<Plc.PlcRuntimeOptions>(configuration.GetSection(Plc.PlcRuntimeOptions.SectionName));
        services.AddScoped<Plc.PlcConfigService>();
        services.AddScoped<Plc.PlcProvisioningService>();   // CR-010
        services.AddSingleton<Plc.IPlcRuntime, Plc.PlcRuntime>();
        services.AddSingleton<Plc.HandshakeCoordinator>();
        services.AddSingleton<Plc.PlcStationSignalService>();

        services.Configure<Auth.AuthOptions>(configuration.GetSection(Auth.AuthOptions.SectionName));
        services.AddScoped<Auth.AuthService>();
        services.AddScoped<Auth.UserAdminService>();
        services.AddScoped<Auth.ProfileService>();
        return services;
    }
}
