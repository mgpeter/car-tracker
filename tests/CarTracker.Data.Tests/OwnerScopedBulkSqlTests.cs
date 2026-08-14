using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Task 3.4 of <c>docs/specs/2026-08-11-pre-public-release-gates</c>: the experiment that decides the shape of
/// the eight cascade methods in <c>ReferenceListEditor</c>, settled against real PostgreSQL rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// The reference cascades rewrite rows in tables that carry no owner of their own — <c>service_records</c>,
/// <c>maintenance_tasks</c>, <c>wash_entries</c>, <c>expense_entries</c>, <c>budget_group_categories</c> — and
/// the spec scopes each one by correlating through <c>Vehicles</c>, which does carry the query filter. That only
/// works if the filter survives two translations: into a <b>subquery</b>, and into the <c>UPDATE</c>/<c>DELETE</c>
/// statement <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c> build, which do not go through the change
/// tracker at all.
/// </para>
/// <para>
/// So these tests assert on the <b>emitted SQL</b>, not on the row counts. A correlated <c>EXISTS</c> that
/// translates <i>without</i> the ownership term is the dangerous outcome: it looks scoped in C#, it passes any
/// single-owner test, and it rewrites every other account's rows exactly as the unscoped statement did. Row
/// counts alone cannot tell those two apart on a database where the other owner's rows happen not to match.
/// </para>
/// <para>
/// The answer, as of EF 10 / Npgsql 10, is that it survives both — the filter lands inside the correlated
/// <c>EXISTS</c> of the bulk statement:
/// </para>
/// <code>
/// UPDATE service_records AS s
/// SET garage = @p
/// WHERE s.garage = 'K &amp; P Motors' AND EXISTS (
///     SELECT 1
///     FROM vehicles AS v
///     WHERE (@ef_filter__BypassOwnership2 OR v.owner_id = @ef_filter__CurrentOwnerId) AND v.id = s.vehicle_id)
/// </code>
/// <para>
/// Note that the bypass arrives as a <b>parameter</b>, not as a compiled-away constant: the same statement runs
/// unscoped when a context has no accessor. Every context here is therefore built with <see cref="As"/>. A
/// context with no accessor has <c>BypassOwnership == true</c>, which passes <c>true</c> for that parameter and
/// matches every row — the <c>owner_id</c> would still be in the SQL, so this class would still be green while
/// proving nothing about isolation.
/// </para>
/// <para>
/// <b>Keep this class.</b> It is the only thing that would notice a future EF or Npgsql release translating the
/// correlation without the filter. The cascades would still compile, still read as owner-scoped, and silently
/// go back to writing across accounts.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class OwnerScopedBulkSqlTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));

    private int _ownerA;
    private int _ownerB;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_ownerscopesql");
        await using var context = NewContext();
        await context.Database.MigrateAsync();

        _ownerA = await TestOwner.SeedAsync(context, "auth0|sql-A");
        _ownerB = await TestOwner.SeedAsync(context, "auth0|sql-B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null, ICollection<string>? sql = null)
    {
        var options = new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString);

        // CommandExecuted carries the statement text EF actually sent. Scoping to that one event keeps the
        // captured list to the statements under test instead of the connection and transaction chatter.
        if (sql is not null) options.LogTo(sql.Add, [RelationalEventId.CommandExecuted]);

        return new CarTrackerDbContext(options.Options, _clock, accessor);
    }

    private static CurrentUserAccessor As(int ownerId)
    {
        var accessor = new CurrentUserAccessor();
        accessor.SetOwner(ownerId);
        return accessor;
    }

    /// <summary>
    /// A vehicle and one service record naming <paramref name="garage"/>, owned by <paramref name="ownerId"/>.
    /// Added directly rather than through <c>VehicleFactory</c>: what is under test is the SQL a statement over
    /// an owned vehicle emits, and the factory's opening reading, checks and purchase mirror are noise here.
    /// </summary>
    private async Task<int> SeedRecordAsync(int ownerId, string registration, string garage)
    {
        await using var seed = NewContext();
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
            OwnerId = ownerId,
        };
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();

        seed.ServiceRecords.Add(new ServiceRecord
        {
            VehicleId = vehicle.Id,
            ServiceDate = new DateOnly(2026, 7, 8),
            Mileage = 80_705,
            Type = "MOT",
            Garage = garage,
            Source = EntrySource.Web,
        });
        await seed.SaveChangesAsync();

        return vehicle.Id;
    }

    private static string SingleStatement(IEnumerable<string> captured, string verb)
    {
        var matches = captured.Where(s => s.Contains(verb, StringComparison.Ordinal)).ToList();
        Assert.Single(matches);
        return matches[0];
    }

    [Fact]
    public async Task The_vehicle_filter_reaches_inside_an_ExecuteUpdate_through_a_correlated_subquery()
    {
        const string garage = "K & P Motors";
        await SeedRecordAsync(_ownerA, "UA11 AAA", garage);
        await SeedRecordAsync(_ownerB, "UB22 BBB", garage);

        var sql = new List<string>();
        await using (var asA = NewContext(As(_ownerA), sql))
        {
            await asA.ServiceRecords
                .Where(s => s.Garage == garage && asA.Vehicles.Any(v => v.Id == s.VehicleId))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Garage, "K & P Motors Ltd"));
        }

        var update = SingleStatement(sql, "UPDATE");

        // owner_id in the statement itself is the whole assertion. An EXISTS without it would restore the
        // cross-tenant rewrite while reading as correct in both the C# and the shape of the SQL.
        Assert.Contains("owner_id", update, StringComparison.Ordinal);
        Assert.Contains("EXISTS", update, StringComparison.Ordinal);

        // And the rows agree with the SQL: A's record renamed, B's untouched.
        await using var verify = NewContext();
        var garages = await verify.ServiceRecords
            .Where(s => s.Garage == garage || s.Garage == "K & P Motors Ltd")
            .OrderBy(s => s.Id)
            .Select(s => s.Garage)
            .ToListAsync();
        Assert.Equal(["K & P Motors Ltd", garage], garages);
    }

    [Fact]
    public async Task The_vehicle_filter_reaches_inside_an_ExecuteDelete_through_a_correlated_subquery()
    {
        const string garage = "Bridge End Garage";
        await SeedRecordAsync(_ownerA, "DA11 AAA", garage);
        await SeedRecordAsync(_ownerB, "DB22 BBB", garage);

        var sql = new List<string>();
        await using (var asA = NewContext(As(_ownerA), sql))
        {
            await asA.ServiceRecords
                .Where(s => s.Garage == garage && asA.Vehicles.Any(v => v.Id == s.VehicleId))
                .ExecuteDeleteAsync();
        }

        var delete = SingleStatement(sql, "DELETE");
        Assert.Contains("owner_id", delete, StringComparison.Ordinal);
        Assert.Contains("EXISTS", delete, StringComparison.Ordinal);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.ServiceRecords.CountAsync(s => s.Garage == garage));
    }

    [Fact]
    public async Task An_entitys_own_query_filter_reaches_inside_an_ExecuteUpdate()
    {
        // The one statement in the cascades the spec says needs no change — the garage rename's
        // context.Vehicles.Where(v => v.DefaultGarage == name).ExecuteUpdateAsync at ReferenceListEditor:125.
        // "Already filtered" is a claim about the emitted SQL, so it is asserted rather than assumed.
        const string garage = "Default Garage Test";
        var aVehicle = await SeedRecordAsync(_ownerA, "PA11 AAA", garage);
        var bVehicle = await SeedRecordAsync(_ownerB, "PB22 BBB", garage);

        await using (var seed = NewContext())
        {
            await seed.Vehicles.Where(v => v.Id == aVehicle || v.Id == bVehicle)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.DefaultGarage, garage));
        }

        var sql = new List<string>();
        await using (var asA = NewContext(As(_ownerA), sql))
        {
            await asA.Vehicles.Where(v => v.DefaultGarage == garage)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.DefaultGarage, "Default Garage Test Ltd"));
        }

        var update = SingleStatement(sql, "UPDATE");
        Assert.Contains("owner_id", update, StringComparison.Ordinal);

        await using var verify = NewContext();
        Assert.Equal(garage, await verify.Vehicles.Where(v => v.Id == bVehicle).Select(v => v.DefaultGarage).SingleAsync());
    }

    [Fact]
    public async Task An_entitys_own_query_filter_reaches_inside_an_ExecuteDelete()
    {
        // The other half the cascades depend on. Each rename and re-home ends by deleting the reference row
        // itself — context.Garages.Where(g => g.Name == name).ExecuteDeleteAsync — which is scoped by the
        // table's own filter, not by a correlation. Vehicle is the only filtered entity today, so it stands in
        // for the three the spec is about to add; if the filter did not reach here, one account's delete would
        // take another account's identically-named row with it.
        await SeedRecordAsync(_ownerA, "FA11 AAA", "Filter Test Garage");
        await SeedRecordAsync(_ownerB, "FB22 BBB", "Filter Test Garage");

        var sql = new List<string>();
        await using (var asA = NewContext(As(_ownerA), sql))
        {
            await asA.Vehicles.Where(v => v.Make == "Land Rover" && v.Registration.StartsWith("F"))
                .ExecuteDeleteAsync();
        }

        var delete = SingleStatement(sql, "DELETE");
        Assert.Contains("owner_id", delete, StringComparison.Ordinal);

        await using var verify = NewContext();
        Assert.Equal(["FB22 BBB"], await verify.Vehicles
            .Where(v => v.Registration.StartsWith("F"))
            .Select(v => v.Registration)
            .ToListAsync());
    }
}
