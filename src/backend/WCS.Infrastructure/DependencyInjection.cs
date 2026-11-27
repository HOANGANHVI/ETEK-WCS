using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WCS.Application.Abstractions;
using WCS.Application.Config;
using WCS.Infrastructure.Config;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Persistence.Repositories;
using WCS.Infrastructure.Seeding;
using WCS.Infrastructure.Simulation;

namespace WCS.Infrastructure;

public sealed class IntegrationOptions
{
    public const string SectionName = "Integration";
    /// <summary>Only "Simulator" exists in Phase 1. Real PLC/RCS adapters are not implemented (no confirmed addresses/APIs).</summary>
    public string Mode { get; set; } = "Simulator";
    /// <summary>
    /// When true the connection check opens (and immediately closes) a TCP connection to the configured host:port. Default false: the check is simulated
    /// and never touches the network. Enable only after the device addresses have been confirmed.
    /// </summary>
    public bool AllowNetworkProbe { get; set; }
}

public static class DependencyInjection
{
    /// <summary>Builds the connection string. The password is NEVER stored in source: it comes from User Secrets / env vars.</summary>
    public static string BuildConnectionString(IConfiguration configuration)
    {
        var baseCs = configuration.GetConnectionString("WcsDatabase")
                     ?? throw new InvalidOperationException("ConnectionStrings:WcsDatabase is not configured.");
        var csb = new NpgsqlConnectionStringBuilder(baseCs);
        var password = configuration["Database:Password"] ?? Environment.GetEnvironmentVariable("WCS_DB_PASSWORD");
        if (!string.IsNullOrEmpty(password)) csb.Password = password;
        if (string.IsNullOrEmpty(csb.Password))
            throw new InvalidOperationException(
                "No database password configured. Set it with 'dotnet user-secrets set \"Database:Password\" \"<password>\"' " +
                "(project WCS.Api) or the WCS_DB_PASSWORD / Database__Password environment variable.");
        return csb.ConnectionString;
    }

    public static IServiceCollection AddWcsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<IntegrationOptions>(configuration.GetSection(IntegrationOptions.SectionName));

        // Server resource monitoring: container metrics (cgroup) and host metrics are separate providers chosen by platform; one sampler feeds the dashboard.
        services.Configure<WCS.Application.Monitoring.ResourceMonitoringOptions>(configuration.GetSection(WCS.Application.Monitoring.ResourceMonitoringOptions.SectionName));
        var diskPath = configuration[$"{WCS.Application.Monitoring.ResourceMonitoringOptions.SectionName}:DiskPath"];
        services.AddSingleton<WCS.Application.Monitoring.IContainerMetricsProvider>(_ => WCS.Infrastructure.Monitoring.MetricsProviderFactory.Container(diskPath));
        services.AddSingleton<WCS.Application.Monitoring.IHostMetricsProvider>(_ => WCS.Infrastructure.Monitoring.MetricsProviderFactory.Host(diskPath));
        services.AddSingleton<WCS.Infrastructure.Monitoring.ResourceMonitor>();
        services.AddSingleton<WCS.Application.Monitoring.IResourceMonitor>(sp => sp.GetRequiredService<WCS.Infrastructure.Monitoring.ResourceMonitor>());
        services.AddHostedService(sp => sp.GetRequiredService<WCS.Infrastructure.Monitoring.ResourceMonitor>());

