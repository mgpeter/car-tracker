using CarTracker.Data;
using CarTracker.Domain.Tests.Workbook;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Domain.Tests;

public sealed class DerivedMetricsServiceTests
{
    private sealed class StubLoader(VehicleMetricsData? data, int openAnomalies = 0) : IVehicleMetricsLoader
    {
        public Task<VehicleMetricsData?> LoadAsync(int vehicleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(data);

        public Task<IReadOnlyList<int>> ListVehicleIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>(data is null ? [] : [data.Vehicle.Id]);

        public Task<IReadOnlyDictionary<int, int>> CountOpenAnomaliesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(
                data is null || openAnomalies == 0
                    ? new Dictionary<int, int>()
                    : new Dictionary<int, int> { [data.Vehicle.Id] = openAnomalies });
    }

    private static DerivedMetricsService Service(VehicleMetricsData? data, int openAnomalies = 0) =>
        new(new StubLoader(data, openAnomalies),
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
        Assert.Equal(0, summary.Fuel.MeasuredIntervalCount);
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

    // ---- The garage ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_garage_card_cannot_disagree_with_the_dashboard()
    {
        var service = Service(WorkbookFixture.Data());

        var card = Assert.Single(await service.GetGarageAsync());
        var dashboard = await service.GetVehicleSummaryAsync(WorkbookFixture.VehicleId);

        Assert.NotNull(dashboard);

        // The whole reason GarageItem is a projection rather than a second computation. If these ever drift,
        // the app has the workbook's original disease: two places claiming different figures for one car.
        Assert.Equal(dashboard.Mileage.CurrentMileage, card.CurrentMileage);
        Assert.Equal(dashboard.Mileage.MilesSincePurchase, card.MilesSincePurchase);
        Assert.Equal(dashboard.Spend.CostPerMile, card.CostPerMile);
        Assert.Equal(dashboard.Fuel.AverageMpg, card.AverageMpg);
        Assert.Equal(dashboard.Renewals.Mot.DaysRemaining, card.Mot.DaysRemaining);
        Assert.Equal(dashboard.Checks.OverdueCount, card.OverdueCheckCount);
    }

    [Fact]
    public async Task The_garage_reports_the_real_workbook_figures()
    {
        var card = Assert.Single(await Service(WorkbookFixture.Data()).GetGarageAsync());

        Assert.Equal("BT53 AKJ", card.Registration);
        Assert.Equal(80_712, card.CurrentMileage);
        Assert.Equal(4_080, card.MilesSincePurchase);
        // 359 days, not the sheet's stale 23 — the defect that started all this.
        Assert.Equal(359, card.Mot.DaysRemaining);
    }

    [Fact]
    public async Task The_latest_mpg_is_the_last_fill_not_the_average()
    {
        var card = Assert.Single(await Service(WorkbookFixture.Data()).GetGarageAsync());
        var summary = await Service(WorkbookFixture.Data()).GetVehicleSummaryAsync(WorkbookFixture.VehicleId);

        Assert.NotNull(summary);

        // "latest 25.4" against an average of 28.7. Showing the average here would tell the owner the car is
        // doing better than the last tank actually did.
        Assert.Equal(summary.Fuel.Entries[^1].Mpg, card.LatestMpg);
        Assert.NotEqual(card.AverageMpg, card.LatestMpg);
    }

    [Fact]
    public async Task An_empty_garage_is_empty_not_an_error()
    {
        // "You have no cars yet" is a state the add-car flow exists to answer.
        Assert.Empty(await Service(null).GetGarageAsync());
    }

    [Fact]
    public async Task Open_anomalies_reach_the_card()
    {
        var card = Assert.Single(await Service(WorkbookFixture.Data(), openAnomalies: 1).GetGarageAsync());

        // The integrity pill. Not derived — anomalies are records with a lifecycle, which is why they arrive
        // beside the summary rather than inside it.
        Assert.Equal(1, card.OpenAnomalyCount);
    }

    [Fact]
    public async Task A_vehicle_with_no_recorded_expiries_is_not_reported_as_renewals_ok()
    {
        var card = Assert.Single(await Service(Empty()).GetGarageAsync());

        // Unknown is not OK. Saying "Renewals OK" about a car whose insurance date nobody has entered would
        // be the app inventing reassurance — the same shape of lie as the sheet's stale MOT countdown.
        Assert.Null(card.Mot.ExpiryDate);
        Assert.False(card.RenewalsOk);
    }

    [Fact]
    public void Integrity_reports_the_count_and_the_worst_open_flag()
    {
        var data = Empty() with
        {
            OpenAnomalies =
            [
                Anomaly(AnomalySeverity.Warning),
                Anomaly(AnomalySeverity.Error),
                Anomaly(AnomalySeverity.Info),
            ],
        };

        var summary = DerivedMetrics.Compute(data, new DateOnly(2026, 7, 14));

        Assert.Equal(3, summary.Integrity.OpenCount);
        // Error < Warning < Info by enum value, so the worst is Error. The panel leads with the worst rather
        // than making the reader open the queue to find it.
        Assert.Equal(AnomalySeverity.Error, summary.Integrity.HighestSeverity);
    }

    [Fact]
    public void Integrity_severity_is_null_when_nothing_is_flagged()
    {
        var summary = DerivedMetrics.Compute(Empty(), new DateOnly(2026, 7, 14));

        Assert.Equal(0, summary.Integrity.OpenCount);
        // Not Info. A headline of "0 flags" should not also assert a severity nobody raised.
        Assert.Null(summary.Integrity.HighestSeverity);
    }

    private static DataAnomaly Anomaly(AnomalySeverity severity) => new()
    {
        VehicleId = 1,
        Kind = AnomalyKind.MileageNonMonotonic,
        Severity = severity,
        EntityType = "MileageReading",
        Message = "test",
        Status = AnomalyStatus.Open,
    };
}
