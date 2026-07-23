namespace CarTracker.Shared.Metrics;

/// <summary>
/// A check's state. Five members: the first four are the date-derived due axis, and the fifth is the outcome
/// axis folded in — a check whose latest log recorded a bad verdict needs action regardless of its date.
/// </summary>
public enum CheckStatus
{
    Ok = 1,
    DueSoon = 2,
    Overdue = 3,

    /// <summary>
    /// Never performed. Not an error, not a default, and emphatically not <see cref="Ok"/>.
    /// </summary>
    /// <remarks>
    /// The workbook has 18 check definitions but its Dashboard counts 17: "Spare tyre pressure" has never been
    /// logged and silently falls out of the OK/due-soon/overdue buckets. Collapsing this into any of the other
    /// three reproduces that bug exactly.
    /// </remarks>
    NeverLogged = 4,

    /// <summary>
    /// The latest log recorded an <see cref="CarTracker.Shared.CheckResult.Attention"/> or
    /// <see cref="CarTracker.Shared.CheckResult.Failed"/> verdict. Overrides the date-derived status: a check
    /// done today but flagged still needs looking at, and a green "OK" pill would hide exactly the signal the
    /// head-gasket watch depends on. Clears the moment a later log records OK (or no verdict).
    /// </summary>
    Attention = 5,
}

/// <param name="DaysRemaining">Null when never logged — there is no interval to count down.</param>
/// <param name="Result">The latest log's verdict, or null when never logged / logged without one.</param>
public sealed record CheckState(
    int CheckDefinitionId,
    string Name,
    string CadenceLabel,
    int IntervalDays,
    DateOnly? LastPerformedOn,
    DateOnly? NextDue,
    int? DaysRemaining,
    CheckStatus Status,
    CheckResult? Result);

/// <summary>
/// Counts per status. They must sum to the number of active definitions — 18 for BT53 AKJ, not 17.
/// </summary>
public sealed record CheckStatusSummary(
    int OkCount,
    int DueSoonCount,
    int OverdueCount,
    int NeverLoggedCount,
    int AttentionCount,
    IReadOnlyList<CheckState> Checks)
{
    public int TotalCount => OkCount + DueSoonCount + OverdueCount + NeverLoggedCount + AttentionCount;
}
