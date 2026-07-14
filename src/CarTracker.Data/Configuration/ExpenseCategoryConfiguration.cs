using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories");
        builder.HasKey(c => c.Name);

        builder.Property(c => c.Name).HasColumnType("varchar(24)");
        builder.Property(c => c.DisplayOrder).HasColumnType("integer").IsRequired();
        builder.Property(c => c.IsSystem).HasColumnType("boolean").IsRequired().HasDefaultValue(false);

        builder.HasData(SystemCategories);
    }

    /// <summary>
    /// The 13 categories from README §2 — the only seed data in the system (DEC-007: vehicles and anything
    /// scoped to them are created by the importer or the add-car flow, never seeded).
    /// </summary>
    /// <remarks>
    /// All are <c>IsSystem</c>: the domain reasons about these by name — notably Fuel, which README §3.2's
    /// auto-mirroring depends on — so they may be renamed for display but never deleted.
    /// </remarks>
    public static readonly ExpenseCategory[] SystemCategories =
    [
        new() { Name = "Fuel", DisplayOrder = 1, IsSystem = true },
        new() { Name = "Service", DisplayOrder = 2, IsSystem = true },
        new() { Name = "Repair", DisplayOrder = 3, IsSystem = true },
        new() { Name = "Parts", DisplayOrder = 4, IsSystem = true },
        new() { Name = "Insurance", DisplayOrder = 5, IsSystem = true },
        new() { Name = "Tax", DisplayOrder = 6, IsSystem = true },
        new() { Name = "MOT", DisplayOrder = 7, IsSystem = true },
        new() { Name = "Wash", DisplayOrder = 8, IsSystem = true },
        new() { Name = "Parking", DisplayOrder = 9, IsSystem = true },
        new() { Name = "Tools/Equipment", DisplayOrder = 10, IsSystem = true },
        new() { Name = "Breakdown", DisplayOrder = 11, IsSystem = true },
        new() { Name = "Purchase", DisplayOrder = 12, IsSystem = true },
        new() { Name = "Misc", DisplayOrder = 13, IsSystem = true },
    ];
}
