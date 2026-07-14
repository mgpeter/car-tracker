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
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reference_rows_round_trip_on_their_natural_keys()
    {
        await using (var context = NewContext())
        {
            context.ExpenseCategories.Add(new ExpenseCategory { Name = "Fuel", DisplayOrder = 1, IsSystem = true });
            context.Garages.Add(new Garage { Name = "K & P Motors", Contact = "01234 567890" });
            context.WashLocations.Add(new WashLocation { Name = "Home driveway" });
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            Assert.True((await reader.ExpenseCategories.SingleAsync(c => c.Name == "Fuel")).IsSystem);
            Assert.Equal("01234 567890", (await reader.Garages.SingleAsync(g => g.Name == "K & P Motors")).Contact);
            Assert.NotNull(await reader.WashLocations.SingleAsync(w => w.Name == "Home driveway"));
        }
    }

    [Fact]
    public async Task Garage_notes_may_be_null_but_never_empty()
    {
        await using var context = NewContext();
        context.Garages.Add(new Garage { Name = "Empty Notes Garage", Notes = "" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
