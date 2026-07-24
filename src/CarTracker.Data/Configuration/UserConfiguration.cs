using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        // 128 covers Auth0 subjects comfortably ("auth0|<hex>", "google-oauth2|<digits>", …); 320 is the RFC
        // 5321 maximum for an email address.
        builder.Property(u => u.ExternalId).HasColumnType("varchar(128)").IsRequired();
        builder.Property(u => u.Email).HasColumnType("varchar(320)").IsRequired();
        builder.Property(u => u.DisplayName).HasColumnType("varchar(120)");
        builder.Property(u => u.CreatedAt).HasColumnType("timestamptz").IsRequired();

        // The sub claim is the lookup key on every authenticated request; it must be unique.
        builder.HasIndex(u => u.ExternalId).IsUnique().HasDatabaseName("ix_users_external_id");
    }
}
