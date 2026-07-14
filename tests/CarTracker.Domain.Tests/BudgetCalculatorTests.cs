using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class BudgetCalculatorTests
{
    private static readonly DateOnly PurchaseDate = new(2026, 3, 14);
    private static readonly DateOnly Reference = new(2026, 7, 14);

    private static BudgetCategory Budget(string category, decimal annual) =>
        new() { VehicleId = 1, Category = category, AnnualBudget = annual, Source = EntrySource.Web };

    private static ExpenseEntry Expense(string date, string category, decimal amount) =>
        new()
        {
            VehicleId = 1,
            EntryDate = DateOnly.Parse(date),
            Category = category,
            Amount = amount,
            Source = EntrySource.Import,
        };

    [Fact]
    public void Reports_actual_remaining_and_percent_used()
    {
        var result = BudgetCalculator.Calculate(
            [Budget("Fuel", 1_200m)],
            [Expense("2026-04-01", "Fuel", 300m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var fuel = result.Lines.Single();
        Assert.Equal(300m, fuel.ActualSpend);
        Assert.Equal(900m, fuel.Remaining);
        Assert.Equal(25m, fuel.PercentUsed);
        Assert.False(fuel.IsOverBudget);
    }

    [Fact]
    public void Over_budget_goes_negative_rather_than_clamping()
    {
        var result = BudgetCalculator.Calculate(
            [Budget("Service", 500m)],
            [Expense("2026-05-01", "Service", 640m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var service = result.Lines.Single();
        Assert.Equal(-140m, service.Remaining);
        Assert.Equal(128m, service.PercentUsed);
        Assert.True(service.IsOverBudget);
    }

    [Fact]
    public void A_zero_budget_has_no_percentage_rather_than_infinity()
    {
        var result = BudgetCalculator.Calculate(
            [Budget("Wash", 0m)],
            [Expense("2026-05-01", "Wash", 12m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var wash = result.Lines.Single();
        Assert.Null(wash.PercentUsed);
        Assert.Equal(12m, wash.ActualSpend);
        Assert.True(wash.IsOverBudget);
    }

    [Fact]
    public void Unbudgeted_spend_is_visible_not_filtered_out()
    {
        var result = BudgetCalculator.Calculate(
            [Budget("Fuel", 1_200m)],
            [
                Expense("2026-04-01", "Fuel", 300m),
                Expense("2026-05-01", "Repair", 450m), // no budget for this
            ],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        // Filtering this out would hide exactly the spending nobody planned for.
        var repair = result.Lines.Single(l => l.Category == "Repair");
        Assert.Null(repair.AnnualBudget);
        Assert.Equal(450m, repair.ActualSpend);
        Assert.Null(repair.Remaining);
        Assert.False(repair.IsOverBudget); // no target to exceed
    }

    [Fact]
    public void A_budgeted_category_with_no_spend_reports_zero_not_absent()
    {
        var result = BudgetCalculator.Calculate(
            [Budget("MOT", 60m)], [], BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var mot = result.Lines.Single();
        Assert.Equal(0m, mot.ActualSpend);
        Assert.Equal(60m, mot.Remaining);
        Assert.Equal(0m, mot.PercentUsed);
    }

    [Theory]
    [InlineData(BudgetPeriod.CalendarYear, "2026-01-01")]
    [InlineData(BudgetPeriod.SincePurchase, "2026-03-14")]
    [InlineData(BudgetPeriod.Rolling12Months, "2025-07-15")]
    public void Period_bounds_are_correct(BudgetPeriod period, string expectedStart)
    {
        var result = BudgetCalculator.Calculate([], [], period, PurchaseDate, Reference);

        Assert.Equal(DateOnly.Parse(expectedStart), result.PeriodStart);
        Assert.Equal(Reference, result.PeriodEnd);
    }

    [Fact]
    public void The_period_changes_which_spend_counts()
    {
        var expenses = new[]
        {
            Expense("2025-12-01", "Fuel", 100m), // last year; rolling only
            Expense("2026-02-01", "Fuel", 200m), // this year, before purchase
            Expense("2026-04-01", "Fuel", 300m), // since purchase
        };

        var calendar = BudgetCalculator.Calculate([Budget("Fuel", 1_200m)], expenses, BudgetPeriod.CalendarYear, PurchaseDate, Reference);
        var sincePurchase = BudgetCalculator.Calculate([Budget("Fuel", 1_200m)], expenses, BudgetPeriod.SincePurchase, PurchaseDate, Reference);
        var rolling = BudgetCalculator.Calculate([Budget("Fuel", 1_200m)], expenses, BudgetPeriod.Rolling12Months, PurchaseDate, Reference);

        Assert.Equal(500m, calendar.Lines.Single().ActualSpend);       // 200 + 300
        Assert.Equal(300m, sincePurchase.Lines.Single().ActualSpend);  // 300
        Assert.Equal(600m, rolling.Lines.Single().ActualSpend);        // all three
    }

    [Fact]
    public void Totals_aggregate_across_lines()
    {
        var result = BudgetCalculator.Calculate(
            [Budget("Fuel", 1_200m), Budget("Service", 500m)],
            [Expense("2026-04-01", "Fuel", 300m), Expense("2026-05-01", "Service", 640m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        Assert.Equal(1_700m, result.TotalBudget);
        Assert.Equal(940m, result.TotalActual);
    }

    [Fact]
    public void An_unknown_period_throws_rather_than_silently_returning_nothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BudgetCalculator.Calculate([], [], (BudgetPeriod)99, PurchaseDate, Reference));
    }
}
