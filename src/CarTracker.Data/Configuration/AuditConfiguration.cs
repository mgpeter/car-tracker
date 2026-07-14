using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public static class AuditConfiguration
{
    /// <summary>
    /// The audit block every mutable entity carries (README §6): timestamptz created/updated stamps and a
    /// lowercase source string constrained to the four known surfaces.
    /// </summary>
    public static void ConfigureAudit<T>(this EntityTypeBuilder<T> builder, string tableName)
        where T : class, IAuditable
    {
        builder.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.Property(e => e.Source)
            .HasColumnType("varchar(8)")
            .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<EntrySource>(s, true))
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint(
            $"ck_{tableName}_source",
            "source IN ('web', 'mcp', 'import', 'seed')"));
    }
}
