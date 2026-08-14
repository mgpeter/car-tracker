using CarTracker.Domain;
using CarTracker.Domain.Logs;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The four raw reads that exist for the account export — fuel entries, check logs, budget groups and the
/// issue-watch links — against a real database.
/// </summary>
/// <remarks>
/// <para>
/// Two of them reach their rows through another table's ids, because neither <c>check_logs</c> nor
/// <c>issue_watch_checks</c> carries a vehicle column. That scoping is the claim worth testing: a second
/// vehicle's rows must not appear, and only a real database with real foreign keys makes the check meaningful.
/// </para>
/// <para>
/// The other claim is what is <b>absent</b>. These rows carry no MPG, no YTD actual, no watch status — an export
/// that shipped a stored derived figure would reproduce the exact defect the five workbook figures document, in
/// the one artefact nobody can recompute later.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class RawRowQueryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock);

    private static LogQueryService NewQueries(CarTrackerDbContext context) => new(context, new Clock(Clock));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_rawrows");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> NewVehicleAsync(CarTrackerDbContext context, string registration)
    {
        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        // CheckSource.None: these tests name their own checks, and the generic fifteen would just be noise.
        // The four default budget groups arrive regardless — they are what the budget read below asserts on.
        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    private static async Task<int> NewCheckAsync(CarTrackerDbContext context, int vehicleId, string name)
    {
        var definition = new CheckDefinition
        {
            VehicleId = vehicleId, Name = name, CadenceLabel = "Weekly", IntervalDays = 7,
            DisplayOrder = 1, IsActive = true, Source = EntrySource.Web,
        };
        context.CheckDefinitions.Add(definition);
        await context.SaveChangesAsync();
        return definition.Id;
    }

    [Fact]
    public async Task Fills_come_back_oldest_first_exactly_as_stored()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "RAW 001");

        // Entered out of order, so the ordering is the query's doing and not the insert's.
        context.FuelEntries.Add(new FuelEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 4, 2), Mileage = 77_881,
            Litres = 44.02m, PricePerLitre = 1.599m, TotalCost = 70.39m, Station = "Applegreen",
            FillLevel = FillLevel.Full, Source = EntrySource.Web,
        });
        context.FuelEntries.Add(new FuelEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 3, 20), Mileage = 77_537,
            Litres = 21.00m, PricePerLitre = 1.579m, TotalCost = 33.16m,
            FillLevel = FillLevel.Half, Source = EntrySource.Mcp,
        });
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var fills = await NewQueries(reader).ListFuelAsync(vehicleId);

        Assert.Equal([new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 2)], fills.Select(f => f.EntryDate));

        var partial = fills[0];
        Assert.Equal(77_537, partial.Mileage);
        Assert.Equal(21.00m, partial.Litres);
        Assert.Equal(1.579m, partial.PricePerLitre);
        Assert.Equal(33.16m, partial.TotalCost);
        Assert.Null(partial.Station);
        // The half fill survives as a half fill: it is what defers MPG to the next fill to full, and an export
        // that flattened it would make the segment un-recomputable.
        Assert.Equal(FillLevel.Half, partial.FillLevel);

        Assert.Equal("Applegreen", fills[1].Station);
        Assert.Equal(FillLevel.Full, fills[1].FillLevel);
    }

    [Fact]
    public async Task Check_logs_are_scoped_through_the_definitions_and_stop_at_the_vehicle()
    {
        await using var context = NewContext();
        var mine = await NewVehicleAsync(context, "RAW 002");
        var theirs = await NewVehicleAsync(context, "RAW 003");

        var myCheck = await NewCheckAsync(context, mine, "Oil filler cap underside");
        var theirCheck = await NewCheckAsync(context, theirs, "Oil filler cap underside");

        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = myCheck, PerformedOn = new DateOnly(2026, 6, 18),
            Result = CheckResult.Attention, Notes = "Mayonnaise on the cap", Source = EntrySource.Web,
        });
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = myCheck, PerformedOn = new DateOnly(2026, 6, 25), Source = EntrySource.Web,
        });
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = theirCheck, PerformedOn = new DateOnly(2026, 6, 18),
            Result = CheckResult.OK, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var logs = await NewQueries(reader).ListCheckLogsAsync(mine);

        Assert.All(logs, l => Assert.Equal(myCheck, l.CheckDefinitionId));
        Assert.Equal([new DateOnly(2026, 6, 18), new DateOnly(2026, 6, 25)], logs.Select(l => l.PerformedOn));
        Assert.Equal(CheckResult.Attention, logs[0].Result);
        Assert.Equal("Mayonnaise on the cap", logs[0].Notes);
        // Null verdict is not OK — a log recorded before verdicts were surfaced says "performed, nothing
        // reported", and the export has to keep the two distinguishable.
        Assert.Null(logs[1].Result);
    }

    [Fact]
    public async Task Budget_groups_come_back_with_their_memberships_and_no_spend()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "RAW 004");

        await using var reader = NewContext();
        var groups = await NewQueries(reader).ListBudgetGroupsAsync(vehicleId);

        Assert.Equal(
            ["Fuel", "Service & Repairs", "Insurance, Tax & MOT", "Equipment & Tools"],
            groups.Select(g => g.Name));
        Assert.Equal(["Parts", "Repair", "Service"], groups[1].Categories);
        // Seeded tracked, not zero-targeted: null is "no target yet", and zero would mean "spend nothing here".
        Assert.All(groups, g => Assert.Null(g.AnnualBudget));
    }

    [Fact]
    public async Task Watch_links_are_scoped_through_the_issues_and_stop_at_the_vehicle()
    {
        await using var context = NewContext();
        var mine = await NewVehicleAsync(context, "RAW 005");
        var theirs = await NewVehicleAsync(context, "RAW 006");

        var cap = await NewCheckAsync(context, mine, "Oil filler cap underside");
        var theirCheck = await NewCheckAsync(context, theirs, "Oil filler cap underside");

        var issues = new IssueService(context, new Clock(Clock));
        var mineIssue = (await issues.AddAsync(
            mine,
            new IssueInput("Head gasket — K-series risk", new DateOnly(2026, 3, 14), Severity.Critical),
            EntrySource.Web)).Value!.Id;
        var theirIssue = (await issues.AddAsync(
            theirs,
            new IssueInput("Head gasket — K-series risk", new DateOnly(2026, 3, 14), Severity.Critical),
            EntrySource.Web)).Value!.Id;

        await issues.SetWatchAsync(mine, mineIssue, [cap]);
        await issues.SetWatchAsync(theirs, theirIssue, [theirCheck]);

        await using var reader = NewContext();
        var links = await NewQueries(reader).ListWatchLinksAsync(mine);

        var link = Assert.Single(links);
        Assert.Equal(mineIssue, link.IssueId);
        Assert.Equal(cap, link.CheckDefinitionId);
    }
}
