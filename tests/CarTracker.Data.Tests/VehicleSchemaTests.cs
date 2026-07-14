using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class VehicleSchemaTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Registration_uniqueness_ignores_case_and_spacing()
    {
        await using (var context = NewContext())
        {
            context.Vehicles.Add(NewVehicle("BT53 AKJ"));
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            context.Vehicles.Add(NewVehicle("bt53akj"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Vehicles_table_has_the_seed_column_but_no_stored_mot_expiry()
    {
        await using var context = NewContext();

        var columns = await context.Database
            .SqlQuery<string>($"SELECT column_name FROM information_schema.columns WHERE table_name = 'vehicles'")
            .ToListAsync();

        Assert.Contains("mot_expiry_seed", columns);
        Assert.DoesNotContain("mot_expiry", columns);
    }

    [Fact]
    public async Task At_most_one_vehicle_can_be_the_default()
    {
        await using (var context = NewContext())
        {
            var first = NewVehicle("D1 AAA");
            first.IsDefault = true;
            context.Vehicles.Add(first);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var second = NewVehicle("D2 BBB");
            second.IsDefault = true;
            context.Vehicles.Add(second);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Zero_default_vehicles_is_legal()
    {
        await using var context = NewContext();
        context.Vehicles.Add(NewVehicle("ND1 CCC"));
        context.Vehicles.Add(NewVehicle("ND2 DDD"));

        var written = await context.SaveChangesAsync();

        Assert.Equal(2, written);
    }

    [Fact]
    public async Task Status_check_constraint_rejects_values_outside_the_enum()
    {
        await using (var context = NewContext())
        {
            context.Vehicles.Add(NewVehicle("ST1 EEE"));
            await context.SaveChangesAsync();
        }

        // The CLR enum cannot express an invalid status, so prove the constraint at the SQL level —
        // it must hold against hand-written SQL and future non-EF writers too.
        await using (var context = NewContext())
        {
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                context.Database.ExecuteSqlAsync(
                    $"UPDATE vehicles SET status = 'Scrapped' WHERE registration = 'ST1 EEE'"));
        }
    }

    [Fact]
    public async Task Vehicle_round_trips_with_owned_blocks()
    {
        int id;

        await using (var context = NewContext())
        {
            var vehicle = NewVehicle("RT1 FFF");
            vehicle.Fluids.CoolantSpec = "OAT (red/pink) — never IAT";
            vehicle.Tyres.PressureFrontPsi = 26.0m;
            vehicle.Insurance.Insurer = "Admiral";
            vehicle.Breakdown.Provider = "RAC";
            context.Vehicles.Add(vehicle);
            await context.SaveChangesAsync();
            id = vehicle.Id;
        }

        await using (var reader = NewContext())
        {
            var reloaded = await reader.Vehicles.SingleAsync(v => v.Id == id);

            Assert.Equal("OAT (red/pink) — never IAT", reloaded.Fluids.CoolantSpec);
            Assert.Equal(26.0m, reloaded.Tyres.PressureFrontPsi);
            Assert.Equal("Admiral", reloaded.Insurance.Insurer);
            Assert.Equal("RAC", reloaded.Breakdown.Provider);
            Assert.Equal(VehicleStatus.Active, reloaded.Status);
        }
    }
}
