using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LogEntityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(_connectionString)
                .Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_schema");
        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Inserts a vehicle (and the Fuel category on first call) and returns the vehicle id.</summary>
    private async Task<int> SeedVehicleAsync(string registration)
    {
        await using var context = NewContext();

        if (!await context.ExpenseCategories.AnyAsync(c => c.Name == "Fuel"))
        {
            context.ExpenseCategories.Add(new ExpenseCategory { Name = "Fuel", DisplayOrder = 1, IsSystem = true });
        }

        var vehicle = new Vehicle
        {
            Registration = registration,
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle.Id;
    }

    private static FuelEntry NewFill(int vehicleId, int mileage) => new()
    {
        VehicleId = vehicleId,
        EntryDate = new DateOnly(2026, 7, 1),
        Mileage = mileage,
        Litres = 45.230m,
        PricePerLitre = 1.489m,
        TotalCost = 67.35m,
        FillLevel = FillLevel.Full,
        Source = EntrySource.Web,
    };

    private static ExpenseEntry MirrorOf(FuelEntry fill) => new()
    {
        VehicleId = fill.VehicleId,
        EntryDate = fill.EntryDate,
        Category = "Fuel",
        Amount = fill.TotalCost,
        Mileage = fill.Mileage,
        FuelEntryId = fill.Id,
        Source = fill.Source,
    };

    [Fact]
    public async Task A_fill_can_mirror_to_at_most_one_expense()
    {
        var vehicleId = await SeedVehicleAsync("LG1 AAA");

        int fillId;
        await using (var context = NewContext())
        {
            var fill = NewFill(vehicleId, 80_000);
            context.FuelEntries.Add(fill);
            await context.SaveChangesAsync();

            context.ExpenseEntries.Add(MirrorOf(fill));
            await context.SaveChangesAsync();
            fillId = fill.Id;
        }

        await using (var context = NewContext())
        {
            context.ExpenseEntries.Add(new ExpenseEntry
            {
                VehicleId = vehicleId,
                EntryDate = new DateOnly(2026, 7, 2),
                Category = "Fuel",
                Amount = 1m,
                FuelEntryId = fillId,
                Source = EntrySource.Web,
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Deleting_a_fill_removes_its_mirrored_expense()
    {
        var vehicleId = await SeedVehicleAsync("LG2 BBB");

        int fillId;
        await using (var context = NewContext())
        {
            var fill = NewFill(vehicleId, 80_100);
            context.FuelEntries.Add(fill);
            await context.SaveChangesAsync();
            context.ExpenseEntries.Add(MirrorOf(fill));
            await context.SaveChangesAsync();
            fillId = fill.Id;
        }

        await using (var context = NewContext())
        {
            context.FuelEntries.Remove(await context.FuelEntries.SingleAsync(f => f.Id == fillId));
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            Assert.False(await reader.ExpenseEntries.AnyAsync(e => e.FuelEntryId == fillId));
        }
    }

    [Fact]
    public async Task A_non_monotonic_mileage_reading_inserts_without_error()
    {
        var vehicleId = await SeedVehicleAsync("LG3 CCC");

        await using var context = NewContext();
        // Later date, lower mileage — wrong, but README §5.3 says flag it, never reject it.
        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 7, 8),
            Mileage = 80_712,
            Origin = MileageOrigin.Service,
            Source = EntrySource.Import,
        });
        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = new DateOnly(2026, 7, 10),
            Mileage = 79_000,
            Origin = MileageOrigin.Manual,
            Source = EntrySource.Import,
        });

        Assert.Equal(2, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_83000_mile_service_row_inserts_without_error()
    {
        var vehicleId = await SeedVehicleAsync("LG4 DDD");

        await using var context = NewContext();
        // The workbook's known-bad row: 27 Jun 2026 at 83,000 mi, above the current 80,712.
        context.ServiceRecords.Add(new ServiceRecord
        {
            VehicleId = vehicleId,
            ServiceDate = new DateOnly(2026, 6, 27),
            Mileage = 83_000,
            Type = "Service",
            Source = EntrySource.Import,
        });

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task No_log_table_carries_a_derived_column()
    {
        await using var context = NewContext();

        var forbidden = new[] { "mpg", "l_per_100km", "miles_since_last", "running_total" };
        var logTables = new[]
        {
            "mileage_readings", "fuel_entries", "expense_entries",
            "service_records", "tyre_readings", "wash_entries",
        };

        var offending = await context.Database
            .SqlQuery<string>($@"
                SELECT table_name || '.' || column_name AS ""Value""
                FROM information_schema.columns
                WHERE table_name = ANY({logTables}) AND column_name = ANY({forbidden})")
            .ToListAsync();

        Assert.Empty(offending);
    }

    [Fact]
    public async Task A_category_still_referenced_by_expenses_cannot_be_deleted()
    {
        var vehicleId = await SeedVehicleAsync("LG5 EEE");

        await using (var context = NewContext())
        {
            context.ExpenseEntries.Add(new ExpenseEntry
            {
                VehicleId = vehicleId,
                EntryDate = new DateOnly(2026, 7, 3),
                Category = "Fuel",
                Amount = 10m,
                Source = EntrySource.Web,
            });
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            context.ExpenseCategories.Remove(await context.ExpenseCategories.SingleAsync(c => c.Name == "Fuel"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }
}
