using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// What `AddChatEntrySource` guarantees in both directions: a chat-attributed row is accepted, and the rollback
/// refuses rather than quietly rewriting attribution.
/// </summary>
/// <remarks>
/// Its own database, and not negotiable: this class migrates <b>down</b>, and a shared database would take the
/// schema out from under whichever class ran next. The name is unique to it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ChatEntrySourceTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>The migration immediately before this one — the target a rollback would land on.</summary>
    private const string PreviousMigration = "AddPendingIdentityDeletions";

    private static readonly DateTimeOffset Reference = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    private string _connectionString = string.Empty;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(_connectionString)
                .Options,
            new FakeTimeProvider(Reference),
            accessor);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_chat_source");
        await using var context = NewContext();

        // Up on every run, because a test in this class may have left the database one migration behind.
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(int OwnerId, int VehicleId)> SeedChatVehicleAsync(string registration)
    {
        int ownerId;
        await using (var seed = NewContext())
        {
            ownerId = await TestOwner.SeedAsync(seed, $"test|chat-{registration}");
        }

        await using var context = NewContext(TestOwner.As(ownerId));
        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Chat,
        };

        // Through the factory, not a bare Add: the vehicle, its opening mileage reading and its check
        // definitions are all stamped, so this proves the whole create path carries the new source rather than
        // just the one column the constraint is on.
        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Chat);

        return (ownerId, vehicle.Id);
    }

    [Fact]
    public async Task A_chat_attributed_write_is_accepted_and_reads_back_as_chat()
    {
        var (ownerId, vehicleId) = await SeedChatVehicleAsync("CH11 CHT");

        await using var context = NewContext(TestOwner.As(ownerId));

        var vehicle = await context.Vehicles.SingleAsync(v => v.Id == vehicleId);
        Assert.Equal(EntrySource.Chat, vehicle.Source);

        // The opening reading the factory writes in the same transaction. A source that survived on the parent
        // and not on the child would mean the enum was threaded through one call site and not the other.
        var reading = await context.MileageReadings.SingleAsync(m => m.VehicleId == vehicleId);
        Assert.Equal(EntrySource.Chat, reading.Source);
    }

    [Fact]
    public async Task The_column_is_stored_lowercase_like_every_other_source()
    {
        var (_, vehicleId) = await SeedChatVehicleAsync("CH12 CHT");

        await using var context = NewContext();

        var stored = await context.Database
            .SqlQuery<string>($@"SELECT source AS ""Value"" FROM vehicles WHERE id = {vehicleId}")
            .SingleAsync();

        Assert.Equal("chat", stored);
    }

    [Fact]
    public async Task Rolling_back_is_refused_while_a_chat_row_exists_and_leaves_the_row_alone()
    {
        var (_, vehicleId) = await SeedChatVehicleAsync("CH13 CHT");

        await using (var context = NewContext())
        {
            // The down migration restores the four-value constraint, which this row now violates. Failing is the
            // correct outcome: the alternative is a rollback that silently rewrites real attribution to make
            // itself succeed, and the attribution is the only evidence of which surface produced the figures.
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => context.Database.MigrateAsync(PreviousMigration));

            Assert.Contains("source", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // Postgres has transactional DDL, so the failed rollback took nothing with it — schema or row.
        await using (var reader = NewContext())
        {
            var stored = await reader.Database
                .SqlQuery<string>($@"SELECT source AS ""Value"" FROM vehicles WHERE id = {vehicleId}")
                .SingleAsync();

            Assert.Equal("chat", stored);
        }
    }
}
