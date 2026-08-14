using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarTracker.Data.Configuration;

public static class AuditConfiguration
{
    /// <summary>
    /// The audit block every mutable entity carries (README §6): timestamptz created/updated stamps and a
    /// lowercase source string constrained to the known surfaces.
    /// </summary>
    /// <remarks>
    /// The constraint is written from <see cref="EntrySource"/> rather than typed out, so adding a member is one
    /// edit and a migration rather than one edit, a migration, and a string nobody thought to update. `'chat'`
    /// (2026-08-14) joined without widening the column: the longest member is still six characters and the
    /// column is <c>varchar(8)</c>.
    /// </remarks>
    public static void ConfigureAudit<T>(this EntityTypeBuilder<T> builder, string tableName)
        where T : class, IAuditable
    {
        builder.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.Property(e => e.Source)
            .HasColumnType("varchar(8)")
            .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<EntrySource>(s, true))
            .IsRequired();

        builder.ToTable(t => t.HasCheckConstraint($"ck_{tableName}_source", SourceCheck));
    }

    /// <summary>
    /// <c>source IN ('web', 'mcp', …)</c> — every <see cref="EntrySource"/> member, lowercased, in declaration
    /// order, matching the conversion above.
    /// </summary>
    /// <remarks>
    /// Ordered by the enum's own declaration so the generated SQL is stable: an unordered set would make EF emit
    /// a spurious constraint drop/recreate on some builds and not others, and a dozen-table migration diff that
    /// appears at random is one nobody reads.
    /// </remarks>
    internal static string SourceCheck { get; } =
        "source IN (" +
        string.Join(", ", Enum.GetValues<EntrySource>().Select(s => $"'{s.ToString().ToLowerInvariant()}'")) +
        ")";
}
