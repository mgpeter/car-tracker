using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Admin;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The one service that reads across owners, proved to actually do it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every context here is pinned with <see cref="TestOwner.As"/>, which is the whole design of the class.</b>
/// A context built with no accessor gets <c>BypassOwnership</c> and every ownership predicate matches every
/// row, so an isolation test written that way passes without isolating anything - the hazard
/// <c>TestOwner.As</c>'s doc comment warns about. This class has the mirror image of it: the service is
/// <i>supposed</i> to see everything, so a bypassed context would make these tests green whether or not a
/// single <c>IgnoreQueryFilters()</c> were present. Pinning to owner A means the vehicle filter is live and
/// every assertion about owner B's data is an assertion that the opt-out is really there.
/// </para>
/// <para>
/// <b>Verified by sabotage before being kept.</b> With every <c>IgnoreQueryFilters()</c> stripped from
/// <see cref="AdminReadService"/>, four of these seven go red: the two that read owner B's vehicles, plates,
/// documents and anomalies, the detail, and the deployment totals. The other three stay green and should -
/// they read <c>users</c>, <c>chat_usage</c> and <c>vehicle_lookup_usage</c>, none of which carry a query
/// filter, so there is nothing for them to opt out of. That is the same discipline
/// <c>Export_never_writes_synchronously_to_its_destination</c> was written under: a test that cannot fail
/// against the broken version is not testing the thing it claims to.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AdminReadServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;
    private int _ownerA;
    private int _ownerB;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_adminread");

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();

        // A clean slate each run: this class asserts on deployment-wide totals, which every other test's rows
        // would otherwise contribute to.
        await seed.ChatUsage.ExecuteDeleteAsync();
        await seed.VehicleLookupUsage.ExecuteDeleteAsync();
        await seed.Documents.ExecuteDeleteAsync();
        await seed.DataAnomalies.ExecuteDeleteAsync();
        await seed.AssistantTokens.ExecuteDeleteAsync();
        await seed.Vehicles.IgnoreQueryFilters().ExecuteDeleteAsync();
        await seed.ExpenseCategories.IgnoreQueryFilters().ExecuteDeleteAsync();
        await seed.Users.ExecuteDeleteAsync();

        _ownerA = await TestOwner.SeedAsync(seed, "admin|owner-a");
        _ownerB = await TestOwner.SeedAsync(seed, "admin|owner-b");

        await SeedOwnerBAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time, accessor);

    /// <summary>The service as a request would build it: pinned to owner A, who is the administrator.</summary>
    private AdminReadService ServiceAsOwnerA(CarTrackerDbContext db, string? compEmails = null) =>
        new(db, new PlanOptions { CompEmails = compEmails }, new Clock(_time));

    /// <summary>Owner B's world. None of it belongs to the administrator reading it back.</summary>
    private async Task SeedOwnerBAsync()
    {
        await using var db = NewContext(TestOwner.As(_ownerB));

        var vehicle = new Vehicle
        {
            OwnerId = _ownerB,
            Registration = "BT53 AKJ",
            Make = "Land Rover",
            Model = "Freelander",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        db.Documents.Add(new Document
        {
            VehicleId = vehicle.Id,
            Type = DocumentType.Other,
            Title = "MOT certificate",
            FilePath = "b/deadbeef",
            ContentType = "application/pdf",
            SizeBytes = 2_048,
            Source = EntrySource.Web,
        });

        db.DataAnomalies.Add(new DataAnomaly
        {
            VehicleId = vehicle.Id,
            Kind = AnomalyKind.MileageNonMonotonic,
            Severity = AnomalySeverity.Warning,
            EntityType = "MileageReading",
            Message = "83,000 above current",
            Status = AnomalyStatus.Open,
            Source = EntrySource.Web,
        });

        db.ChatUsage.Add(new ChatUsage
        {
            OwnerId = _ownerB,
            Day = new DateOnly(2026, 8, 22),
            InputTokens = 1_000,
            OutputTokens = 100,
            CacheReadTokens = 10,
            CacheWriteTokens = 1,
            Turns = 3,
        });

        db.ChatUsage.Add(new ChatUsage
        {
            OwnerId = _ownerB,
            Day = new DateOnly(2026, 8, 20),
            InputTokens = 500,
            OutputTokens = 50,
            Turns = 2,
        });

        db.VehicleLookupUsage.Add(new VehicleLookupUsage { OwnerId = _ownerB, Day = new DateOnly(2026, 8, 22), Lookups = 2 });

        db.AssistantTokens.Add(new AssistantToken
        {
            OwnerId = _ownerB,
            Name = "Laptop",
            TokenHash = "not-a-real-hash",
            Scope = AssistantScope.ReadOnly,
            CreatedAt = Reference,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_list_carries_an_account_the_reader_does_not_own()
    {
        await using var db = NewContext(TestOwner.As(_ownerA));

        var list = await ServiceAsOwnerA(db).ListAccountsAsync(limit: 100);
        var b = list.Users.Single(u => u.Id == _ownerB);

        // Every one of these reaches a table the reader has no rows in. The vehicle count and the plates come
        // through the filtered Vehicles set; documents and anomalies join to it.
        Assert.Equal(1, b.VehicleCount);
        Assert.Equal(1, b.DocumentCount);
        Assert.Equal(1, b.OpenAnomalyCount);
        Assert.Equal(1_111, b.ChatTokensToday);
        Assert.Equal(1_661, b.ChatTokens30d);
        Assert.Equal(5, b.ChatTurns30d);
        Assert.Equal(2, b.VehicleLookupsToday);
        Assert.Equal(1, b.AssistantTokenCount);
    }

    [Fact]
    public async Task No_full_registration_leaves_the_service()
    {
        await using var db = NewContext(TestOwner.As(_ownerA));

        var list = await ServiceAsOwnerA(db).ListAccountsAsync(limit: 100);
        var b = list.Users.Single(u => u.Id == _ownerB);

        Assert.Equal(["BT** **J"], b.MaskedRegistrations);
        Assert.DoesNotContain("BT53 AKJ", string.Join('|', b.MaskedRegistrations));

        var detail = await ServiceAsOwnerA(db).AccountAsync(_ownerB);
        Assert.Equal("BT** **J", detail!.Vehicles.Single().MaskedRegistration);
    }

    [Fact]
    public async Task The_detail_reaches_another_owner_and_carries_no_token_secret()
    {
        await using var db = NewContext(TestOwner.As(_ownerA));

        var detail = await ServiceAsOwnerA(db).AccountAsync(_ownerB);

        Assert.NotNull(detail);
        Assert.Equal("Land Rover", detail.Vehicles.Single().Make);
        Assert.Equal(2_048, detail.DocumentBytes);
        Assert.Equal(1, detail.OpenAnomaliesByKind[nameof(AnomalyKind.MileageNonMonotonic)]);
        Assert.Equal(2, detail.ChatUsageByDay.Count);

        var token = detail.AssistantTokens.Single();
        Assert.Equal("Laptop", token.Name);
        // The hash is not on the record at all, so this is a statement about the shape rather than the value.
        Assert.DoesNotContain("not-a-real-hash", string.Join('|', detail.AssistantTokens.Select(t => t.Name + t.Scope)));
    }

    [Fact]
    public async Task An_unknown_account_is_null_rather_than_empty()
    {
        await using var db = NewContext(TestOwner.As(_ownerA));

        Assert.Null(await ServiceAsOwnerA(db).AccountAsync(int.MaxValue));
    }

    [Fact]
    public async Task Deployment_usage_counts_a_day_the_reader_contributed_nothing_to()
    {
        await using var db = NewContext(TestOwner.As(_ownerA));

        var usage = await ServiceAsOwnerA(db).UsageAsync(days: 30);

        Assert.Equal(1_111, usage.Today.TotalTokens);
        Assert.Equal(3, usage.Today.Turns);
        Assert.Equal(1, usage.AccountsActiveToday);
        Assert.Equal(2, usage.ByDay.Count);
        Assert.Equal(2, usage.VehicleLookupsToday);

        // The totals are the deployment's, so they include the administrator's own empty account.
        Assert.Equal(2, usage.Totals.Accounts);
        Assert.Equal(1, usage.Totals.Vehicles);
        Assert.Equal(1, usage.Totals.Documents);
        Assert.Equal(2_048, usage.Totals.DocumentBytes);
        Assert.Equal(1, usage.Totals.AssistantTokens);

        var top = usage.TopAccounts.Single();
        Assert.Equal(_ownerB, top.UserId);
        Assert.Equal(1_661, top.Tokens);
    }

    [Fact]
    public async Task The_plan_on_a_row_is_the_plan_that_account_actually_resolves_to()
    {
        // The same resolver the entitlement path calls. If the admin list grew its own copy of the ladder the
        // two would drift and both answers would look plausible, which is why there is only one.
        await using var db = NewContext(TestOwner.As(_ownerA));

        var comped = await ServiceAsOwnerA(db, compEmails: "admin|owner-b@example.test").ListAccountsAsync(100);
        Assert.Equal(PlanReason.NotOnCompList, comped.Users.Single(u => u.Id == _ownerB).PlanReason);

        await using (var write = NewContext())
        {
            var user = await write.Users.SingleAsync(u => u.Id == _ownerB);
            user.PlanOverride = AccountPlan.Pro;
            await write.SaveChangesAsync();
        }

        await using var fresh = NewContext(TestOwner.As(_ownerA));
        var granted = await ServiceAsOwnerA(fresh, compEmails: "someone@example.test").ListAccountsAsync(100);
        var row = granted.Users.Single(u => u.Id == _ownerB);

        Assert.Equal(AccountPlan.Pro, row.Plan);
        Assert.Equal(PlanReason.AdminGranted, row.PlanReason);
        Assert.Equal(AccountPlan.Pro, row.PlanOverride);
    }

    [Fact]
    public async Task The_list_reports_the_total_beside_what_it_returned()
    {
        // An unpaged endpoint that truncates silently reads as the whole population, which is the one thing a
        // list of accounts must not do.
        await using var db = NewContext(TestOwner.As(_ownerA));

        var list = await ServiceAsOwnerA(db).ListAccountsAsync(limit: 1);

        Assert.Equal(2, list.TotalAccounts);
        Assert.Equal(1, list.Returned);
        Assert.Single(list.Users);
    }

    [Fact]
    public async Task The_account_limit_is_clamped_at_both_ends()
    {
        // A limit of zero or a negative would otherwise mean "return nothing" and read as an empty deployment;
        // an unbounded one is the paging this endpoint does not have.
        await using var db = NewContext(TestOwner.As(_ownerA));

        Assert.Equal(1, (await ServiceAsOwnerA(db).ListAccountsAsync(limit: 0)).Returned);
        Assert.Equal(1, (await ServiceAsOwnerA(db).ListAccountsAsync(limit: -5)).Returned);
        Assert.Equal(2, (await ServiceAsOwnerA(db).ListAccountsAsync(limit: int.MaxValue)).Returned);
    }

    [Fact]
    public async Task The_usage_window_is_clamped_and_a_short_one_excludes_older_days()
    {
        await using var db = NewContext(TestOwner.As(_ownerA));

        // One day means today only, so the 20 August row falls out and the 22nd stays.
        var oneDay = await ServiceAsOwnerA(db).UsageAsync(days: 1);
        Assert.Single(oneDay.ByDay);
        Assert.Equal(new DateOnly(2026, 8, 22), oneDay.ByDay[0].Day);

        // Zero and negative clamp up to one rather than returning an empty series that reads as no spend.
        Assert.Single((await ServiceAsOwnerA(db).UsageAsync(days: 0)).ByDay);
        Assert.Single((await ServiceAsOwnerA(db).UsageAsync(days: -3)).ByDay);
    }

    [Fact]
    public async Task The_database_posture_names_the_migration_this_container_is_actually_on()
    {
        // The fastest answer to "is this running the code I think it is", which is the question behind both
        // incidents the diagnostics endpoint exists for.
        await using var db = NewContext(TestOwner.As(_ownerA));

        var diagnostics = await ServiceAsOwnerA(db).DatabaseDiagnosticsAsync();

        Assert.NotNull(diagnostics.LastAppliedMigration);
        Assert.Contains("AddAdminObservability", diagnostics.LastAppliedMigration);
        Assert.Equal(0, diagnostics.PendingMigrationCount);
        Assert.Equal(0, diagnostics.PendingIdentityDeletions);
    }
}
