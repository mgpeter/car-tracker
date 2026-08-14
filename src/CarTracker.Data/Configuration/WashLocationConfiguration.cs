using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class WashLocationConfiguration : IEntityTypeConfiguration<WashLocation>
{
    public void Configure(EntityTypeBuilder<WashLocation> builder)
    {
        builder.ToTable("wash_locations");

        // Per-owner list — see GarageConfiguration for why the key and the cascade are shaped this way.
        builder.HasKey(w => new { w.OwnerId, w.Name });

        builder.Property(w => w.OwnerId).HasColumnType("integer");
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.OwnerId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(w => w.Name).HasColumnType("varchar(80)");
        builder.Property(w => w.Notes).HasColumnType("text");

        builder.ToTable(t => t.HasCheckConstraint("ck_wash_locations_notes", "notes <> ''"));
    }
}
