using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class BudgetCategoryConfiguration : IEntityTypeConfiguration<BudgetCategory>
{
    public void Configure(EntityTypeBuilder<BudgetCategory> builder)
    {
        builder.ToTable("budget_categories", t =>
        {
            t.HasCheckConstraint("ck_budget_categories_annual_budget", "annual_budget >= 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Category).HasColumnType("varchar(24)").IsRequired();
        builder.Property(b => b.AnnualBudget).HasColumnType("numeric(10,2)").IsRequired();

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(b => b.VehicleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExpenseCategory>()
            .WithMany()
            .HasForeignKey(b => b.Category)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => new { b.VehicleId, b.Category })
            .IsUnique()
            .HasDatabaseName("ix_budget_vehicle_category");

        builder.ConfigureAudit("budget_categories");
    }
}
