using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Projects an issue's early-warning watch onto the check statuses <see cref="CheckStatusCalculator"/> already
/// computed.
/// </summary>
/// <remarks>
/// <para>
/// <b>No new arithmetic.</b> This does not decide whether a check is overdue — it reads the
/// <see cref="CheckState"/> the check calculator produced and groups it by issue. A second "is this check
/// overdue" path is exactly the divergence the project exists to prevent, and it would be especially bad here,
/// where the whole point is that the dashboard's named watch and the checks screen agree.
/// </para>
/// <para>
/// Nothing about the watch is stored — not its status, not a lapsed flag. The link is stored; the verdict is
/// computed on read like every other figure (DEC-002).
/// </para>
/// </remarks>
public static class WatchCalculator
{
    /// <summary>
    /// Whether a check has stopped providing early warning. The single definition of "lapsed" — both the issues
    /// screen and the dashboard read it from here.
    /// </summary>
    /// <remarks>
    /// <see cref="CheckStatus.NeverLogged"/> counts: a check that has never been done is not reassurance, and
    /// treating it as one would reproduce the workbook's own bug of quietly dropping the never-logged row out of
    /// the buckets. <see cref="CheckStatus.Attention"/> counts too, and counts hardest — it means the latest log
    /// recorded Attention or Failed, which for an early-warning check is the alarm actually going off rather
    /// than merely going unheard. <see cref="CheckStatus.DueSoon"/> does not: a check that is still in date is
    /// still watching.
    /// </remarks>
    public static bool IsLapsed(CheckStatus status) =>
        status is CheckStatus.Overdue or CheckStatus.NeverLogged or CheckStatus.Attention;

    /// <summary>
    /// The checks one issue watches, in the order the checks screen lists them, with their live status.
    /// </summary>
    /// <remarks>
    /// A link whose check definition is missing from <paramref name="checkStates"/> is skipped rather than
    /// rendered as unknown. That happens for a <b>retired</b> definition — <c>CheckStatusCalculator</c> only
    /// evaluates <c>IsActive</c> ones — and a retired check genuinely no longer watches anything, so the honest
    /// reading is that the issue watches fewer checks, not that it watches one of indeterminate status.
    /// </remarks>
    public static IReadOnlyList<WatchedCheck> ChecksFor(
        int issueId,
        IReadOnlyCollection<IssueWatchCheck> links,
        IReadOnlyCollection<CheckState> checkStates)
    {
        var watched = links
            .Where(l => l.IssueId == issueId)
            .Select(l => l.CheckDefinitionId)
            .ToHashSet();

        if (watched.Count == 0) return [];

        return checkStates
            .Where(s => watched.Contains(s.CheckDefinitionId))
            .Select(s => new WatchedCheck(
                s.CheckDefinitionId, s.Name, s.Status, s.DaysRemaining, IsLapsed(s.Status)))
            .ToList();
    }

    /// <summary>
    /// Every issue that watches at least one live check, worst first — most lapsed checks, then most watched.
    /// </summary>
    /// <remarks>
    /// Issues with no watch are absent rather than present with zero counts: the dashboard's panel iterates this
    /// list, and an entry meaning "nothing to say" is an entry it would have to filter out again. An issue whose
    /// every watched check has been retired is likewise absent, for the same reason it shows no checks above.
    /// </remarks>
    public static IReadOnlyList<WatchSummary> Calculate(
        IReadOnlyCollection<Issue> issues,
        IReadOnlyCollection<IssueWatchCheck> links,
        IReadOnlyCollection<CheckState> checkStates)
    {
        if (links.Count == 0) return [];

        var summaries = new List<WatchSummary>();

        foreach (var issue in issues)
        {
            var checks = ChecksFor(issue.Id, links, checkStates);
            if (checks.Count == 0) continue;

            summaries.Add(new WatchSummary(
                issue.Id,
                issue.Title,
                issue.Status,
                TotalCheckCount: checks.Count,
                LapsedCheckCount: checks.Count(c => c.IsLapsed)));
        }

        return summaries
            .OrderByDescending(w => w.LapsedCheckCount)
            .ThenByDescending(w => w.TotalCheckCount)
            .ThenBy(w => w.IssueTitle)
            .ToList();
    }
}
