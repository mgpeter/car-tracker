using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class EquipmentItemConfiguration : IEntityTypeConfiguration<EquipmentItem>
{
    public void Configure(EntityTypeBuilder<EquipmentItem> builder)
    {
        builder.ToTable("equipment_items", t =>
        {
            t.HasCheckConstraint("ck_equipment_items_status", "status IN ('Owned', 'OnOrder', 'ToOrder')");
            t.HasCheckConstraint("ck_equipment_items_notes", "notes <> ''");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasColumnType("varchar(120)").IsRequired();
        builder.Property(e => e.Category).HasColumnType("varchar(60)");
        builder.Property(e => e.PurchasedDate).HasColumnType("date");
        builder.Property(e => e.SourceVendor).HasColumnType("varchar(120)");
        builder.Property(e => e.Cost).HasColumnType("numeric(10,2)");
        builder.Property(e => e.StoredAt).HasColumnType("varchar(120)");
        builder.Property(e => e.Status).HasColumnType("varchar(10)").HasConversion<string>().IsRequired();
        builder.Property(e => e.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.VehicleId, e.Status })
            .HasDatabaseName("ix_equipment_vehicle_status");

        builder.ConfigureAudit("equipment_items");
    }
}
