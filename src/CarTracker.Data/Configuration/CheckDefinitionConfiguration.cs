using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class CheckDefinitionConfiguration : IEntityTypeConfiguration<CheckDefinition>
{
    public void Configure(EntityTypeBuilder<CheckDefinition> builder)
    {
        builder.ToTable("check_definitions", t =>
        {
            t.HasCheckConstraint("ck_check_definitions_interval", "interval_days > 0");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasColumnType("varchar(80)").IsRequired();
        builder.Property(d => d.CadenceLabel).HasColumnType("varchar(40)").IsRequired();
        builder.Property(d => d.IntervalDays).HasColumnType("integer").IsRequired();
        builder.Property(d => d.Guidance).HasColumnType("text");
        builder.Property(d => d.DisplayOrder).HasColumnType("integer").IsRequired();
        builder.Property(d => d.IsActive).HasColumnType("boolean").IsRequired().HasDefaultValue(true);

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(d => d.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.VehicleId, d.Name })
            .IsUnique()
            .HasDatabaseName("ix_check_definitions_vehicle_name");

        builder.ConfigureAudit("check_definitions");
    }
}
