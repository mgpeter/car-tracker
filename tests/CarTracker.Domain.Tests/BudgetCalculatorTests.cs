using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class BudgetCalculatorTests
{
    private static readonly DateOnly PurchaseDate = new(2026, 3, 14);
    private static readonly DateOnly Reference = new(2026, 7, 14);

    private static BudgetGroup Group(string name, decimal? annual, params string[] categories) =>
        new()
        {
            VehicleId = 1,
            Name = name,
            AnnualBudget = annual,
            DisplayOrder = 0,
            Source = EntrySource.Web,
            Categories = [.. categories.Select(c => new BudgetGroupCategory { VehicleId = 1, Category = c })],
        };

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
            [Group("Fuel", 1_200m, "Fuel")],
            [Expense("2026-04-01", "Fuel", 300m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var fuel = result.Lines.Single();
        Assert.Equal(300m, fuel.ActualSpend);
        Assert.Equal(900m, fuel.Remaining);
        Assert.Equal(25m, fuel.PercentUsed);
        Assert.False(fuel.IsOverBudget);
    }

    [Fact]
    public void A_group_sums_the_spend_of_all_its_categories()
    {
        var result = BudgetCalculator.Calculate(
            [Group("Insurance, Tax & MOT", 1_000m, "Insurance", "Tax", "MOT")],
            [
                Expense("2026-04-01", "Insurance", 200m),
                Expense("2026-05-01", "Tax", 150m),
                Expense("2026-06-01", "MOT", 54m),
            ],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var line = result.Lines.Single();
        Assert.Equal(404m, line.ActualSpend);
        Assert.Equal(596m, line.Remaining);
        Assert.Equal(["Insurance", "MOT", "Tax"], line.Categories); // ordinal-sorted
    }

    [Fact]
    public void Over_budget_goes_negative_rather_than_clamping()
    {
        var result = BudgetCalculator.Calculate(
            [Group("Service", 500m, "Service")],
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
            [Group("Wash", 0m, "Wash")],
            [Expense("2026-05-01", "Wash", 12m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var wash = result.Lines.Single();
        Assert.Null(wash.PercentUsed);
        Assert.Equal(12m, wash.ActualSpend);
        Assert.True(wash.IsOverBudget);
    }

    [Fact]
    public void A_tracked_group_with_no_target_shows_spend_without_a_bar()
    {
        var result = BudgetCalculator.Calculate(
            [Group("Fuel", null, "Fuel")],
            [Expense("2026-04-01", "Fuel", 300m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        // Null target is not zero: spend is shown, there is simply no bar to fill and nothing to be over.
        var fuel = result.Lines.Single();
        Assert.Null(fuel.AnnualBudget);
        Assert.Equal(300m, fuel.ActualSpend);
        Assert.Null(fuel.Remaining);
        Assert.Null(fuel.PercentUsed);
        Assert.False(fuel.IsOverBudget);
    }

    [Fact]
    public void Unbudgeted_spend_folds_into_the_uncategorised_line_not_lost()
    {
        var result = BudgetCalculator.Calculate(
            [Group("Fuel", 1_200m, "Fuel")],
            [
                Expense("2026-04-01", "Fuel", 300m),
                Expense("2026-05-01", "Parking", 40m),   // in no group
                Expense("2026-05-02", "Misc", 10m),      // in no group
            ],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        // Filtering these out would hide exactly the spending nobody planned for; they fold into "Everything else".
        var everythingElse = result.Lines.Single(l => l.IsUncategorised);
        Assert.Equal(BudgetCalculator.UncategorisedName, everythingElse.Name);
        Assert.Null(everythingElse.AnnualBudget);
        Assert.Equal(50m, everythingElse.ActualSpend);
        Assert.Empty(everythingElse.Categories);
    }

    [Fact]
    public void Purchase_spend_is_excluded_from_the_uncategorised_line()
    {
        var result = BudgetCalculator.Calculate(
            [],
            [
                Expense("2026-03-14", "Purchase", 5_000m), // buying the car is not a running cost
                Expense("2026-05-01", "Wash", 12m),
            ],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        var everythingElse = result.Lines.Single();
        Assert.True(everythingElse.IsUncategorised);
        Assert.Equal(12m, everythingElse.ActualSpend); // Purchase's 5,000 is not counted
    }

    [Fact]
    public void A_budgeted_group_with_no_spend_reports_zero_not_absent()
    {
        var result = BudgetCalculator.Calculate(
            [Group("MOT", 60m, "MOT")], [], BudgetPeriod.CalendarYear, PurchaseDate, Reference);

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

        var calendar = BudgetCalculator.Calculate([Group("Fuel", 1_200m, "Fuel")], expenses, BudgetPeriod.CalendarYear, PurchaseDate, Reference);
        var sincePurchase = BudgetCalculator.Calculate([Group("Fuel", 1_200m, "Fuel")], expenses, BudgetPeriod.SincePurchase, PurchaseDate, Reference);
        var rolling = BudgetCalculator.Calculate([Group("Fuel", 1_200m, "Fuel")], expenses, BudgetPeriod.Rolling12Months, PurchaseDate, Reference);

        Assert.Equal(500m, calendar.Lines.Single().ActualSpend);       // 200 + 300
        Assert.Equal(300m, sincePurchase.Lines.Single().ActualSpend);  // 300
        Assert.Equal(600m, rolling.Lines.Single().ActualSpend);        // all three
    }

    [Fact]
    public void Totals_aggregate_across_lines()
    {
        var result = BudgetCalculator.Calculate(
            [Group("Fuel", 1_200m, "Fuel"), Group("Service & Repairs", 500m, "Service", "Repair")],
            [Expense("2026-04-01", "Fuel", 300m), Expense("2026-05-01", "Service", 640m)],
            BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        Assert.Equal(1_700m, result.TotalBudget);
        Assert.Equal(940m, result.TotalActual);
    }

    [Fact]
    public void Groups_are_ordered_by_display_order()
    {
        var result = BudgetCalculator.Calculate(
            [
                new BudgetGroup { VehicleId = 1, Name = "Second", DisplayOrder = 2, Source = EntrySource.Web,
                    Categories = [new BudgetGroupCategory { VehicleId = 1, Category = "Fuel" }] },
                new BudgetGroup { VehicleId = 1, Name = "First", DisplayOrder = 1, Source = EntrySource.Web,
                    Categories = [new BudgetGroupCategory { VehicleId = 1, Category = "Service" }] },
            ],
            [], BudgetPeriod.CalendarYear, PurchaseDate, Reference);

        Assert.Equal(["First", "Second"], result.Lines.Select(l => l.Name));
    }

    [Fact]
    public void An_unknown_period_throws_rather_than_silently_returning_nothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BudgetCalculator.Calculate([], [], (BudgetPeriod)99, PurchaseDate, Reference));
    }
}
