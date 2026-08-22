using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The <c>AddAccountPlans</c> backfill, run against a database that already holds accounts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one line in that migration that decides whether an upgrade is a no-op.</b> A new column defaults to
/// false, so without the backfill every account this deployment already has would land on the free tier and
/// lose the assistant - repaired only if it happens to sign in again against a configured Management
/// credential, and on a deployment with none, never. That is invisible from a fresh database, which is what
/// every other test in this project runs against.
/// </para>
/// <para>
/// The claim the backfill rests on is provable rather than optimistic: until 0.24.0 the only door was the
/// invitation allowlist, and <c>SignupPolicy.Admits</c> refused an unverified address outright, so a row that
/// exists at all was provisioned against an address the tenant had confirmed. The exception is a row still
/// carrying the pre-Management fallback, where <c>email = external_id</c> and no address was ever resolved -
/// those stay false and are repaired by <c>AccountProvisioner.BackfillEmailAsync</c> on the next request.
/// </para>
/// <para>
/// Structured like <see cref="PerOwnerReferenceListBackfillTests"/>: migrate to the migration <i>before</i> the
/// one under test, seed through the old schema with raw SQL because the EF model has moved on, then migrate the
/// rest of the way.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AccountPlansBackfillTests(PostgresFixture postgres)
{
    /// <summary>The migration immediately before <c>AddAccountPlans</c>.</summary>
    private const string Before = "AddChatUsage";

    private static CarTrackerDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(connectionString).Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero)));

    private static Task MigrateToAsync(CarTrackerDbContext context, string target) =>
        context.Database.GetService<IMigrator>().MigrateAsync(target);

    [Fact]
    public async Task Existing_accounts_keep_their_verification_and_the_unresolved_one_does_not_gain_it()
    {
        var connectionString = await postgres.EnsureDatabaseAsync("cartracker_plans_backfill");

        await using (var old = NewContext(connectionString))
        {
            await MigrateToAsync(old, Before);

            // Raw SQL: at this migration `users` has no email_verified column for EF to write, and the model
            // knows about one. Two rows, because the backfill has to tell them apart.
            await old.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO users (external_id, email, created_at) VALUES
                    ('auth0|resolved',   'resolved@example.test', '1970-01-01T00:00:00Z'),
                    ('auth0|unresolved', 'auth0|unresolved',      '1970-01-01T00:00:00Z')
                """);
        }

        await using (var migrate = NewContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await using var after = NewContext(connectionString);

        // The account with a real address was admitted through a door that required verification, so it keeps
        // the assistant across the upgrade rather than silently dropping to the free tier.
        Assert.True(await after.Users
            .Where(u => u.ExternalId == "auth0|resolved").Select(u => u.EmailVerified).SingleAsync());

        // The sentinel row is left false, which is the honest answer: nothing ever read this address, so
        // nothing ever verified it. It is also unmatchable by any comp list, so the two agree.
        Assert.False(await after.Users
            .Where(u => u.ExternalId == "auth0|unresolved").Select(u => u.EmailVerified).SingleAsync());
    }

    [Fact]
    public async Task The_lookup_ledger_arrives_empty_and_usable()
    {
        var connectionString = await postgres.EnsureDatabaseAsync("cartracker_plans_backfill_ledger");

        await using (var old = NewContext(connectionString))
        {
            await MigrateToAsync(old, Before);
            await old.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO users (external_id, email, created_at)
                VALUES ('auth0|ledger', 'ledger@example.test', '1970-01-01T00:00:00Z')
                """);
        }

        await using (var migrate = NewContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await using var after = NewContext(connectionString);
        var ownerId = await after.Users.Where(u => u.ExternalId == "auth0|ledger").Select(u => u.Id).SingleAsync();

        Assert.Empty(await after.VehicleLookupUsage.ToListAsync());

        // The composite key and the cascade to users both come from the same migration, so writing one row is
        // what proves the table is not merely present.
        after.VehicleLookupUsage.Add(new VehicleLookupUsage
        {
            OwnerId = ownerId,
            Day = new DateOnly(2026, 8, 21),
            Lookups = 1,
        });
        await after.SaveChangesAsync();

        Assert.Equal(1, await after.VehicleLookupUsage.CountAsync(u => u.OwnerId == ownerId));
    }
}
