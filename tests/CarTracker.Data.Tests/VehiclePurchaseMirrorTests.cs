using CarTracker.Domain;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Vehicles;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The fourth expense mirror: the car itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>Vehicle.PurchasePrice</c> was stored, shown on Vehicle Info, and read by no calculation — the purchase
/// cost came from expense rows in the <c>Purchase</c> category and nothing ever wrote one. So on any vehicle
/// created through the app, <c>TotalSincePurchase</c> equalled <c>TotalSincePurchaseExcludingPurchase</c> and
/// the two cost-per-mile figures were the same number: four fields silently collapsed to two, and the
/// dashboard's "including the £1,700 car itself" clause — conditional on those totals differing — never
/// rendered. Nothing looked broken. These tests are what stop it returning.
/// </para>
/// <para>
/// The double-count is the sharper risk and gets the most attention here: one purchase per vehicle, enforced at
/// the database, with hand-entry refused on both write paths.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class VehiclePurchaseMirrorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _clock);

    private VehicleUpdateService NewService(CarTrackerDbContext context) =>
        new(context, new DerivedMetricsService(new VehicleMetricsLoader(context), new Clock(_clock)),
            new ReferenceWriter(context), new VehiclePurchaseMirror(context));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_purchasemirror");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedVehicleAsync(string registration, decimal? purchasePrice)
    {
        await using var context = NewContext();
        var vehicle = await new VehicleFactory(context).CreateAsync(
            new Vehicle
            {
                Registration = registration,
                Make = "Land Rover",
                Model = "Freelander",
                Year = 2003,
                PurchaseDate = new DateOnly(2026, 3, 14),
                PurchaseMileage = 76_632,
                PurchasePrice = purchasePrice,
                Seller = "Lee (private)",
                FuelType = FuelType.Petrol,
                Source = EntrySource.Web,
            },
            _ownerId,
            EntrySource.Web);
        return vehicle.Id;
    }

    [Fact]
    public async Task Creating_a_vehicle_with_a_price_mirrors_it_into_one_purchase_expense()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 AAA", 1_700m);

        await using var context = NewContext();
        var expense = await context.ExpenseEntries.SingleAsync(e => e.VehicleId == vehicleId && e.IsVehiclePurchase);

        Assert.Equal("Purchase", expense.Category);
        Assert.Equal(1_700m, expense.Amount);
        // Dated the day the car arrived, not the day the row was written — otherwise it lands in the wrong
        // month of spend and, for a car entered long after purchase, outside "since purchase" entirely.
        Assert.Equal(new DateOnly(2026, 3, 14), expense.EntryDate);
        Assert.Equal("Lee (private)", expense.Vendor);
        // The same odometer the founding MileageReading carries, so the expense log and the mileage log agree
        // about the day the car arrived.
        Assert.Equal(76_632, expense.Mileage);
    }

    [Fact]
    public async Task Creating_a_vehicle_without_a_price_mirrors_nothing()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 BBB", null);

        await using var context = NewContext();
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.VehicleId == vehicleId));
    }

    [Fact]
    public async Task The_price_reaches_the_derived_figures_and_splits_them_from_running_cost()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 CCC", 1_700m);

        await using var context = NewContext();
        await new ExpenseService(context, new AnomalyScanner(context, new VehicleMetricsLoader(context), _clock, new Clock(_clock))).AddAsync(
            vehicleId,
            // Carries a mileage, so it writes an odometer reading too — without one the car has covered no
            // miles since purchase and both cost-per-mile figures are null by design, which would make the
            // comparison below pass for the wrong reason.
            new ExpenseInput(new DateOnly(2026, 5, 1), "Service", 300m, Vendor: "Bob's", Mileage: 80_712),
            EntrySource.Web);

        var metrics = new DerivedMetricsService(new VehicleMetricsLoader(context), new Clock(_clock));
        var summary = await metrics.GetVehicleSummaryAsync(vehicleId);

        // The four fields are four fields again. Before the mirror these pairs were equal on every vehicle the
        // app created, and the dashboard reported a "total since purchase" that omitted the purchase.
        Assert.Equal(2_000m, summary!.Spend.TotalSincePurchase);
        Assert.Equal(300m, summary.Spend.TotalSincePurchaseExcludingPurchase);
        Assert.NotEqual(summary.Spend.CostPerMile, summary.Spend.CostPerMileExcludingPurchase);
        Assert.NotEqual(summary.Spend.MonthlyAverage, summary.Spend.MonthlyAverageExcludingPurchase);
    }

    [Fact]
    public async Task Correcting_the_price_moves_the_mirror_with_it()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 DDD", 1_700m);

        await using var context = NewContext();
        // A typo in the price used to be permanent — purchase price was create-only — and cosmetic. It is
        // neither now: it moves total outlay and cost-per-mile, so it has to be correctable.
        var result = await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(PurchasePrice: 1_850m));
        Assert.Equal(WriteStatus.Updated, result.Status);

        await using var reader = NewContext();
        var expense = await reader.ExpenseEntries.SingleAsync(e => e.VehicleId == vehicleId && e.IsVehiclePurchase);
        Assert.Equal(1_850m, expense.Amount);
        Assert.Equal(1_850m, result.Value!.Spend.TotalSincePurchase);
    }

    [Fact]
    public async Task A_patch_that_does_not_mention_the_price_leaves_the_mirror_alone()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 EEE", 1_700m);

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(Colour: "Blenheim Silver"));
        Assert.Equal(WriteStatus.Updated, result.Status);

        await using var reader = NewContext();
        // Still exactly one, still £1,700. The merge treats an omitted field as "leave it", so syncing on every
        // patch must be a no-op for the many edits that never touch the price.
        var expense = await reader.ExpenseEntries.SingleAsync(e => e.VehicleId == vehicleId && e.IsVehiclePurchase);
        Assert.Equal(1_700m, expense.Amount);
    }

    [Fact]
    public async Task A_negative_price_is_refused_rather_than_pulling_the_total_down()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 FFF", 1_700m);

        await using var context = NewContext();
        var result = await NewService(context).ApplyAsync(vehicleId, new VehiclePatch(PurchasePrice: -50m));

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Contains("purchasePrice", result.Errors!.Keys);
    }

    [Fact]
    public async Task A_second_purchase_row_is_refused_by_the_database()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 GGG", 1_700m);

        await using var context = NewContext();
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId,
            EntryDate = new DateOnly(2026, 3, 14),
            Category = "Purchase",
            Amount = 1_700m,
            IsVehiclePurchase = true,
            Source = EntrySource.Web,
        });

        // The partial unique index is the backstop under the write-path guards: one purchase per vehicle, so no
        // code path — present or future — can double the largest line in the log.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Typing_a_purchase_expense_by_hand_is_refused_like_fuel_is()
    {
        var vehicleId = await SeedVehicleAsync("VPM1 HHH", 1_700m);

        await using var context = NewContext();
        var result = await new ExpenseService(context, new AnomalyScanner(context, new VehicleMetricsLoader(context), _clock, new Clock(_clock))).AddAsync(
            vehicleId,
            new ExpenseInput(new DateOnly(2026, 3, 14), "Purchase", 1_700m, Vendor: "Lee"),
            EntrySource.Web);

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Contains("Category", result.Errors!.Keys);
    }
}
