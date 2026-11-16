using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WCS.Infrastructure.Logging;

/// <summary>Row of the unified read-only view <c>v_log_entries</c> (system_log + plc_comm_log + audit_log). Never written through EF.</summary>
public sealed class LogRow
{
    public string EntryId { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
    public string Category { get; set; } = default!;
    public string Level { get; set; } = default!;
    public int LevelRank { get; set; }
    public string Source { get; set; } = default!;
    public string? Device { get; set; }
    public string? Event { get; set; }
    public string Message { get; set; } = default!;
}

internal sealed class LogRowConfiguration : IEntityTypeConfiguration<LogRow>
{
    public void Configure(EntityTypeBuilder<LogRow> b)
    {
        b.HasNoKey();
        b.ToView("v_log_entries");
    }
}

internal sealed class SystemLogEntryConfiguration : IEntityTypeConfiguration<WCS.Domain.Logging.SystemLogEntry>
{
    public void Configure(EntityTypeBuilder<WCS.Domain.Logging.SystemLogEntry> b)
    {
        b.ToTable("system_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Category).HasMaxLength(20).IsRequired();
        b.Property(x => x.Level).HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasMaxLength(200).IsRequired();
        b.Property(x => x.Device).HasMaxLength(60);
        b.Property(x => x.EventCode).HasMaxLength(60);
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Exception).HasColumnType("text");
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.DetailsJson).HasColumnType("text");
        b.Property(x => x.Host).HasMaxLength(100);
        b.Property(x => x.ExternalId).HasMaxLength(64);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.Category, x.OccurredAt });
        b.HasIndex(x => new { x.Level, x.OccurredAt });
        b.HasIndex(x => new { x.Device, x.OccurredAt });
        b.HasIndex(x => x.EventCode);
        b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
    }
}
