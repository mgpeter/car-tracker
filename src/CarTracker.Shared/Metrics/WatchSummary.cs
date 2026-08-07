namespace CarTracker.Shared.Metrics;

/// <summary>
/// One check in an issue's early-warning watch, with the live status that check already has.
/// </summary>
/// <param name="IsLapsed">
/// Whether this check has stopped providing early warning. Carried from the server rather than re-derived per
/// surface: "which statuses count as lapsed" is a rule, and a rule evaluated in two places is a rule that can
/// disagree with itself. See <c>WatchCalculator.IsLapsed</c> for the definition.
/// </param>
public sealed record WatchedCheck(
    int CheckDefinitionId,
    string Name,
    CheckStatus Status,
    int? DaysRemaining,
    bool IsLapsed);

/// <summary>
/// A named watch as the dashboard's attention panel needs it — the issue's name and how much of its watch has
/// lapsed. A headline, like <see cref="IntegritySummary"/>: the panel needs the name and the counts, not the
/// whole check list, which the issues screen carries instead.
/// </summary>
/// <param name="IssueStatus">
/// Carried because it changes the sentence, not the severity: a lapsed watch on a <i>Resolved</i> issue is the
/// head-gasket case — "resolved, and the thing keeping it resolved has stopped happening" — while a lapsed
/// watch on a Monitoring issue is simply an early-warning check going unlogged. Neither reopens the issue.
/// </param>
public sealed record WatchSummary(
    int IssueId,
    string IssueTitle,
    IssueStatus IssueStatus,
    int TotalCheckCount,
    int LapsedCheckCount);
