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
    }
}
