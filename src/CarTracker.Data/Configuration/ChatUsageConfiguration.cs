using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class ChatUsageConfiguration : IEntityTypeConfiguration<ChatUsage>
{
    public void Configure(EntityTypeBuilder<ChatUsage> builder)
    {
        builder.ToTable("chat_usage", t => t.HasCheckConstraint(
            "ck_chat_usage_non_negative",
            "input_tokens >= 0 AND output_tokens >= 0 AND cache_write_tokens >= 0 AND cache_read_tokens >= 0 AND turns >= 0"));

        // (owner, day) — the same composite shape the per-owner reference lists took, and for the same reason:
        // the natural key is the whole identity of the row, so there is nothing for a surrogate id to add.
        builder.HasKey(u => new { u.OwnerId, u.Day });

        builder.Property(u => u.OwnerId).HasColumnType("integer");
        builder.Property(u => u.Day).HasColumnType("date");

        // Cascade: an erased account leaves no ledger behind. The account-deletion path deletes data first and
        // the identity last, and a counter is data.
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.OwnerId).OnDelete(DeleteBehavior.Cascade);

        // bigint, not integer: a busy day of cached prefixes runs to hundreds of thousands, and an int would
        // overflow somewhere north of two billion with nothing visible until it did.
        builder.Property(u => u.InputTokens).HasColumnType("bigint");
        builder.Property(u => u.OutputTokens).HasColumnType("bigint");
        builder.Property(u => u.CacheWriteTokens).HasColumnType("bigint");
        builder.Property(u => u.CacheReadTokens).HasColumnType("bigint");
        builder.Property(u => u.Turns).HasColumnType("integer");

        // Computed, never stored — the central constraint, applied to a counter like everything else.
        builder.Ignore(u => u.Total);

        // The global ceiling reads every account's row for one day, so the day leads its own index.
        builder.HasIndex(u => u.Day).HasDatabaseName("ix_chat_usage_day");
    }
}
