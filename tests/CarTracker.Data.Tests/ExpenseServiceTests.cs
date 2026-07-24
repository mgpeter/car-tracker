using CarTracker.Domain;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The shared expense read + add path (spec §5, DEC-014) — the code both the REST endpoint and the MCP
/// <c>log_expense</c> tool call. The Fuel-category refusal and the odometer shadow are the invariants that used
/// to live inline in the endpoint; extracting them here is what lets the assistant reuse them instead of forking.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExpenseServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _clock);

    private ExpenseService NewService(CarTrackerDbContext context) =>
        new(context, new AnomalyScanner(context, new VehicleMetricsLoader(context), _clock));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_expenseservice");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedVehicleAsync(string registration)
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
                FuelType = FuelType.Petrol,
                Source = EntrySource.Web,
            },
            _ownerId,
            EntrySource.Web);
        return vehicle.Id;
    }

    [Fact]
    public async Task Add_refuses_the_Fuel_category()
    {
        var vehicleId = await SeedVehicleAsync("EXP1 AAA");

        await using var context = NewContext();
        var result = await NewService(context).AddAsync(
            vehicleId,
            new ExpenseInput(new DateOnly(2026, 7, 20), "Fuel", 50m),
            EntrySource.Mcp);

        // The invariant that keeps the fuel log and the fuel-category total equal by construction.
        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.NotNull(result.Errors);
        Assert.True(result.Errors!.ContainsKey("Category"));
        Assert.Equal(0, await context.ExpenseEntries.CountAsync(e => e.VehicleId == vehicleId));
    }

    [Fact]
    public async Task Add_rejects_an_unknown_category()
    {
        var vehicleId = await SeedVehicleAsync("EXP2 BBB");

        await using var context = NewContext();
        var result = await NewService(context).AddAsync(
            vehicleId,
            new ExpenseInput(new DateOnly(2026, 7, 20), "Nonsense", 12m),
            EntrySource.Mcp);

        Assert.Equal(WriteStatus.Validation, result.Status);
    }

    [Fact]
    public async Task Add_with_a_mileage_writes_a_manual_odometer_shadow_stamped_with_the_source()
    {
        var vehicleId = await SeedVehicleAsync("EXP3 CCC");

        await using (var context = NewContext())
        {
            var result = await NewService(context).AddAsync(
                vehicleId,
                new ExpenseInput(new DateOnly(2026, 7, 20), "Tax", 165m, Vendor: "DVLA", Mileage: 80_705),
                EntrySource.Mcp);

            Assert.Equal(WriteStatus.Created, result.Status);
        }

        await using (var reader = NewContext())
        {
            var expense = await reader.ExpenseEntries.SingleAsync(e => e.VehicleId == vehicleId);
            Assert.Equal(EntrySource.Mcp, expense.Source);

            // An expense that carries a mileage is an odometer reading too — the same rule a fill follows — and
            // the reading inherits the MCP provenance so the audit trail knows which surface entered it.
            var shadow = await reader.MileageReadings
                .SingleAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Manual);
            Assert.Equal(80_705, shadow.Mileage);
            Assert.Equal(EntrySource.Mcp, shadow.Source);
        }
    }

    [Fact]
    public async Task List_returns_entries_newest_first()
    {
        var vehicleId = await SeedVehicleAsync("EXP4 DDD");

        await using (var context = NewContext())
        {
            var service = NewService(context);
            await service.AddAsync(vehicleId, new ExpenseInput(new DateOnly(2026, 5, 1), "Tax", 10m), EntrySource.Web);
            await service.AddAsync(vehicleId, new ExpenseInput(new DateOnly(2026, 7, 1), "Repair", 20m), EntrySource.Web);
        }

        await using (var reader = NewContext())
        {
            List<ExpenseItem> rows = await NewService(reader).ListAsync(vehicleId);
            Assert.Equal(2, rows.Count);
            Assert.Equal(new DateOnly(2026, 7, 1), rows[0].EntryDate);
            Assert.Equal(new DateOnly(2026, 5, 1), rows[1].EntryDate);
        }
    }
}
