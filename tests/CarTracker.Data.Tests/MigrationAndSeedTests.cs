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

    // A second database, because provisioning an account writes 13 category rows and the assertions below say
    // the migrated database is empty. One database would make those two tests race each other.
    private string _accountConnectionString = string.Empty;

    private CarTrackerDbContext NewContext() => NewContext(_connectionString);

    private static CarTrackerDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(connectionString)
                .Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_seed");
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        _accountConnectionString = await postgres.EnsureDatabaseAsync("cartracker_seed_account");
        await using var account = NewContext(_accountConnectionString);
        await account.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_provisioned_account_holds_exactly_the_thirteen_system_expense_categories()
    {
        // The 13 stopped being seed data when the reference lists gained an owner: a seeded row has no owner and
        // there is no owner to invent for one. So the statement is about an account, not about the migration —
        // the same 13, in the same order, all system, but belonging to somebody.
        await using var context = NewContext(_accountConnectionString);
        var ownerId = await TestOwner.SeedAsync(context, "test|seed-owner");

        var categories = await context.ExpenseCategories
            .Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

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
    public async Task Migration_seeds_nothing_at_all()
    {
        // Expense categories joined this list when they gained an owner: the migration now creates no rows in
        // any table, and every reference list — like every check definition — arrives with an account or a car.
        await using var context = NewContext();

        Assert.False(await context.CheckDefinitions.AnyAsync());
        Assert.False(await context.ExpenseCategories.AnyAsync());
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
    public async Task The_three_reference_tables_are_keyed_by_owner_and_name()
    {
        await using var context = NewContext();

        var keyColumns = await context.Database
            .SqlQuery<string>($@"
                SELECT tc.table_name || '.' || kcu.column_name AS ""Value""
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
                WHERE tc.table_schema = 'public'
                  AND tc.constraint_type = 'PRIMARY KEY'
                  AND tc.table_name IN ('garages', 'wash_locations', 'expense_categories')
                ORDER BY tc.table_name, kcu.ordinal_position")
            .ToListAsync();

        // Owner first, deliberately: it leads the key, so the foreign key to users needs no index of its own.
        Assert.Equal(
            [
                "expense_categories.owner_id", "expense_categories.name",
                "garages.owner_id", "garages.name",
                "wash_locations.owner_id", "wash_locations.name",
            ],
            keyColumns);
    }

    [Fact]
    public async Task No_child_column_still_points_at_a_reference_list()
    {
        // The six foreign keys the per-owner reference lists dropped. They are asserted absent rather than left
        // to be noticed, because every one of them was load-bearing in a test somewhere: a SetNull the editor
        // exists to prevent (four), a Restrict it duplicates (one), and a Cascade it overrides (one). Their
        // columns are untouched and still carry the same names.
        await using var context = NewContext();

        var referencing = await context.Database
            .SqlQuery<string>($@"
                SELECT tc.table_name || '.' || kcu.column_name AS ""Value""
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
                JOIN information_schema.constraint_column_usage ccu
                  ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
                WHERE tc.table_schema = 'public'
                  AND tc.constraint_type = 'FOREIGN KEY'
                  AND ccu.table_name IN ('garages', 'wash_locations', 'expense_categories')
                ORDER BY 1")
            .ToListAsync();

        Assert.Empty(referencing);
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
            "maintenance_tasks", "budget_groups", "budget_group_categories", "issues", "equipment_items", "documents",
        ];

        Assert.Empty(expected.Except(tables));
    }
}
