using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class TyreReadingConfiguration : IEntityTypeConfiguration<TyreReading>
{
    public void Configure(EntityTypeBuilder<TyreReading> builder)
    {
        builder.ToTable("tyre_readings", t =>
        {
            t.HasCheckConstraint("ck_tyre_readings_mileage", "mileage >= 0");
            t.HasCheckConstraint("ck_tyre_readings_notes", "notes <> ''");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReadingDate).HasColumnType("date").IsRequired();
        builder.Property(r => r.Mileage).HasColumnType("integer");

        builder.Property(r => r.PsiFrontLeft).HasColumnType("numeric(4,1)");
        builder.Property(r => r.PsiFrontRight).HasColumnType("numeric(4,1)");
        builder.Property(r => r.PsiRearLeft).HasColumnType("numeric(4,1)");
        builder.Property(r => r.PsiRearRight).HasColumnType("numeric(4,1)");
        builder.Property(r => r.PsiSpare).HasColumnType("numeric(4,1)");

        builder.Property(r => r.TreadFrontLeft).HasColumnType("numeric(3,1)");
        builder.Property(r => r.TreadFrontRight).HasColumnType("numeric(3,1)");
        builder.Property(r => r.TreadRearLeft).HasColumnType("numeric(3,1)");
        builder.Property(r => r.TreadRearRight).HasColumnType("numeric(3,1)");

        builder.Property(r => r.Location).HasColumnType("varchar(80)");
        builder.Property(r => r.Tool).HasColumnType("varchar(60)");
        builder.Property(r => r.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.VehicleId, r.ReadingDate })
            .IsDescending(false, true)
            .HasDatabaseName("ix_tyre_readings_vehicle_date");

        builder.ConfigureAudit("tyre_readings");
    }
}
