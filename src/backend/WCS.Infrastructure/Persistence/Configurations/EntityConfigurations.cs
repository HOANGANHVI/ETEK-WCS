using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WCS.Domain.Agvs;
using WCS.Domain.Common;
using WCS.Domain.Inventory;
using WCS.Domain.Locations;
using WCS.Domain.Tasks;

namespace WCS.Infrastructure.Persistence.Configurations;

internal sealed class AgvConfiguration : IEntityTypeConfiguration<Agv>
{
    public void Configure(EntityTypeBuilder<Agv> b)
    {
        b.ToTable("agvs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.RcsRobotCode).HasMaxLength(100);
        b.Property(x => x.StatusDetail).HasMaxLength(500);
        b.Property(x => x.DispatchHold).HasDefaultValue(false);   // CR-008B-5 (additive)
        b.Property(x => x.DispatchHoldReason).HasMaxLength(200);
        b.Property(x => x.Version).IsRowVersion(); // PostgreSQL xmin
        b.Ignore(x => x.CarriedRackKind);
        b.HasIndex(x => x.Code).IsUnique();
        // One AGV per business role (AGV1 = full racks, AGV2 = empty racks).
        b.HasIndex(x => x.Role).IsUnique();
        // A robot code identifies one AGV in RCS.
        b.HasIndex(x => x.RcsRobotCode).IsUnique().HasFilter("\"RcsRobotCode\" IS NOT NULL");
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("locations", t => t.HasCheckConstraint("ck_locations_capacity", "\"RackCapacity\" >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class RackConfiguration : IEntityTypeConfiguration<Rack>
{
    public void Configure(EntityTypeBuilder<Rack> b)
    {
        // IN_TRANSIT racks have no location (a rack has at most one current location, inventory-management.md 4.4).
        b.ToTable("racks", t => t.HasCheckConstraint("ck_racks_transit_no_location",
            "\"Status\" <> 'InTransit' OR \"CurrentLocationId\" IS NULL"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.ExternalRackId).HasMaxLength(100);
        b.Property(x => x.RackType).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne<Location>().WithMany().HasForeignKey(x => x.CurrentLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CurrentLocationId);
        // A rack id, when known, identifies exactly one rack (NEEDS_CONFIRMATION: rack id model).
        b.HasIndex(x => x.ExternalRackId).IsUnique().HasFilter("\"ExternalRackId\" IS NOT NULL");
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.ToTable("reservations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.HasIndex(x => x.TaskId);
        b.HasIndex(x => new { x.LocationId, x.Status });
        // Two tasks can never hold the same rack: unique while the source-rack reservation is ACTIVE.
        b.HasIndex(x => x.RackId).IsUnique()
            .HasFilter("\"Status\" = 'Active' AND \"Kind\" = 'SourceRack' AND \"RackId\" IS NOT NULL");
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("inventory_transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.UserName).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => x.TaskId);
        b.HasIndex(x => x.FromLocationId);
        b.HasIndex(x => x.ToLocationId);
        b.HasIndex(x => x.OccurredAt);
    }
}

internal sealed class StationRequestConfiguration : IEntityTypeConfiguration<WCS.Domain.Intake.StationRequestRecord>
{
    public void Configure(EntityTypeBuilder<WCS.Domain.Intake.StationRequestRecord> b)
    {
        b.ToTable("station_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RequestKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.StationCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Flow).HasMaxLength(50).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsPending);
        b.HasIndex(x => x.RequestKey).IsUnique();            // request idempotency: one record per PlcId:Station:Request_ID
        b.HasIndex(x => new { x.Status, x.FirstSeenAt });
    }
}

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> b)
    {
        b.ToTable("processed_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.Property(x => x.EventKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.Source, x.EventKey }).IsUnique(); // callback idempotency
    }
}

internal sealed class TransportTaskConfiguration : IEntityTypeConfiguration<TransportTask>
{
    public void Configure(EntityTypeBuilder<TransportTask> b)
    {
        b.ToTable("transport_tasks");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.RequestKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.RcsRequestCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(100);   // CR-021
        b.HasIndex(x => x.CorrelationId);
        b.Property(x => x.RcsTaskCode).HasMaxLength(100);
        b.Property(x => x.StateReason).HasMaxLength(1000);
        b.Property(x => x.StateReasonCode).HasMaxLength(100);
        b.Property(x => x.StateReasonArgsJson).HasColumnType("text");
        b.Property(x => x.RequestedBy).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsTerminal);
        b.Ignore(x => x.OccupiesAgv);

        b.HasOne<Agv>().WithMany().HasForeignKey(x => x.AgvId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(x => x.RequestLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(x => x.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(x => x.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.History).WithOne().HasForeignKey(h => h.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.RequestKey).IsUnique();   // request idempotency
        b.HasIndex(x => x.RcsRequestCode).IsUnique();
        b.HasIndex(x => x.RcsTaskCode).IsUnique().HasFilter("\"RcsTaskCode\" IS NOT NULL");
        b.HasIndex(x => new { x.AgvId, x.State, x.CreatedAt });
        b.HasIndex(x => x.State);

        // PER_AGV (BR-01): at most ONE AGV-occupying task per AGV, enforced by the database itself.
        var occupying = string.Join(", ", TaskStateMachine.AgvOccupyingStates.Select(s => $"'{s}'"));
        b.HasIndex(x => x.AgvId).IsUnique().HasDatabaseName("ux_transport_tasks_one_active_per_agv")
            .HasFilter($"\"State\" IN ({occupying})");
    }
}

internal sealed class TaskHistoryConfiguration : IEntityTypeConfiguration<TaskHistoryEntry>
{
    public void Configure(EntityTypeBuilder<TaskHistoryEntry> b)
    {
        b.ToTable("task_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the application; without this EF treats a new child with a preset Guid as Modified (UPDATE hits 0 rows)
        b.Property(x => x.EventName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(2000);
        b.Property(x => x.DetailCode).HasMaxLength(100);
        b.Property(x => x.DetailArgsJson).HasColumnType("text");
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.TaskId, x.OccurredAt });
    }
}

internal sealed class ConnectionSettingConfiguration : IEntityTypeConfiguration<ConnectionSetting>
{
    public void Configure(EntityTypeBuilder<ConnectionSetting> b)
    {
        b.ToTable("connection_settings");
        b.HasKey(x => x.Kind);
        b.Property(x => x.SettingsJson).HasColumnType("text").IsRequired();
        b.Property(x => x.ProtectedSecrets).HasColumnType("text"); // encrypted blob, never plaintext
        b.Property(x => x.LastTestMode).HasMaxLength(40);
        b.Property(x => x.LastTestMessage).HasMaxLength(1000);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<WCS.Domain.Users.AppUser>
{
    public void Configure(EntityTypeBuilder<WCS.Domain.Users.AppUser> b)
    {
        b.ToTable("app_users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Username).HasMaxLength(50).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Theme).HasMaxLength(10).IsRequired().HasDefaultValue(WCS.Domain.Users.UserPreferences.DefaultTheme);
        b.Property(x => x.SidebarCollapsed).HasDefaultValue(false);
        b.Property(x => x.Email).HasMaxLength(WCS.Domain.Users.AppUser.EmailMax);
        b.Property(x => x.Position).HasMaxLength(WCS.Domain.Users.AppUser.OrganizationMax);
        b.Property(x => x.Department).HasMaxLength(WCS.Domain.Users.AppUser.OrganizationMax);
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired(); // salted hash only, never the password
        b.HasIndex(x => x.Username).IsUnique();
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class UserAvatarConfiguration : IEntityTypeConfiguration<WCS.Domain.Users.UserAvatar>
{
    public void Configure(EntityTypeBuilder<WCS.Domain.Users.UserAvatar> b)
    {
        b.ToTable("user_avatars");
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.ContentType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Data).IsRequired();
        // The avatar belongs to the account and goes with it; the account is never touched when the image changes.
        b.HasOne<WCS.Domain.Users.AppUser>().WithOne().HasForeignKey<WCS.Domain.Users.UserAvatar>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<WCS.Domain.Users.AuditEntry>
{
    public void Configure(EntityTypeBuilder<WCS.Domain.Users.AuditEntry> b)
    {
        b.ToTable("audit_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Actor).HasMaxLength(100);
        b.Property(x => x.ActorRole).HasMaxLength(40);
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.TargetType).HasMaxLength(50);
        b.Property(x => x.TargetId).HasMaxLength(100);
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.Property(x => x.BeforeJson).HasColumnType("text");
        b.Property(x => x.AfterJson).HasColumnType("text");
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.Actor, x.OccurredAt });
        b.HasIndex(x => x.Action);
    }
}

internal sealed class SystemMaintenanceConfiguration : IEntityTypeConfiguration<SystemMaintenance>
{
    public void Configure(EntityTypeBuilder<SystemMaintenance> b)
    {
        b.ToTable("system_maintenance", t => t.HasCheckConstraint("ck_system_maintenance_singleton", "\"Id\" = 1"));   // CR-026: exactly one row
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.ChangedBy).HasMaxLength(100);
        b.Property(x => x.Version).IsRowVersion();
        b.HasData(new { Id = SystemMaintenance.SingletonId, Enabled = false, UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });   // off by default
    }
}
