using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The <c>AddPerOwnerReferenceLists</c> backfill, run against a database that already holds data — the case a
/// freshly migrated database cannot reach and the only one where the migration deletes anything.
/// </summary>
/// <remarks>
/// <para>
/// The spec asked for this to be verified against a restored production dump. That is not something a test can
/// do, so the migration carries the precondition instead — it refuses to run above one account — and this class
/// exercises both sides of it: the one-account backfill in full, and the refusal.
/// </para>
/// <para>
/// Each test migrates to the migration <i>before</i> the one under test, seeds through the old schema, and then
/// migrates the rest of the way. The reference tables are seeded with raw SQL because the EF model has moved on
/// — it knows about <c>owner_id</c>, which does not exist yet at that point. Everything else uses EF, because
/// none of those tables change.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class PerOwnerReferenceListBackfillTests(PostgresFixture postgres)
{
    /// <summary>The migration immediately before <c>AddPerOwnerReferenceLists</c>.</summary>
    private const string Before = "AddFutureDatedAnomalyKind";

    private static CarTrackerDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(connectionString).Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero)));

    private static Task MigrateToAsync(CarTrackerDbContext context, string target) =>
        context.Database.GetService<IMigrator>().MigrateAsync(target);

    [Fact]
    public async Task One_account_takes_ownership_of_every_reference_row_and_no_child_row_moves()
    {
        var connectionString = await postgres.EnsureDatabaseAsync("cartracker_refbackfill_one");

        int ownerId, vehicleId;
        await using (var old = NewContext(connectionString))
        {
            await MigrateToAsync(old, Before);

            var user = new User
            {
                ExternalId = "auth0|backfill",
                Email = "backfill@example.test",
                CreatedAt = DateTimeOffset.UnixEpoch,
            };
            old.Users.Add(user);
            await old.SaveChangesAsync();
            ownerId = user.Id;

            // Raw SQL: at this migration the reference tables have no owner_id for EF to write. The 13 expense
            // categories are already here, put there by InitialSchema's HasData.
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO garages (name, contact) VALUES ('K & P Motors', '01234 567890')");
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO wash_locations (name) VALUES ('Home driveway')");
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO expense_categories (name, display_order, is_system) VALUES ('Detailing', 20, false)");

            var vehicle = new Vehicle
            {
                Registration = "BF53 AKJ", Make = "Land Rover", Model = "Freelander 1", Year = 2003,
                PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, FuelType = FuelType.Petrol,
                Source = EntrySource.Web, OwnerId = ownerId, DefaultGarage = "K & P Motors",
            };
            old.Vehicles.Add(vehicle);
            await old.SaveChangesAsync();
            vehicleId = vehicle.Id;

            old.ServiceRecords.Add(new ServiceRecord
            {
                VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 7, 8), Mileage = 80_705, Type = "MOT",
                Garage = "K & P Motors", Source = EntrySource.Web,
            });
            old.MaintenanceTasks.Add(new MaintenanceTask
            {
                VehicleId = vehicleId, Kind = MaintenanceTaskKind.Workshop, Priority = Priority.Medium,
                Title = "Cambelt", Status = MaintenanceTaskStatus.Open, AssignedGarage = "K & P Motors",
                Source = EntrySource.Web,
            });
            old.WashEntries.Add(new WashEntry
            {
                VehicleId = vehicleId, WashDate = new DateOnly(2026, 7, 1), Location = "Home driveway",
                Source = EntrySource.Web,
            });
            old.ExpenseEntries.Add(new ExpenseEntry
            {
                VehicleId = vehicleId, EntryDate = new DateOnly(2026, 7, 1), Category = "Detailing",
                Amount = 40m, Source = EntrySource.Web,
            });

            var group = new BudgetGroup { VehicleId = vehicleId, Name = "Cleaning", DisplayOrder = 1, Source = EntrySource.Web };
            group.Categories.Add(new BudgetGroupCategory { VehicleId = vehicleId, Category = "Detailing" });
            old.BudgetGroups.Add(group);

            await old.SaveChangesAsync();
        }

        await using (var migrate = NewContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await using var reader = NewContext(connectionString);

        // Every reference row belongs to the one account, and none is left ownerless — the copy ran and the
        // originals went. 14 categories: the 13 that were seed data plus the hand-added one.
        Assert.Equal(
            [(ownerId, "K & P Motors")],
            await reader.Garages.OrderBy(g => g.Name).Select(g => new ValueTuple<int, string>(g.OwnerId, g.Name)).ToListAsync());
        Assert.Equal(
            [(ownerId, "Home driveway")],
            await reader.WashLocations.Select(w => new ValueTuple<int, string>(w.OwnerId, w.Name)).ToListAsync());
        Assert.Equal(14, await reader.ExpenseCategories.CountAsync(c => c.OwnerId == ownerId));
        Assert.Equal(14, await reader.ExpenseCategories.CountAsync());

        // The contact survived the copy: this is a row move, not a re-creation from the name alone.
        Assert.Equal("01234 567890", (await reader.Garages.SingleAsync()).Contact);

        // And the point of the whole shape — not one child row was touched. Their columns still hold the same
        // names, and nothing was blanked by a SetNull on the way through.
        Assert.Equal("K & P Motors", (await reader.Vehicles.IgnoreQueryFilters().SingleAsync(v => v.Id == vehicleId)).DefaultGarage);
        Assert.Equal("K & P Motors", (await reader.ServiceRecords.SingleAsync()).Garage);
        Assert.Equal("K & P Motors", (await reader.MaintenanceTasks.SingleAsync()).AssignedGarage);
        Assert.Equal("Home driveway", (await reader.WashEntries.SingleAsync()).Location);
        Assert.Equal("Detailing", (await reader.ExpenseEntries.SingleAsync()).Category);
        Assert.Equal("Detailing", (await reader.BudgetGroupCategories.SingleAsync()).Category);
    }

    [Fact]
    public async Task Two_accounts_abort_the_migration_rather_than_hand_each_the_others_lists()
    {
        var connectionString = await postgres.EnsureDatabaseAsync("cartracker_refbackfill_two");

        await using (var old = NewContext(connectionString))
        {
            await MigrateToAsync(old, Before);

            old.Users.AddRange(
                new User { ExternalId = "auth0|abort-A", Email = "a@example.test", CreatedAt = DateTimeOffset.UnixEpoch },
                new User { ExternalId = "auth0|abort-B", Email = "b@example.test", CreatedAt = DateTimeOffset.UnixEpoch });
            await old.SaveChangesAsync();

            await old.Database.ExecuteSqlRawAsync("INSERT INTO garages (name, address) VALUES ('K & P Motors', '12 Somewhere Lane')");
        }

        await using (var migrate = NewContext(connectionString))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => migrate.Database.MigrateAsync());
            Assert.Contains("refuses to run", error.Message + error.InnerException?.Message);
        }

        // It aborted before touching anything: the garage is still there, still ownerless, and still the only
        // row. A half-applied migration here would be worse than none.
        await using var reader = NewContext(connectionString);
        var rows = await reader.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM garages").SingleAsync();
        Assert.Equal(1, rows);
        Assert.Equal(Before, (await reader.Database.GetAppliedMigrationsAsync()).Last()[15..]);
    }
}
