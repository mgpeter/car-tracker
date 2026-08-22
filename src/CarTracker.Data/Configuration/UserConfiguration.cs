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
        // Not nullable: "we have not been told" and "the tenant says no" are the same thing to every caller,
        // and both mean the free tier. A third state would be a distinction nothing acts on.
        builder.Property(u => u.EmailVerified).HasColumnType("boolean").IsRequired().HasDefaultValue(false);
        builder.Property(u => u.CreatedAt).HasColumnType("timestamptz").IsRequired();

        // Nullable, and the null is the design: it means "no administrator has decided anything", which is
        // different from Free and must stay different. No check constraint enumerating the two plans - the
        // neighbouring ck_*_source constraints exist because those columns are written by several paths
        // including an importer, and this one is written by one endpoint from a parsed enum. No index either:
        // it is read inside a single-row primary-key lookup, and scanned over at most 500 rows on the admin
        // list.
        builder.Property(u => u.PlanOverride).HasColumnType("integer");

        // timestamptz rather than date. "Did they come back" wants a date, but "are they in the app right now"
        // wants a time, and that is the question asked while a tester is on the phone. No index: sorting a few
        // hundred rows in memory does not need one, and indexing a column written on the request path costs
        // every write for a read that happens when an operator opens a screen.
        builder.Property(u => u.LastSeenAt).HasColumnType("timestamptz");

        // The sub claim is the lookup key on every authenticated request; it must be unique.
        builder.HasIndex(u => u.ExternalId).IsUnique().HasDatabaseName("ix_users_external_id");
    }
}
