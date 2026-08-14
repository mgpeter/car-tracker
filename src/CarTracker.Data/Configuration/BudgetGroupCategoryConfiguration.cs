using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class BudgetGroupCategoryConfiguration : IEntityTypeConfiguration<BudgetGroupCategory>
{
    public void Configure(EntityTypeBuilder<BudgetGroupCategory> builder)
    {
        builder.ToTable("budget_group_categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Category).HasColumnType("varchar(24)").IsRequired();

        // The group FK/cascade is configured from the BudgetGroup side (HasMany(b => b.Categories)).
        //
        // No FK to expense_categories. Its Cascade was the sharpest of the six the per-owner reference lists
        // dropped: it silently deleted a group membership when a category went, on the one path the editor —
        // which re-homes memberships explicitly — does not take.

        // The invariant: a category is in at most one group per vehicle. Enforced at the database, so the
        // endpoint's own check is belt-and-braces rather than the sole guard.
        builder.HasIndex(c => new { c.VehicleId, c.Category })
            .IsUnique()
            .HasDatabaseName("ix_budget_group_category_vehicle_category");
    }
}
