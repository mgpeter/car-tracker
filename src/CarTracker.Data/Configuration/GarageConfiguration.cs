using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class GarageConfiguration : IEntityTypeConfiguration<Garage>
{
    public void Configure(EntityTypeBuilder<Garage> builder)
    {
        builder.ToTable("garages");
        builder.HasKey(g => g.Name);

        builder.Property(g => g.Name).HasColumnType("varchar(80)");
        builder.Property(g => g.Contact).HasColumnType("varchar(120)");
        builder.Property(g => g.Address).HasColumnType("text");
        builder.Property(g => g.Notes).HasColumnType("text");

        builder.ToTable(t => t.HasCheckConstraint("ck_garages_notes", "notes <> ''"));
    }
}
