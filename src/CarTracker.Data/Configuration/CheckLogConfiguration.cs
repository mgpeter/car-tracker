using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class CheckLogConfiguration : IEntityTypeConfiguration<CheckLog>
{
    public void Configure(EntityTypeBuilder<CheckLog> builder)
    {
        builder.ToTable("check_logs", t =>
        {
            t.HasCheckConstraint("ck_check_logs_result", "result IN ('OK', 'Attention', 'Failed')");
            t.HasCheckConstraint("ck_check_logs_notes", "notes <> ''");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.PerformedOn).HasColumnType("date").IsRequired();
        builder.Property(l => l.Result).HasColumnType("varchar(12)").HasConversion<string?>();
        builder.Property(l => l.Notes).HasColumnType("text");

        builder.HasOne<CheckDefinition>()
            .WithMany()
            .HasForeignKey(l => l.CheckDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.CheckDefinitionId, l.PerformedOn })
            .IsDescending(false, true)
            .HasDatabaseName("ix_check_logs_definition_date");

        builder.ConfigureAudit("check_logs");
    }
}
