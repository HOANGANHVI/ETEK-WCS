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
    public DbSet<WCS.Domain.Intake.StationRequestRecord> StationRequests => Set<WCS.Domain.Intake.StationRequestRecord>();
    public DbSet<ConnectionSetting> ConnectionSettings => Set<ConnectionSetting>();
    /// <summary>CR-026: the single system-wide Maintenance flag.</summary>
    public DbSet<SystemMaintenance> SystemMaintenance => Set<SystemMaintenance>();
    /// <summary>CR-026: bookkeeping of configuration imports.</summary>
    public DbSet<ConfigTransferSession> ConfigTransferSessions => Set<ConfigTransferSession>();
    /// <summary>CR-027: AMRs reported by the RCS (read model) and the health of the fleet query.</summary>
    public DbSet<WCS.Domain.Fleet.RcsAmrSnapshot> RcsAmrSnapshots => Set<WCS.Domain.Fleet.RcsAmrSnapshot>();
    public DbSet<WCS.Domain.Fleet.RcsFleetState> RcsFleetStates => Set<WCS.Domain.Fleet.RcsFleetState>();
    public DbSet<WCS.Domain.Users.AppUser> Users => Set<WCS.Domain.Users.AppUser>();
    public DbSet<WCS.Domain.Users.UserAvatar> UserAvatars => Set<WCS.Domain.Users.UserAvatar>();
    public DbSet<WCS.Domain.Users.AuditEntry> AuditLog => Set<WCS.Domain.Users.AuditEntry>();
    public DbSet<WCS.Domain.Plc.PlcConnection> PlcConnections => Set<WCS.Domain.Plc.PlcConnection>();
    public DbSet<WCS.Domain.Plc.PlcStation> PlcStations => Set<WCS.Domain.Plc.PlcStation>();
    public DbSet<WCS.Domain.Plc.StationIoPoint> StationIoPoints => Set<WCS.Domain.Plc.StationIoPoint>();
    public DbSet<WCS.Domain.Plc.PlcCommLogEntry> PlcCommLog => Set<WCS.Domain.Plc.PlcCommLogEntry>();
    /// <summary>PLC diagnostics: latest MC request/response pairs per PLC.</summary>
    public DbSet<WCS.Domain.Plc.PlcExchangeLogEntry> PlcExchangeLog => Set<WCS.Domain.Plc.PlcExchangeLogEntry>();
    public DbSet<WCS.Domain.Logging.SystemLogEntry> SystemLog => Set<WCS.Domain.Logging.SystemLogEntry>();
    // CR-010: configurable Business Process, multi-leg Transport Task and per-port Station Transaction (additive).
    public DbSet<WCS.Domain.Routing.BusinessProcess> BusinessProcesses => Set<WCS.Domain.Routing.BusinessProcess>();
    public DbSet<WCS.Domain.Routing.BusinessProcessVersion> BusinessProcessVersions => Set<WCS.Domain.Routing.BusinessProcessVersion>();
    public DbSet<WCS.Domain.Routing.RouteStep> RouteSteps => Set<WCS.Domain.Routing.RouteStep>();
    public DbSet<WCS.Domain.Routing.RouteCandidate> RouteCandidates => Set<WCS.Domain.Routing.RouteCandidate>();
    public DbSet<WCS.Domain.Routing.TransportOrder> TransportOrders => Set<WCS.Domain.Routing.TransportOrder>();
    public DbSet<WCS.Domain.Routing.TransportLeg> TransportLegs => Set<WCS.Domain.Routing.TransportLeg>();
    public DbSet<WCS.Domain.Routing.StationTransaction> StationTransactions => Set<WCS.Domain.Routing.StationTransaction>();
    public DbSet<WCS.Domain.Routing.RoutingEvent> RoutingEvents => Set<WCS.Domain.Routing.RoutingEvent>();
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
