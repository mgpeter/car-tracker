using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class ExpenseEntryConfiguration : IEntityTypeConfiguration<ExpenseEntry>
{
    public void Configure(EntityTypeBuilder<ExpenseEntry> builder)
    {
        builder.ToTable("expense_entries", t =>
        {
            t.HasCheckConstraint("ck_expense_entries_mileage", "mileage >= 0");
            t.HasCheckConstraint("ck_expense_entries_notes", "notes <> ''");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntryDate).HasColumnType("date").IsRequired();
        builder.Property(e => e.Category).HasColumnType("varchar(24)").IsRequired();
        builder.Property(e => e.SubCategory).HasColumnType("varchar(60)");
        builder.Property(e => e.Vendor).HasColumnType("varchar(120)");
        builder.Property(e => e.Amount).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(e => e.Mileage).HasColumnType("integer");
        builder.Property(e => e.PaymentMethod).HasColumnType("varchar(30)");
        builder.Property(e => e.Notes).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.Cascade);

        // RESTRICT: a category still referenced by expenses cannot be deleted.
        builder.HasOne<ExpenseCategory>()
            .WithMany()
            .HasForeignKey(e => e.Category)
            .OnDelete(DeleteBehavior.Restrict);

        // The mirror link: at most one expense per fill, and deleting the fill removes its mirror.
        builder.Property(e => e.FuelEntryId).HasColumnType("integer");
        builder.HasIndex(e => e.FuelEntryId).IsUnique();
        builder.HasOne<FuelEntry>()
            .WithMany()
            .HasForeignKey(e => e.FuelEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same link for a service record's cost. Cascade for the same reason: the mirror is a shadow and
        // cannot outlive its source.
        builder.Property(e => e.ServiceRecordId).HasColumnType("integer");
        builder.HasIndex(e => e.ServiceRecordId).IsUnique();
        builder.HasOne<ServiceRecord>()
            .WithMany()
            .HasForeignKey(e => e.ServiceRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // And for an equipment purchase's cost — same shadow/cascade contract.
        builder.Property(e => e.EquipmentItemId).HasColumnType("integer");
        builder.HasIndex(e => e.EquipmentItemId).IsUnique();
        builder.HasOne<EquipmentItem>()
            .WithMany()
            .HasForeignKey(e => e.EquipmentItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // And for a wash's cost — same shadow/cascade contract.
        builder.Property(e => e.WashEntryId).HasColumnType("integer");
        builder.HasIndex(e => e.WashEntryId).IsUnique();
        builder.HasOne<WashEntry>()
            .WithMany()
            .HasForeignKey(e => e.WashEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        // The vehicle-purchase mirror. No FK — the source is the vehicle this row already points at — so the
        // "one mirror per source" guarantee the other three get from a unique FK index comes from a partial
        // unique index instead: one purchase row per vehicle, and the filter means every ordinary expense row
        // (is_vehicle_purchase = false) sits outside the index entirely.
        builder.Property(e => e.IsVehiclePurchase).HasColumnType("boolean").IsRequired().HasDefaultValue(false);
        builder.HasIndex(e => e.VehicleId)
            .IsUnique()
            .HasFilter("is_vehicle_purchase")
            .HasDatabaseName("ux_expense_entries_vehicle_purchase");

        builder.HasIndex(e => new { e.VehicleId, e.EntryDate })
            .IsDescending(false, true)
            .HasDatabaseName("ix_expense_entries_vehicle_date");
        builder.HasIndex(e => new { e.VehicleId, e.Category })
            .HasDatabaseName("ix_expense_entries_vehicle_category");

        builder.ConfigureAudit("expense_entries");
    }
}
