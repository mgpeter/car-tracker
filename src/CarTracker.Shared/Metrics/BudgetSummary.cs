namespace CarTracker.Shared.Metrics;

/// <summary>The window a budget is measured over (README §3.5).</summary>
public enum BudgetPeriod
{
    CalendarYear = 1,
    Rolling12Months = 2,
    SincePurchase = 3,
}

/// <param name="AnnualBudget">
/// Null when spend exists in a category with no budget. Unbudgeted spend must be visible, not filtered out.
/// </param>
/// <param name="Remaining">Negative when over budget.</param>
/// <param name="PercentUsed">Null when the budget is zero — there is no meaningful percentage of nothing.</param>
public sealed record BudgetLine(
    string Category,
    decimal? AnnualBudget,
    decimal ActualSpend,
    decimal? Remaining,
    decimal? PercentUsed,
    bool IsOverBudget);

public sealed record BudgetSummary(
    BudgetPeriod Period,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalBudget,
    decimal TotalActual,
    IReadOnlyList<BudgetLine> Lines);