        // CR-021: slow commands and failed commands (deadlock, constraint, timeout) are logged without parameter values (Diagnostics:SlowQueryMs, default 1000)
        var slowQueryMs = configuration.GetValue("Diagnostics:SlowQueryMs", 1000);
        services.AddDbContext<WcsDbContext>((sp, o) => o.UseNpgsql(BuildConnectionString(configuration),
            npgsql => npgsql.MigrationsAssembly(typeof(WcsDbContext).Assembly.FullName))
            .AddInterceptors(new DbDiagnosticsInterceptor(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DbDiagnosticsInterceptor>>(), slowQueryMs)));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<WCS.Application.Abstractions.IMaintenanceStore, WCS.Infrastructure.Maintenance.MaintenanceStore>();   // CR-026
        services.AddScoped<WCS.Application.Abstractions.IMaintenanceGate, WCS.Infrastructure.Maintenance.MaintenanceGate>();
        services.AddScoped<WCS.Application.ConfigTransfer.IConfigTransferStore, WCS.Infrastructure.ConfigTransfer.ConfigTransferStore>();   // CR-026
        // CR-032: RCS Diagnostics & Manual API Tester (Admin only): own HTTP path to the saved RCS connection, history repository, in-memory gate / last checks
        services.AddScoped<WCS.Application.RcsDiagnostics.IRcsDiagnosticsTransport, WCS.Infrastructure.RcsDiagnostics.RcsDiagnosticsTransport>();
        services.AddScoped<WCS.Application.RcsDiagnostics.IRcsDiagnosticsRepository, WCS.Infrastructure.RcsDiagnostics.RcsDiagnosticsRepository>();
        services.AddScoped<WCS.Application.RcsDiagnostics.RcsDiagnosticsService>();
        services.AddSingleton<WCS.Application.RcsDiagnostics.RcsDiagnosticsState>();
        services.Configure<WCS.Application.RcsDiagnostics.RcsDiagnosticsOptions>(configuration.GetSection(WCS.Application.RcsDiagnostics.RcsDiagnosticsOptions.SectionName));
        // CR-027: the AMR fleet read model and where the list comes from (Rcs:Fleet:Source = Simulator (default) | Real = HTTP queryAgvStatus)
        services.AddScoped<WCS.Application.Fleet.IRcsFleetRepository, WCS.Infrastructure.Fleet.RcsFleetRepository>();
        if (string.Equals(configuration[$"{WCS.Application.Fleet.RcsFleetOptions.SectionName}:Source"], "Real", StringComparison.OrdinalIgnoreCase)) services.AddScoped<WCS.Application.Fleet.IRcsFleetQuery, WCS.Infrastructure.Fleet.HikFleetQueryClient>();
        else services.AddScoped<WCS.Application.Fleet.IRcsFleetQuery, WCS.Infrastructure.Fleet.SimulatorFleetQuery>();
        services.AddScoped<IAgvRepository, AgvRepository>();
        services.AddScoped<IStationRequestRepository, StationRequestRepository>();   // CR-008B-2
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IRackRepository, RackRepository>();
        services.AddScoped<IProductModelRepository, ProductModelRepository>();   // CR-036
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<ITransportTaskRepository, TransportTaskRepository>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        // CR-010: configurable Business Process / Route Engine persistence and the simulator-only leg dispatcher
        services.AddScoped<WCS.Application.Routing.IRoutingRepository, RoutingRepository>();
        // CR-030: Rcs:RealDispatch:Enabled=true replaces the RCS SIMULATOR by the real adapter (never switched automatically). Routing legs have NO real dispatcher yet (no confirmed leg mapping): with the
        // real adapter they are refused (Blocked) instead of being "accepted" by the simulator.
        var realRcsDispatch = configuration.GetValue<bool>($"{WCS.Application.RealDispatch.RealDispatchOptions.SectionName}:Enabled");
        // CR-038: BOTH dispatchers always exist; ONE routed dispatcher picks the simulator or the real adapter per order (the mode pinned when it was created). Rcs:RealDispatch:Enabled stays the
        // deployment's permission for Real AGV: without it the mode cannot be switched to Real and the real adapter refuses every call.
        services.AddScoped<SimulatedLegDispatcher>();
        services.AddScoped<WCS.Infrastructure.RealDispatch.RealLegDispatcher>();
        services.AddScoped<WCS.Application.Routing.ILegDispatcher, WCS.Infrastructure.ModeControl.RoutedLegDispatcher>();
        services.AddSingleton<WCS.Application.Routing.IPlcLinkStatus, PlcRuntimeLinkStatus>();
        services.AddSingleton<WCS.Application.Routing.IPlcObservationSource, UnavailablePlcObservationSource>();
        services.AddScoped<WcsDataSeeder>();
        services.AddScoped<DefaultProcessSeeder>();   // CR-039
        services.AddScoped<WCS.Application.Logging.ILogQueryService, WCS.Infrastructure.Logging.LogQueryService>();   // CR-007
        services.AddScoped<WCS.Infrastructure.Logging.DeploymentLogImporter>();
        services.AddScoped<WCS.Application.Plc.IPlcConfigRepository, WCS.Infrastructure.Plc.PlcConfigRepository>();
        services.AddScoped<WCS.Infrastructure.Plc.PlcCatalogSeeder>();
        services.AddScoped<WCS.Application.Diagnostics.IPlcDiagnosticSource, WCS.Infrastructure.Plc.PlcDiagnosticSource>();
        // CR-021: read-only readers of the stored log sources for the system diagnostic export
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticEventSource, WCS.Infrastructure.Diagnostics.SystemLogSource>();
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticEventSource, WCS.Infrastructure.Diagnostics.PlcCommLogSource>();
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticEventSource, WCS.Infrastructure.Diagnostics.AuditLogSource>();
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticEventSource, WCS.Infrastructure.Diagnostics.TaskHistorySource>();
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticEventSource, WCS.Infrastructure.Diagnostics.RoutingEventSource>();
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticEventSource, WCS.Infrastructure.Diagnostics.InventoryTransactionSource>();
        services.AddScoped<WCS.Application.Diagnostics.IDiagnosticHealthSource, WCS.Infrastructure.Diagnostics.DiagnosticHealthSource>();
        services.AddSingleton(_ => WCS.Infrastructure.Plc.PlcCatalog.Current);   // CR-046: one catalog for the seeder, the signal template and the IO map view
        services.AddSingleton<WCS.Application.Plc.IPlcTemplateProvider, WCS.Infrastructure.Plc.PlcTemplateProvider>();   // CR-010 Default IO Mapping Template
        services.AddSingleton<WCS.Infrastructure.Plc.SimulatedPlcNetwork>();
        services.AddSingleton<WCS.Application.Plc.ISimulatorControl>(sp => sp.GetRequiredService<WCS.Infrastructure.Plc.SimulatedPlcNetwork>());
        services.AddSingleton<WCS.Infrastructure.Plc.PlcExchangeRecorder>();   // PLC diagnostics: stored MC exchange history
        services.AddSingleton<WCS.Application.Plc.IPlcExchangeSink>(sp => sp.GetRequiredService<WCS.Infrastructure.Plc.PlcExchangeRecorder>());
        services.AddSingleton<WCS.Application.Plc.IPlcEndpointFactory, WCS.Infrastructure.Plc.PlcEndpointFactory>();
        services.AddSingleton<WCS.Application.Plc.IPlcEvents, WCS.Infrastructure.Plc.PlcEventDispatcher>();

