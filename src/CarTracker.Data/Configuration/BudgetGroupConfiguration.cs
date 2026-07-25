using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class BudgetGroupConfiguration : IEntityTypeConfiguration<BudgetGroup>
{
    public void Configure(EntityTypeBuilder<BudgetGroup> builder)
    {
        builder.ToTable("budget_groups", t =>
        {
            // Nullable now: a tracked group has no target. The old per-category constraint was a non-null >= 0.
            t.HasCheckConstraint("ck_budget_groups_annual_budget", "annual_budget IS NULL OR annual_budget >= 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasColumnType("varchar(40)").IsRequired();
        builder.Property(b => b.AnnualBudget).HasColumnType("numeric(10,2)");
        builder.Property(b => b.DisplayOrder).HasColumnType("integer").IsRequired();

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(b => b.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Categories)
            .WithOne()
            .HasForeignKey(c => c.BudgetGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // No two groups on one vehicle share a name — the endpoint reconciles the target set by name.
        builder.HasIndex(b => new { b.VehicleId, b.Name })
            .IsUnique()
            .HasDatabaseName("ix_budget_groups_vehicle_name");

        builder.ConfigureAudit("budget_groups");
    }
}
