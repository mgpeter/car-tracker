using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class IssueWatchCheckConfiguration : IEntityTypeConfiguration<IssueWatchCheck>
{
    public void Configure(EntityTypeBuilder<IssueWatchCheck> builder)
    {
        builder.ToTable("issue_watch_checks");

        // The pair is the identity — no surrogate id. Linking the same check twice is then impossible rather
        // than merely discouraged, and the write path can add links without first reading what is there.
        builder.HasKey(w => new { w.IssueId, w.CheckDefinitionId });

        // Cascade from both ends. The row asserts a relationship between two things; if either goes, the
        // assertion is void. Cascade removes the link and leaves the survivor alone — deleting a check
        // definition must not delete the issue that watched it, and vice versa.
        builder.HasOne<Issue>()
            .WithMany()
            .HasForeignKey(w => w.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CheckDefinition>()
            .WithMany()
            .HasForeignKey(w => w.CheckDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // The composite PK already indexes (issue_id, check_definition_id) left-to-right, so "which checks does
        // this issue watch" is covered. This one covers the other direction — "which issues watch this check" —
        // which is what a check-definition delete and the per-check reverse lookup need.
        builder.HasIndex(w => w.CheckDefinitionId)
            .HasDatabaseName("ix_issue_watch_checks_check_definition_id");

        // No same-vehicle CHECK here: it would have to reach across two tables, which Postgres cannot express in
        // a table constraint without a trigger. The write path enforces it (see IssueService.SetWatchAsync) —
        // the honest place, since the endpoint already knows the vehicle.
    }
}