        services.AddScoped<WCS.Application.Auth.IUserRepository, WCS.Infrastructure.Auth.UserRepository>();
        services.AddSingleton<WCS.Application.Auth.IPasswordService, WCS.Infrastructure.Auth.PasswordService>();
        services.AddScoped<WCS.Application.Auth.IAuditLog, WCS.Infrastructure.Auth.AuditLog>();
        services.AddSingleton<WCS.Application.Plc.IPlcWriteAudit, WCS.Infrastructure.Plc.PlcWriteAudit>();   // CR-044
        services.AddScoped<WCS.Infrastructure.Auth.AdminBootstrapper>();

        var dp = services.AddDataProtection().SetApplicationName("ETEK.WCS");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath)) dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IConnectionSettingRepository, ConnectionSettingRepository>();
        if (configuration.GetValue<bool>($"{IntegrationOptions.SectionName}:AllowNetworkProbe")) services.AddSingleton<IConnectionProbe, TcpReachabilityProbe>();
        else services.AddSingleton<IConnectionProbe, SimulatedConnectionProbe>();

        var mode = configuration[$"{IntegrationOptions.SectionName}:Mode"] ?? "Simulator";
        if (!mode.Equals("Simulator", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Integration:Mode '{mode}' is not available. Only 'Simulator' exists: real Mitsubishi PLC and HIK RCS-2000 adapters " +
                "are not implemented until addresses, API version and mappings are confirmed.");

        services.AddSingleton<PlcSimulator>();
        // CR-027: Rcs:Fleet:SimulatorSize (default 0 = an empty simulated fleet) fills the RCS SIMULATOR with that many AMRs (SIM-001..) for a demo / test. Real AMRs are never simulated by default.
        // CR-050: Rcs:Fleet:SimulatorRobotCodes (optional) names them, e.g. with the AGV robot codes of the Legacy dispatch, so that no second set of AMRs appears when work is sent
        services.AddSingleton(_ => { var sim = new RcsSimulator(); sim.SetFleetSize(configuration.GetValue("Rcs:Fleet:SimulatorSize", 0), configuration.GetSection("Rcs:Fleet:SimulatorRobotCodes").Get<string[]>()); return sim; });
        // CR-050: AMR execution behind the simulated RCS. Off unless configured or switched on by an Admin.
        services.Configure<WCS.Infrastructure.Simulation.AmrSimulationOptions>(configuration.GetSection(WCS.Infrastructure.Simulation.AmrSimulationOptions.SectionName));
        services.AddSingleton<WCS.Infrastructure.Simulation.AmrExecutionSimulator>();
        services.AddHostedService<WCS.Infrastructure.Simulation.AmrSimulationHostedService>();
        services.AddSingleton<StationSignalSimulator>();
        services.AddSingleton<IPlcAdapter>(sp => sp.GetRequiredService<PlcSimulator>());
        // CR-021: every RCS call is logged (operation, request code, outcome, duration, adapter); the Production guard looks through the wrapper (IRcsClientDecorator.Unwrap)
        // CR-038: the system's IRcsClient is the ROUTED client (simulator or real adapter per task, decided by the mode pinned on the task). The real HTTP client is always registered but
        // is reached only for a task created in Real mode, and fails closed without its configuration.
        services.AddSingleton<WCS.Infrastructure.RealDispatch.HikRcsClient>();
        services.AddSingleton<IExecutionModeProvider, WCS.Infrastructure.ModeControl.ExecutionModeProvider>();
        services.AddSingleton<IExecutionModeResolver, WCS.Infrastructure.ModeControl.ExecutionModeResolver>();
        services.AddSingleton<IRcsMessageRecorder, WCS.Infrastructure.ModeControl.RcsMessageRecorder>();
        services.AddScoped<IExecutionModeGate, WCS.Infrastructure.ModeControl.ExecutionModeGate>();
        services.AddScoped<IExecutionModeService, WCS.Infrastructure.ModeControl.ExecutionModeService>();
        services.AddScoped<IFleetStatusReader, WCS.Infrastructure.ModeControl.FleetStatusReader>();
        services.AddScoped<IRcsMessageQuery, WCS.Infrastructure.ModeControl.RcsMessageQuery>();
        services.AddSingleton<WCS.Infrastructure.ModeControl.RoutedRcsClient>();
        services.AddSingleton<IRcsClient>(sp => new LoggingRcsClient(sp.GetRequiredService<WCS.Infrastructure.ModeControl.RoutedRcsClient>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LoggingRcsClient>>()));
        // CR-008A: Production must not dispatch to the simulator (explicit opt-in only).
        var allowRcsSimulatorInProduction = configuration.GetValue("Rcs:AllowSimulatorInProduction", false);
        services.AddSingleton<IRcsDispatchGuard>(sp => new RcsDispatchGuard(sp.GetRequiredService<IRcsClient>(),
            sp.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>(), allowRcsSimulatorInProduction, sp.GetRequiredService<IExecutionModeProvider>(), realRcsDispatch));
        // The legacy simulator stays the default. The PLC-backed service is used only when Plc:Integration:Enabled=true (approved flow mappings required).
        services.AddSingleton<IStationSignalService>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WCS.Application.Plc.PlcRuntimeOptions>>().Value.Integration.Enabled
                ? sp.GetRequiredService<WCS.Application.Plc.PlcStationSignalService>()
                : sp.GetRequiredService<StationSignalSimulator>());
        services.AddScoped<SimulationDriver>();

        services.TryAddSingleton<IWcsNotifier, NullWcsNotifier>();
        return services;
    }
}

internal sealed class NullWcsNotifier : IWcsNotifier
{
    public Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default) => Task.CompletedTask;
}
