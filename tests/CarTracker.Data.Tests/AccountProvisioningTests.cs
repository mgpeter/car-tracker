using CarTracker.Domain.Accounts;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The invitation door and the retirement of DEC-016, tested against a real database because the half that
/// matters is what is <b>not</b> written.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SignupPolicy"/> decides; this asserts the consequence. A refusal that still left a
/// <see cref="User"/> row behind would satisfy every unit test of the policy and would be exactly the
/// half-state the spec refuses — an account the ownership filter can resolve, belonging to someone who was
/// turned away.
/// </para>
/// <para>
/// The contexts here bypass ownership, and that is the accurate reproduction rather than a shortcut: at
/// provisioning time there is no owner yet, because the middleware pins one only once this returns.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AccountProvisioningTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _clock, null);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_accounts");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>The tenant, as far as provisioning reads it: an address for a subject, or nothing.</summary>
    /// <param name="emailVerified">
    /// Defaults to a verified address so that a test about the allowlist is about the allowlist. The tests that
    /// are about verification say <c>emailVerified: false</c> and say why.
    /// </param>
    private sealed class FakeIdentity(
        string? email,
        string? displayName = null,
        bool configured = true,
        bool emailVerified = true)
        : IIdentityProviderClient
    {
        public int Calls { get; private set; }

        public bool IsConfigured => configured;

        public Task<IdentityProfile?> GetProfileAsync(string externalId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(email is null
                ? null
                : new IdentityProfile(externalId, email, displayName, emailVerified));
        }

        /// <remarks>
        /// The other half of the interface, and provisioning must never reach it — an account coming into
        /// existence has no business erasing a login. Throwing says so louder than returning a success would.
        /// Account deletion has its own double in <c>AccountDeletionTests</c>.
        /// </remarks>
        public Task<IdentityDeletionResult> DeleteUserAsync(string externalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Provisioning an account must not delete an identity.");
    }

    /// <summary>The unreachable or unconfigured tenant — every address unknown.</summary>
    private static FakeIdentity Silent() => new(null, configured: false);

    /// <param name="refusals">
    /// Passed in only by the tests that are about it. A fresh cache per provisioner otherwise, so that no test
    /// inherits another's remembered refusal — the production singleton spans requests, which is its whole job.
    /// </param>
    private AccountProvisioner ProvisionerFor(
        CarTrackerDbContext db,
        IIdentityProviderClient identity,
        string? allowedEmails = null,
        string? allowedDomains = null,
        string? claimUnownedFor = null,
        SignupRefusalCache? refusals = null) =>
        new(db,
            _clock,
            new SignupPolicy(new SignupOptions { AllowedEmails = allowedEmails, AllowedDomains = allowedDomains }),
            identity,
            refusals ?? new SignupRefusalCache(_clock),
            new OwnershipOptions { ClaimUnownedVehiclesFor = claimUnownedFor });

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
    public async Task An_address_outside_the_allowlist_creates_no_user_row()
    {
        await using var db = NewContext();
        var provisioner = ProvisionerFor(db, new FakeIdentity("stranger@elsewhere.test"),
            allowedEmails: "owner@example.com");

        var result = await provisioner.ResolveAsync("auth0|refused", emailClaim: null, emailClaimVerified: false, nameClaim: null);

        Assert.Equal(AccountOutcome.NotInvited, result.Outcome);
        Assert.Null(result.UserId);
        Assert.Contains("invitation-only", result.Detail);

        // The assertion the whole task exists for. A flagged-but-present row would be an account.
        Assert.False(await db.Users.AnyAsync(u => u.ExternalId == "auth0|refused"));
    }

    [Fact]
    public async Task An_empty_allowlist_refuses_a_perfectly_ordinary_address()
    {
        await using var db = NewContext();
        var provisioner = ProvisionerFor(db, new FakeIdentity("someone@example.com"));

        var result = await provisioner.ResolveAsync("auth0|closed-door", null, false, null);

        Assert.Equal(AccountOutcome.NotInvited, result.Outcome);
        Assert.False(await db.Users.AnyAsync(u => u.ExternalId == "auth0|closed-door"));
    }

    [Fact]
    public async Task An_address_that_cannot_be_resolved_is_refused_rather_than_admitted()
    {
        await using var db = NewContext();

        // A configured allowlist and no way to read the address — an unconfigured or unreachable tenant. The
        // failure direction is the point: nobody gets in, rather than everybody.
        var provisioner = ProvisionerFor(db, Silent(), allowedDomains: "example.com");

        var result = await provisioner.ResolveAsync("auth0|unknowable", null, false, null);

        Assert.Equal(AccountOutcome.NotInvited, result.Outcome);
        Assert.Contains("could not read", result.Detail);
        Assert.False(await db.Users.AnyAsync(u => u.ExternalId == "auth0|unknowable"));
    }

    [Fact]
    public async Task An_invited_address_is_provisioned_with_its_thirteen_categories()
    {
        await using var db = NewContext();
        var identity = new FakeIdentity("owner@example.com", "The Owner");
        var provisioner = ProvisionerFor(db, identity, allowedDomains: "example.com");

        var result = await provisioner.ResolveAsync("auth0|invited", null, false, null);

        Assert.Equal(AccountOutcome.Resolved, result.Outcome);

        var user = await db.Users.SingleAsync(u => u.ExternalId == "auth0|invited");
        Assert.Equal(result.UserId, user.Id);
        // The real address, not the `auth0|…` fallback the token would otherwise have left here.
        Assert.Equal("owner@example.com", user.Email);
        Assert.Equal("The Owner", user.DisplayName);
        Assert.Equal(_clock.GetUtcNow(), user.CreatedAt);

        var categories = await db.ExpenseCategories.IgnoreQueryFilters()
            .Where(c => c.OwnerId == user.Id)
            .ToListAsync();
        Assert.Equal(13, categories.Count);
        Assert.All(categories, c => Assert.True(c.IsSystem));
    }

    [Fact]
    public async Task A_token_that_carries_the_address_is_not_asked_of_the_tenant()
    {
        await using var db = NewContext();
        var identity = new FakeIdentity("wrong@example.com");
        var provisioner = ProvisionerFor(db, identity, allowedDomains: "example.com");

        // If the tenant ever gains an Action that adds `email` to the access token, the claim is authoritative
        // and the round trip is skipped — the Management call is a fallback, not the mechanism.
        var result = await provisioner.ResolveAsync("auth0|claimed", "claimed@example.com", emailClaimVerified: true, "Claim Holder");

        Assert.Equal(AccountOutcome.Resolved, result.Outcome);
        Assert.Equal(0, identity.Calls);
        Assert.Equal("claimed@example.com", (await db.Users.SingleAsync(u => u.ExternalId == "auth0|claimed")).Email);
    }

    /// <remarks>
    /// The defect: with the address alone deciding, <c>AllowedDomains=example.com</c> admits anybody who
    /// self-registers as <c>anything@example.com</c> on a database connection, and the allowlist the README
    /// presents as the gate for public release is not one. The address is a claim until the tenant has
    /// confirmed it.
    /// </remarks>
    [Fact]
    public async Task An_unverified_address_creates_no_user_row_however_well_it_matches_the_list()
    {
        await using var db = NewContext();
        var provisioner = ProvisionerFor(db,
            new FakeIdentity("impostor@example.com", emailVerified: false),
            allowedDomains: "example.com");

        var result = await provisioner.ResolveAsync("auth0|unverified", null, false, null);

        Assert.Equal(AccountOutcome.NotInvited, result.Outcome);
        Assert.False(await db.Users.AnyAsync(u => u.ExternalId == "auth0|unverified"));

        // And told the truth about which of the three refusals this is: the fix is a link in their inbox, not
        // an email to whoever runs the deployment.
        Assert.Contains("not been verified", result.Detail);
    }

    [Fact]
    public async Task A_token_claim_carrying_an_unverified_address_is_refused_as_well()
    {
        await using var db = NewContext();

        // The other half of the same door. A tenant that grows an Action adding `email` to the access token
        // must add `email_verified` beside it — otherwise the Management lookup is skipped and the claim walks
        // an unproven address straight past the check the lookup path now makes.
        var result = await ProvisionerFor(db, new FakeIdentity("wrong@example.com"), allowedDomains: "example.com")
            .ResolveAsync("auth0|claimed-unverified", "claimed@example.com", emailClaimVerified: false, null);

        Assert.Equal(AccountOutcome.NotInvited, result.Outcome);
        Assert.Contains("not been verified", result.Detail);
        Assert.False(await db.Users.AnyAsync(u => u.ExternalId == "auth0|claimed-unverified"));
    }

    /// <remarks>
    /// A refusal writes no row by design, so without the cache every request an uninvited visitor makes asks the
    /// tenant again — and the browser's <c>refetchOnWindowFocus</c> makes that a lookup per tab focus. The
    /// person it eventually shuts out is not them: a throttled Management API answers nothing, an unreadable
    /// address is on no list, and the refusal lands on whichever invited newcomer signs in during the throttle.
    /// </remarks>
    [Fact]
    public async Task A_refused_subject_is_not_asked_of_the_tenant_again_until_the_refusal_expires()
    {
        await using var db = NewContext();
        var identity = new FakeIdentity("stranger@elsewhere.test");
        var refusals = new SignupRefusalCache(_clock);

        // A new provisioner per call over one cache, because that is the production shape: the provisioner is
        // per request and the cache is the singleton that outlives it.
        Task<AccountResolution> Probe() =>
            ProvisionerFor(db, identity, allowedEmails: "owner@example.com", refusals: refusals)
                .ResolveAsync("auth0|persistent-stranger", null, false, null);

        var first = await Probe();
        var second = await Probe();

        Assert.Equal(AccountOutcome.NotInvited, second.Outcome);
        Assert.Equal(first.Detail, second.Detail);   // the same words, not a differently-worded second refusal
        Assert.Equal(1, identity.Calls);

        // It is a cache, not a ban list. A minute later the tenant is asked again — so someone who has just been
        // invited, or has just verified their address, is one retry away rather than one restart away.
        _clock.Advance(SignupRefusalCache.Window + TimeSpan.FromSeconds(1));
        await Probe();
        Assert.Equal(2, identity.Calls);
    }

    [Fact]
    public async Task A_remembered_refusal_never_outranks_an_account_that_exists()
    {
        await using var db = NewContext();
        var refusals = new SignupRefusalCache(_clock);

        // Refused while the allowlist was empty, then invited — the ordering hazard the cache introduces: asked
        // before the Users lookup it would lock a real account out of its own app for the length of the window.
        await ProvisionerFor(db, new FakeIdentity("late@example.com"), refusals: refusals)
            .ResolveAsync("auth0|late-invite", null, false, null);
        await ProvisionerFor(db, new FakeIdentity("late@example.com"), allowedDomains: "example.com")
            .ResolveAsync("auth0|late-invite", null, false, null);

        var result = await ProvisionerFor(db, Silent(), refusals: refusals)
            .ResolveAsync("auth0|late-invite", null, false, null);

        Assert.Equal(AccountOutcome.Resolved, result.Outcome);
    }

    [Fact]
    public async Task An_existing_account_is_admitted_without_being_rechecked()
    {
        await using var db = NewContext();
        await ProvisionerFor(db, new FakeIdentity("resident@example.com"), allowedDomains: "example.com")
            .ResolveAsync("auth0|resident", null, false, null);

        // The allowlist is now empty — tightened, or simply never set on a deployment that predates it. An
        // invitation list decides who may join, not who may stay: the resident still gets in.
        var result = await ProvisionerFor(db, Silent()).ResolveAsync("auth0|resident", null, false, null);

        Assert.Equal(AccountOutcome.Resolved, result.Outcome);
        Assert.NotNull(result.UserId);
    }

    [Fact]
    public async Task A_placeholder_email_is_backfilled_the_next_time_its_owner_signs_in()
    {
        int userId;
        await using (var seed = NewContext())
        {
            // What every account provisioned before the Management lookup existed looks like: the old `?? sub`
            // fallback stored the subject in Email.
            var user = new User
            {
                ExternalId = "auth0|legacy",
                Email = "auth0|legacy",
                CreatedAt = DateTimeOffset.UnixEpoch,
            };
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
            userId = user.Id;
        }

        await using var db = NewContext();
        var result = await ProvisionerFor(db, new FakeIdentity("legacy@example.com"))
            .ResolveAsync("auth0|legacy", null, false, null);

        Assert.Equal(userId, result.UserId);
        Assert.Equal("legacy@example.com", (await db.Users.SingleAsync(u => u.Id == userId)).Email);
    }

    /// <remarks>
    /// The other half of <c>RetryPendingAsync</c>'s refusal to delete an identity that has an account again, and
    /// both are needed: with only the retry guard, a row sits in the queue being re-examined every hour for an
    /// account nobody intends to delete; with only this one, a pass that runs between the sign-in and the insert
    /// still deletes the login. The judgement is that coming back cancels the queued removal — the data the row
    /// promised to erase went with the earlier account, and the login is one the person is now asking to keep.
    /// </remarks>
    [Fact]
    public async Task Signing_in_again_cancels_the_identity_deletion_still_queued_for_that_subject()
    {
        await using var db = NewContext();
        db.PendingIdentityDeletions.AddRange(
            new PendingIdentityDeletion { ExternalId = "auth0|came-back", RequestedAt = DateTimeOffset.UnixEpoch },
            new PendingIdentityDeletion { ExternalId = "auth0|still-gone", RequestedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();

        var result = await ProvisionerFor(db, new FakeIdentity("returner@example.com"), allowedDomains: "example.com")
            .ResolveAsync("auth0|came-back", null, false, null);

        Assert.Equal(AccountOutcome.Resolved, result.Outcome);
        Assert.False(await db.PendingIdentityDeletions.AnyAsync(p => p.ExternalId == "auth0|came-back"));

        // Only that subject's. Somebody else's queued erasure is not cancelled by a stranger signing in.
        Assert.True(await db.PendingIdentityDeletions.AnyAsync(p => p.ExternalId == "auth0|still-gone"));
    }

    [Fact]
    public async Task A_refused_address_leaves_a_queued_identity_deletion_alone()
    {
        await using var db = NewContext();
        db.PendingIdentityDeletions.Add(
            new PendingIdentityDeletion { ExternalId = "auth0|refused-return", RequestedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();

        // No account comes into existence, so nothing has affirmed the identity — the queued removal stands.
        var result = await ProvisionerFor(db, new FakeIdentity("stranger@elsewhere.test"),
            allowedEmails: "owner@example.com").ResolveAsync("auth0|refused-return", null, false, null);

        Assert.Equal(AccountOutcome.NotInvited, result.Outcome);
        Assert.True(await db.PendingIdentityDeletions.AnyAsync(p => p.ExternalId == "auth0|refused-return"));
    }

    [Fact]
    public async Task A_new_account_adopts_no_unowned_vehicle_by_default()
    {
        await using var db = NewContext();
        var unowned = NewVehicle("UN01 AAA");
        unowned.OwnerId = null;
        db.Vehicles.Add(unowned);
        await db.SaveChangesAsync();

        // DEC-016 retired. On a deployment anyone can reach, "the first user claims every unowned vehicle" hands
        // a stranger somebody else's car, its history and its documents, and nothing afterwards looks wrong.
        await ProvisionerFor(db, new FakeIdentity("first@example.com"), allowedDomains: "example.com")
            .ResolveAsync("auth0|first-through-the-door", null, false, null);

        Assert.Null(await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.Registration == "UN01 AAA").Select(v => v.OwnerId).SingleAsync());
    }

    [Fact]
    public async Task Only_the_named_external_id_adopts_the_unowned_vehicles()
    {
        await using var db = NewContext();
        var unowned = NewVehicle("UN02 BBB");
        unowned.OwnerId = null;
        db.Vehicles.Add(unowned);
        await db.SaveChangesAsync();

        var result = await ProvisionerFor(db, new FakeIdentity("keeper@example.com"),
                allowedDomains: "example.com", claimUnownedFor: "auth0|the-keeper")
            .ResolveAsync("auth0|the-keeper", null, false, null);

        Assert.Equal(result.UserId, await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.Registration == "UN02 BBB").Select(v => v.OwnerId).SingleAsync());
    }

    [Fact]
    public async Task An_external_id_that_only_differs_in_case_adopts_nothing()
    {
        await using var db = NewContext();
        var unowned = NewVehicle("UN03 CCC");
        unowned.OwnerId = null;
        db.Vehicles.Add(unowned);
        await db.SaveChangesAsync();

        // An Auth0 subject is an opaque identifier, not a name. Two that differ in case are two people, and the
        // setting names one of them.
        await ProvisionerFor(db, new FakeIdentity("impostor@example.com"),
                allowedDomains: "example.com", claimUnownedFor: "auth0|Cased-Keeper")
            .ResolveAsync("auth0|cased-keeper", null, false, null);

        Assert.Null(await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.Registration == "UN03 CCC").Select(v => v.OwnerId).SingleAsync());
    }
}
