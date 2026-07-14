using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class ServiceRecordConfiguration : IEntityTypeConfiguration<ServiceRecord>
{
    public void Configure(EntityTypeBuilder<ServiceRecord> builder)
    {
        builder.ToTable("service_records", t =>
        {
            t.HasCheckConstraint("ck_service_records_mileage", "mileage >= 0");
            t.HasCheckConstraint("ck_service_records_next_due_mileage", "next_due_mileage >= 0");
            t.HasCheckConstraint("ck_service_records_notes", "notes <> ''");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ServiceDate).HasColumnType("date").IsRequired();
        builder.Property(s => s.Mileage).HasColumnType("integer").IsRequired();
        builder.Property(s => s.Type).HasColumnType("varchar(40)").IsRequired();
        builder.Property(s => s.Garage).HasColumnType("varchar(80)");
        builder.Property(s => s.WorkDone).HasColumnType("text");
        builder.Property(s => s.PartsReplaced).HasColumnType("text");
        builder.Property(s => s.Cost).HasColumnType("numeric(10,2)");
        builder.Property(s => s.NextDueDate).HasColumnType("date");
        builder.Property(s => s.NextDueMileage).HasColumnType("integer");
        builder.Property(s => s.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(s => s.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Garage>().WithMany().HasForeignKey(s => s.Garage).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.VehicleId, s.ServiceDate })
            .IsDescending(false, true)
            .HasDatabaseName("ix_service_records_vehicle_date");

        // Serves the derived-MOT-expiry lookup: MAX(next_due_date) WHERE type = 'MOT'.
        builder.HasIndex(s => new { s.VehicleId, s.Type, s.NextDueDate })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_service_records_type_next_due");

        builder.ConfigureAudit("service_records");
    }
}
