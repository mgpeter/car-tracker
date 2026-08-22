namespace CarTracker.Data;

/// <summary>
/// One account's DVLA registration lookups for one day - the ledger the daily allowance is checked against.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one allowance that needs a table of its own.</b> The other two are derived from rows that already
/// exist: the chat's spend has <c>ChatUsage</c> because tokens leave no trace anywhere else, and the document
/// ceiling is a <c>COUNT(*)</c> over the documents themselves. A lookup is a read-through to somebody else's
/// API - it writes nothing here, succeeds, and is gone - so counting it is the only way to know it happened.
/// </para>
/// <para>
/// <b>Stored rather than counted in memory</b>, for the reason <see cref="ChatUsage"/> records: Watchtower
/// recreates the container within minutes of every CI publish, and an in-memory counter would hand every
/// account a fresh day's allowance each time - silently, and most often on the days work is being done.
/// </para>
/// <para>
/// Deliberately a second table rather than a shared <c>daily_usage</c> with a kind column. Generalising would
/// mean rewriting a working migration and its tests to save one entity, and the two ledgers do not have the
/// same columns: this one counts calls, that one counts four kinds of token.
/// </para>
/// <para>
/// <b>No query filter</b>, matching <see cref="ChatUsage"/>. Nothing asks a deployment-wide question of this
/// table today, but the per-owner read scopes itself explicitly either way, and one style across the two
/// ledgers is worth more than a filter that would have to be bypassed the first time somebody wants a total.
/// </para>
/// </remarks>
public sealed class VehicleLookupUsage
{
    /// <summary>The account that spent them. Half the primary key.</summary>
    public int OwnerId { get; set; }

    /// <summary>
    /// The Europe/London day they were spent on. The reset must land at the owner's midnight rather than at
    /// UTC's, the same rule every other date in this domain follows.
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>
    /// How many lookups actually reached DVLA.
    /// </summary>
    /// <remarks>
    /// Only successful calls are counted. A 503 from an unconfigured deployment, or an upstream outage, costs
    /// nobody an allowance - the allowance exists to protect somebody else's quota, and a call that never
    /// consumed any has nothing to protect against.
    /// </remarks>
    public int Lookups { get; set; }
}
