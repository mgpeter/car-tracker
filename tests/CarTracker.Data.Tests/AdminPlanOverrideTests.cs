using CarTracker.Domain.Accounts;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// A plan an administrator set, read back through the real entitlement path.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PlanResolverTests"/> in the Domain project proves the ladder. This proves the column is actually
/// consulted: the override is only worth anything if <see cref="AccountEntitlements"/> reads it on the next
/// request, and that is a claim about a column, a projection and a scoped service rather than about the
/// ordering. A unit test of the resolver would pass while the property went unprojected.
/// </para>
/// <para>
/// <b>Every assertion is made on a fresh context</b>, because the entitlement cache lives for the life of one
/// instance. Reading back through the same context would prove the cache, not the database - and "no restart
/// needed" is precisely the claim being tested.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AdminPlanOverrideTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_planoverride");

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time, accessor);

    private async Task<int> OwnerAsync(string externalId, string email, bool verified = true)
    {
        await using var db = NewContext();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId);
        if (existing is not null)
        {
            existing.Email = email;
            existing.EmailVerified = verified;
            existing.PlanOverride = null;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var user = new User
        {
            ExternalId = externalId,
            Email = email,
            EmailVerified = verified,
            CreatedAt = Reference,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ExpenseCategories.AddRange(ExpenseCategoryProvisioner.ForNewUser(user));
        await db.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>What the account resolves to right now, on a context that has never seen it.</summary>
    private async Task<PlanResolution> ResolveAsync(int ownerId, string? compEmails = null)
    {
        await using var db = NewContext(TestOwner.As(ownerId));

        var entitlements = new AccountEntitlements(
            db, new PlanOptions { CompEmails = compEmails }, TestOwner.As(ownerId));

        return await entitlements.ResolveAsync();
    }

    private async Task SetOverrideAsync(int ownerId, AccountPlan? plan)
    {
        await using var db = NewContext();
        var user = await db.Users.SingleAsync(u => u.Id == ownerId);
        user.PlanOverride = plan;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_override_reaches_the_account_with_no_restart_and_no_comp_list()
    {
        // The whole point of the feature, and the shape cambelt.app was in when 0.24.0 shipped: nobody comped,
        // so nobody has the assistant. Before the override the only fix was an edit to deploy/.env and a
        // container recreate.
        var ownerId = await OwnerAsync("test|granted", "granted@example.test");

        Assert.Equal(PlanReason.NobodyIsComped, (await ResolveAsync(ownerId)).Reason);

        await SetOverrideAsync(ownerId, AccountPlan.Pro);

        var resolution = await ResolveAsync(ownerId);
        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.AdminGranted, resolution.Reason);
    }

    [Fact]
    public async Task An_override_of_free_pins_an_account_below_a_comp_list_it_matches()
    {
        // The direction a boolean could not express, and the reason the column is a nullable plan.
        var ownerId = await OwnerAsync("test|pinned", "pinned@example.test");
        const string comped = "pinned@example.test";

        Assert.Equal(AccountPlan.Pro, (await ResolveAsync(ownerId, comped)).Plan);

        await SetOverrideAsync(ownerId, AccountPlan.Free);

        var resolution = await ResolveAsync(ownerId, comped);
        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AdminGranted, resolution.Reason);
    }

    [Fact]
    public async Task Clearing_an_override_returns_the_account_to_what_the_comp_list_says()
    {
        // Null is not Free. An account with no override falls back through the ladder, which is what makes
        // DELETE on the endpoint a different operation from PUT with plan=Free.
        var ownerId = await OwnerAsync("test|cleared", "cleared@example.test");
        const string comped = "cleared@example.test";

        await SetOverrideAsync(ownerId, AccountPlan.Free);
        Assert.Equal(PlanReason.AdminGranted, (await ResolveAsync(ownerId, comped)).Reason);

        await SetOverrideAsync(ownerId, null);

        var resolution = await ResolveAsync(ownerId, comped);
        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.Comped, resolution.Reason);
    }

    [Fact]
    public async Task An_override_reaches_only_the_account_it_was_set_on()
    {
        // The cross-account case, asserted because an override is written by an endpoint that takes an id from
        // a URL and DEC-018's cross-tenant write started life as a rename that looked equally local.
        var granted = await OwnerAsync("test|neighbour-a", "neighbour-a@example.test");
        var untouched = await OwnerAsync("test|neighbour-b", "neighbour-b@example.test");

        await SetOverrideAsync(granted, AccountPlan.Pro);

        Assert.Equal(AccountPlan.Pro, (await ResolveAsync(granted)).Plan);

        var neighbour = await ResolveAsync(untouched);
        Assert.Equal(AccountPlan.Free, neighbour.Plan);
        Assert.Equal(PlanReason.NobodyIsComped, neighbour.Reason);
    }

    [Fact]
    public async Task An_override_outranks_an_unverified_address()
    {
        // Verification gates the comp list because a domain entry would otherwise be satisfiable by typing.
        // An override is not satisfiable by typing - somebody with the permission chose this account - so the
        // gate does not apply to it, and the reason says so rather than sending the holder to their inbox.
        var ownerId = await OwnerAsync("test|unverified-granted", "unverified@example.test", verified: false);

        Assert.Equal(PlanReason.AddressNotVerified, (await ResolveAsync(ownerId, "unverified@example.test")).Reason);

        await SetOverrideAsync(ownerId, AccountPlan.Pro);

        var resolution = await ResolveAsync(ownerId, "unverified@example.test");
        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.AdminGranted, resolution.Reason);
    }
}
