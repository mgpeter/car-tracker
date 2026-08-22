using CarTracker.Data;
using CarTracker.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Lookup;

/// <summary>Why a lookup was refused, and when the allowance comes back.</summary>
/// <param name="Used">Lookups already made today.</param>
/// <param name="Limit">What the plan allows in a day.</param>
public sealed record VehicleLookupRefusal(int Used, int Limit, DateTimeOffset ResetsAt);

/// <summary>
/// The daily DVLA allowance, per account, kept in the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>This one guards somebody else's quota rather than our wallet</b>, which is why even the paid tier has a
/// number. The VES and MOT History keys are issued to this deployment and rate-limited to it; an open sign-up
/// with no ceiling means the first stranger to script a plate generator spends the allowance every legitimate
/// account shares, and the failure arrives as DVLA refusing everybody at once.
/// </para>
/// <para>
/// <b>Counted rather than derived, alone among the three allowances.</b> A chat turn leaves a ledger row
/// because tokens leave no trace; a document <i>is</i> a row and needs only a <c>COUNT(*)</c>. A lookup is a
/// read-through that writes nothing at all - it answers, pre-fills a form somebody may abandon, and is gone -
/// so a counter is the only record it can have. See <see cref="VehicleLookupUsage"/> for why that counter is a
/// table and not a field in memory.
/// </para>
/// <para>
/// <b>Charged only for a call that reached DVLA.</b> The unconfigured 503 and an upstream outage cost nobody an
/// allowance: they consumed none of the quota this exists to protect, and spending somebody's third lookup of
/// the day on an answer they did not get would be the app taking its own failure out on the owner.
/// </para>
/// </remarks>
public sealed class VehicleLookupQuota(
    CarTrackerDbContext db,
    IAccountEntitlements entitlements,
    ICurrentUserAccessor currentUser,
    Clock clock)
{
    /// <summary>Null when a lookup may proceed.</summary>
    /// <remarks>
    /// <b>No resolved owner refuses.</b> An unattributable lookup is one nobody is accountable for and one no
    /// ledger can charge, which is the same rule <c>ChatBudget</c> applies to a turn. It is unreachable behind
    /// the Auth0 fallback policy today; it is the direction to be wrong in if it ever is not.
    /// </remarks>
    public async Task<VehicleLookupRefusal?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var resetsAt = clock.StartOfNextDay();

        if (currentUser.OwnerId is not { } ownerId) return new VehicleLookupRefusal(0, 0, resetsAt);

        var allowances = await entitlements.AllowancesAsync(cancellationToken);
        var limit = allowances.DailyVehicleLookups;

        if (limit <= 0) return new VehicleLookupRefusal(0, 0, resetsAt);

        var used = await db.VehicleLookupUsage
            .Where(u => u.OwnerId == ownerId && u.Day == clock.Today())
            .Select(u => u.Lookups)
            .SingleOrDefaultAsync(cancellationToken);

        return used >= limit ? new VehicleLookupRefusal(used, limit, resetsAt) : null;
    }

    /// <summary>Charges one lookup to the current account. Never refuses - the call already happened.</summary>
    public async Task RecordAsync(CancellationToken cancellationToken = default)
    {
        // Unreachable behind CheckAsync, which refuses an unattributable lookup outright. Guarded anyway,
        // because the alternative to skipping the record is charging one account for another's call.
        if (currentUser.OwnerId is not { } ownerId) return;

        var today = clock.Today();

        var row = await db.VehicleLookupUsage
            .SingleOrDefaultAsync(u => u.OwnerId == ownerId && u.Day == today, cancellationToken);

        if (row is null)
        {
            row = new VehicleLookupUsage { OwnerId = ownerId, Day = today };
            db.VehicleLookupUsage.Add(row);
        }

        row.Lookups++;

        await db.SaveChangesAsync(cancellationToken);
    }
}
