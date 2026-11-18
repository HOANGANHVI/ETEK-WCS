using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WCS.Domain.Routing;

namespace WCS.Infrastructure.Persistence.Configurations;

// CR-010 (additive tables only; no existing table is changed).

internal sealed class BusinessProcessConfiguration : IEntityTypeConfiguration<BusinessProcess>
{
    public void Configure(EntityTypeBuilder<BusinessProcess> b)
    {
        b.ToTable("business_processes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.TriggerLocationCodesCsv).HasMaxLength(500).IsRequired();
        b.Property(x => x.ReviewReason).HasMaxLength(500);
        b.Property(x => x.SourceReference).HasMaxLength(300);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.TriggerLocationCodes);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class BusinessProcessVersionConfiguration : IEntityTypeConfiguration<BusinessProcessVersion>
{
    public void Configure(EntityTypeBuilder<BusinessProcessVersion> b)
    {
        b.ToTable("business_process_versions", t => t.HasCheckConstraint("ck_process_versions_number", "\"Number\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.PublishedBy).HasMaxLength(100);
        b.Property(x => x.FirstStepKey).HasMaxLength(60).IsRequired();
        b.Property(x => x.ValidationJson).HasColumnType("text");
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsEditable);
        b.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.VersionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProcessId, x.Number }).IsUnique();
        // exactly one Published version per process (the one new Transport Tasks use)
        b.HasIndex(x => x.ProcessId).IsUnique().HasDatabaseName("ux_process_versions_one_published").HasFilter("\"State\" = 'Published'");
    }
}

internal sealed class RouteStepConfiguration : IEntityTypeConfiguration<RouteStep>
{
    public void Configure(EntityTypeBuilder<RouteStep> b)
    {
        b.ToTable("route_steps");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StepKey).HasMaxLength(60).IsRequired();
        b.Property(x => x.NextStepKey).HasMaxLength(60);
        b.Property(x => x.AlternativeNextStepKey).HasMaxLength(60);
        b.Property(x => x.ConditionsJson).HasColumnType("text");
        b.Property(x => x.WaitingPositionCode).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Ignore(x => x.Conditions);
        b.Ignore(x => x.IsMovement);
        b.HasMany(x => x.Candidates).WithOne().HasForeignKey(c => c.StepId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.VersionId, x.StepKey }).IsUnique();
    }
}

internal sealed class RouteCandidateConfiguration : IEntityTypeConfiguration<RouteCandidate>
{
    public void Configure(EntityTypeBuilder<RouteCandidate> b)
    {
        b.ToTable("route_candidates", t => t.HasCheckConstraint("ck_route_candidates_priority", "\"Priority\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.LocationCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.ConditionsJson).HasColumnType("text");
        b.Property(x => x.Note).HasMaxLength(500);
        b.Ignore(x => x.Conditions);
        b.HasIndex(x => new { x.StepId, x.Role, x.Priority, x.Order });
    }
}

internal sealed class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public void Configure(EntityTypeBuilder<TransportOrder> b)
    {
        b.ToTable("transport_orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RequestKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProcessCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.SnapshotJson).HasColumnType("text").IsRequired();
        b.Property(x => x.RequestLocationCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.RequestedBy).HasMaxLength(100);
        b.Property(x => x.StateReason).HasMaxLength(1000);
        b.Property(x => x.CurrentStepKey).HasMaxLength(60);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsTerminal);
        b.Ignore(x => x.Snapshot);
        b.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessProcessVersion>().WithMany().HasForeignKey(x => x.ProcessVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Legs).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.RequestKey).IsUnique();   // request idempotency
        b.HasIndex(x => x.State);
    }
}

internal sealed class TransportLegConfiguration : IEntityTypeConfiguration<TransportLeg>
{
    public void Configure(EntityTypeBuilder<TransportLeg> b)
    {
        b.ToTable("transport_legs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StepKey).HasMaxLength(60).IsRequired();
        b.Property(x => x.SourceLocationCode).HasMaxLength(50);
        b.Property(x => x.DestinationLocationCode).HasMaxLength(50);
        b.Property(x => x.AgvRequestCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.AgvTaskCode).HasMaxLength(100);
        b.Property(x => x.StateReason).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsTerminal);
        b.Ignore(x => x.OccupiesAgv);
        b.Ignore(x => x.RackOnAgv);
        b.HasOne<WCS.Domain.Agvs.Agv>().WithMany().HasForeignKey(x => x.AgvId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OrderId, x.Sequence }).IsUnique();
        b.HasIndex(x => x.AgvRequestCode).IsUnique();
        b.HasIndex(x => new { x.State, x.CreatedAt });
        // PER_AGV for legs: at most ONE leg that holds an AGV (InProgress, or Waiting while the AMR holds the rack). The database enforces it.
        b.HasIndex(x => x.AgvId).IsUnique().HasDatabaseName("ux_transport_legs_one_active_per_agv")
            .HasFilter("\"AgvId\" IS NOT NULL AND \"State\" IN ('InProgress', 'Waiting')");
    }
}

internal sealed class StationTransactionConfiguration : IEntityTypeConfiguration<StationTransaction>
{
    public void Configure(EntityTypeBuilder<StationTransaction> b)
    {
        b.ToTable("station_transactions", t => t.HasCheckConstraint("ck_station_transactions_step", "\"HandshakeStep\" BETWEEN 0 AND 9"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PortId).HasMaxLength(20).IsRequired();
        b.Property(x => x.PlcId).HasMaxLength(20).IsRequired();
        b.Property(x => x.LocationCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.RequestId).HasMaxLength(StationTransaction.MaxRequestIdLength);
        b.Property(x => x.SuspendedReason).HasMaxLength(500);
        b.Property(x => x.ReconciliationReason).HasMaxLength(500);
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.IsFinal);
        b.HasOne<WCS.Domain.Plc.PlcStation>().WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WCS.Domain.Plc.PlcConnection>().WithMany().HasForeignKey(x => x.PlcId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.LegId);
        b.HasIndex(x => new { x.PlcId, x.State });
        // ONE active transaction per port, enforced by the database (CR-010 5.2). Other ports - same PLC or not - are never blocked.
        b.HasIndex(x => x.PortId).IsUnique().HasDatabaseName("ux_station_transactions_one_active_per_port")
            .HasFilter("\"State\" NOT IN ('Completed', 'Failed')");
        // a PLC Request_ID is processed once
        b.HasIndex(x => new { x.PlcId, x.RequestId }).IsUnique().HasDatabaseName("ux_station_transactions_request_id").HasFilter("\"RequestId\" IS NOT NULL");
    }
}

internal sealed class RoutingEventConfiguration : IEntityTypeConfiguration<RoutingEvent>
{
    public void Configure(EntityTypeBuilder<RoutingEvent> b)
    {
        b.ToTable("routing_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(60).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.Property(x => x.Source).HasMaxLength(60).IsRequired();
        b.HasIndex(x => new { x.OrderId, x.OccurredAt });
        b.HasOne<TransportOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
