using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WCS.Domain.Rcs;
using WCS.Domain.Routing;
using WCS.Domain.Tasks;

namespace WCS.Infrastructure.ModeControl;

internal sealed class ExecutionModeStateConfiguration : IEntityTypeConfiguration<ExecutionModeState>
{
    public void Configure(EntityTypeBuilder<ExecutionModeState> b)
    {
        b.ToTable("execution_mode_state", t => t.HasCheckConstraint("ck_execution_mode_state_single", "\"Id\" = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.RecoveryReason).HasMaxLength(500);
        b.Property(x => x.UpdatedBy).HasMaxLength(100).IsRequired();
    }
}

internal sealed class ExecutionModeTransitionConfiguration : IEntityTypeConfiguration<ExecutionModeTransition>
{
    public void Configure(EntityTypeBuilder<ExecutionModeTransition> b)
    {
        b.ToTable("execution_mode_transitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RequestId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.RequestId).IsUnique();
        b.Property(x => x.FromMode).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ToMode).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.At);
    }
}

internal sealed class RcsMessageConfiguration : IEntityTypeConfiguration<RcsMessage>
{
    public void Configure(EntityTypeBuilder<RcsMessage> b)
    {
        b.ToTable("rcs_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Seq).UseIdentityByDefaultColumn();
        b.HasIndex(x => x.Seq).IsUnique();
        b.Property(x => x.RequestKey).HasMaxLength(200);
        b.Property(x => x.RequestCode).HasMaxLength(100);
        b.Property(x => x.RcsTaskCode).HasMaxLength(100);
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.MessageType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Protocol).HasMaxLength(60).IsRequired();
        b.Property(x => x.Endpoint).HasMaxLength(300);
        b.Property(x => x.HttpMethod).HasMaxLength(10);
        b.Property(x => x.Result).HasMaxLength(40);
        b.Property(x => x.ErrorCode).HasMaxLength(60);
        b.Property(x => x.ErrorMessage).HasMaxLength(500);
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.HasIndex(x => new { x.TaskId, x.CreatedAt });
        b.HasIndex(x => x.RequestCode);
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class TaskExecutionModeConfiguration : IEntityTypeConfiguration<TransportTask>
{
    public void Configure(EntityTypeBuilder<TransportTask> b) => b.Property(x => x.ExecutionMode).HasConversion<string>().HasMaxLength(20);
}

internal sealed class OrderExecutionModeConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public void Configure(EntityTypeBuilder<TransportOrder> b) => b.Property(x => x.ExecutionMode).HasConversion<string>().HasMaxLength(20);
}
