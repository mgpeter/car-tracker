using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class VehicleLookupUsageConfiguration : IEntityTypeConfiguration<VehicleLookupUsage>
{
    public void Configure(EntityTypeBuilder<VehicleLookupUsage> builder)
    {
        builder.ToTable("vehicle_lookup_usage", t => t.HasCheckConstraint(
            "ck_vehicle_lookup_usage_non_negative",
            "lookups >= 0"));

        // (owner, day) - the shape ChatUsage and the per-owner reference lists both take. The natural key is
        // the whole identity of the row, so there is nothing for a surrogate id to add.
        builder.HasKey(u => new { u.OwnerId, u.Day });

        builder.Property(u => u.OwnerId).HasColumnType("integer");
        builder.Property(u => u.Day).HasColumnType("date");

        // Cascade: an erased account leaves no ledger behind. The account-deletion path deletes data first and
        // the identity last, and a counter is data.
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.OwnerId).OnDelete(DeleteBehavior.Cascade);

        // integer, not bigint: this counts calls to a rate-limited third party, in tens per day at the paid
        // tier. ChatUsage needs bigint because it counts tokens, which run to hundreds of thousands.
        builder.Property(u => u.Lookups).HasColumnType("integer");
    }
}
