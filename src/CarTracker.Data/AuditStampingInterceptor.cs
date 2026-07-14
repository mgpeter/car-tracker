using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CarTracker.Data;

/// <summary>
/// Stamps <see cref="IAuditable.CreatedAt"/> and <see cref="IAuditable.UpdatedAt"/> on save, and refuses
/// to save an entity whose <see cref="IAuditable.Source"/> was never set.
/// </summary>
/// <remarks>
/// An interceptor rather than a SaveChanges override so the behaviour can be attached to any context and
/// tested without the full model.
/// </remarks>
public sealed class AuditStampingInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (!Enum.IsDefined(entry.Entity.Source))
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} was saved without a Source. Every write must be " +
                    "attributed to the surface that made it (web/mcp/import/seed) per README §6, and §5.3 " +
                    "requires MCP writes to be identifiable after the fact. Set Source explicitly.");
            }

            if (entry.State is EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else
            {
                entry.Entity.UpdatedAt = now;

                // A later write must not be able to rewrite history, whether by mistake or otherwise.
                entry.Property(e => e.CreatedAt).IsModified = false;
            }
        }
    }
}
