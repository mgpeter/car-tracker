using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Per-check status from the latest log plus its interval.
/// </summary>
public static class CheckStatusCalculator
{
    /// <summary>
    /// How much of an interval counts as "due soon", as a fraction.
    /// </summary>
    /// <remarks>
    /// Scaled rather than fixed, because a fixed window is wrong at both ends: seven days' notice on a
    /// seven-day check means it is always due soon, and on an annual check it is useless. 20% gives ~1.4 days
    /// on a weekly check and ~73 on an annual one.
    /// </remarks>
    private const decimal DueSoonFraction = 0.2m;

    public static CheckStatusSummary Calculate(
        IReadOnlyCollection<CheckDefinition> definitions,
        IReadOnlyCollection<CheckLog> logs,
        DateOnly referenceDate)
    {
        var logsByDefinition = logs
            .GroupBy(l => l.CheckDefinitionId)
            .ToDictionary(g => g.Key, g => g.Max(l => l.PerformedOn));

        var states = definitions
            .Where(d => d.IsActive)
            .OrderBy(d => d.DisplayOrder)
            .Select(d => Evaluate(d, logsByDefinition, referenceDate))
            .ToList();

        return new CheckStatusSummary(
            OkCount: states.Count(s => s.Status == CheckStatus.Ok),
            DueSoonCount: states.Count(s => s.Status == CheckStatus.DueSoon),
            OverdueCount: states.Count(s => s.Status == CheckStatus.Overdue),
            NeverLoggedCount: states.Count(s => s.Status == CheckStatus.NeverLogged),
            Checks: states);
    }

    private static CheckState Evaluate(
        CheckDefinition definition,
        IReadOnlyDictionary<int, DateOnly> lastPerformed,
        DateOnly referenceDate)
    {
        if (!lastPerformed.TryGetValue(definition.Id, out var last))
        {
            // A first-class state, not an error and not a default. The workbook's Dashboard counts 17 of 18
            // checks because it has nowhere to put this: "Spare tyre pressure" has never been logged and
            // silently falls out of the OK/due-soon/overdue buckets. Collapsing it into any of the three
            // reproduces that bug exactly.
            return new CheckState(
                definition.Id, definition.Name, definition.CadenceLabel, definition.IntervalDays,
                LastPerformedOn: null, NextDue: null, DaysRemaining: null, Status: CheckStatus.NeverLogged);
        }

        var nextDue = last.AddDays(definition.IntervalDays);
        var daysRemaining = nextDue.DayNumber - referenceDate.DayNumber;
        var dueSoonWindow = Math.Max(1, (int)Math.Ceiling(definition.IntervalDays * DueSoonFraction));

        var status = daysRemaining switch
        {
            < 0 => CheckStatus.Overdue,
            _ when daysRemaining <= dueSoonWindow => CheckStatus.DueSoon,
            _ => CheckStatus.Ok,
        };

        return new CheckState(
            definition.Id, definition.Name, definition.CadenceLabel, definition.IntervalDays,
            last, nextDue, daysRemaining, status);
    }
}
