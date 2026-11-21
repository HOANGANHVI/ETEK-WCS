using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WCS.Application;
using WCS.Application.Abstractions;
using WCS.Application.Services;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;
using WCS.Infrastructure;
using WCS.Infrastructure.Persistence;
using WCS.Infrastructure.Simulation;

namespace WCS.Tests.Support;

/// <summary>
/// One throw-away PostgreSQL database per test run (created, migrated with the REAL migrations, dropped at the end).
/// The password comes from User Secrets (shared id with WCS.Api) or the WCS_DB_PASSWORD env var - never from source.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public IConfigurationRoot Configuration { get; private set; } = default!;
    public string ConnectionString { get; private set; } = default!;
    private string _adminConnectionString = default!;
    private string _dbName = default!;

    public async Task InitializeAsync()
    {
        Configuration = new ConfigurationBuilder()
            .AddJsonFile("api.appsettings.json", optional: false)
            .AddJsonFile("api.appsettings.Development.json", optional: false)
            .AddUserSecrets<DatabaseFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var main = new NpgsqlConnectionStringBuilder(WCS.Infrastructure.DependencyInjection.BuildConnectionString(Configuration));
        _dbName = $"wcs_test_{Guid.NewGuid():N}";
        _adminConnectionString = new NpgsqlConnectionStringBuilder(main.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(main.ConnectionString) { Database = _dbName }.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", admin);
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<WcsDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new WcsDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", admin);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetAsync()
    {
        await using var c = new NpgsqlConnection(ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE rcs_amr_snapshots, rcs_fleet_state, routing_events, station_transactions, transport_legs, transport_orders, route_candidates, route_steps, business_process_versions, business_processes, station_requests, task_history, transport_tasks, reservations, inventory_transactions, racks, processed_events, locations, agvs, connection_settings, app_users, audit_log, plc_comm_log, plc_exchange_log, system_log, station_io_points, plc_stations, plc_connections CASCADE", c);
        await cmd.ExecuteNonQueryAsync();
        await using var maintenance = new NpgsqlCommand("UPDATE system_maintenance SET \"Enabled\" = false, \"Reason\" = NULL, \"ChangedBy\" = NULL", c);   // CR-026: every test starts with Maintenance off
        await maintenance.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Business-level test harness around the real application services, PostgreSQL and the simulators.</summary>
public sealed class Harness : IAsyncDisposable
{
    public ServiceProvider Services { get; private set; } = default!;
    private readonly DatabaseFixture _db;
    private readonly Action<IServiceCollection>? _configure;
    public TestClock Clock { get; } = new();
    public RcsSimulator Rcs => Services.GetRequiredService<RcsSimulator>();
    public StationSignalSimulator Stations => Services.GetRequiredService<StationSignalSimulator>();
    public SchedulerOptions SchedulerOptions => Services.GetRequiredService<IOptions<SchedulerOptions>>().Value;

    private readonly IReadOnlyDictionary<string, string?>? _extra;

    private Harness(DatabaseFixture db, Action<IServiceCollection>? configure, IReadOnlyDictionary<string, string?>? extra = null)
    {
        _db = db; _configure = configure; _extra = extra;
        Build();
    }

    /// <summary>CR-008B-2: simulates a backend restart - a NEW service provider (all in-memory state such as the station signal queue is gone) on the SAME database and clock.</summary>
    public async Task RestartAsync()
    {
        var keep = SchedulerOptions;
        var (initial, max, maxAttempts, maxDispatch) = (keep.RcsRetryInitialDelayMs, keep.RcsRetryMaxDelayMs, keep.RcsMaxAttempts, keep.MaxDispatchAttempts);
        await Services.DisposeAsync();
        Build();
        SchedulerOptions.RcsRetryInitialDelayMs = initial; SchedulerOptions.RcsRetryMaxDelayMs = max;
        SchedulerOptions.RcsMaxAttempts = maxAttempts; SchedulerOptions.MaxDispatchAttempts = maxDispatch;
    }

    private void Build()
    {
        var db = _db; var configure = _configure;
        var config = new ConfigurationBuilder()
            .AddConfiguration(db.Configuration)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:WcsDatabase"] = db.ConnectionString })
            .AddInMemoryCollection(_extra ?? new Dictionary<string, string?>())   // CR-027: a test may add configuration keys (for example Rcs:Fleet:Source)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);   // CR-021: the system diagnostic export reads (masked) configuration sections
        services.AddWcsApplication(config);
        services.AddWcsInfrastructure(config);
        services.AddSingleton<IClock>(Clock);
        configure?.Invoke(services);   // CR-008A: lets a test replace a registration (e.g. a Production host environment)
        Services = services.BuildServiceProvider();
        // CR-008A: the production default spaces RCS retries (2 s, doubling). The pre-existing tests exercise one retry per scheduler tick, so the harness starts with NO
        // spacing; the CR-008A tests set the delays they need explicitly.
        SchedulerOptions.RcsRetryInitialDelayMs = 0;
    }

    public static async Task<Harness> CreateAsync(DatabaseFixture db, Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        await db.ResetAsync();
        return new Harness(db, configure, extraConfig);
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    // ------------------------------------------------------------------ plumbing
    public async Task<T> Scoped<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<T> Db<T>(Func<WcsDbContext, Task<T>> query) => Scoped(sp => query(sp.GetRequiredService<WcsDbContext>()));

    // ------------------------------------------------------------------ arrangement
    /// <summary>AGV1/AGV2 + A1,A2,B1,B2 (cap 1, PLC permission) + F1..F5. Capacities are TEST values, not business rules.</summary>
    public async Task SeedStandardAsync(int bufferCapacity = 3, int stationCapacity = 1)
    {
        await Db(async db =>
        {
            db.Agvs.Add(new Agv("AGV1", AgvRole.FullRack, "R-FULL"));
            db.Agvs.Add(new Agv("AGV2", AgvRole.EmptyRack, "R-EMPTY"));
            foreach (var c in new[] { "A1", "A2", "B1", "B2" })
                db.Locations.Add(new Location(c, c, LocationType.Station, stationCapacity, AllowedRackKind.Any, true, Clock.UtcNow));
            foreach (var c in new[] { "F1", "F2" })
                db.Locations.Add(new Location(c, c, LocationType.Buffer, bufferCapacity, AllowedRackKind.Full, false, Clock.UtcNow));
            foreach (var c in new[] { "F3", "F4" })
                db.Locations.Add(new Location(c, c, LocationType.Buffer, bufferCapacity, AllowedRackKind.Empty, false, Clock.UtcNow));
            db.Locations.Add(new Location("F5", "F5", LocationType.ManualBuffer, bufferCapacity, AllowedRackKind.Any, false, Clock.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
    }

    public Task StockAsync(string location, RackKind kind, int qty) =>
        Scoped(async sp => await sp.GetRequiredService<IInventoryService>().DeclareInitialStockAsync(location, kind, qty, "test"));

    public Task SetCapacityAsync(string location, int capacity) => Db(async db =>
    {
        var l = await db.Locations.SingleAsync(x => x.Code == location);
        l.Configure(l.Name, capacity, l.AllowedRackKind, l.Enabled, l.RequiresPlcPermission, Clock.UtcNow);
        await db.SaveChangesAsync();
        return 0;
    });

    // ------------------------------------------------------------------ actions
    public Task<CreateTaskResult> CreateTaskAsync(string flow, string station, string? key = null) =>
        Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>()
            .CreateTaskAsync(new CreateTaskCommand(key ?? Guid.NewGuid().ToString("N"), flow, station, "test")));

    public Task<SignalResult> SignalAsync(Guid taskId, SignalKind kind, string? eventKey = null, string? externalRackId = null) =>
        Scoped(sp => sp.GetRequiredService<ITaskOrchestrator>().HandleSignalAsync(
            new TaskSignal(kind, taskId, EventKey: eventKey, Source: ConfirmationSource.Simulator, User: "test", ExternalRackId: externalRackId)));

    public async Task RunAsync(Func<IServiceProvider, Task> action) { await using var scope = Services.CreateAsyncScope(); await action(scope.ServiceProvider); }

    /// <summary>One station-intake pass (what the scheduler hosted service runs before the scheduler pass). Returns the number of accepted requests.</summary>
    public Task<int> IntakeAsync() => Scoped(sp => sp.GetRequiredService<StationRequestProcessor>().RunOnceAsync());

    public Task TickAsync() => Scoped(async sp => { await sp.GetRequiredService<ITaskScheduler>().RunOnceAsync(); return 0; });

    public Task<SimulationStep> AdvanceAsync(Guid taskId) => Scoped(sp => sp.GetRequiredService<SimulationDriver>().AdvanceAsync(taskId));

    /// <summary>Advance the simulated devices until the task reaches <paramref name="state"/> (scheduler ticks in between).</summary>
    public async Task<TransportTask> AdvanceUntilAsync(Guid taskId, TaskState state, int maxSteps = 30)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            var t = await GetTaskAsync(taskId);
            if (t.State == state) return t;
            var step = await AdvanceAsync(taskId);
            if (step.Signal is null) await TickAsync();
        }
        var final = await GetTaskAsync(taskId);
        Assert.Fail($"Task did not reach {state}; stuck in {final.State} ({final.StateReason}).");
        return final;
    }

    // ------------------------------------------------------------------ inspection
    public Task<TransportTask> GetTaskAsync(Guid id) => Db(db => db.Tasks.Include(t => t.History).AsNoTracking().SingleAsync(t => t.Id == id));
    public Task<Location> LocationAsync(string code) => Db(db => db.Locations.AsNoTracking().SingleAsync(l => l.Code == code));
    public Task<Agv> AgvAsync(string code) => Db(db => db.Agvs.AsNoTracking().SingleAsync(a => a.Code == code));
    public Task<List<Rack>> RacksAtAsync(string code) => Db(async db =>
    {
        var id = (await db.Locations.SingleAsync(l => l.Code == code)).Id;
        return await db.Racks.AsNoTracking().Where(r => r.CurrentLocationId == id).ToListAsync();
    });
    public Task<int> ActiveTaskCountAsync(string agvCode) => Db(async db =>
    {
        var id = (await db.Agvs.SingleAsync(a => a.Code == agvCode)).Id;
        var occupying = TaskStateMachine.AgvOccupyingStates.ToArray();
        return await db.Tasks.CountAsync(t => t.AgvId == id && occupying.Contains(t.State));
    });
    public Task<List<Reservation>> ActiveReservationsAsync() =>
        Db(db => db.Reservations.AsNoTracking().Where(r => r.Status == ReservationStatus.Active).ToListAsync());
    public Task<List<InventoryTransaction>> TransactionsAsync(Guid taskId) =>
        Db(db => db.InventoryTransactions.AsNoTracking().Where(t => t.TaskId == taskId).OrderBy(t => t.OccurredAt).ToListAsync());
}
