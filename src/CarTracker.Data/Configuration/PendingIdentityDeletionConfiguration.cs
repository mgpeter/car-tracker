using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class PendingIdentityDeletionConfiguration : IEntityTypeConfiguration<PendingIdentityDeletion>
{
    public void Configure(EntityTypeBuilder<PendingIdentityDeletion> builder)
    {
        builder.ToTable("pending_identity_deletions", t =>
            // An empty string would be a failure that says nothing — either name the error or leave it null.
            t.HasCheckConstraint("ck_pending_identity_deletions_last_error", "last_error <> ''"));

        builder.HasKey(p => p.Id);

        // varchar(128) to match User.ExternalId, because that is the value this carries after the user is gone.
        builder.Property(p => p.ExternalId).HasColumnType("varchar(128)").IsRequired();
        builder.Property(p => p.RequestedAt).HasColumnType("timestamptz");
        builder.Property(p => p.Attempts).HasColumnType("integer");
        builder.Property(p => p.LastError).HasColumnType("text");

        // Unique so a second deletion of the same identity — a retry, a re-provisioned account deleted again —
        // cannot queue a second attempt for one subject.
        builder.HasIndex(p => p.ExternalId).IsUnique()
            .HasDatabaseName("ix_pending_identity_deletions_external_id");

        // No foreign key to users, deliberately: the row is written in the transaction that deletes the user it
        // names, so a constraint would refuse the one insert it exists for.
    }
}
