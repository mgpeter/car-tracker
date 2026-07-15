using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class DataAnomalyConfiguration : IEntityTypeConfiguration<DataAnomaly>
{
    public void Configure(EntityTypeBuilder<DataAnomaly> builder)
    {
        builder.ToTable("data_anomalies", t =>
        {
            t.HasCheckConstraint(
                "ck_anomalies_kind",
                "kind IN ('MileageNonMonotonic', 'FuelCostDiscrepancy', 'ImplausibleMpg')");
            t.HasCheckConstraint("ck_anomalies_severity", "severity IN ('Error', 'Warning', 'Info')");
            t.HasCheckConstraint("ck_anomalies_status", "status IN ('Open', 'Accepted', 'Corrected', 'Dismissed')");
            // Open means unresolved and resolved means resolved — the two directions must agree, or the row
            // is incoherent.
            t.HasCheckConstraint("ck_anomalies_resolved_iff_terminal", "(status = 'Open') = (resolved_at IS NULL)");
            t.HasCheckConstraint("ck_anomalies_resolution_note", "resolution_note <> ''");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).HasColumnType("varchar(24)").HasConversion<string>().IsRequired();
        builder.Property(a => a.Severity).HasColumnType("varchar(8)").HasConversion<string>().IsRequired();
        builder.Property(a => a.Status).HasColumnType("varchar(10)").HasConversion<string>().IsRequired();

        builder.Property(a => a.EntityType).HasColumnType("varchar(40)").IsRequired();
        builder.Property(a => a.EntityId).HasColumnType("integer");

        builder.Property(a => a.Message).HasColumnType("text").IsRequired();
        builder.Property(a => a.Detail).HasColumnType("jsonb");

        builder.Property(a => a.ResolvedAt).HasColumnType("timestamptz");
        builder.Property(a => a.ResolutionNote).HasColumnType("text");

        builder.HasOne<Vehicle>().WithMany().HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.VehicleId, a.Status, a.Severity })
            .HasDatabaseName("ix_anomalies_vehicle_status");
        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("ix_anomalies_entity");

        builder.ConfigureAudit("data_anomalies");
    }
}
