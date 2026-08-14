using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class GarageConfiguration : IEntityTypeConfiguration<Garage>
{
    public void Configure(EntityTypeBuilder<Garage> builder)
    {
        builder.ToTable("garages");

        // Per-owner list: the name is unique within an account, not across the deployment.
        builder.HasKey(g => new { g.OwnerId, g.Name });

        // Cascade, unlike Vehicle.OwnerId and AssistantToken.OwnerId, which are Restrict. A vehicle is data
        // whose deletion should be an explicit act; a list entry cannot outlive its list.
        builder.Property(g => g.OwnerId).HasColumnType("integer");
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(g => g.Name).HasColumnType("varchar(80)");
        builder.Property(g => g.Contact).HasColumnType("varchar(120)");
        builder.Property(g => g.Address).HasColumnType("text");
        builder.Property(g => g.Notes).HasColumnType("text");

        builder.ToTable(t => t.HasCheckConstraint("ck_garages_notes", "notes <> ''"));
    }
}
