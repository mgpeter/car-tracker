using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Retention;

/// <summary>What one sweep removed, so the log can say something other than "done".</summary>
public sealed record RetentionSweep(int ChatUsage, int VehicleLookupUsage, int AssistantWriteAudits)
{
    public static readonly RetentionSweep Nothing = new(0, 0, 0);

    public int Total => ChatUsage + VehicleLookupUsage + AssistantWriteAudits;
}

/// <summary>
/// Deletes ledger rows older than the retention window (DEC-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three tables, one window.</b> A policy with three periods is three sentences that can disagree with each
/// other and with the page stating them, and none of these three is read by anybody after a season.
/// </para>
/// <para>
/// <b>None of the three carries an ownership query filter</b> - only <c>Vehicle</c>, <c>Garage</c>,
/// <c>WashLocation</c> and <c>ExpenseCategory</c> do - so a deployment-wide <c>ExecuteDelete</c> from a
/// background scope is correct here and needs no <c>IgnoreQueryFilters()</c>. It must not reach for
/// <c>BypassOwnership</c> either: that is a request-wide switch which would silently widen anything else
/// running in the same scope, which is the reason <c>AdminReadService</c> refused it.
/// </para>
/// <para>
/// <b><c>pending_identity_deletions</c> is deliberately not pruned.</b> It is already transient - a row exists
/// only while an Auth0 deletion is being retried hourly - and deleting one by age would abandon an erasure
/// somebody asked for, which is the opposite of what a retention policy is for.
/// </para>
/// </remarks>
public sealed class RetentionService(
    CarTrackerDbContext context,
    RetentionOptions options,
    Clock clock,
    TimeProvider timeProvider)
{
    /// <summary>Prunes once. Returns what went, or <see cref="RetentionSweep.Nothing"/> when disabled.</summary>
    public async Task<RetentionSweep> SweepAsync(CancellationToken cancellationToken = default)
    {
        if (options.ResolvedLedgerDays is not { } days) return RetentionSweep.Nothing;

        // Two cutoffs for one window, from two different sources, because the three tables answer two
        // different questions. chat_usage and vehicle_lookup_usage are keyed by a Europe/London DAY - a daily
        // allowance resets at the owner's midnight, not at UTC's - while an audit row records an INSTANT.
        // That is the distinction AnomalyScanner already carries a Clock beside a TimeProvider for.
        //
        // THE INSTANT COMES FROM THE TIMEPROVIDER, NOT FROM Clock.Now(). Clock.Now() returns the current
        // moment expressed in Europe/London, so it carries a +01:00 offset through BST - and Npgsql refuses
        // any offset but UTC for a `timestamp with time zone`, which is a runtime throw on a nightly
        // background job for seven months of the year. The database caught this; nothing else would have.
        var oldestDay = clock.Today().AddDays(-days);
        var oldestInstant = timeProvider.GetUtcNow().AddDays(-days);

        // Strictly older than the cutoff: a row exactly `days` old is inside the window the page promises.
        var chat = await context.ChatUsage
            .Where(u => u.Day < oldestDay)
            .ExecuteDeleteAsync(cancellationToken);

        var lookups = await context.VehicleLookupUsage
            .Where(u => u.Day < oldestDay)
            .ExecuteDeleteAsync(cancellationToken);

        var audits = await context.AssistantWriteAudits
            .Where(a => a.TimestampUtc < oldestInstant)
            .ExecuteDeleteAsync(cancellationToken);

        return new RetentionSweep(chat, lookups, audits);
    }
}
