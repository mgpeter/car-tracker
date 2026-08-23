using CarTracker.Shared;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The retention window the privacy page states, proved against a real database.
/// </summary>
/// <remarks>
/// <para>
/// Against real PostgreSQL because <c>ExecuteDelete</c> is translated SQL rather than something that runs in
/// memory: an in-memory provider would happily "pass" a comparison Npgsql renders differently, on the one
/// mechanism whose failure mode is a published document making a false promise.
/// </para>
/// <para>
/// The boundary cases are the whole point. A window nobody can state precisely is a window a policy cannot
/// describe, so a row one day outside it goes and a row one day inside it stays, and both are asserted rather
/// than one.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class RetentionServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;
    private int _owner;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_retention");

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();

        _owner = await TestOwner.SeedAsync(seed, "test|retention-owner");

        // Every test starts from empty ledgers; the database outlives the fixture and these tests share it.
        // The TOKENS matter as much as the audits: they are seeded per test with distinct ids, and
        // `Nothing_else_in_the_database_is_touched` counts them - so without this it passes alone and fails
        // in a batch, which is the worst way for a test to be wrong.
        await seed.AssistantWriteAudits.ExecuteDeleteAsync();
        await seed.AssistantTokens.ExecuteDeleteAsync();
        await seed.ChatUsage.ExecuteDeleteAsync();
        await seed.VehicleLookupUsage.ExecuteDeleteAsync();
        await seed.PendingIdentityDeletions.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time, accessor);

    private RetentionService ServiceFor(CarTrackerDbContext context, int? ledgerDays = null) =>
        new(context, new RetentionOptions { LedgerDays = ledgerDays }, new Clock(_time), _time);

    /// <summary>Seeds one row in each of the three ledgers, aged by <paramref name="daysOld"/>.</summary>
    private async Task SeedAtAgeAsync(int daysOld, int tokenId)
    {
        await using var context = NewContext();
        var today = new Clock(_time).Today();
        var day = today.AddDays(-daysOld);

        context.ChatUsage.Add(new ChatUsage
        {
            OwnerId = _owner,
            Day = day,
            InputTokens = 100,
            OutputTokens = 10,
            Turns = 1,
        });

        context.VehicleLookupUsage.Add(new VehicleLookupUsage { OwnerId = _owner, Day = day, Lookups = 1 });

        context.AssistantTokens.Add(new AssistantToken
        {
            Id = tokenId,
            OwnerId = _owner,
            Name = $"token-{tokenId}",
            TokenHash = $"hash-{tokenId}",
            Scope = AssistantScope.ReadWrite,
            CreatedAt = Reference.AddDays(-daysOld),
        });
        await context.SaveChangesAsync();

        context.AssistantWriteAudits.Add(new AssistantWriteAudit
        {
            TokenId = tokenId,
            Tool = "log_fuel_fillup",
            Summary = "a fill",
            TimestampUtc = Reference.AddDays(-daysOld),
        });
        await context.SaveChangesAsync();
    }

    private async Task<(int Chat, int Lookups, int Audits)> CountsAsync()
    {
        await using var context = NewContext();
        return (
            await context.ChatUsage.CountAsync(),
            await context.VehicleLookupUsage.CountAsync(),
            await context.AssistantWriteAudits.CountAsync());
    }

    [Fact]
    public async Task A_row_older_than_the_window_is_removed_from_all_three_ledgers()
    {
        await SeedAtAgeAsync(daysOld: 401, tokenId: 9001);

        await using var context = NewContext();
        var swept = await ServiceFor(context).SweepAsync();

        Assert.Equal(new RetentionSweep(1, 1, 1), swept);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_row_inside_the_window_is_left_alone()
    {
        // One day the other side of the same boundary. Without this the test above passes just as well against
        // a service that deletes everything, which is the failure that matters most here.
        await SeedAtAgeAsync(daysOld: 399, tokenId: 9002);

        await using var context = NewContext();
        var swept = await ServiceFor(context).SweepAsync();

        Assert.Equal(RetentionSweep.Nothing, swept);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_row_exactly_at_the_window_is_kept_because_the_page_promises_that_long()
    {
        await SeedAtAgeAsync(daysOld: 400, tokenId: 9003);

        await using var context = NewContext();
        await ServiceFor(context).SweepAsync();

        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Zero_never_prunes()
    {
        // The third polarity deploy/.env.example already documents for the chat ceilings: blank is the default,
        // a number is that number, and 0 is off. An operator who wants to keep everything must be able to.
        await SeedAtAgeAsync(daysOld: 5_000, tokenId: 9004);

        await using var context = NewContext();
        var swept = await ServiceFor(context, ledgerDays: 0).SweepAsync();

        Assert.Equal(RetentionSweep.Nothing, swept);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_negative_window_is_treated_as_off_rather_than_deleting_everything()
    {
        // A typo, not an instruction. Read literally it would delete rows dated in the future relative to the
        // cutoff, which is every row there is.
        await SeedAtAgeAsync(daysOld: 1, tokenId: 9005);

        await using var context = NewContext();
        var swept = await ServiceFor(context, ledgerDays: -30).SweepAsync();

        Assert.Equal(RetentionSweep.Nothing, swept);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_shorter_configured_window_is_honoured()
    {
        await SeedAtAgeAsync(daysOld: 40, tokenId: 9006);

        await using var context = NewContext();
        var swept = await ServiceFor(context, ledgerDays: 30).SweepAsync();

        Assert.Equal(3, swept.Total);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Nothing_else_in_the_database_is_touched()
    {
        // The claim worth pinning: this runs unattended, on every deployment, against a context whose vehicle
        // filter is inactive in a background scope. A pruner that reached one table too far would do so
        // quietly and nightly.
        await SeedAtAgeAsync(daysOld: 401, tokenId: 9007);

        await using var context = NewContext();
        await ServiceFor(context).SweepAsync();

        Assert.Equal(1, await context.Users.CountAsync());
        // The token the audit hung off survives its audit rows: a credential is not a ledger entry, and
        // revoking one is the owner's decision rather than a retention sweep's.
        Assert.Equal(1, await context.AssistantTokens.CountAsync());
    }

    [Fact]
    public async Task Pending_identity_deletions_are_never_pruned()
    {
        // Deliberately excluded. A row here exists only while an Auth0 deletion is being retried, so removing
        // one by age would abandon an erasure somebody asked for - the opposite of what retention is for.
        await using (var seed = NewContext())
        {
            seed.PendingIdentityDeletions.Add(new PendingIdentityDeletion
            {
                ExternalId = "auth0|long-ago",
                RequestedAt = Reference.AddDays(-5_000),
            });
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        await ServiceFor(context).SweepAsync();

        Assert.Equal(1, await context.PendingIdentityDeletions.CountAsync());
    }
}
