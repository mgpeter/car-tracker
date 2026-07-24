using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Per-user isolation, tested at the layer that enforces it: <see cref="CarTrackerDbContext"/>'s vehicle query
/// filter. Every web and MCP endpoint resolves a vehicle through this context, so a filter that hides another
/// owner's vehicle makes their entire data chain unreachable — a cross-user read 404s because the vehicle never
/// resolves, and a write cannot target a vehicle it cannot see.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OwnershipTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _clock, accessor);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_ownership");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static CurrentUserAccessor As(int ownerId)
    {
        var accessor = new CurrentUserAccessor();
        accessor.SetOwner(ownerId);
        return accessor;
    }

    private static Vehicle NewVehicle(string registration) => new()
    {
        Registration = registration,
        Make = "Land Rover",
        Model = "Freelander",
        Year = 2003,
        PurchaseDate = new DateOnly(2026, 3, 14),
        PurchaseMileage = 76_632,
        FuelType = FuelType.Petrol,
        Source = EntrySource.Web,
    };

    [Fact]
    public async Task A_user_sees_only_their_own_vehicles()
    {
        int ownerA, ownerB;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|iso-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|iso-B");
            await new VehicleFactory(seed).CreateAsync(NewVehicle("AA11 AAA"), ownerA, EntrySource.Web, CheckSource.None);
            await new VehicleFactory(seed).CreateAsync(NewVehicle("BB22 BBB"), ownerB, EntrySource.Web, CheckSource.None);
        }

        await using var asA = NewContext(As(ownerA));

        // A's garage is exactly A's vehicle; B's plate does not resolve at all — the 404 case.
        Assert.Equal(["AA11 AAA"], await asA.Vehicles.Select(v => v.Registration).ToListAsync());
        Assert.False(await asA.Vehicles.AnyAsync(v => v.Registration == "BB22 BBB"));
    }

    [Fact]
    public async Task A_child_row_is_unreachable_across_users_because_its_vehicle_does_not_resolve()
    {
        int ownerA, ownerB, bVehicleId;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|child-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|child-B");
            await new VehicleFactory(seed).CreateAsync(NewVehicle("CA11 AAA"), ownerA, EntrySource.Web, CheckSource.None);
            var b = await new VehicleFactory(seed).CreateAsync(NewVehicle("CB22 BBB"), ownerB, EntrySource.Web, CheckSource.None);
            bVehicleId = b.Id;
        }

        // The child (the opening odometer reading) carries only a VehicleId. A malicious A who knows B's vehicle
        // id cannot use it: every endpoint first resolves the vehicle, and B's vehicle does not resolve for A.
        await using var asA = NewContext(As(ownerA));
        Assert.Null(await asA.Vehicles.Where(v => v.Id == bVehicleId).Select(v => (int?)v.Id).SingleOrDefaultAsync());
    }

    [Fact]
    public async Task Two_users_can_register_the_same_plate()
    {
        await using var seed = NewContext();
        var ownerA = await TestOwner.SeedAsync(seed, "auth0|plate-A");
        var ownerB = await TestOwner.SeedAsync(seed, "auth0|plate-B");

        await new VehicleFactory(seed).CreateAsync(NewVehicle("SAME 1"), ownerA, EntrySource.Web, CheckSource.None);
        // Same normalised plate, different owner — allowed by the per-owner unique index, refused within one owner.
        await new VehicleFactory(seed).CreateAsync(NewVehicle("same1"), ownerB, EntrySource.Web, CheckSource.None);

        var shared = await seed.Vehicles.IgnoreQueryFilters()
            .CountAsync(v => EF.Property<string>(v, "RegistrationNormalized") == "SAME1");
        Assert.Equal(2, shared);
    }

    [Fact]
    public async Task Each_user_has_their_own_default_vehicle()
    {
        await using var seed = NewContext();
        var ownerA = await TestOwner.SeedAsync(seed, "auth0|def-A");
        var ownerB = await TestOwner.SeedAsync(seed, "auth0|def-B");

        var a = NewVehicle("DA11 AAA"); a.OwnerId = ownerA; a.IsDefault = true;
        var b = NewVehicle("DB22 BBB"); b.OwnerId = ownerB; b.IsDefault = true;
        seed.Vehicles.AddRange(a, b);

        // Two defaults across two owners is fine; the per-owner index only forbids two within one owner.
        Assert.Equal(2, await seed.SaveChangesAsync());
    }

    [Fact]
    public async Task A_system_context_sees_every_owner()
    {
        int ownerA;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|sys-A");
            await new VehicleFactory(seed).CreateAsync(NewVehicle("SY11 AAA"), ownerA, EntrySource.Web, CheckSource.None);
        }

        // No accessor → BypassOwnership: the background/system view (the reminders job) sees across owners.
        await using var system = NewContext();
        Assert.True(await system.Vehicles.AnyAsync(v => v.Registration == "SY11 AAA"));
    }

    [Fact]
    public async Task The_first_login_claim_adopts_an_unowned_vehicle()
    {
        int owner;
        await using (var seed = NewContext())
        {
            owner = await TestOwner.SeedAsync(seed, "auth0|claim");
            // A pre-multi-user vehicle: created directly with no owner (the founding BT53's state).
            var unowned = NewVehicle("UN11 AAA");
            unowned.OwnerId = null;
            seed.Vehicles.Add(unowned);
            await seed.SaveChangesAsync();
        }

        // The claim the first-login middleware runs: IgnoreQueryFilters because the owner is not set yet.
        await using (var claim = NewContext())
        {
            await claim.Vehicles.IgnoreQueryFilters()
                .Where(v => v.OwnerId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.OwnerId, owner));
        }

        await using var asOwner = NewContext(As(owner));
        Assert.True(await asOwner.Vehicles.AnyAsync(v => v.Registration == "UN11 AAA"));
    }
}
