namespace CarTracker.Data;

/// <summary>
/// One account's chat consumption for one day — the ledger the daily budget is checked against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored rather than counted in memory, because a budget that resets on redeploy is not a budget.</b>
/// Watchtower recreates the container within minutes of every CI publish, and an in-memory counter would hand
/// every account a fresh day's allowance each time — silently, and most often on the days work is being done.
/// </para>
/// <para>
/// <b>No query filter on this table</b>, unlike <c>Vehicle</c> and the three reference lists. The global daily
/// ceiling is a question about every account at once, and a filter would answer it with one account's usage
/// while looking exactly right. The per-owner read scopes itself explicitly instead.
/// </para>
/// <para>
/// It is a counter, not a record: no <c>Source</c>, no audit block. What the assistant actually wrote is
/// attributed on the rows it wrote, by <c>EntrySource.Chat</c>.
/// </para>
/// </remarks>
public sealed class ChatUsage
{
    /// <summary>The account that spent it. Half the primary key.</summary>
    public int OwnerId { get; set; }

    /// <summary>
    /// The Europe/London day it was spent on. A budget is a daily allowance, and a day here is the same local
    /// day every other date in this domain uses — the reset must land at the owner's midnight, not at UTC's.
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>Uncached prompt tokens, as the provider reported them.</summary>
    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    /// <summary>The prefix written to cache, billed above list price. Written once per conversation if all is well.</summary>
    public long CacheWriteTokens { get; set; }

    /// <summary>The prefix read back at a tenth of list price — normally the majority of the count.</summary>
    public long CacheReadTokens { get; set; }

    /// <summary>Model round trips, so a cost figure can be divided by something meaningful.</summary>
    public int Turns { get; set; }

    /// <summary>
    /// Every token the provider reported, weighted equally.
    /// </summary>
    /// <remarks>
    /// A cached prefix costs a tenth of an uncached one and is counted here at full weight, deliberately: this
    /// is a guard rail on volume, not an invoice, and the strict direction is the right one for a ceiling. It
    /// does mean the tool catalogue's ~17k tokens land on the count every turn, so a daily allowance is better
    /// read as a number of turns than as a quantity of conversation.
    /// </remarks>
    public long Total => InputTokens + OutputTokens + CacheWriteTokens + CacheReadTokens;
}
