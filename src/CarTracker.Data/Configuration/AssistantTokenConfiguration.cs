using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public sealed class AssistantTokenConfiguration : IEntityTypeConfiguration<AssistantToken>
{
    public void Configure(EntityTypeBuilder<AssistantToken> builder)
    {
        builder.ToTable("assistant_tokens", t =>
            t.HasCheckConstraint("ck_assistant_tokens_scope", "scope IN ('ReadOnly', 'ReadWrite')"));

        builder.HasKey(t => t.Id);

        // The owning user (multi-user). Restrict: a user with live tokens cannot be silently deleted.
        builder.Property(t => t.OwnerId).HasColumnType("integer");
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.Name).HasColumnType("varchar(80)").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnType("varchar(64)").IsRequired();
        builder.Property(t => t.Scope).HasColumnType("varchar(10)").HasConversion<string>().IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz");
        builder.Property(t => t.LastUsedAt).HasColumnType("timestamptz");
        builder.Property(t => t.RevokedAt).HasColumnType("timestamptz");
        builder.Property(t => t.ReadCount).HasColumnType("integer");
        builder.Property(t => t.WriteCount).HasColumnType("integer");

        // The hash is the lookup key on every authenticated request, and it must be unique.
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_assistant_tokens_hash");
    }
}

public sealed class AssistantWriteAuditConfiguration : IEntityTypeConfiguration<AssistantWriteAudit>
{
    public void Configure(EntityTypeBuilder<AssistantWriteAudit> builder)
    {
        builder.ToTable("assistant_write_audits");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Tool).HasColumnType("varchar(60)").IsRequired();
        builder.Property(a => a.VehicleId).HasColumnType("integer");
        builder.Property(a => a.Summary).HasColumnType("text").IsRequired();
        builder.Property(a => a.TimestampUtc).HasColumnType("timestamptz");

        builder.HasOne<AssistantToken>().WithMany().HasForeignKey(a => a.TokenId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.TimestampUtc).HasDatabaseName("ix_assistant_write_audits_time");
    }
}
