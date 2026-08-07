using CarTracker.Domain;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The head-gasket watch's write path and its cascades, against a real database.
/// </summary>
/// <remarks>
/// The claims here are about which rows exist after a write — that a cross-vehicle link is refused (the join
/// carries no vehicle column, so nothing but this guard prevents one car's dashboard naming a watch over
/// another car's checks), and that deleting either end removes the link and leaves the survivor alone. Those
/// are database claims and the in-memory provider would not test them.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class IssueWatchTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock);

    private static IssueService NewIssues(CarTrackerDbContext context) => new(context, new Clock(Clock));

    private static LogQueryService NewQueries(CarTrackerDbContext context) => new(context, new Clock(Clock));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_issuewatch");
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
        await new VehicleFactory(context).CreateAsync(
            vehicle, _ownerId, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    private static async Task<int> NewCheckAsync(
        CarTrackerDbContext context, int vehicleId, string name, int order)
    {
        var definition = new CheckDefinition
        {
            VehicleId = vehicleId, Name = name, CadenceLabel = "Weekly", IntervalDays = 7,
            DisplayOrder = order, IsActive = true, Source = EntrySource.Web,
        };
        context.CheckDefinitions.Add(definition);
        await context.SaveChangesAsync();
        return definition.Id;
    }

    private static async Task<int> NewIssueAsync(CarTrackerDbContext context, int vehicleId, string title)
    {
        var result = await NewIssues(context).AddAsync(
            vehicleId,
            new IssueInput(title, new DateOnly(2026, 3, 14), Severity.Critical, IssueStatus.Resolved),
            EntrySource.Web);
        return result.Value!.Id;
    }

    [Fact]
    public async Task Setting_a_watch_links_the_checks_and_replacing_it_diffs()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "HGW 001");
        var cap = await NewCheckAsync(context, vehicleId, "Oil filler cap underside", 1);
        var coolant = await NewCheckAsync(context, vehicleId, "Coolant reservoir colour", 2);
        var spare = await NewCheckAsync(context, vehicleId, "Spare tyre pressure", 3);
        var issueId = await NewIssueAsync(context, vehicleId, "Head gasket — K-series risk");

        var set = await NewIssues(context).SetWatchAsync(vehicleId, issueId, [cap, coolant]);
        Assert.Equal(WriteStatus.Updated, set.Status);

        await using (var reader = NewContext())
        {
            var links = await reader.IssueWatchChecks.Where(w => w.IssueId == issueId).ToListAsync();
            Assert.Equal(2, links.Count);
        }

        // Replace: coolant stays, cap goes, spare arrives.
        Assert.Equal(WriteStatus.Updated,
            (await NewIssues(context).SetWatchAsync(vehicleId, issueId, [coolant, spare])).Status);

        await using (var reader = NewContext())
        {
            var ids = await reader.IssueWatchChecks
                .Where(w => w.IssueId == issueId).Select(w => w.CheckDefinitionId).ToListAsync();
            Assert.Equal([coolant, spare], ids.Order());
        }

        // An empty list is how the watch is cleared — distinct from omitting the field, which the endpoint
        // treats as "leave it alone".
        Assert.Equal(WriteStatus.Updated,
            (await NewIssues(context).SetWatchAsync(vehicleId, issueId, [])).Status);

        await using (var reader = NewContext())
        {
            Assert.False(await reader.IssueWatchChecks.AnyAsync(w => w.IssueId == issueId));
        }
    }

    [Fact]
    public async Task A_cross_vehicle_link_is_refused()
    {
        await using var context = NewContext();
        var mine = await NewVehicleAsync(context, "HGW 002");
        var theirs = await NewVehicleAsync(context, "HGW 003");

        var myCheck = await NewCheckAsync(context, mine, "Oil filler cap underside", 1);
        var theirCheck = await NewCheckAsync(context, theirs, "Oil filler cap underside", 1);
        var issueId = await NewIssueAsync(context, mine, "Head gasket — K-series risk");

        var result = await NewIssues(context).SetWatchAsync(mine, issueId, [myCheck, theirCheck]);

        // Refused whole, not silently filtered to the valid one: a caller passing a wrong id should be told,
        // not handed a shorter watch than it asked for.
        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Contains("WatchCheckDefinitionIds", result.Errors!.Keys);
        Assert.False(await context.IssueWatchChecks.AnyAsync(w => w.IssueId == issueId));
    }

    [Fact]
    public async Task Deleting_a_watched_check_removes_the_link_and_leaves_the_issue()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "HGW 004");
        var cap = await NewCheckAsync(context, vehicleId, "Oil filler cap underside", 1);
        var coolant = await NewCheckAsync(context, vehicleId, "Coolant reservoir colour", 2);
        var issueId = await NewIssueAsync(context, vehicleId, "Head gasket — K-series risk");

        await NewIssues(context).SetWatchAsync(vehicleId, issueId, [cap, coolant]);

        var definition = await context.CheckDefinitions.SingleAsync(d => d.Id == cap);
        context.CheckDefinitions.Remove(definition);
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var remaining = await reader.IssueWatchChecks.Where(w => w.IssueId == issueId).ToListAsync();
        Assert.Equal(coolant, remaining.Single().CheckDefinitionId);
        // The issue survives its check being deleted — it simply watches fewer things.
        Assert.True(await reader.Issues.AnyAsync(i => i.Id == issueId));
    }

    [Fact]
    public async Task Deleting_the_issue_removes_its_links_and_leaves_the_checks()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "HGW 005");
        var cap = await NewCheckAsync(context, vehicleId, "Oil filler cap underside", 1);
        var issueId = await NewIssueAsync(context, vehicleId, "Head gasket — K-series risk");
        await NewIssues(context).SetWatchAsync(vehicleId, issueId, [cap]);

        context.Issues.Remove(await context.Issues.SingleAsync(i => i.Id == issueId));
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        Assert.False(await reader.IssueWatchChecks.AnyAsync(w => w.IssueId == issueId));
        Assert.True(await reader.CheckDefinitions.AnyAsync(d => d.Id == cap));
    }

    /// <summary>
    /// The whole feature, end to end through the read models the two screens actually consume.
    /// </summary>
    [Fact]
    public async Task A_lapsed_watch_surfaces_on_the_issue_log_and_the_summary_without_reopening_the_issue()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "HGW 006");
        var cap = await NewCheckAsync(context, vehicleId, "Oil filler cap underside", 1);
        var coolant = await NewCheckAsync(context, vehicleId, "Coolant reservoir colour", 2);
        var issueId = await NewIssueAsync(context, vehicleId, "Head gasket — K-series risk");
        await NewIssues(context).SetWatchAsync(vehicleId, issueId, [cap, coolant]);

        // The design's scenario: last done 18 June against a 14 July reference, weekly cadence.
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = cap, PerformedOn = new DateOnly(2026, 6, 18), Source = EntrySource.Web,
        });
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = coolant, PerformedOn = new DateOnly(2026, 6, 18), Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        await using var reader = NewContext();

        var log = await NewQueries(reader).GetIssueLogAsync(vehicleId);
        var issue = log.Issues.Single();
        Assert.Equal(2, issue.Watch.Count);
        Assert.All(issue.Watch, w => Assert.True(w.IsLapsed));
        // Flagged, not reopened — the status the owner set is untouched.
        Assert.Equal(IssueStatus.Resolved, issue.Status);

        var summary = await new DerivedMetricsService(new VehicleMetricsLoader(reader), new Clock(Clock))
            .GetVehicleSummaryAsync(vehicleId);
        var watch = summary!.Watches.Single();
        Assert.Equal("Head gasket — K-series risk", watch.IssueTitle);
        Assert.Equal(2, watch.LapsedCheckCount);
        Assert.Equal(2, watch.TotalCheckCount);
        Assert.Equal(IssueStatus.Resolved, watch.IssueStatus);
    }

    [Fact]
    public async Task An_issue_with_no_watch_behaves_exactly_as_before()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "HGW 007");
        await NewCheckAsync(context, vehicleId, "Oil filler cap underside", 1);
        await NewIssueAsync(context, vehicleId, "Brake pipe corrosion");

        await using var reader = NewContext();

        Assert.Empty((await NewQueries(reader).GetIssueLogAsync(vehicleId)).Issues.Single().Watch);

        var summary = await new DerivedMetricsService(new VehicleMetricsLoader(reader), new Clock(Clock))
            .GetVehicleSummaryAsync(vehicleId);
        Assert.Empty(summary!.Watches);
    }
}
