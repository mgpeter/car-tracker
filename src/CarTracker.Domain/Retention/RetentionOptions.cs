namespace CarTracker.Domain.Retention;

/// <summary>
/// How long the operational ledgers are kept (DEC-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half of the retention policy that code can enforce, and it exists because the privacy page
/// states it.</b> A document promising that something is deleted after a period, on a deployment where nothing
/// deletes it, is the workbook's stored-derived-value problem in its most embarrassing form: a figure asserted
/// in the one artefact whose entire worth is being accurate.
/// </para>
/// <para>
/// The other two thirds of the policy need no configuration. Vehicle and account data live until the account
/// is deleted, which <c>AccountDeletionService</c> already does. Dormant accounts are never deleted, and the
/// policy says so rather than promising a period nothing enforces - erasure has to be preceded by telling
/// somebody, and the only notification channel that exists is the in-app badge (DEC-006), which a dormant
/// account by definition never sees.
/// </para>
/// <para>
/// <b>It prunes regardless of whether the deployment publishes anything.</b> Retention is a property of the
/// data, not of whether a policy page renders; tying the two would mean a self-hoster's ledgers grew for ever.
/// </para>
/// </remarks>
public sealed class RetentionOptions
{
    /// <summary>The shipped window. Thirteen months, so a year-on-year comparison still has a predecessor.</summary>
    public const int DefaultLedgerDays = 400;

    /// <summary>
    /// How many days of ledger history to keep. Blank means <see cref="DefaultLedgerDays"/>; <c>0</c> never
    /// prunes.
    /// </summary>
    /// <remarks>
    /// <b>Nullable for the reason <c>ChatSettings.DailyTokensPerOwner</c> and <c>Signup:Mode</c> both record:</b>
    /// the compose file writes every key it knows about, so an unset variable arrives as <c>""</c>, and
    /// <c>""</c> bound to a plain <c>int</c> throws at boot over a key nobody filled in.
    /// <para>
    /// <b>Three states, and the third is the one to state out loud.</b> Blank is the default, a number is that
    /// number, and <c>0</c> is off - the same third polarity <c>deploy/.env.example</c> already documents for
    /// the chat ceilings, which is why it is written where it is set rather than only here.
    /// </para>
    /// </remarks>
    public int? LedgerDays { get; set; }

    /// <summary>How often the sweep wakes. A day by default.</summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>The resolved window, or null when pruning is switched off.</summary>
    public int? ResolvedLedgerDays =>
        LedgerDays switch
        {
            null => DefaultLedgerDays,
            0 => null,
            // A negative window would delete everything including today, which is a configuration typo rather
            // than an instruction. Treated as off, and the sweep logs that it did nothing.
            < 0 => null,
            var days => days,
        };

    /// <summary>Whether anything is pruned at all.</summary>
    public bool IsEnabled => ResolvedLedgerDays is not null;

    public TimeSpan ResolvedInterval => Interval ?? TimeSpan.FromHours(24);
}
