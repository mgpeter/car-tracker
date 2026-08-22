using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Lookup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Who is on which plan, and the one allowance that needs a ledger to answer.
/// </summary>
/// <remarks>
/// <para>
/// Against a real database because the resolution reads a row: the comp list is matched against
/// <see cref="User.Email"/> and gated on <see cref="User.EmailVerified"/>, and both of those are columns.
/// A fake would prove the arithmetic and none of the wiring, and the wiring is where a paid tier gets handed to
/// the wrong account.
/// </para>
/// <para>
/// <see cref="TestEntitlements"/> is what everything else in this project uses. This is the one class that
/// exercises the real <see cref="AccountEntitlements"/>, which is why it also carries the negative cases.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AccountEntitlementsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_entitlements");

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();
        await seed.VehicleLookupUsage.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time, accessor);

    /// <summary>An account with the address and verification a test needs, created once per external id.</summary>
    private async Task<int> OwnerAsync(string externalId, string email, bool verified)
    {
        await using var db = NewContext();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId);
        if (existing is not null)
        {
            existing.Email = email;
            existing.EmailVerified = verified;
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

    private AccountEntitlements EntitlementsFor(
        CarTrackerDbContext db,
        int? ownerId,
        string? compEmails = null,
        string? compDomains = null,
        PlanOptions? options = null) =>
        new(
            db,
            options ?? new PlanOptions { CompEmails = compEmails, CompDomains = compDomains },
            ownerId is { } id ? TestOwner.As(id) : new CurrentUserAccessor());

    [Fact]
    public async Task An_account_on_no_comp_list_is_free()
    {
        var ownerId = await OwnerAsync("test|plain", "plain@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        var entitlements = EntitlementsFor(db, ownerId);

        Assert.Equal(AccountPlan.Free, (await entitlements.ResolveAsync()).Plan);

        var allowances = await entitlements.AllowancesAsync();
        Assert.False(allowances.ChatEnabled);
        Assert.Equal(0, allowances.DailyChatTokens);
        Assert.Equal(100, allowances.MaxDocuments);
        Assert.Equal(3, allowances.DailyVehicleLookups);
    }

    [Fact]
    public async Task A_comped_address_is_pro()
    {
        var ownerId = await OwnerAsync("test|comped", "comped@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        var entitlements = EntitlementsFor(db, ownerId, compEmails: "comped@example.test");

        Assert.Equal(AccountPlan.Pro, (await entitlements.ResolveAsync()).Plan);

        var allowances = await entitlements.AllowancesAsync();
        Assert.True(allowances.ChatEnabled);
        // Null, not a number: the paid tier names no ceiling of its own and defers to Chat:DailyTokensPerOwner.
        Assert.Null(allowances.DailyChatTokens);
        Assert.Equal(2_000, allowances.MaxDocuments);
        Assert.Equal(50, allowances.DailyVehicleLookups);
    }

    [Fact]
    public async Task A_comped_domain_is_pro()
    {
        var ownerId = await OwnerAsync("test|comped-domain", "anyone@comped.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        Assert.Equal(AccountPlan.Pro, (await EntitlementsFor(db, ownerId, compDomains: "comped.test").ResolveAsync()).Plan);
    }

    [Fact]
    public async Task An_unverified_address_is_free_however_it_is_listed()
    {
        // The load-bearing one. Without it a comp list written as a domain hands the paid tier to anybody
        // willing to register as anything@that-domain - an allowlist that can be satisfied by typing is not an
        // allowlist, which is the sentence the invitation door carried before this took the argument over.
        var ownerId = await OwnerAsync("test|unproven", "anyone@comped.test", verified: false);
        await using var db = NewContext(TestOwner.As(ownerId));

        Assert.Equal(
            AccountPlan.Free,
            (await EntitlementsFor(db, ownerId, compEmails: "anyone@comped.test", compDomains: "comped.test").ResolveAsync()).Plan);
    }

    [Fact]
    public async Task An_account_whose_address_could_not_be_read_is_free()
    {
        // It holds its own subject in Email, and no entry on any list can match `auth0|…`. Nothing decides this
        // - it falls out of the sentinel, which is why the sentinel is worth keeping.
        var ownerId = await OwnerAsync("auth0|unreadable", "auth0|unreadable", verified: false);
        await using var db = NewContext(TestOwner.As(ownerId));

        Assert.Equal(
            AccountPlan.Free,
            (await EntitlementsFor(db, ownerId, compDomains: "example.test").ResolveAsync()).Plan);
    }

    [Fact]
    public async Task No_resolved_owner_is_free()
    {
        // Anonymous, an API-key principal, a refused sign-in. Free is the direction that costs nothing to be
        // wrong about, and it is the rule ChatBudget already applies to an unattributable turn.
        await using var db = NewContext();

        Assert.Equal(AccountPlan.Free, (await EntitlementsFor(db, ownerId: null, compDomains: "example.test").ResolveAsync()).Plan);
    }

    [Fact]
    public async Task Configuration_overrides_one_number_and_inherits_the_rest()
    {
        // PlanOptions starts entirely null so the shipped numbers live in exactly one place. Setting one key
        // must not reset the other three, which is what a whole-section replacement does to a compose file
        // naming only the number somebody wanted to change.
        var ownerId = await OwnerAsync("test|tuned", "tuned@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        var options = new PlanOptions();
        options.Free.MaxDocuments = 5;

        var allowances = await EntitlementsFor(db, ownerId, options: options).AllowancesAsync();

        Assert.Equal(5, allowances.MaxDocuments);
        Assert.Equal(3, allowances.DailyVehicleLookups);
        Assert.False(allowances.ChatEnabled);
    }

    // ── The DVLA ledger ──────────────────────────────────────────────────────────────────────────────────────

    private VehicleLookupQuota QuotaFor(CarTrackerDbContext db, int? ownerId, int perDay = 3) =>
        new(
            db,
            TestEntitlements.With(dailyVehicleLookups: perDay),
            ownerId is { } id ? TestOwner.As(id) : new CurrentUserAccessor(),
            new Clock(_time));

    [Fact]
    public async Task The_third_lookup_of_the_day_is_allowed_and_the_fourth_is_not()
    {
        var ownerId = await OwnerAsync("test|looker", "looker@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));
        await db.VehicleLookupUsage.Where(u => u.OwnerId == ownerId).ExecuteDeleteAsync();

        var quota = QuotaFor(db, ownerId);

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await quota.CheckAsync());
            await quota.RecordAsync();
        }

        var refusal = await quota.CheckAsync();

        Assert.NotNull(refusal);
        Assert.Equal(3, refusal.Used);
        Assert.Equal(3, refusal.Limit);
    }

    [Fact]
    public async Task The_allowance_is_per_account()
    {
        var mine = await OwnerAsync("test|looker-mine", "mine@example.test", verified: true);
        var theirs = await OwnerAsync("test|looker-theirs", "theirs@example.test", verified: true);

        await using var db = NewContext();
        await db.VehicleLookupUsage.Where(u => u.OwnerId == mine || u.OwnerId == theirs).ExecuteDeleteAsync();

        await using (var spend = NewContext(TestOwner.As(mine)))
        {
            var quota = QuotaFor(spend, mine);
            for (var i = 0; i < 3; i++) await quota.RecordAsync();
        }

        await using var other = NewContext(TestOwner.As(theirs));
        Assert.Null(await QuotaFor(other, theirs).CheckAsync());
    }

    [Fact]
    public async Task Tomorrow_starts_a_fresh_allowance()
    {
        var ownerId = await OwnerAsync("test|looker-day", "day@example.test", verified: true);

        await using (var today = NewContext(TestOwner.As(ownerId)))
        {
            await today.VehicleLookupUsage.Where(u => u.OwnerId == ownerId).ExecuteDeleteAsync();

            var quota = QuotaFor(today, ownerId);
            for (var i = 0; i < 3; i++) await quota.RecordAsync();
            Assert.NotNull(await quota.CheckAsync());
        }

        _time.Advance(TimeSpan.FromDays(1));

        await using var tomorrow = NewContext(TestOwner.As(ownerId));
        var quotaTomorrow = QuotaFor(tomorrow, ownerId);
        Assert.Null(await quotaTomorrow.CheckAsync());
        await quotaTomorrow.RecordAsync();

        // Yesterday's row survives beside today's. The ledger is a record, not a counter that gets reset - a
        // reset would make "how much did this cost us" unanswerable the day after it mattered.
        Assert.Equal(2, await tomorrow.VehicleLookupUsage.CountAsync(u => u.OwnerId == ownerId));
    }

    [Fact]
    public async Task A_plan_allowing_none_refuses_the_first()
    {
        var ownerId = await OwnerAsync("test|looker-none", "none@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));
        await db.VehicleLookupUsage.Where(u => u.OwnerId == ownerId).ExecuteDeleteAsync();

        var refusal = await QuotaFor(db, ownerId, perDay: 0).CheckAsync();

        // Reported as zero of zero, which is what it is - the endpoint says "not on this plan" rather than
        // implying tomorrow will be different.
        Assert.NotNull(refusal);
        Assert.Equal(0, refusal.Limit);
    }

    [Fact]
    public async Task An_unattributable_lookup_is_refused()
    {
        await using var db = NewContext();

        Assert.NotNull(await QuotaFor(db, ownerId: null).CheckAsync());
    }

    // -- Why an account is on the tier it is on (0.24.1) ------------------------------------------------------
    //
    // The plan alone is not a diagnosis. cambelt.app shipped 0.24.0 with an empty comp list, every account
    // landed on Free, and the only place that said so was a line in a container log. These pin the reason the
    // account screen renders, and the deployment-level one first, because it is the one that was missing.

    [Fact]
    public async Task An_empty_comp_list_reports_that_nobody_at_all_is_comped()
    {
        // The operator signal, and the regression guard for what actually happened. "Not on the list" would be
        // true here and useless: there is no list, so nothing anyone does to their own account can help.
        var ownerId = await OwnerAsync("test|reason-nobody", "nobody@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        var resolution = await EntitlementsFor(db, ownerId).ResolveAsync();

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.NobodyIsComped, resolution.Reason);
    }

    [Fact]
    public async Task A_comped_address_reports_that_it_is_comped()
    {
        var ownerId = await OwnerAsync("test|reason-comped", "comped@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        var resolution = await EntitlementsFor(db, ownerId, compEmails: "comped@example.test").ResolveAsync();

        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.Comped, resolution.Reason);
    }

    [Fact]
    public async Task An_address_missing_from_a_populated_list_reports_that_and_not_the_deployment()
    {
        // The distinction the previous test exists to protect: somebody IS comped here, just not you. That is
        // a different thing to do next - ask the owner - from "this deployment comps nobody".
        var ownerId = await OwnerAsync("test|reason-missing", "missing@example.test", verified: true);
        await using var db = NewContext(TestOwner.As(ownerId));

        var resolution = await EntitlementsFor(db, ownerId, compEmails: "someone-else@example.test").ResolveAsync();

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.NotOnCompList, resolution.Reason);
    }

    [Fact]
    public async Task An_unverified_address_reports_the_verification_and_not_the_list()
    {
        // Listed AND unverified. Reporting "not on the list" here would send somebody to ask for an invitation
        // they already have, when what they actually need is the confirmation link in their inbox.
        var ownerId = await OwnerAsync("test|reason-unverified", "unproven@example.test", verified: false);
        await using var db = NewContext(TestOwner.As(ownerId));

        var resolution = await EntitlementsFor(db, ownerId, compEmails: "unproven@example.test").ResolveAsync();

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AddressNotVerified, resolution.Reason);
    }

    [Fact]
    public async Task An_account_whose_address_was_never_resolved_reports_that()
    {
        // The sentinel row: provisioned with no Management credential, so it holds its own subject. Telling
        // this person they are "not on the list" would be true and unactionable - the deployment cannot read
        // their address at all, and that is the operator's problem rather than theirs.
        var ownerId = await OwnerAsync("auth0|reason-unknown", "auth0|reason-unknown", verified: false);
        await using var db = NewContext(TestOwner.As(ownerId));

        var resolution = await EntitlementsFor(db, ownerId, compDomains: "example.test").ResolveAsync();

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AddressUnknown, resolution.Reason);
    }

    [Fact]
    public async Task No_resolved_owner_reports_an_unknown_address()
    {
        await using var db = NewContext();

        var resolution = await EntitlementsFor(db, ownerId: null, compDomains: "example.test").ResolveAsync();

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AddressUnknown, resolution.Reason);
    }
}
