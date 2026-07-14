using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Data.Tests;

/// <summary>
/// A stand-in auditable entity, existing only to exercise <see cref="AuditStampingInterceptor"/>.
/// </summary>
/// <remarks>
/// Lives in the test project so the interceptor can be tested before any real entity exists, and so the
/// production model never carries a type that is only there for tests. Once real entities land, the
/// interceptor is covered through them too — this probe stays as the isolated case.
/// </remarks>
internal sealed class AuditProbe : IAuditable
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public EntrySource Source { get; set; }
}

/// <summary>
/// A standalone context wiring up the same interceptor as <see cref="CarTrackerDbContext"/>.
/// </summary>
/// <remarks>
/// Deliberately does not derive from <see cref="CarTrackerDbContext"/>: EF requires the options generic
/// argument to match the concrete context type, so a derived test context would need its own
/// DbContextOptions anyway. Sharing the interceptor is the part that matters.
/// </remarks>
internal sealed class AuditProbeContext(DbContextOptions<AuditProbeContext> options, TimeProvider timeProvider)
    : DbContext(options)
{
    public DbSet<AuditProbe> Probes => Set<AuditProbe>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSnakeCaseNamingConvention();
        optionsBuilder.AddInterceptors(new AuditStampingInterceptor(timeProvider));
        base.OnConfiguring(optionsBuilder);
    }
}
