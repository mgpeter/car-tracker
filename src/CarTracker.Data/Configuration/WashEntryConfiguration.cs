using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class WashEntryConfiguration : IEntityTypeConfiguration<WashEntry>
{
    public void Configure(EntityTypeBuilder<WashEntry> builder)
    {
        builder.ToTable("wash_entries", t =>
        {
            t.HasCheckConstraint("ck_wash_entries_mileage", "mileage >= 0");
            t.HasCheckConstraint("ck_wash_entries_notes", "notes <> ''");
        });

        builder.HasKey(w => w.Id);

        builder.Property(w => w.WashDate).HasColumnType("date").IsRequired();
        builder.Property(w => w.Location).HasColumnType("varchar(80)");
        builder.Property(w => w.WashType).HasColumnType("varchar(40)");
        builder.Property(w => w.Cost).HasColumnType("numeric(10,2)");
        builder.Property(w => w.Mileage).HasColumnType("integer");
        builder.Property(w => w.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(w => w.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WashLocation>().WithMany().HasForeignKey(w => w.Location).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(w => new { w.VehicleId, w.WashDate })
            .IsDescending(false, true)
            .HasDatabaseName("ix_wash_entries_vehicle_date");

        builder.ConfigureAudit("wash_entries");
    }
}
