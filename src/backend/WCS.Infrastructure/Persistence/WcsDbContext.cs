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
