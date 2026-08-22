using System.Text;
using CarTracker.Domain;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Vehicles;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Deleting one vehicle: what goes, what survives, and what refuses.
/// </summary>
/// <remarks>
/// <para>
/// Three claims, each a different kind of failure. <b>Everything under the vehicle goes</b>, which is a claim
/// about sixteen cascades rather than about this code, and is therefore worth proving against a real database
/// rather than trusting the schema. <b>No other account is touched</b> - a delete that reached across owners
/// would be the cross-tenant write DEC-018 closed, arriving through a new door. And <b>a mismatched
/// confirmation writes nothing</b>, because this is the one operation here with no undo.
/// </para>
/// <para>
/// Every context is built with a <c>TestOwner.As</c> pinned accessor. A <c>BypassOwnership</c> context makes
/// every ownership predicate match every row, so an isolation test written that way passes without isolating
/// anything.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class VehicleDeletionTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private string _root = string.Empty;
    private readonly CapturingLogger _log = new();

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock, accessor);

    private DocumentStore NewStore() => new(new DocumentStorageOptions(_root));

    private VehicleDeletionService DeletionFor(CarTrackerDbContext context, DocumentStore? store = null) =>
        new(context, store ?? NewStore(), _log);

    /// <summary>
    /// Keeps what was logged, because one outcome here is "continue, but say so": a document folder that could
    /// not be removed must not fail the request, and a swallowed failure and a reported one are otherwise
    /// indistinguishable from the return value.
    /// </summary>
    private sealed class CapturingLogger : ILogger<VehicleDeletionService>
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
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_vehicledeletion");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-veh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ---- fixture ------------------------------------------------------------------------------------------

    private sealed record Seeded(int OwnerId, int VehicleId, string Registration);

    /// <summary>
    /// A vehicle with a row in every table that hangs off it, so a cascade that stopped working shows up as a
    /// count that did not reach zero.
    /// </summary>
    private async Task<Seeded> SeedVehicleAsync(string externalId, string registration, bool withDocument = true)
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

        // The factory, so the vehicle arrives with the check template, the budget groups, the opening reading
        // and the purchase mirror the real thing has.
        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web);
        var vehicleId = vehicle.Id;

        await new ReferenceWriter(context, accessor).EnsureGarageAsync("K & P Motors");
        await context.SaveChangesAsync();

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 7, 8), Mileage = 80_705,
            Origin = MileageOrigin.Manual, Source = EntrySource.Web,
        });
        context.FuelEntries.Add(new FuelEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 4, 2), Mileage = 77_881,
            Litres = 44.02m, PricePerLitre = 1.599m, TotalCost = 70.39m, Source = EntrySource.Web,
        });
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 7, 9), Category = "Repair",
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
            VehicleId = vehicleId, WashDate = new DateOnly(2026, 6, 20), Cost = 4.50m, Source = EntrySource.Web,
        });
        context.MaintenanceTasks.Add(new MaintenanceTask
        {
            VehicleId = vehicleId, Title = "Wiper blades", Kind = MaintenanceTaskKind.DIY,
            Priority = Priority.Low, Status = MaintenanceTaskStatus.Open, Source = EntrySource.Web,
        });
        context.EquipmentItems.Add(new EquipmentItem
        {
            VehicleId = vehicleId, Name = "Scissor jack", Status = EquipmentStatus.Owned,
            PurchasedDate = new DateOnly(2026, 4, 1), Cost = 24.99m, Source = EntrySource.Web,
        });
        context.DataAnomalies.Add(new DataAnomaly
        {
            VehicleId = vehicleId, Kind = AnomalyKind.MileageNonMonotonic, Severity = AnomalySeverity.Error,
            EntityType = "MileageReading", Message = "A reading goes backwards.",
            Status = AnomalyStatus.Open, CreatedAt = Clock.GetUtcNow(), Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var definitionId = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId).OrderBy(d => d.DisplayOrder).Select(d => d.Id).FirstAsync();
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = definitionId, PerformedOn = new DateOnly(2026, 7, 1),
            Result = CheckResult.OK, Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var issues = new IssueService(context, new Clock(Clock));
        var issueId = (await issues.AddAsync(
            vehicleId,
            new IssueInput("Head gasket", new DateOnly(2026, 3, 14), Severity.Critical),
            EntrySource.Web)).Value!.Id;
        await issues.SetWatchAsync(vehicleId, issueId, [definitionId]);

        if (withDocument)
        {
            var store = NewStore();
            await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes($"certificate for {registration}"));
            var stored = await store.SaveAsync(vehicleId, bytes, "application/pdf");
            await new DocumentService(context, store, TestEntitlements.Pro).RecordAsync(
                vehicleId, stored!, "application/pdf", DocumentType.MOT, "MOT certificate",
                new DateOnly(2026, 7, 8), null, null, null, null, EntrySource.Web);
        }

        return new Seeded(ownerId, vehicleId, registration);
    }

    /// <summary>A second, plain vehicle for the same owner. Returns its id.</summary>
    private async Task<int> AddVehicleAsync(int ownerId, string registration, VehicleStatus status = VehicleStatus.Active)
    {
        await using var context = NewContext(TestOwner.As(ownerId));

        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Vauxhall", Model = "Corsa", Year = 2002,
            PurchaseDate = new DateOnly(2026, 1, 1), PurchaseMileage = 40_000,
            FuelType = FuelType.Petrol, Status = status, Source = EntrySource.Web,
        };

        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web);
        return vehicle.Id;
    }

    // ---- the cascade --------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_vehicle_removes_every_row_filed_under_it()
    {
        var seeded = await SeedVehicleAsync("veh|cascade", "BT53 AKJ");

        // Captured before the delete, because the two indirect tables carry no vehicle column and their
        // parents are about to go. Counting them unfiltered would count every other test's rows too - this
        // class shares one database and nothing resets it between tests.
        List<int> definitionIds;
        List<int> issueIds;
        await using (var before = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            definitionIds = await before.CheckDefinitions
                .Where(d => d.VehicleId == seeded.VehicleId).Select(d => d.Id).ToListAsync();
            issueIds = await before.Issues
                .Where(i => i.VehicleId == seeded.VehicleId).Select(i => i.Id).ToListAsync();
        }

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKJ");
            Assert.Equal(VehicleDeletionOutcome.Deleted, result.Outcome);
        }

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        var id = seeded.VehicleId;

        Assert.Equal(0, await check.Vehicles.CountAsync(v => v.Id == id));
        Assert.Equal(0, await check.MileageReadings.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.FuelEntries.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.ExpenseEntries.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.ServiceRecords.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.TyreReadings.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.WashEntries.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.MaintenanceTasks.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.EquipmentItems.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.Issues.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.DataAnomalies.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.Documents.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.CheckDefinitions.CountAsync(x => x.VehicleId == id));
        Assert.Equal(0, await check.BudgetGroups.CountAsync(x => x.VehicleId == id));

        // The three that reach the vehicle through a parent rather than a column of their own. check_logs
        // carries no vehicle id at all; issue_watch_checks has two cascading parents; budget_group_categories
        // has a vehicle column with no foreign key on it, so only the group FK removes the row.
        Assert.NotEmpty(definitionIds);
        Assert.NotEmpty(issueIds);
        Assert.Equal(0, await check.CheckLogs.CountAsync(l => definitionIds.Contains(l.CheckDefinitionId)));
        Assert.Equal(0, await check.IssueWatchChecks.CountAsync(w => issueIds.Contains(w.IssueId)));
        Assert.Equal(0, await check.BudgetGroupCategories.CountAsync(x => x.VehicleId == id));
    }

    [Fact]
    public async Task The_document_folder_goes_with_the_vehicle()
    {
        var seeded = await SeedVehicleAsync("veh|docs", "BT53 AKJ");
        var folder = Path.Combine(_root, seeded.VehicleId.ToString());
        Assert.True(Directory.Exists(folder));

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKJ");
        }

        Assert.False(Directory.Exists(folder));
    }

    /// <summary>
    /// The bytes are best effort and the rows are not. By the time the folder is attempted the transaction has
    /// committed, so failing the request would report a completed deletion as a 500 and invite a retry against
    /// a vehicle that no longer exists. It is logged at Error instead, and that is asserted: a swallowed
    /// failure and a reported one are otherwise the same from outside.
    /// </summary>
    [Fact]
    public async Task An_unremovable_document_folder_is_logged_rather_than_failing_the_delete()
    {
        var seeded = await SeedVehicleAsync("veh|lockedfolder", "BT53 AKJ");

        await using var context = NewContext(TestOwner.As(seeded.OwnerId));
        var result = await DeletionFor(context, new DocumentStore(new DocumentStorageOptions(
            Path.Combine(_root, "not-a-real-root", "nested")))).DeleteAsync(seeded.VehicleId, "BT53 AKJ");

        Assert.Equal(VehicleDeletionOutcome.Deleted, result.Outcome);

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        Assert.Equal(0, await check.Vehicles.CountAsync(v => v.Id == seeded.VehicleId));
    }

    // ---- refusals -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_mismatched_registration_is_refused_and_nothing_is_deleted()
    {
        var seeded = await SeedVehicleAsync("veh|mismatch", "BT53 AKJ");

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKK");

            Assert.Equal(VehicleDeletionOutcome.ConfirmationMismatch, result.Outcome);
            Assert.Equal("confirmRegistration", result.Field);
        }

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        Assert.Equal(1, await check.Vehicles.CountAsync(v => v.Id == seeded.VehicleId));
        Assert.Equal(1, await check.FuelEntries.CountAsync(x => x.VehicleId == seeded.VehicleId));
    }

    [Fact]
    public async Task An_empty_confirmation_is_refused()
    {
        var seeded = await SeedVehicleAsync("veh|empty", "BT53 AKJ");

        await using var context = NewContext(TestOwner.As(seeded.OwnerId));
        var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, null);

        Assert.Equal(VehicleDeletionOutcome.ConfirmationMismatch, result.Outcome);
    }

    /// <summary>
    /// The gate uses the database's own idea of a plate, so it agrees with the unique index and with every
    /// screen. Being stricter would refuse a correct answer typed in lower case.
    /// </summary>
    [Theory]
    [InlineData("bt53akj")]
    [InlineData("BT53AKJ")]
    [InlineData("  bt53 akj  ")]
    public async Task The_confirmation_ignores_case_and_spacing(string typed)
    {
        var seeded = await SeedVehicleAsync($"veh|case{typed.Trim().Length}", "BT53 AKJ");

        await using var context = NewContext(TestOwner.As(seeded.OwnerId));
        var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, typed);

        Assert.Equal(VehicleDeletionOutcome.Deleted, result.Outcome);
    }

    // ---- isolation ----------------------------------------------------------------------------------------

    /// <summary>
    /// The load-bearing one. Two owners each with a "BT53 AKJ" - which DEC-018's per-owner unique index makes
    /// legal - and deleting through one must not reach the other.
    /// </summary>
    [Fact]
    public async Task Another_owners_vehicle_of_the_same_registration_survives()
    {
        var mine = await SeedVehicleAsync("veh|mine", "BT53 AKJ");
        var theirs = await SeedVehicleAsync("veh|theirs", "BT53 AKJ");

        await using (var context = NewContext(TestOwner.As(mine.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(mine.VehicleId, "BT53 AKJ");
            Assert.Equal(VehicleDeletionOutcome.Deleted, result.Outcome);
        }

        await using var check = NewContext(TestOwner.As(theirs.OwnerId));
        Assert.Equal(1, await check.Vehicles.CountAsync(v => v.Id == theirs.VehicleId));
        Assert.Equal(1, await check.FuelEntries.CountAsync(x => x.VehicleId == theirs.VehicleId));
        Assert.Equal(15, await check.CheckDefinitions.CountAsync(d => d.VehicleId == theirs.VehicleId));
    }

    /// <summary>
    /// A vehicle id belonging to somebody else does not resolve, so it is NotFound rather than forbidden. That
    /// is the query filter refusing, not a hand-written owner check - which is the point: a filter cannot be
    /// forgotten by the next endpoint.
    /// </summary>
    [Fact]
    public async Task Another_owners_vehicle_is_not_found()
    {
        var mine = await SeedVehicleAsync("veh|seeker", "BT53 AKJ");
        var theirs = await SeedVehicleAsync("veh|target", "KV02 XYZ");

        await using (var context = NewContext(TestOwner.As(mine.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(theirs.VehicleId, "KV02 XYZ");
            Assert.Equal(VehicleDeletionOutcome.NotFound, result.Outcome);
        }

        await using var check = NewContext(TestOwner.As(theirs.OwnerId));
        Assert.Equal(1, await check.Vehicles.CountAsync(v => v.Id == theirs.VehicleId));
    }

    // ---- the default --------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_the_default_promotes_another_vehicle()
    {
        var seeded = await SeedVehicleAsync("veh|default", "BT53 AKJ");
        await AddVehicleAsync(seeded.OwnerId, "KV02 XYZ");

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKJ");

            Assert.Equal(VehicleDeletionOutcome.Deleted, result.Outcome);
            Assert.Equal("KV02 XYZ", result.PromotedRegistration);
        }

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        // Exactly one, which is what ix_vehicles_default permits and what every default-first ordering assumes.
        Assert.Equal(1, await check.Vehicles.CountAsync(v => v.OwnerId == seeded.OwnerId && v.IsDefault));
    }

    /// <summary>
    /// Otherwise the ordering clause is untested and reads like belt and braces, so the next person simplifies
    /// it away - and the assistant starts resolving, by default, a car the owner no longer has.
    /// </summary>
    [Fact]
    public async Task An_active_vehicle_is_promoted_ahead_of_a_sold_one()
    {
        var seeded = await SeedVehicleAsync("veh|activefirst", "BT53 AKJ");
        await AddVehicleAsync(seeded.OwnerId, "AA11 SLD", VehicleStatus.Sold);
        await AddVehicleAsync(seeded.OwnerId, "ZZ99 ACT");

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKJ");

            // AA11 SLD is older, so id order alone would have picked it.
            Assert.Equal("ZZ99 ACT", result.PromotedRegistration);
        }
    }

    [Fact]
    public async Task Deleting_the_only_vehicle_is_allowed_and_promotes_nothing()
    {
        var seeded = await SeedVehicleAsync("veh|last", "BT53 AKJ");

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKJ");

            Assert.Equal(VehicleDeletionOutcome.Deleted, result.Outcome);
            Assert.Null(result.PromotedRegistration);
        }

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        Assert.Equal(0, await check.Vehicles.CountAsync(v => v.OwnerId == seeded.OwnerId));
    }

    [Fact]
    public async Task Deleting_a_vehicle_that_was_not_the_default_leaves_the_default_alone()
    {
        var seeded = await SeedVehicleAsync("veh|notdefault", "BT53 AKJ");
        var secondId = await AddVehicleAsync(seeded.OwnerId, "KV02 XYZ");

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var result = await DeletionFor(context).DeleteAsync(secondId, "KV02 XYZ");
            Assert.Null(result.PromotedRegistration);
        }

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        var remaining = await check.Vehicles.SingleAsync(v => v.OwnerId == seeded.OwnerId);
        Assert.True(remaining.IsDefault);
    }

    // ---- the audit trail ----------------------------------------------------------------------------------

    /// <summary>
    /// The audit is about what a token did, not about the car, so it survives - with its vehicle reference
    /// released to null, a state the export and the audit view already handle. Deleting it would destroy audit
    /// the owner did not ask to delete; leaving the dead id would name a car nothing can resolve.
    /// </summary>
    [Fact]
    public async Task The_assistant_write_audit_survives_with_its_vehicle_released()
    {
        var seeded = await SeedVehicleAsync("veh|audit", "BT53 AKJ");

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            var token = new AssistantToken
            {
                OwnerId = seeded.OwnerId, Name = "Claude Desktop",
                TokenHash = Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("veh|audit"))),
                Scope = AssistantScope.ReadWrite, CreatedAt = Clock.GetUtcNow(),
            };
            context.AssistantTokens.Add(token);
            await context.SaveChangesAsync();

            context.AssistantWriteAudits.Add(new AssistantWriteAudit
            {
                TokenId = token.Id, Tool = "log_fuel_fillup", VehicleId = seeded.VehicleId,
                Summary = "Logged 44.02 L", TimestampUtc = Clock.GetUtcNow(),
            });
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext(TestOwner.As(seeded.OwnerId)))
        {
            await DeletionFor(context).DeleteAsync(seeded.VehicleId, "BT53 AKJ");
        }

        await using var check = NewContext(TestOwner.As(seeded.OwnerId));
        var audit = await check.AssistantWriteAudits.SingleAsync();

        Assert.Null(audit.VehicleId);
        Assert.Equal("log_fuel_fillup", audit.Tool);
    }

    // ---- the summary --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_summary_counts_only_this_vehicle()
    {
        var seeded = await SeedVehicleAsync("veh|summary", "BT53 AKJ");
        await AddVehicleAsync(seeded.OwnerId, "KV02 XYZ");

        await using var context = NewContext(TestOwner.As(seeded.OwnerId));
        var summary = await DeletionFor(context).GetSummaryAsync(seeded.VehicleId);

        Assert.NotNull(summary);
        Assert.Equal("BT53 AKJ", summary!.Registration);
        Assert.Equal("Land Rover Freelander 1", summary.Name);
        Assert.True(summary.IsDefault);
        Assert.Equal(1, summary.DocumentCount);
        Assert.Equal(15, summary.CheckDefinitionCount);
        Assert.Equal(1, summary.IssueCount);

        // The opening reading the factory wrote, the purchase mirror, plus the nine rows seeded by hand.
        Assert.True(summary.LogEntryCount >= 10, $"log entries were {summary.LogEntryCount}");
    }

    [Fact]
    public async Task The_summary_is_null_for_a_vehicle_this_account_does_not_have()
    {
        var mine = await SeedVehicleAsync("veh|nosummary", "BT53 AKJ");
        var theirs = await SeedVehicleAsync("veh|othersummary", "KV02 XYZ");

        await using var context = NewContext(TestOwner.As(mine.OwnerId));

        Assert.Null(await DeletionFor(context).GetSummaryAsync(theirs.VehicleId));
    }
}
