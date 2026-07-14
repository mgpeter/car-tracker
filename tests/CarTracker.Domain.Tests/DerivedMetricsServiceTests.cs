using CarTracker.Data;
using CarTracker.Domain.Tests.Workbook;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Domain.Tests;

public sealed class DerivedMetricsServiceTests
{
    private sealed class StubLoader(VehicleMetricsData? data) : IVehicleMetricsLoader
    {
        public Task<VehicleMetricsData?> LoadAsync(int vehicleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(data);
    }

    private static DerivedMetricsService Service(VehicleMetricsData? data) =>
        new(new StubLoader(data),
            // 21:00 UTC on 14 July is 22:00 BST the same day — still the reference date.
            new Clock(new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 21, 0, 0, TimeSpan.Zero))));

    [Fact]
    public async Task An_unknown_vehicle_yields_null_not_an_empty_summary()
    {
        var summary = await Service(null).GetVehicleSummaryAsync(999);

        // "Does not exist" and "exists but has no data" are different answers and the caller must tell them
        // apart — an empty summary would claim the vehicle is real with zero miles.
        Assert.Null(summary);
    }

    [Fact]
    public async Task The_facade_resolves_the_reference_date_through_the_clock()
    {
        var summary = await Service(WorkbookFixture.Data()).GetVehicleSummaryAsync(WorkbookFixture.VehicleId);

        Assert.NotNull(summary);
        Assert.Equal(new DateOnly(2026, 7, 14), summary.AsOfDate);
        Assert.Equal(359, summary.Renewals.Mot.DaysRemaining);
    }

    [Fact]
    public async Task The_facade_composes_every_calculator()
    {
        var summary = await Service(WorkbookFixture.Data()).GetVehicleSummaryAsync(WorkbookFixture.VehicleId);

        Assert.NotNull(summary);
        Assert.Equal("BT53 AKJ", summary.Registration);
        Assert.Equal("Land Rover Freelander 1", summary.Name);
        Assert.Equal(80_712, summary.Mileage.CurrentMileage);
        Assert.Equal(556.47m, summary.Fuel.TotalLitres);
        Assert.Equal(5_146.71m, summary.Spend.TotalYtd);
        Assert.Equal(new DateOnly(2027, 7, 8), summary.Renewals.Mot.ExpiryDate);
    }

    // ---- Edge cases (task 5.6) --------------------------------------------------------------------------

    private static VehicleMetricsData Empty(int purchaseMileage = 76_632) => new(
        new Vehicle
        {
            Id = 1,
            Registration = "EMP 7Y",
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 7, 14),
            PurchaseMileage = purchaseMileage,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        },
        MileageReadings: [], FuelEntries: [], ExpenseEntries: [], ServiceRecords: [],
        CheckDefinitions: [], CheckLogs: [], BudgetCategories: []);

    [Fact]
    public async Task A_brand_new_vehicle_reports_unknowns_rather_than_zeroes()
    {
        var summary = await Service(Empty()).GetVehicleSummaryAsync(1);

        Assert.NotNull(summary);

        // No readings: current mileage is unknown, not zero.
        Assert.Null(summary.Mileage.CurrentMileage);
        Assert.Null(summary.Mileage.MilesSincePurchase);

        // No fills: no economy, but totals are a real zero.
        Assert.Null(summary.Fuel.AverageMpg);
        Assert.Null(summary.Fuel.AveragePricePerLitre);
        Assert.Equal(0m, summary.Fuel.TotalLitres);

        // No mileage: cost-per-mile is unanswerable, but zero spend is a fact.
        Assert.Null(summary.Spend.CostPerMile);
        Assert.Equal(0m, summary.Spend.TotalYtd);

        // No expiries recorded: unknown, not urgent. Defaulting to Red would cry wolf.
        Assert.Null(summary.Renewals.Mot.ExpiryDate);
        Assert.Null(summary.Renewals.Mot.Urgency);

        Assert.Equal(0, summary.Checks.TotalCount);
    }

    [Fact]
    public async Task A_vehicle_with_one_fill_has_totals_but_no_economy()
    {
        var data = Empty() with
        {
            FuelEntries = [new FuelEntry
            {
                Id = 1,
                VehicleId = 1,
                EntryDate = new DateOnly(2026, 7, 14),
                Mileage = 76_700,
                Litres = 40m,
                PricePerLitre = 1.50m,
                TotalCost = 60m,
                FillLevel = FillLevel.Full,
                Source = EntrySource.Web,
            }],
        };

        var summary = await Service(data).GetVehicleSummaryAsync(1);

        Assert.NotNull(summary);
        Assert.Equal(40m, summary.Fuel.TotalLitres);
        Assert.Equal(1.50m, summary.Fuel.AveragePricePerLitre);
        Assert.Null(summary.Fuel.AverageMpg);
        Assert.Equal(0, summary.Fuel.ReliableIntervalCount);
    }

    [Fact]
    public async Task Zero_miles_since_purchase_yields_no_cost_per_mile()
    {
        var data = Empty() with
        {
            MileageReadings = [new MileageReading
            {
                VehicleId = 1,
                ReadingDate = new DateOnly(2026, 7, 14),
                Mileage = 76_632, // exactly the purchase mileage
                Origin = MileageOrigin.Manual,
                Source = EntrySource.Web,
            }],
            ExpenseEntries = [new ExpenseEntry
            {
                VehicleId = 1,
                EntryDate = new DateOnly(2026, 7, 14),
                Category = "Purchase",
                Amount = 1_700m,
                Source = EntrySource.Web,
            }],
        };

        var summary = await Service(data).GetVehicleSummaryAsync(1);

        Assert.NotNull(summary);
        Assert.Equal(0, summary.Mileage.MilesSincePurchase);
        Assert.Null(summary.Spend.CostPerMile);
        Assert.Equal(1_700m, summary.Spend.TotalSincePurchase);
    }

    [Fact]
    public async Task A_zero_budget_yields_no_percentage()
    {
        var data = Empty() with
        {
            BudgetCategories = [new BudgetCategory
            {
                VehicleId = 1, Category = "Wash", AnnualBudget = 0m, Source = EntrySource.Web,
            }],
            ExpenseEntries = [new ExpenseEntry
            {
                VehicleId = 1,
                EntryDate = new DateOnly(2026, 7, 14),
                Category = "Wash",
                Amount = 12m,
                Source = EntrySource.Web,
            }],
        };

        var budget = await Service(data).GetBudgetSummaryAsync(1);

        Assert.NotNull(budget);
        var wash = budget.Lines.Single();
        Assert.Null(wash.PercentUsed);
        Assert.True(wash.IsOverBudget);
    }

    [Fact]
    public async Task An_unknown_vehicle_yields_no_budget_either()
    {
        Assert.Null(await Service(null).GetBudgetSummaryAsync(999));
    }

    [Fact]
    public async Task The_budget_period_reaches_the_calculator()
    {
        var summary = await Service(WorkbookFixture.Data())
            .GetBudgetSummaryAsync(WorkbookFixture.VehicleId, BudgetPeriod.SincePurchase);

        Assert.NotNull(summary);
        Assert.Equal(BudgetPeriod.SincePurchase, summary.Period);
        Assert.Equal(new DateOnly(2026, 3, 14), summary.PeriodStart);
    }
}
