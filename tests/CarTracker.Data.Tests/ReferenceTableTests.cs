using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ReferenceTableTests(PostgresFixture postgres) : IAsyncLifetime
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

    [Fact]
    public async Task Reference_rows_round_trip_on_their_natural_keys()
    {
        int ownerId;
        await using (var context = NewContext())
        {
            // The 13 categories arrive with the account now, not with the migration.
            ownerId = await TestOwner.SeedAsync(context);
            context.Garages.Add(new Garage { OwnerId = ownerId, Name = "K & P Motors", Contact = "01234 567890" });
            context.WashLocations.Add(new WashLocation { OwnerId = ownerId, Name = "Home driveway" });
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            Assert.True((await reader.ExpenseCategories.SingleAsync(c => c.OwnerId == ownerId && c.Name == "Fuel")).IsSystem);
            Assert.Equal("01234 567890", (await reader.Garages.SingleAsync(g => g.OwnerId == ownerId && g.Name == "K & P Motors")).Contact);
            Assert.NotNull(await reader.WashLocations.SingleAsync(w => w.OwnerId == ownerId && w.Name == "Home driveway"));
        }
    }

    [Fact]
    public async Task Two_owners_can_each_hold_a_garage_of_the_same_name()
    {
        await using var context = NewContext();
        var ownerA = await TestOwner.SeedAsync(context, "test|ref-same-name-A");
        var ownerB = await TestOwner.SeedAsync(context, "test|ref-same-name-B");

        // The single-column key made this impossible: the second owner to type the name silently adopted the
        // first owner's row, address and contact included. Both rows exist, and each keeps its own contact.
        context.Garages.Add(new Garage { OwnerId = ownerA, Name = "K & P Motors", Contact = "01234 567890" });
        context.Garages.Add(new Garage { OwnerId = ownerB, Name = "K & P Motors", Contact = "09876 543210" });
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var rows = await reader.Garages
            .Where(g => g.Name == "K & P Motors" && (g.OwnerId == ownerA || g.OwnerId == ownerB))
            .OrderBy(g => g.OwnerId)
            .Select(g => g.Contact)
            .ToListAsync();

        Assert.Equal(["01234 567890", "09876 543210"], rows);
    }

    [Fact]
    public async Task A_garage_name_is_still_unique_within_one_owner()
    {
        await using var context = NewContext();
        var ownerId = await TestOwner.SeedAsync(context, "test|ref-dup-within-owner");

        context.Garages.Add(new Garage { OwnerId = ownerId, Name = "Duplicate Within Owner" });
        await context.SaveChangesAsync();

        await using var second = NewContext();
        second.Garages.Add(new Garage { OwnerId = ownerId, Name = "Duplicate Within Owner" });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Contains("pk_garages", error.InnerException?.Message ?? string.Empty);
    }

    [Fact]
    public async Task Garage_notes_may_be_null_but_never_empty()
    {
        await using var context = NewContext();
        var ownerId = await TestOwner.SeedAsync(context);
        context.Garages.Add(new Garage { OwnerId = ownerId, Name = "Empty Notes Garage", Notes = "" });

        // Named, not just typed: a bare ThrowsAsync<DbUpdateException> passes for any constraint at all, and
        // this row now has three it could break (the check, the composite key, the owner FK).
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains("ck_garages_notes", error.InnerException?.Message ?? string.Empty);
    }
}
