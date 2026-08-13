namespace CarTracker.Shared.Metrics;

/// <summary>The window a budget is measured over (README §3.5).</summary>
public enum BudgetPeriod
{
    CalendarYear = 1,
    Rolling12Months = 2,
    SincePurchase = 3,
}

/// <summary>
/// One budget group's variance — its target against the summed spend of its member categories.
/// </summary>
/// <param name="Name">The group's display name ("Fuel", "Insurance, Tax &amp; MOT"), or "Everything else".</param>
/// <param name="AnnualBudget">
/// Null for a <b>tracked</b> group (no target set) and for the uncategorised line. Spend is still shown; there is
/// simply no bar to fill. Null is not zero — zero means "spend nothing here and tell me when you do".
/// </param>
/// <param name="Remaining">Negative when over budget; null when there is no target.</param>
/// <param name="PercentUsed">Null when the target is null or zero — there is no meaningful percentage of nothing.</param>
/// <param name="Categories">The member category names (empty for the uncategorised line).</param>
/// <param name="IsUncategorised">
/// True for the synthetic "Everything else" line — spend in categories that belong to no group (Purchase
/// excluded). It has no target and cannot be edited; assigning a category to a group moves it out of here.
/// </param>
public sealed record BudgetGroupLine(
    string Name,
    decimal? AnnualBudget,
    decimal ActualSpend,
    decimal? Remaining,
    decimal? PercentUsed,
    bool IsOverBudget,
    IReadOnlyList<string> Categories,
    bool IsUncategorised);

/// <param name="ExcludedPurchase">
/// Spend in the <c>Purchase</c> category over this period — the car itself, and the one thing this screen
/// leaves out. Buying a car is not a running cost, so it belongs in no group and not in "Everything else"
/// either; but the budget page promises that "money the app knows about is never hidden", and £1,700 appearing
/// nowhere at all made that untrue. Reported so the screen can state the exclusion rather than perform it in
/// silence. Zero when there is none.
/// </param>
public sealed record BudgetSummary(
    BudgetPeriod Period,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal TotalBudget,
    decimal TotalActual,
    IReadOnlyList<BudgetGroupLine> Lines,
    decimal ExcludedPurchase);
