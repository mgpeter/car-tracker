using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Budget targets against actual spend. Only the target is stored; everything else derives.
/// </summary>
public static class BudgetCalculator
{
    public static BudgetSummary Calculate(
        IReadOnlyCollection<BudgetCategory> budgets,
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

        var budgetByCategory = budgets.ToDictionary(b => b.Category, b => b.AnnualBudget);

        // Union, not just the budgeted set: a category with spend and no budget must be visible. Filtering it
        // out would hide exactly the spending nobody planned for.
        var categories = budgetByCategory.Keys
            .Union(actualByCategory.Keys)
            .OrderBy(c => c, StringComparer.Ordinal);

        var lines = categories.Select(category =>
        {
            var budget = budgetByCategory.TryGetValue(category, out var b) ? b : (decimal?)null;
            var actual = actualByCategory.TryGetValue(category, out var a) ? a : 0m;

            return new BudgetLine(
                Category: category,
                AnnualBudget: budget,
                // A budgeted category with no spend reports zero, not absent — the target still applies.
                ActualSpend: actual,
                Remaining: budget - actual,
                // Null on a zero budget: there is no meaningful percentage of nothing, and dividing would
                // throw or produce infinity.
                PercentUsed: budget is > 0 ? actual / budget.Value * 100m : null,
                IsOverBudget: budget is not null && actual > budget.Value);
        }).ToList();

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
