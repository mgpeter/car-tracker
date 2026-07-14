namespace CarTracker.Shared.Metrics;

/// <summary>
/// A check's state. Four members, and the fourth is the point.
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
}

/// <param name="DaysRemaining">Null when never logged — there is no interval to count down.</param>
public sealed record CheckState(
    int CheckDefinitionId,
    string Name,
    string CadenceLabel,
    int IntervalDays,
    DateOnly? LastPerformedOn,
    DateOnly? NextDue,
    int? DaysRemaining,
    CheckStatus Status);

/// <summary>
/// Counts per status. They must sum to the number of active definitions — 18 for BT53 AKJ, not 17.
/// </summary>
public sealed record CheckStatusSummary(
    int OkCount,
    int DueSoonCount,
    int OverdueCount,
    int NeverLoggedCount,
    IReadOnlyList<CheckState> Checks)
{
    public int TotalCount => OkCount + DueSoonCount + OverdueCount + NeverLoggedCount;
}
