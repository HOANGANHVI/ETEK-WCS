using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WCS.Application.Abstractions;
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

        services.AddDbContext<WcsDbContext>(o => o.UseNpgsql(BuildConnectionString(configuration),
            npgsql => npgsql.MigrationsAssembly(typeof(WcsDbContext).Assembly.FullName)));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAgvRepository, AgvRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IRackRepository, RackRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<ITransportTaskRepository, TransportTaskRepository>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        services.AddScoped<WcsDataSeeder>();

        var mode = configuration[$"{IntegrationOptions.SectionName}:Mode"] ?? "Simulator";
        if (!mode.Equals("Simulator", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Integration:Mode '{mode}' is not available. Only 'Simulator' exists: real Mitsubishi PLC and HIK RCS-2000 adapters " +
                "are not implemented until addresses, API version and mappings are confirmed.");

        services.AddSingleton<PlcSimulator>();
        services.AddSingleton<RcsSimulator>();
        services.AddSingleton<StationSignalSimulator>();
        services.AddSingleton<IPlcAdapter>(sp => sp.GetRequiredService<PlcSimulator>());
        services.AddSingleton<IRcsClient>(sp => sp.GetRequiredService<RcsSimulator>());
        services.AddSingleton<IStationSignalService>(sp => sp.GetRequiredService<StationSignalSimulator>());
        services.AddScoped<SimulationDriver>();

        services.TryAddSingleton<IWcsNotifier, NullWcsNotifier>();
        return services;
    }
}

internal sealed class NullWcsNotifier : IWcsNotifier
{
    public Task PublishAsync(IReadOnlyCollection<WcsEvent> events, CancellationToken ct = default) => Task.CompletedTask;
}
