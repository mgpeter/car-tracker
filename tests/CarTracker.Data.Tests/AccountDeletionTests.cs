using System.Text;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Logs;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Account deletion (UK GDPR Art. 17) — against a real database and a real directory, because both halves of
/// the claim are physical: every row gone, and every file gone.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing assertion is not "the account's rows are gone" but "and the other account's are not". A
/// delete written against an unscoped table would satisfy the first while quietly failing the second, which is
/// the same shape of defect the reference lists carried into this release.
/// </para>
/// <para>
/// <b>The <c>Restrict</c> foreign keys are asserted to still be there.</b> The service works by ordering —
/// vehicles and tokens before the user row — and the cheap way to make that unnecessary would have been to
/// weaken the constraints. <see cref="The_restrict_foreign_key_is_real_and_the_order_is_what_satisfies_it"/>
/// proves the database still refuses the wrong order, so the ordering is load-bearing rather than decorative.
/// </para>
/// <para>
/// Contexts are pinned with <see cref="TestOwner.As"/>: a context with no accessor bypasses ownership, and a
/// reference-list write refuses outright under one. Seeding an account through a bypassed context and then
/// deleting through a pinned one is what the request pipeline actually does.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AccountDeletionTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private string _root = string.Empty;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));

    /// <summary>Per test, because one of the assertions below is about what the service said, not just did.</summary>
    private readonly CapturingLogger _log = new();

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock, accessor);

    private DocumentStore NewStore() => new(new DocumentStorageOptions(_root));

    private AccountDeletionService DeletionFor(
        CarTrackerDbContext context, IIdentityProviderClient identity, DocumentStore? store = null) =>
        new(context, store ?? NewStore(), identity, Clock, _log);

    /// <summary>
    /// Keeps what was logged, because one fix here is precisely "continue, but say so": a failure that is
    /// swallowed and a failure that is reported are indistinguishable from the status code alone.
    /// </summary>
    private sealed class CapturingLogger : ILogger<AccountDeletionService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_accountdeletion");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-del-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>The tenant as deletion reads it: an identity to erase, and whether it can be erased at all.</summary>
    private sealed class FakeIdentity(bool configured = true, IdentityDeletionResult? answer = null)
        : IIdentityProviderClient
    {
        public List<string> Asked { get; } = [];

        public bool IsConfigured => configured;

        /// <remarks>Deletion never reads a profile; a call here would mean the wrong seam was used.</remarks>
        public Task<IdentityProfile?> GetProfileAsync(string externalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Deleting an account must not look a profile up.");

        public Task<IdentityDeletionResult> DeleteUserAsync(string externalId, CancellationToken cancellationToken = default)
        {
            Asked.Add(externalId);
            return Task.FromResult(answer ?? IdentityDeletionResult.Deleted);
        }
    }

    // ---- fixtures -----------------------------------------------------------------------------------------

    /// <summary>An account with one vehicle and at least one row in every table that hangs off either.</summary>
    /// <remarks>
    /// Deliberately exhaustive. An account deletion is only correct if it reaches everything, and the way it
    /// stops being correct is that a table added later is never added here — so the assertions below count rows
    /// per table by name rather than trusting a cascade to have been configured.
    /// </remarks>
    private async Task<Account> SeedAccountAsync(string externalId, string registration)
    {
        int ownerId;
        await using (var seed = NewContext())
        {
            ownerId = await TestOwner.SeedAsync(seed, externalId);
        }

        var accessor = TestOwner.As(ownerId);
        await using var context = NewContext(accessor);

        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, PurchasePrice = 1_700m,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        // The generic set, so check definitions (and their logs) are real rows rather than an empty table that
        // would pass an "is it gone" assertion for the wrong reason.
        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web);
        var vehicleId = vehicle.Id;

        // The reference lists, through the writer that stamps the owner — two accounts each holding a garage of
        // the same name is the case that proves the delete is scoped and not keyed on a name.
        var writer = new ReferenceWriter(context, accessor);
        await writer.EnsureGarageAsync("K & P Motors");
        await writer.EnsureWashLocationAsync("Home driveway");

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 7, 8), Mileage = 80_705,
            Origin = MileageOrigin.Manual, Source = EntrySource.Web,
        });
        context.FuelEntries.Add(new FuelEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 4, 2), Mileage = 77_881,
            Litres = 44.02m, PricePerLitre = 1.599m, TotalCost = 70.39m, Station = "Applegreen",
            FillLevel = FillLevel.Full, Source = EntrySource.Web,
        });
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 7, 8), Category = "Repair",
            Amount = 129.99m, Source = EntrySource.Web,
        });
        context.ServiceRecords.Add(new ServiceRecord
        {
            VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 7, 8), Type = "MOT", Mileage = 80_705,
            Garage = "K & P Motors", Source = EntrySource.Web,
        });
        context.TyreReadings.Add(new TyreReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 6, 1), PsiFrontLeft = 30m,
            Source = EntrySource.Web,
        });
        context.WashEntries.Add(new WashEntry
        {
            VehicleId = vehicleId, WashDate = new DateOnly(2026, 6, 20), Location = "Home driveway",
            Cost = 4.50m, Source = EntrySource.Web,
        });
        context.MaintenanceTasks.Add(new MaintenanceTask
        {
            VehicleId = vehicleId, Title = "Replace wiper blades", Kind = MaintenanceTaskKind.DIY,
            Priority = Priority.Low, Status = MaintenanceTaskStatus.Open, Source = EntrySource.Web,
        });
        context.EquipmentItems.Add(new EquipmentItem
        {
            VehicleId = vehicleId, Name = "Scissor jack", Status = EquipmentStatus.Owned,
            Source = EntrySource.Web,
        });
        context.DataAnomalies.Add(new DataAnomaly
        {
            VehicleId = vehicleId, Kind = AnomalyKind.MileageNonMonotonic, Severity = AnomalySeverity.Error,
            EntityType = "MileageReading", Message = "A reading goes backwards.", Status = AnomalyStatus.Open,
            CreatedAt = Clock.GetUtcNow(), Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        // A check log, reached the only way there is: through a definition the factory created.
        var definitionId = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId).OrderBy(d => d.DisplayOrder).Select(d => d.Id).FirstAsync();
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = definitionId, PerformedOn = new DateOnly(2026, 7, 1),
            Result = CheckResult.OK, Source = EntrySource.Web,
        });

        // No budget group is added by hand: VehicleFactory seeds the four default groups with their category
        // memberships, and ix_budget_group_category_vehicle_category is unique per vehicle, so a second "Repair"
        // would collide with the one the template already placed.

        var token = new AssistantToken
        {
            // A hash, not a placeholder: ix_assistant_tokens_hash is unique across every account, so two
            // fixtures sharing a constant would collide before anything under test ran.
            OwnerId = ownerId, Name = "Claude Desktop", TokenHash = HashOf(externalId),
            Scope = AssistantScope.ReadWrite, CreatedAt = Clock.GetUtcNow(),
        };
        context.AssistantTokens.Add(token);
        await context.SaveChangesAsync();

        context.AssistantWriteAudits.Add(new AssistantWriteAudit
        {
            TokenId = token.Id, Tool = "log_fuel_fillup", VehicleId = vehicleId,
            Summary = "Logged 44.02 L", TimestampUtc = Clock.GetUtcNow(),
        });
        await context.SaveChangesAsync();

        var issues = new IssueService(context, new Clock(Clock));
        var issueId = (await issues.AddAsync(
            vehicleId,
            new IssueInput("Head gasket — K-series risk", new DateOnly(2026, 3, 14), Severity.Critical),
            EntrySource.Web)).Value!.Id;
        await issues.SetWatchAsync(vehicleId, issueId, [definitionId]);

        // Real bytes on the volume, under {root}/{vehicleId}/.
        await using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes($"certificate for {registration}")))
        {
            var stored = await NewStore().SaveAsync(vehicleId, bytes, "application/pdf");
            await new DocumentService(context, NewStore(), TestEntitlements.Pro).RecordAsync(
                vehicleId, stored!, "application/pdf", DocumentType.MOT, "MOT certificate — pass",
                new DateOnly(2026, 7, 8), null, null, null, null, EntrySource.Web);
        }

        return new Account(ownerId, externalId, vehicleId, $"{externalId.Replace('|', '.')}@example.test");
    }

    private sealed record Account(int OwnerId, string ExternalId, int VehicleId, string Email);

    private static string HashOf(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Every row anywhere that belongs to this account, as one number.</summary>
    /// <remarks>
    /// Table by table rather than "count the vehicles": the point of the assertion is that nothing survived
    /// <i>anywhere</i>, and a cascade that silently stopped working would leave a vehicle-less orphan that a
    /// vehicle count would not see.
    /// </remarks>
    private async Task<int> RowsFor(Account account)
    {
        await using var context = NewContext();
        var id = account.VehicleId;

        var definitionIds = await context.CheckDefinitions.Where(d => d.VehicleId == id).Select(d => d.Id).ToListAsync();
        var issueIds = await context.Issues.Where(i => i.VehicleId == id).Select(i => i.Id).ToListAsync();
        var groupIds = await context.BudgetGroups.Where(g => g.VehicleId == id).Select(g => g.Id).ToListAsync();
        var tokenIds = await context.AssistantTokens.Where(t => t.OwnerId == account.OwnerId).Select(t => t.Id).ToListAsync();

        return await context.Users.CountAsync(u => u.Id == account.OwnerId)
            + await context.Vehicles.IgnoreQueryFilters().CountAsync(v => v.Id == id)
            + await context.MileageReadings.CountAsync(x => x.VehicleId == id)
            + await context.FuelEntries.CountAsync(x => x.VehicleId == id)
            + await context.ExpenseEntries.CountAsync(x => x.VehicleId == id)
            + await context.ServiceRecords.CountAsync(x => x.VehicleId == id)
            + await context.TyreReadings.CountAsync(x => x.VehicleId == id)
            + await context.WashEntries.CountAsync(x => x.VehicleId == id)
            + await context.MaintenanceTasks.CountAsync(x => x.VehicleId == id)
            + await context.EquipmentItems.CountAsync(x => x.VehicleId == id)
            + await context.Documents.CountAsync(x => x.VehicleId == id)
            + await context.DataAnomalies.CountAsync(x => x.VehicleId == id)
            + definitionIds.Count
            + await context.CheckLogs.CountAsync(l => definitionIds.Contains(l.CheckDefinitionId))
            + issueIds.Count
            + await context.IssueWatchChecks.CountAsync(w => issueIds.Contains(w.IssueId))
            + groupIds.Count
            + await context.BudgetGroupCategories.CountAsync(c => groupIds.Contains(c.BudgetGroupId))
            + tokenIds.Count
            + await context.AssistantWriteAudits.CountAsync(a => tokenIds.Contains(a.TokenId))
            + await context.Garages.IgnoreQueryFilters().CountAsync(g => g.OwnerId == account.OwnerId)
            + await context.WashLocations.IgnoreQueryFilters().CountAsync(w => w.OwnerId == account.OwnerId)
            + await context.ExpenseCategories.IgnoreQueryFilters().CountAsync(c => c.OwnerId == account.OwnerId);
    }

    private Task<AccountDeletionResult> DeleteAsync(Account account, IIdentityProviderClient identity, string? confirm = null)
    {
        var context = NewContext(TestOwner.As(account.OwnerId));
        return DeletionFor(context, identity)
            .DeleteAsync(account.OwnerId, account.ExternalId, confirm ?? account.Email);
    }

    // ---- the deletion itself ------------------------------------------------------------------------------

    [Fact]
    public async Task Everything_the_account_owns_goes_and_the_other_account_is_untouched()
    {
        var mine = await SeedAccountAsync("del|a", "DEL 001");
        var theirs = await SeedAccountAsync("del|b", "DEL 002");

        var before = await RowsFor(theirs);
        Assert.True(before > 20, "the second account must actually hold rows for its survival to mean anything");

        var identity = new FakeIdentity();
        var result = await DeleteAsync(mine, identity);

        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.True(result.IdentityDeleted);
        Assert.Equal(["del|a"], identity.Asked);

        Assert.Equal(0, await RowsFor(mine));
        Assert.Equal(before, await RowsFor(theirs));

        // The bytes, not just the rows. Nothing else in the schema knows these files exist, so a deletion that
        // forgot them would leave the one trace no query could find.
        Assert.False(Directory.Exists(Path.Combine(_root, mine.VehicleId.ToString())));
        Assert.True(Directory.Exists(Path.Combine(_root, theirs.VehicleId.ToString())));

        // Erased at the provider on the first attempt, so nothing is left queued.
        await using var after = NewContext();
        Assert.False(await after.PendingIdentityDeletions.AnyAsync(p => p.ExternalId == "del|a"));
    }

    [Fact]
    public async Task The_restrict_foreign_key_is_real_and_the_order_is_what_satisfies_it()
    {
        var account = await SeedAccountAsync("del|restrict", "DEL 003");

        await using (var context = NewContext())
        {
            // What the service would hit if it deleted the user first. vehicles.owner_id and
            // assistant_tokens.owner_id are Restrict deliberately — a vehicle is data whose deletion should be
            // an explicit act — so the ordering in AccountDeletionService is the thing making the delete legal,
            // not a convention it happens to follow.
            var user = await context.Users.SingleAsync(u => u.Id == account.OwnerId);
            context.Users.Remove(user);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        // And the ordered path succeeds against those same live constraints.
        Assert.Equal(AccountDeletionOutcome.Deleted, (await DeleteAsync(account, new FakeIdentity())).Outcome);
        Assert.Equal(0, await RowsFor(account));
    }

    // ---- the refusals -------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unconfigured_identity_provider_refuses_and_deletes_nothing()
    {
        var account = await SeedAccountAsync("del|noconfig", "DEL 004");
        var before = await RowsFor(account);

        var identity = new FakeIdentity(configured: false);
        var result = await DeleteAsync(account, identity);

        Assert.Equal(AccountDeletionOutcome.IdentityDeletionNotConfigured, result.Outcome);
        // The check happens before the transaction opens, so the provider is never even asked.
        Assert.Empty(identity.Asked);

        // The status code is not the assertion — the database is. Deleting the data and leaving the login
        // standing is the outcome this refusal exists to prevent, and it would look identical from outside.
        Assert.Equal(before, await RowsFor(account));
        Assert.True(Directory.Exists(Path.Combine(_root, account.VehicleId.ToString())));
    }

    [Fact]
    public async Task A_confirmation_that_does_not_match_deletes_nothing_and_marks_the_field()
    {
        var account = await SeedAccountAsync("del|confirm", "DEL 005");
        var before = await RowsFor(account);

        var wrong = await DeleteAsync(account, new FakeIdentity(), confirm: "someone.else@example.test");
        Assert.Equal(AccountDeletionOutcome.ConfirmationMismatch, wrong.Outcome);
        Assert.Equal("confirmEmail", wrong.Field);
        Assert.Equal(before, await RowsFor(account));

        var empty = await DeleteAsync(account, new FakeIdentity(), confirm: "");
        Assert.Equal(AccountDeletionOutcome.ConfirmationMismatch, empty.Outcome);
        Assert.Equal(before, await RowsFor(account));

        // Case-insensitive and trimmed, because an email address is not case-sensitive and a typed one arrives
        // with whatever the keyboard added. Refusing a correct address on its spelling would only teach someone
        // to paste harder.
        var shouting = await DeleteAsync(account, new FakeIdentity(), confirm: $"  {account.Email.ToUpperInvariant()} ");
        Assert.Equal(AccountDeletionOutcome.Deleted, shouting.Outcome);
        Assert.Equal(0, await RowsFor(account));
    }

    [Fact]
    public async Task Only_the_person_signed_in_as_the_account_can_delete_it()
    {
        var account = await SeedAccountAsync("del|holder", "DEL 006");
        var before = await RowsFor(account);

        await using var context = NewContext(TestOwner.As(account.OwnerId));
        var identity = new FakeIdentity();

        // A resolved owner is not enough. An assistant token carries one and no subject, which is what this
        // stands for — the route's Auth0 policy refuses it first, and the operation refuses it again.
        var result = await DeletionFor(context, identity)
            .DeleteAsync(account.OwnerId, subjectClaim: null, confirmEmail: account.Email);

        Assert.Equal(AccountDeletionOutcome.NotAccountHolder, result.Outcome);
        Assert.Empty(identity.Asked);
        Assert.Equal(before, await RowsFor(account));

        // Nor a different signed-in subject holding somebody's owner id.
        var impostor = await DeletionFor(context, identity)
            .DeleteAsync(account.OwnerId, "del|someone-else", account.Email);
        Assert.Equal(AccountDeletionOutcome.NotAccountHolder, impostor.Outcome);
        Assert.Equal(before, await RowsFor(account));
    }

    [Fact]
    public async Task No_account_behind_the_request_is_refused_rather_than_ignored()
    {
        await using var context = NewContext();
        var accounts = DeletionFor(context, new FakeIdentity());

        Assert.Equal(AccountDeletionOutcome.NoAccount,
            (await accounts.DeleteAsync(null, "del|nobody", "nobody@example.test")).Outcome);

        // A resolved owner id that names no row — the accessor and the database disagreeing — is the same
        // answer, not an exception.
        Assert.Equal(AccountDeletionOutcome.NoAccount,
            (await accounts.DeleteAsync(-1, "del|nobody", "nobody@example.test")).Outcome);

        Assert.Null(await accounts.GetSummaryAsync(null));
        Assert.Null(await accounts.GetSummaryAsync(-1));
    }

    // ---- the identity half --------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_identity_call_still_deletes_the_data_and_queues_the_login()
    {
        var account = await SeedAccountAsync("del|queued", "DEL 007");

        var refusing = new FakeIdentity(answer: IdentityDeletionResult.Failed("Auth0 Management returned 403."));
        var result = await DeleteAsync(account, refusing);

        // The 204 promises the data is gone, not that the identity already is. Data-first is chosen precisely
        // because this failure is the benign one: a login with nothing behind it.
        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.False(result.IdentityDeleted);
        Assert.Equal(0, await RowsFor(account));

        await using (var context = NewContext())
        {
            var pending = await context.PendingIdentityDeletions.SingleAsync(p => p.ExternalId == "del|queued");
            Assert.Equal(1, pending.Attempts);
            Assert.Contains("403", pending.LastError);
            Assert.Equal(Clock.GetUtcNow(), pending.RequestedAt);
        }

        // Recorded rather than logged, so the next pass can finish the job. The count is a lower bound because
        // the queue is one table shared by every test in this class — the claim is that this identity was asked
        // about and cleared, not how many others were sitting beside it.
        await using (var context = NewContext())
        {
            var working = new FakeIdentity();
            Assert.True(await DeletionFor(context, working).RetryPendingAsync() >= 1);
            Assert.Contains("del|queued", working.Asked);
        }

        await using (var context = NewContext())
            Assert.False(await context.PendingIdentityDeletions.AnyAsync(p => p.ExternalId == "del|queued"));
    }

    [Fact]
    public async Task An_unconfigured_provider_leaves_the_queue_alone_rather_than_marking_it()
    {
        var account = await SeedAccountAsync("del|stuck", "DEL 008");
        await DeleteAsync(account, new FakeIdentity(answer: IdentityDeletionResult.Failed("unreachable")));

        await using (var context = NewContext())
        {
            var silent = new FakeIdentity(configured: false);
            Assert.Equal(0, await DeletionFor(context, silent).RetryPendingAsync());
            Assert.Empty(silent.Asked);
        }

        // Still queued, and still carrying the real reason rather than fifty copies of "not configured" —
        // the credential is the fix, and burying the original error would hide what to fix it for.
        await using (var context = NewContext())
        {
            var pending = await context.PendingIdentityDeletions.SingleAsync(p => p.ExternalId == "del|stuck");
            Assert.Equal(1, pending.Attempts);
            Assert.Equal("unreachable", pending.LastError);
        }
    }

    /// <remarks>
    /// The worst reachable outcome in this whole lifecycle, and it needed nothing exotic to reach: a refused
    /// identity call, then the same person signing in again. Nothing consults the queue at the door, so they are
    /// provisioned a perfectly ordinary new account — and an hour later the retry pass would delete the login in
    /// front of it, orphaning everything they had put in since, while logging that it had succeeded.
    /// </remarks>
    [Fact]
    public async Task A_queued_identity_whose_subject_has_an_account_again_is_dropped_unsent()
    {
        var gone = await SeedAccountAsync("del|returned", "DEL 011");
        await DeleteAsync(gone, new FakeIdentity(answer: IdentityDeletionResult.Failed("Auth0 Management returned 403.")));

        // Seeded rather than provisioned: AccountProvisioner clears the queue itself, and this is the half that
        // must hold on its own — a row reaching the pass with a live account behind it is the case under test.
        var returned = await SeedAccountAsync("del|returned", "DEL 012");

        await using (var context = NewContext())
        {
            var identity = new FakeIdentity();
            await DeletionFor(context, identity).RetryPendingAsync();

            // Not asked about at all. Refusing the answer would not be enough — the provider was told to delete.
            Assert.DoesNotContain("del|returned", identity.Asked);
        }

        await using (var check = NewContext())
        {
            // Dropped, not left queued: the obligation was to erase the data, and that happened when the first
            // account went. Leaving the row would only mean asking again next hour.
            Assert.False(await check.PendingIdentityDeletions.AnyAsync(p => p.ExternalId == "del|returned"));
            Assert.True(await check.Users.AnyAsync(u => u.Id == returned.OwnerId));
        }
    }

    /// <remarks>
    /// The unique index was described as keeping the queue to one row per subject. Its actual effect on an
    /// unconditional insert was to make the second deletion impossible: on a deployment whose Management grant is
    /// permanently missing the row never clears, so the operation threw and 500'd forever.
    /// </remarks>
    [Fact]
    public async Task A_second_deletion_by_a_subject_already_in_the_queue_succeeds()
    {
        var refusing = new FakeIdentity(answer: IdentityDeletionResult.Failed("Auth0 Management returned 403."));

        var first = await SeedAccountAsync("del|again", "DEL 013");
        Assert.Equal(AccountDeletionOutcome.Deleted, (await DeleteAsync(first, refusing)).Outcome);

        var second = await SeedAccountAsync("del|again", "DEL 014");
        var result = await DeleteAsync(second, refusing);

        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.Equal(0, await RowsFor(second));

        await using var context = NewContext();
        // Single, so the queue still holds one row per subject — an upsert, not a second insert dodged.
        var pending = await context.PendingIdentityDeletions.SingleAsync(p => p.ExternalId == "del|again");

        // Restated for this request rather than accumulated across two: the count describes the erasure being
        // asked for now, and the first account's attempt was against a user row that no longer exists.
        Assert.Equal(1, pending.Attempts);
        Assert.Equal(Clock.GetUtcNow(), pending.RequestedAt);
    }

    /// <remarks>
    /// The bytes are the one part of the erasure that is best effort, and it is the part most likely to fail on a
    /// NAS. Failing the request would report a completed deletion as a 500 — and the retry that invites answers
    /// 401, because the caller no longer has an account to authenticate with.
    /// </remarks>
    [Fact]
    public async Task A_folder_that_cannot_be_removed_is_reported_rather_than_failing_the_deletion()
    {
        var account = await SeedAccountAsync("del|bytes", "DEL 015");

        // A store whose root was never configured, standing in for the causes that cannot be provoked portably —
        // a file held open by an indexer, a volume remounted read-only. What matters is that the throw happens
        // after the transaction has committed, which is true of all of them.
        var broken = new DocumentStore(new DocumentStorageOptions(string.Empty));

        var context = NewContext(TestOwner.As(account.OwnerId));
        var result = await DeletionFor(context, new FakeIdentity(), broken)
            .DeleteAsync(account.OwnerId, account.ExternalId, account.Email);

        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.True(result.IdentityDeleted);
        Assert.Equal(0, await RowsFor(account));

        // The residual files the class remarks admit to — still there, and named in the log, because nothing in
        // the database points at them any more and this line is all anyone will ever have to find them by.
        Assert.True(Directory.Exists(Path.Combine(_root, account.VehicleId.ToString())));
        Assert.Contains(_log.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains($"vehicle {account.VehicleId}"));
    }

    /// <remarks>
    /// The execution strategy re-runs the transaction body on the same <see cref="CarTrackerDbContext"/>, so
    /// whatever the failed attempt staged is still tracked. The residue is staged by hand here: the test context
    /// has no retrying strategy to produce it — Aspire's <c>EnrichNpgsqlDbContext</c> installs one in production
    /// and this suite cannot, the same blind spot the <c>BeginTransaction</c> comment names.
    /// </remarks>
    [Fact]
    public async Task A_retried_transaction_starts_from_a_clean_tracker()
    {
        var account = await SeedAccountAsync("del|retried", "DEL 016");

        await using var context = NewContext(TestOwner.As(account.OwnerId));
        context.PendingIdentityDeletions.Add(new PendingIdentityDeletion
        {
            ExternalId = account.ExternalId,
            RequestedAt = Clock.GetUtcNow(),
        });

        var result = await DeletionFor(context, new FakeIdentity())
            .DeleteAsync(account.OwnerId, account.ExternalId, account.Email);

        // Unswept, the staged row and the body's own insert both go and collide on
        // ix_pending_identity_deletions_external_id — so the retry that exists to absorb a transient failure is
        // what makes it permanent.
        Assert.Equal(AccountDeletionOutcome.Deleted, result.Outcome);
        Assert.Equal(0, await RowsFor(account));
    }

    // ---- the confirmation's own numbers -------------------------------------------------------------------

    [Fact]
    public async Task The_summary_states_what_is_about_to_go()
    {
        var account = await SeedAccountAsync("del|summary", "DEL 009");
        await SeedAccountAsync("del|summary-other", "DEL 010");

        await using var context = NewContext(TestOwner.As(account.OwnerId));
        var summary = await DeletionFor(context, new FakeIdentity()).GetSummaryAsync(account.OwnerId);

        Assert.NotNull(summary);
        Assert.Equal(account.Email, summary.Email);
        Assert.Equal(1, summary.VehicleCount);
        Assert.Equal(1, summary.DocumentCount);
        Assert.Equal(1, summary.AssistantTokenCount);
        Assert.True(summary.DocumentBytes > 0);

        // The ten log tables the fixture writes one row into each of, plus the two the vehicle's own creation
        // writes — the founding odometer reading at purchase, and the expense mirroring the purchase price. Not
        // a row more: check definitions and budget groups are configuration and anomalies are flags the app
        // raised, and counting those would inflate the figure the consent rests on.
        Assert.Equal(12, summary.LogEntryCount);
    }
}
