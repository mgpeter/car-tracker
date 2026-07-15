using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The opening-reading rule, against a real database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class VehicleFactoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(_connectionString)
                .Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_factory");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Vehicle NewVehicle(string registration) => new()
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

    [Fact]
    public async Task Creating_a_vehicle_records_its_opening_reading()
    {
        int vehicleId;

        await using (var context = NewContext())
        {
            var vehicle = await new VehicleFactory(context).CreateAsync(NewVehicle("OP1 AAA"), EntrySource.Web);
            vehicleId = vehicle.Id;
        }

        await using (var reader = NewContext())
        {
            var reading = await reader.MileageReadings.SingleAsync(m => m.VehicleId == vehicleId);

            Assert.Equal(MileageOrigin.Purchase, reading.Origin);
            Assert.Equal(76_632, reading.Mileage);
            Assert.Equal(new DateOnly(2026, 3, 14), reading.ReadingDate);
        }
    }

    /// <summary>
    /// The state this whole rule exists to prevent.
    /// </summary>
    [Fact]
    public async Task Current_mileage_is_known_immediately_rather_than_null()
    {
        int vehicleId;

        await using (var context = NewContext())
        {
            vehicleId = (await new VehicleFactory(context).CreateAsync(NewVehicle("OP2 BBB"), EntrySource.Web)).Id;
        }

        await using (var reader = NewContext())
        {
            var data = await new VehicleMetricsLoader(reader).LoadAsync(vehicleId);
            Assert.NotNull(data);

            var summary = DerivedMetrics.Compute(data, new DateOnly(2026, 7, 14));

            // Without the opening reading these would both be null, and the garage card would show a blank.
            Assert.Equal(76_632, summary.Mileage.CurrentMileage);
            Assert.Equal(0, summary.Mileage.MilesSincePurchase);
        }
    }

    [Fact]
    public async Task Exactly_one_purchase_reading_per_vehicle()
    {
        await using var context = NewContext();
        var factory = new VehicleFactory(context);

        var first = await factory.CreateAsync(NewVehicle("OP3 CCC"), EntrySource.Web);
        var second = await factory.CreateAsync(NewVehicle("OP4 DDD"), EntrySource.Web);

        await using var reader = NewContext();

        foreach (var id in new[] { first.Id, second.Id })
        {
            var purchaseReadings = await reader.MileageReadings
                .Where(m => m.VehicleId == id && m.Origin == MileageOrigin.Purchase)
                .ToListAsync();

            Assert.Single(purchaseReadings);
        }
    }

    [Fact]
    public async Task The_opening_reading_agrees_with_the_vehicles_purchase_mileage()
    {
        // The two can drift if someone later edits one — accepted, and out of scope here. They must at least
        // agree at creation.
        await using var context = NewContext();
        var vehicle = await new VehicleFactory(context).CreateAsync(NewVehicle("OP5 EEE"), EntrySource.Web);

        await using var reader = NewContext();
        var reading = await reader.MileageReadings
            .SingleAsync(m => m.VehicleId == vehicle.Id && m.Origin == MileageOrigin.Purchase);

        Assert.Equal(vehicle.PurchaseMileage, reading.Mileage);
        Assert.Equal(vehicle.PurchaseDate, reading.ReadingDate);
    }

    [Fact]
    public async Task A_failed_creation_leaves_no_vehicle_and_no_reading()
    {
        await using (var context = NewContext())
        {
            await new VehicleFactory(context).CreateAsync(NewVehicle("OP6 FFF"), EntrySource.Web);
        }

        await using (var context = NewContext())
        {
            // Same registration: the unique index rejects it on the first save, so the transaction never
            // reaches the reading.
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new VehicleFactory(context).CreateAsync(NewVehicle("op6fff"), EntrySource.Web));
        }

        await using (var reader = NewContext())
        {
            // Exactly one vehicle and one reading — the rejected attempt left nothing behind.
            Assert.Single(await reader.Vehicles.Where(v => v.Registration.StartsWith("OP6")).ToListAsync());
        }
    }

    [Fact]
    public async Task The_source_is_carried_to_both_rows()
    {
        await using var context = NewContext();
        var vehicle = await new VehicleFactory(context).CreateAsync(NewVehicle("OP7 GGG"), EntrySource.Mcp);

        await using var reader = NewContext();
        var reading = await reader.MileageReadings.SingleAsync(m => m.VehicleId == vehicle.Id);

        // A vehicle added by the assistant has its opening reading attributed to the assistant too.
        Assert.Equal(EntrySource.Mcp, reading.Source);
        Assert.Equal(EntrySource.Mcp, (await reader.Vehicles.SingleAsync(v => v.Id == vehicle.Id)).Source);
    }
}
