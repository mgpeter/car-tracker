using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class FuelEntryConfiguration : IEntityTypeConfiguration<FuelEntry>
{
    public void Configure(EntityTypeBuilder<FuelEntry> builder)
    {
        builder.ToTable("fuel_entries", t =>
        {
            t.HasCheckConstraint("ck_fuel_entries_mileage", "mileage >= 0");
            t.HasCheckConstraint("ck_fuel_entries_litres", "litres > 0");
            t.HasCheckConstraint("ck_fuel_entries_price_per_litre", "price_per_litre > 0");
            t.HasCheckConstraint("ck_fuel_entries_total_cost", "total_cost > 0");
            t.HasCheckConstraint("ck_fuel_entries_fill_level", "fill_level IN ('Full', 'Half', 'Quarter')");
            t.HasCheckConstraint("ck_fuel_entries_notes", "notes <> ''");
        });

        builder.HasKey(f => f.Id);

        builder.Property(f => f.EntryDate).HasColumnType("date").IsRequired();
        builder.Property(f => f.Mileage).HasColumnType("integer").IsRequired();
        builder.Property(f => f.Litres).HasColumnType("numeric(6,3)").IsRequired();
        builder.Property(f => f.PricePerLitre).HasColumnType("numeric(6,3)").IsRequired();
        builder.Property(f => f.TotalCost).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(f => f.Station).HasColumnType("varchar(80)");
        builder.Property(f => f.FillLevel).HasColumnType("varchar(8)").HasConversion<string>().IsRequired();
        builder.Property(f => f.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(f => f.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.VehicleId, f.EntryDate })
            .IsDescending(false, true)
            .HasDatabaseName("ix_fuel_entries_vehicle_date");
        builder.HasIndex(f => new { f.VehicleId, f.Mileage })
            .IsDescending(false, true)
            .HasDatabaseName("ix_fuel_entries_vehicle_mileage");

        builder.ConfigureAudit("fuel_entries");
    }
}
