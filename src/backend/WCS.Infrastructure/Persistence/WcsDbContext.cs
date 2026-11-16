using Microsoft.EntityFrameworkCore;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Infrastructure.Persistence;

public class WcsDbContext(DbContextOptions<WcsDbContext> options) : DbContext(options)
{
    public DbSet<Agv> Agvs => Set<Agv>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<TransportTask> Tasks => Set<TransportTask>();
    public DbSet<TaskHistoryEntry> TaskHistory => Set<TaskHistoryEntry>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<ConnectionSetting> ConnectionSettings => Set<ConnectionSetting>();
    public DbSet<WCS.Domain.Users.AppUser> Users => Set<WCS.Domain.Users.AppUser>();
    public DbSet<WCS.Domain.Users.AuditEntry> AuditLog => Set<WCS.Domain.Users.AuditEntry>();
    public DbSet<WCS.Domain.Plc.PlcConnection> PlcConnections => Set<WCS.Domain.Plc.PlcConnection>();
    public DbSet<WCS.Domain.Plc.PlcStation> PlcStations => Set<WCS.Domain.Plc.PlcStation>();
    public DbSet<WCS.Domain.Plc.StationIoPoint> StationIoPoints => Set<WCS.Domain.Plc.StationIoPoint>();
    public DbSet<WCS.Domain.Plc.PlcCommLogEntry> PlcCommLog => Set<WCS.Domain.Plc.PlcCommLogEntry>();
    public DbSet<WCS.Domain.Logging.SystemLogEntry> SystemLog => Set<WCS.Domain.Logging.SystemLogEntry>();
    /// <summary>CR-007: read-only unified view over system_log, plc_comm_log and audit_log.</summary>
    public DbSet<WCS.Infrastructure.Logging.LogRow> LogRows => Set<WCS.Infrastructure.Logging.LogRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WcsDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable strings so partial-index filters and ad-hoc SQL stay self-explanatory.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }
}
