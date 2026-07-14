using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class MileageReadingConfiguration : IEntityTypeConfiguration<MileageReading>
{
    public void Configure(EntityTypeBuilder<MileageReading> builder)
    {
        builder.ToTable("mileage_readings", t =>
        {
            t.HasCheckConstraint("ck_mileage_readings_mileage", "mileage >= 0");
            t.HasCheckConstraint("ck_mileage_readings_origin", "origin IN ('manual', 'fuel', 'tyre', 'wash', 'service')");
            t.HasCheckConstraint("ck_mileage_readings_notes", "notes <> ''");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ReadingDate).HasColumnType("date").IsRequired();
        builder.Property(m => m.Mileage).HasColumnType("integer").IsRequired();
        builder.Property(m => m.Origin)
            .HasColumnType("varchar(10)")
            .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<MileageOrigin>(s, true))
            .IsRequired();
        builder.Property(m => m.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);

        // Two indexes because "latest reading" is ambiguous in this data: max-by-mileage and
        // most-recent-by-date disagree (the 83,000 mi row), and both access paths must stay cheap.
        builder.HasIndex(m => new { m.VehicleId, m.ReadingDate })
            .IsDescending(false, true)
            .HasDatabaseName("ix_mileage_readings_vehicle_date");
        builder.HasIndex(m => new { m.VehicleId, m.Mileage })
            .IsDescending(false, true)
            .HasDatabaseName("ix_mileage_readings_vehicle_mileage");

        builder.ConfigureAudit("mileage_readings");
    }
}
