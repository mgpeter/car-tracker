using CarTracker.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Asserts what a freshly migrated database contains.
/// </summary>
/// <remarks>
/// Uses its own database, not the shared <c>cartracker_schema</c> one: the other test classes insert
/// vehicles into that, and "the vehicles table is empty" must be a statement about the migration rather
/// than a race with whichever class ran first.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MigrationAndSeedTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(_connectionString)
                .Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_seed");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_seeds_exactly_the_thirteen_system_expense_categories()
    {
        await using var context = NewContext();

        var categories = await context.ExpenseCategories.OrderBy(c => c.DisplayOrder).ToListAsync();

        Assert.Equal(13, categories.Count);
        Assert.All(categories, c => Assert.True(c.IsSystem));
        Assert.Equal(
            ["Fuel", "Service", "Repair", "Parts", "Insurance", "Tax", "MOT", "Wash",
             "Parking", "Tools/Equipment", "Breakdown", "Purchase", "Misc"],
            categories.Select(c => c.Name));
    }

    [Fact]
    public async Task Migration_seeds_no_vehicle()
    {
        // DEC-007: vehicles arrive via the importer or the add-car flow. A seeded vehicle would collide
        // with the importer on the registration index.
        await using var context = NewContext();

        Assert.False(await context.Vehicles.AnyAsync());
    }

    [Fact]
    public async Task Migration_seeds_nothing_scoped_to_a_vehicle()
    {
        await using var context = NewContext();

        Assert.False(await context.CheckDefinitions.AnyAsync());
        Assert.False(await context.Garages.AnyAsync());
        Assert.False(await context.WashLocations.AnyAsync());
        Assert.False(await context.MileageReadings.AnyAsync());
        Assert.False(await context.FuelEntries.AnyAsync());
        Assert.False(await context.ExpenseEntries.AnyAsync());
    }

    [Fact]
    public void The_seed_constant_matches_README_section_2()
    {
        Assert.Equal(13, ExpenseCategoryConfiguration.SystemCategories.Length);
        Assert.Contains(ExpenseCategoryConfiguration.SystemCategories, c => c.Name == "Fuel");
    }

    [Fact]
    public async Task No_table_anywhere_carries_a_derived_column()
    {
        await using var context = NewContext();

        // The names the workbook stored and got wrong, plus the ones the domain must always compute.
        var forbidden = new[]
        {
            "mpg", "l_per_100km", "litres_per_100km", "miles_since_last", "running_total",
            "total_litres", "current_mileage", "miles_since_purchase", "cost_per_mile",
            "days_to_renewal", "ytd_actual", "percent_used", "avg_price_per_litre", "next_due",
        };

        var offending = await context.Database
            .SqlQuery<string>($@"
                SELECT table_name || '.' || column_name AS ""Value""
                FROM information_schema.columns
                WHERE table_schema = 'public' AND column_name = ANY({forbidden})
                ORDER BY 1")
            .ToListAsync();

        Assert.Empty(offending);
    }

    [Fact]
    public async Task Migration_produces_every_entity_table()
    {
        await using var context = NewContext();

        var tables = await context.Database
            .SqlQuery<string>($@"
                SELECT table_name AS ""Value"" FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'")
            .ToListAsync();

        string[] expected =
        [
            "vehicles", "expense_categories", "garages", "wash_locations",
            "mileage_readings", "fuel_entries", "expense_entries", "service_records",
            "tyre_readings", "wash_entries", "check_definitions", "check_logs",
            "maintenance_tasks", "budget_categories", "issues", "equipment_items", "documents",
        ];

        Assert.Empty(expected.Except(tables));
    }
}
