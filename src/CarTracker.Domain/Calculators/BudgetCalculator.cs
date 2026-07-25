using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Budget targets against actual spend, by group. A budget group spans one or more expense categories; its actual
/// is the summed spend of those categories over the period. Only the target is stored; everything else derives.
/// Spend in categories that belong to no group folds into a single "Everything else" line (Purchase excluded),
/// so unplanned spending is visible rather than filtered out.
/// </summary>
public static class BudgetCalculator
{
    /// <summary>Excluded from running costs and from the uncategorised line — buying the car is not a running cost.</summary>
    private const string PurchaseCategory = "Purchase";

    /// <summary>The synthetic line for spend in no group. Not a real, editable group.</summary>
    public const string UncategorisedName = "Everything else";

    public static BudgetSummary Calculate(
        IReadOnlyCollection<BudgetGroup> groups,
        IReadOnlyCollection<ExpenseEntry> expenses,
        BudgetPeriod period,
        DateOnly purchaseDate,
        DateOnly referenceDate)
    {
        var (start, end) = PeriodBounds(period, purchaseDate, referenceDate);

        var actualByCategory = expenses
            .Where(e => e.EntryDate >= start && e.EntryDate <= end)
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        // Which group a category belongs to. The DB guarantees a category is in at most one group per vehicle,
        // so this map is unambiguous.
        var groupOfCategory = groups
            .SelectMany(g => g.Categories.Select(c => (c.Category, Group: g)))
            .ToDictionary(x => x.Category, x => x.Group);

        var lines = groups
            .OrderBy(g => g.DisplayOrder)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .Select(g =>
            {
                var actual = g.Categories.Sum(c => actualByCategory.TryGetValue(c.Category, out var a) ? a : 0m);
                var budget = g.AnnualBudget;
                return new BudgetGroupLine(
                    Name: g.Name,
                    AnnualBudget: budget,
                    ActualSpend: actual,
                    Remaining: budget is { } b ? b - actual : null,
                    // Null on a null or zero target: no meaningful percentage of nothing.
                    PercentUsed: budget is > 0 ? actual / budget.Value * 100m : null,
                    IsOverBudget: budget is not null && actual > budget.Value,
                    Categories: g.Categories.Select(c => c.Category).OrderBy(c => c, StringComparer.Ordinal).ToList(),
                    IsUncategorised: false);
            })
            .ToList();

        // Spend in categories that belong to no group (Purchase excluded) — a single tracked "Everything else"
        // line, appended only when there is any.
        var uncategorised = actualByCategory
            .Where(kv => kv.Key != PurchaseCategory && !groupOfCategory.ContainsKey(kv.Key))
            .Sum(kv => kv.Value);

        if (uncategorised > 0)
        {
            lines.Add(new BudgetGroupLine(
                Name: UncategorisedName,
                AnnualBudget: null,
                ActualSpend: uncategorised,
                Remaining: null,
                PercentUsed: null,
                IsOverBudget: false,
                Categories: [],
                IsUncategorised: true));
        }

        return new BudgetSummary(
            Period: period,
            PeriodStart: start,
            PeriodEnd: end,
            TotalBudget: lines.Sum(l => l.AnnualBudget ?? 0m),
            TotalActual: lines.Sum(l => l.ActualSpend),
            Lines: lines);
    }

    private static (DateOnly Start, DateOnly End) PeriodBounds(
        BudgetPeriod period,
        DateOnly purchaseDate,
        DateOnly referenceDate) => period switch
    {
        BudgetPeriod.CalendarYear => (new DateOnly(referenceDate.Year, 1, 1), referenceDate),
        BudgetPeriod.Rolling12Months => (referenceDate.AddYears(-1).AddDays(1), referenceDate),
        BudgetPeriod.SincePurchase => (purchaseDate, referenceDate),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown budget period."),
    };
}
