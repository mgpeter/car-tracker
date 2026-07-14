using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class MaintenanceTaskConfiguration : IEntityTypeConfiguration<MaintenanceTask>
{
    public void Configure(EntityTypeBuilder<MaintenanceTask> builder)
    {
        builder.ToTable("maintenance_tasks", t =>
        {
            t.HasCheckConstraint("ck_tasks_kind", "kind IN ('DIY', 'Workshop')");
            t.HasCheckConstraint("ck_tasks_priority", "priority IN ('High', 'Medium', 'Low')");
            t.HasCheckConstraint("ck_tasks_status", "status IN ('Open', 'InProgress', 'Scheduled', 'Done')");
            t.HasCheckConstraint("ck_tasks_garage_workshop_only", "assigned_garage IS NULL OR kind = 'Workshop'");
            t.HasCheckConstraint("ck_tasks_completed_date_iff_done", "(status = 'Done') = (completed_date IS NOT NULL)");
            t.HasCheckConstraint("ck_tasks_notes", "notes <> ''");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Kind).HasColumnType("varchar(8)").HasConversion<string>().IsRequired();
        builder.Property(m => m.Priority).HasColumnType("varchar(6)").HasConversion<string>().IsRequired();
        builder.Property(m => m.Title).HasColumnType("varchar(160)").IsRequired();
        builder.Property(m => m.Description).HasColumnType("text");
        builder.Property(m => m.EstimatedCost).HasColumnType("numeric(10,2)");
        builder.Property(m => m.Status).HasColumnType("varchar(12)").HasConversion<string>().IsRequired();
        builder.Property(m => m.TargetDate).HasColumnType("date");
        builder.Property(m => m.TargetService).HasColumnType("varchar(80)");
        builder.Property(m => m.CompletedDate).HasColumnType("date");
        builder.Property(m => m.AssignedGarage).HasColumnType("varchar(80)");
        builder.Property(m => m.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Garage>().WithMany().HasForeignKey(m => m.AssignedGarage).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ServiceRecord>()
            .WithMany()
            .HasForeignKey(m => m.ServiceRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => new { m.VehicleId, m.Status, m.Priority })
            .HasDatabaseName("ix_tasks_vehicle_status");

        builder.ConfigureAudit("maintenance_tasks");
    }
}
