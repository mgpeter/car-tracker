using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("issues", t =>
        {
            t.HasCheckConstraint("ck_issues_severity", "severity IN ('Critical', 'Medium', 'Low')");
            t.HasCheckConstraint("ck_issues_status", "status IN ('Monitoring', 'Resolved')");
            t.HasCheckConstraint("ck_issues_resolved_date_iff_resolved", "(status = 'Resolved') = (resolved_date IS NOT NULL)");
            t.HasCheckConstraint("ck_issues_notes", "notes <> ''");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Title).HasColumnType("varchar(160)").IsRequired();
        builder.Property(i => i.Severity).HasColumnType("varchar(8)").HasConversion<string>().IsRequired();
        builder.Property(i => i.FirstNoted).HasColumnType("date").IsRequired();
        builder.Property(i => i.LastChecked).HasColumnType("date");
        builder.Property(i => i.CurrentObservation).HasColumnType("text");
        builder.Property(i => i.ActionIfWorsens).HasColumnType("text");
        builder.Property(i => i.EstimatedFixCost).HasColumnType("numeric(10,2)");
        builder.Property(i => i.Status).HasColumnType("varchar(10)").HasConversion<string>().IsRequired();
        builder.Property(i => i.ResolvedDate).HasColumnType("date");
        builder.Property(i => i.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(i => i.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.VehicleId, i.Status, i.Severity })
            .HasDatabaseName("ix_issues_vehicle_status_severity");

        builder.ConfigureAudit("issues");
    }
}
