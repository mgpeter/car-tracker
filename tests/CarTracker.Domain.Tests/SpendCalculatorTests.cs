using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

public sealed class SpendCalculatorTests
{
    private static readonly DateOnly PurchaseDate = new(2026, 3, 14);
    private static readonly DateOnly Reference = new(2026, 7, 14);

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
    public void Groups_match_the_dashboard()
    {
        var result = SpendCalculator.Calculate(
            [
                Expense("2026-04-01", "Fuel", 60m),
                Expense("2026-05-01", "Fuel", 70m),
                Expense("2026-05-02", "Service", 200m),
                Expense("2026-05-03", "Repair", 150m),
                Expense("2026-05-04", "Parts", 50m),
                Expense("2026-06-01", "Insurance", 517.14m),
                Expense("2026-06-02", "Tax", 190m),
                Expense("2026-06-03", "MOT", 54.85m),
                Expense("2026-06-04", "Wash", 12m),
            ],
            PurchaseDate, Reference, milesSincePurchase: 4_080);

        Assert.Equal(130m, result.FuelYtd);
        Assert.Equal(400m, result.ServiceAndRepairsYtd);       // 200 + 150 + 50
        Assert.Equal(761.99m, result.StatutoryYtd);            // 517.14 + 190 + 54.85
        Assert.Equal(1303.99m, result.TotalYtd);               // includes the ungrouped wash
    }

    [Fact]
    public void Fuel_ytd_comes_from_mirrored_expense_rows()
    {
        // The workbook reports fuel YTD as £725.70 on the Dashboard and £888.86 in the Fuel Log, because the
        // Expenses sheet carries one lumped "fuel to date" row. With per-fill mirroring the two agree,
        // because they are the same rows — not because two code paths happen to concur.
        var mirrored = new[]
        {
            Expense("2026-04-01", "Fuel", 67.35m),
            Expense("2026-05-01", "Fuel", 71.20m),
            Expense("2026-06-01", "Fuel", 64.10m),
        };

        var result = SpendCalculator.Calculate(mirrored, PurchaseDate, Reference, 4_080);

        Assert.Equal(202.65m, result.FuelYtd);
        Assert.Equal(mirrored.Sum(e => e.Amount), result.FuelYtd);
    }

    [Fact]
    public void Cost_per_mile_is_null_at_zero_miles_rather_than_infinite()
    {
        var result = SpendCalculator.Calculate(
            [Expense("2026-03-14", "Purchase", 2_500m)],
            PurchaseDate, Reference, milesSincePurchase: 0);

        Assert.Null(result.CostPerMile);
        Assert.Null(result.CostPerMileExcludingPurchase);
    }

    [Fact]
    public void Cost_per_mile_is_null_when_mileage_is_unknown()
    {
        var result = SpendCalculator.Calculate(
            [Expense("2026-04-01", "Fuel", 60m)],
            PurchaseDate, Reference, milesSincePurchase: null);

        Assert.Null(result.CostPerMile);
    }

    [Fact]
    public void The_purchase_price_is_included_in_the_total_and_excluded_from_the_running_cost()
    {
        var result = SpendCalculator.Calculate(
            [
                Expense("2026-03-14", "Purchase", 2_500m),
                Expense("2026-04-01", "Fuel", 60m),
                Expense("2026-05-01", "Service", 340m),
            ],
            PurchaseDate, Reference, milesSincePurchase: 4_080);

        // Both are legitimate answers to different questions, so both are exposed rather than choosing.
        Assert.Equal(2_900m, result.TotalSincePurchase);
        Assert.Equal(400m, result.TotalSincePurchaseExcludingPurchase);

        Assert.Equal(Math.Round(2_900m / 4_080m, 4), Math.Round(result.CostPerMile!.Value, 4));
        Assert.Equal(Math.Round(400m / 4_080m, 4), Math.Round(result.CostPerMileExcludingPurchase!.Value, 4));
    }

    [Fact]
    public void Ytd_excludes_last_year_but_since_purchase_includes_it()
    {
        var result = SpendCalculator.Calculate(
            [
                Expense("2025-12-31", "Fuel", 999m), // before this calendar year
                Expense("2026-01-01", "Fuel", 50m),
            ],
            purchaseDate: new DateOnly(2025, 1, 1), Reference, milesSincePurchase: 10_000);

        Assert.Equal(50m, result.FuelYtd);
        Assert.Equal(1_049m, result.TotalSincePurchase);
    }

    [Fact]
    public void Future_dated_expenses_are_excluded()
    {
        var result = SpendCalculator.Calculate(
            [
                Expense("2026-07-14", "Fuel", 50m),  // the reference date itself counts
                Expense("2026-07-15", "Fuel", 999m), // tomorrow does not
            ],
            PurchaseDate, Reference, milesSincePurchase: 4_080);

        Assert.Equal(50m, result.FuelYtd);
    }

    [Fact]
    public void Monthly_average_uses_fractional_months()
    {
        // 14 Mar to 14 Jul 2026 is 122 days: 122 / 365.25 * 12 = 4.008 months.
        var result = SpendCalculator.Calculate(
            [Expense("2026-04-01", "Fuel", 400m)],
            PurchaseDate, Reference, milesSincePurchase: 4_080);

        Assert.Equal(99.8m, Math.Round(result.MonthlyAverage!.Value, 1));
    }

    [Fact]
    public void Monthly_average_is_floored_at_one_month()
    {
        // Bought yesterday: dividing by 0.03 months would report a monthly average 30x the actual spend.
        var result = SpendCalculator.Calculate(
            [Expense("2026-07-13", "Fuel", 60m)],
            purchaseDate: new DateOnly(2026, 7, 13), Reference, milesSincePurchase: 30);

        Assert.Equal(60m, result.MonthlyAverage);
    }

    [Fact]
    public void No_expenses_yields_zeroes_not_nulls()
    {
        var result = SpendCalculator.Calculate([], PurchaseDate, Reference, milesSincePurchase: 4_080);

        // Zero spend is a real, known figure — unlike cost-per-mile at zero miles, which is unanswerable.
        Assert.Equal(0m, result.TotalYtd);
        Assert.Equal(0m, result.FuelYtd);
        Assert.Equal(0m, result.CostPerMile);
        Assert.Empty(result.YtdByCategory);
    }
}
