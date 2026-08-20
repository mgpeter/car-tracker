using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Accounts.Import;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Logs;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The export read back in: a faithful clone, and the four things it deliberately does not carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>The round trip is the headline and everything else is a corollary.</b> Export account A, import into an
/// empty account B, export B, and compare the two payloads with ids, timestamps, provenance and the four
/// not-imported blocks normalised away. It is the only test here that fails when a <i>table</i> is forgotten,
/// because every other test asserts on the tables somebody remembered - and forgetting a table is the failure
/// mode this feature actually has.
/// </para>
/// <para>
/// <b>Second is mirror fidelity.</b> The import inserts rows rather than replaying them through the factories,
/// precisely because a fill replayed writes its own mileage reading and its own mirrored expense on top of the
/// ones the file already contains. "The expense count equals the file's expense count" is the regression test
/// for that decision, and it is the assertion that would have caught the workbook's doubled-litres defect.
/// </para>
/// <para>
/// Against real PostgreSQL through Testcontainers, per the house rule, and here for a specific reason: the
/// partial unique index on <c>is_vehicle_purchase</c>, the per-owner unique registration index and the
/// <c>notes &lt;&gt; ''</c> check constraints are all things this code leans on and the in-memory provider
/// ignores every one of them.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AccountImportTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private string _root = string.Empty;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero));

    private const string AppVersion = "0.19.0";

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock, accessor);

    private AccountExportService ExportFor(CarTrackerDbContext context) => new(
        context,
        new LogQueryService(context, new Clock(Clock)),
        new ExpenseService(context, Scanner(context)),
        new DocumentService(context, new DocumentStore(new DocumentStorageOptions(_root))),
        Clock);

    private static AnomalyScanner Scanner(CarTrackerDbContext context) =>
        new(context, new VehicleMetricsLoader(context), Clock, new Clock(Clock));

    /// <summary>
    /// The service exactly as the container builds it, including the owner-pinned accessor.
    /// </summary>
    /// <remarks>
    /// Never a <c>BypassOwnership</c> context: it would make every ownership predicate match every row, so an
    /// isolation test written that way passes without isolating anything - the warning <c>TestOwner.As</c>
    /// carries in its own doc comment.
    /// </remarks>
    private AccountImportService ImportFor(CarTrackerDbContext context, ICurrentUserAccessor accessor) => new(
        context,
        accessor,
        new PendingImportStore(_cache),
        new ImportWriter(context, new ReferenceWriter(context, accessor), Scanner(context)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_accountimport");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-imp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _cache.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ---- fixture ------------------------------------------------------------------------------------------

    /// <summary>
    /// An account with a row in every table the export walks, so a table the import forgets shows up as an
    /// array that stopped matching.
    /// </summary>
    /// <remarks>
    /// Dates are deliberately distinct within each table. Several of the export's orderings break ties on
    /// <c>Id</c>, and the source's ids and the clone's ascend in different sequences, so a same-day pair could
    /// come back in the other order and fail the round trip for a reason that is not a bug.
    /// </remarks>
    private async Task<int> SeedAccountAsync(string externalId, string registration, string garage)
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
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Variant = "1.8 SE",
            Year = 2003, Colour = "Bonatti Grey", Vin = "SALLNABG73A123456",
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, PurchasePrice = 1_700m,
            Seller = "Private", EngineCode = "18K4F", EngineSizeCc = 1796, FuelType = FuelType.Petrol,
            Transmission = "Manual 5-speed", Drivetrain = "AWD (VCU)",
            MotExpirySeed = new DateOnly(2026, 8, 6), VedAnnualCost = 360m, VedExpiry = new DateOnly(2027, 3, 1),
            UlezCompliant = false, DefaultGarage = garage, Notes = "K-series: watch the head gasket.",
            Fluids = new FluidSpecs { OilSpec = "5W-30 A3/B4", OilCapacityLitres = 4.1m, CoolantSpec = "OAT (red)", FuelTankCapacityLitres = 59m },
            Tyres = new TyreSpecs { TyreSize = "215/65 R16", PressureFrontPsi = 30m, PressureRearPsi = 33m, MinTreadMm = 1.6m },
            Insurance = new InsurancePolicy { Insurer = "Adrian Flux", PolicyNumber = "AF-99123", PeriodStart = new DateOnly(2026, 3, 14), PeriodEnd = new DateOnly(2027, 3, 13), Premium = 412.55m, NcbYears = 9 },
            Breakdown = new BreakdownCover { Provider = "RAC", PolicyNumber = "RAC-7781", Expiry = new DateOnly(2027, 3, 13) },
            Source = EntrySource.Web,
        };

        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web);
        var vehicleId = vehicle.Id;

        var references = new ReferenceWriter(context, accessor);
        await references.EnsureGarageAsync(garage, "01234 567890", "Unit 4, Bridge Road", "Does the MOT.");
        await references.EnsureWashLocationAsync("Tesco Extra", "Jet wash round the back.");
        await context.SaveChangesAsync();

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 7, 8), Mileage = 80_705,
            Origin = MileageOrigin.Manual, Notes = "Read at the MOT bay.", Source = EntrySource.Web,
        });
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 7, 9), Category = "Repair",
            Amount = 129.99m, Vendor = "Euro Car Parts", PaymentMethod = "Card", Source = EntrySource.Web,
        });
        context.ServiceRecords.Add(new ServiceRecord
        {
            VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 7, 8), Type = "MOT", Mileage = 80_705,
            Garage = garage, WorkDone = "MOT test", Cost = 54.85m, NextDueDate = new DateOnly(2027, 7, 8),
            Notes = "Advisory: nearside headlamp lens hazed.", Source = EntrySource.Web,
        });
        context.TyreReadings.Add(new TyreReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 6, 1), Mileage = 79_400,
            PsiFrontLeft = 30m, PsiFrontRight = 30.5m, PsiRearLeft = 33m, PsiRearRight = 33m,
            TreadFrontLeft = 4.2m, TreadRearLeft = 5.1m, Location = "Home", Tool = "Ring gauge",
            Source = EntrySource.Web,
        });
        context.WashEntries.Add(new WashEntry
        {
            VehicleId = vehicleId, WashDate = new DateOnly(2026, 6, 20), Location = "Tesco Extra",
            WashType = "Jet wash", Cost = 4.50m, Mileage = 79_800, Source = EntrySource.Web,
        });
        context.MaintenanceTasks.Add(new MaintenanceTask
        {
            VehicleId = vehicleId, Title = $"Wiper blades for {registration}", Kind = MaintenanceTaskKind.DIY,
            Priority = Priority.Low, Status = MaintenanceTaskStatus.Open, EstimatedCost = 18m,
            TargetDate = new DateOnly(2026, 9, 1), Notes = "Bosch Aerotwin.", Source = EntrySource.Web,
        });
        context.EquipmentItems.Add(new EquipmentItem
        {
            VehicleId = vehicleId, Name = "Scissor jack", Category = "Recovery", Status = EquipmentStatus.Owned,
            PurchasedDate = new DateOnly(2026, 4, 1), Cost = 24.99m, SourceVendor = "Halfords",
            StoredAt = "Boot", Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        // Through the factory, deliberately: a fill is three rows - the entry, a mileage reading stamped Fuel,
        // and a mirrored expense in the Fuel category - and all three land in the export. A fixture that added
        // FuelEntry rows by hand would leave the import nothing to double, so the mirror-fidelity test below
        // would pass against an implementation that fires every mirror twice.
        var fuel = new FuelEntryFactory(context);
        await fuel.CreateAsync(
            new FuelEntry
            {
                VehicleId = vehicleId, EntryDate = new DateOnly(2026, 4, 2), Mileage = 77_881,
                Litres = 44.02m, PricePerLitre = 1.599m, TotalCost = 70.39m, Station = "Applegreen",
                FillLevel = FillLevel.Half,
            },
            EntrySource.Web);
        await fuel.CreateAsync(
            new FuelEntry
            {
                VehicleId = vehicleId, EntryDate = new DateOnly(2026, 5, 6), Mileage = 78_540,
                Litres = 47.31m, PricePerLitre = 1.612m, TotalCost = 76.26m, Station = "Shell",
                FillLevel = FillLevel.Full,
            },
            EntrySource.Web);

        var definitionId = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId).OrderBy(d => d.DisplayOrder).Select(d => d.Id).FirstAsync();
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = definitionId, PerformedOn = new DateOnly(2026, 7, 1),
            Result = CheckResult.Attention, Notes = "Mayonnaise on the filler cap.", Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var issues = new IssueService(context, new Clock(Clock));
        var issueId = (await issues.AddAsync(
            vehicleId,
            new IssueInput("Head gasket - K-series risk", new DateOnly(2026, 3, 14), Severity.Critical),
            EntrySource.Web)).Value!.Id;
        await issues.SetWatchAsync(vehicleId, issueId, [definitionId]);

        // A document row and an anomaly, so the four not-imported blocks are non-empty and their skip counts
        // are counts of something.
        context.DataAnomalies.Add(new DataAnomaly
        {
            VehicleId = vehicleId, Kind = AnomalyKind.MileageNonMonotonic, Severity = AnomalySeverity.Error,
            EntityType = "MileageReading", Message = "A reading goes backwards.",
            Detail = """{"mileage":83000,"currentMileage":80900}""",
            Status = AnomalyStatus.Open, CreatedAt = Clock.GetUtcNow(), Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var store = new DocumentStore(new DocumentStorageOptions(_root));
        await using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes($"certificate for {registration}")))
        {
            var stored = await store.SaveAsync(vehicleId, bytes, "application/pdf");
            await new DocumentService(context, store).RecordAsync(
                vehicleId, stored!, "application/pdf", DocumentType.MOT, $"MOT certificate - {registration}",
                new DateOnly(2026, 7, 8), null, null, null, null, EntrySource.Web);
        }

        var tokenHash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        context.AssistantTokens.Add(new AssistantToken
        {
            OwnerId = ownerId, Name = $"Claude Desktop ({registration})", TokenHash = tokenHash,
            Scope = AssistantScope.ReadWrite, CreatedAt = Clock.GetUtcNow(),
        });
        await context.SaveChangesAsync();

        return ownerId;
    }

    /// <summary>An account with nothing but its thirteen system categories - a fresh sign-up.</summary>
    private async Task<int> SeedEmptyAccountAsync(string externalId)
    {
        await using var context = NewContext();
        return await TestOwner.SeedAsync(context, externalId);
    }

    private async Task<string> ExportAsync(int ownerId)
    {
        await using var context = NewContext(TestOwner.As(ownerId));
        using var destination = new MemoryStream();
        await ExportFor(context).WriteAsync(ownerId, AppVersion, destination);
        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private static Stream FileOf(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    /// <summary>Preview then commit, as the two endpoints do, on two contexts as two requests would.</summary>
    private async Task<ImportCommitResult> ImportAsync(
        int ownerId, string json, IReadOnlyList<ImportVehicleDecision>? decisions = null)
    {
        string importId;
        await using (var context = NewContext(TestOwner.As(ownerId)))
        {
            var preview = await ImportFor(context, TestOwner.As(ownerId)).PreviewAsync(FileOf(json), AppVersion);
            Assert.Equal(ImportOutcome.Previewed, preview.Outcome);
            importId = preview.Preview!.ImportId;
        }

        await using (var context = NewContext(TestOwner.As(ownerId)))
        {
            return await ImportFor(context, TestOwner.As(ownerId)).CommitAsync(importId, decisions);
        }
    }

    // ---- the round trip -----------------------------------------------------------------------------------

    /// <summary>
    /// Export A, import into an empty B, export B, compare.
    /// </summary>
    /// <remarks>
    /// What is normalised away is the list of things that are <i>meant</i> to differ, and it is deliberately
    /// short: the ids (they belong to another database), the audit timestamps (these rows were created here,
    /// now), <c>source</c> (every imported row says so, which is the point of <c>EntrySource.Import</c>), the
    /// export's own header, the account block (an import cannot change who you are), the vehicle's notes (they
    /// gain the provenance line, asserted separately below) and the four blocks the spec says are not imported.
    /// Every other column of every other row is compared.
    /// </remarks>
    [Fact]
    public async Task Round_trips_an_account_into_an_empty_one()
    {
        var source = await SeedAccountAsync("import|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("import|destination");

        var original = await ExportAsync(source);
        var result = await ImportAsync(destination, original);
        Assert.Equal(ImportOutcome.Committed, result.Outcome);

        var clone = await ExportAsync(destination);

        Assert.Equal(Comparable(original), Comparable(clone));
    }

    [Fact]
    public async Task The_clone_carries_every_row_the_file_did()
    {
        var source = await SeedAccountAsync("counts|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("counts|destination");

        var report = (await ImportAsync(destination, await ExportAsync(source))).Report!;
        var vehicle = Assert.Single(report.Vehicles);

        await using var context = NewContext(TestOwner.As(destination));
        var vehicleId = await context.Vehicles.Where(v => v.OwnerId == destination).Select(v => v.Id).SingleAsync();

        Assert.Equal("BT53 AKJ", vehicle.Registration);
        Assert.Equal(2, await context.FuelEntries.CountAsync(f => f.VehicleId == vehicleId));
        Assert.Equal(1, await context.ServiceRecords.CountAsync(s => s.VehicleId == vehicleId));
        Assert.Equal(1, await context.TyreReadings.CountAsync(t => t.VehicleId == vehicleId));
        Assert.Equal(1, await context.WashEntries.CountAsync(w => w.VehicleId == vehicleId));
        Assert.Equal(1, await context.MaintenanceTasks.CountAsync(t => t.VehicleId == vehicleId));
        Assert.Equal(1, await context.EquipmentItems.CountAsync(e => e.VehicleId == vehicleId));
        Assert.Equal(1, await context.Issues.CountAsync(i => i.VehicleId == vehicleId));
        Assert.Equal(15, await context.CheckDefinitions.CountAsync(d => d.VehicleId == vehicleId));
        Assert.Equal(1, await context.IssueWatchChecks.CountAsync(
            w => context.Issues.Any(i => i.Id == w.IssueId && i.VehicleId == vehicleId)));
        Assert.Equal(1, await context.CheckLogs.CountAsync(
            l => context.CheckDefinitions.Any(d => d.Id == l.CheckDefinitionId && d.VehicleId == vehicleId)));
        Assert.Equal(4, await context.BudgetGroups.CountAsync(g => g.VehicleId == vehicleId));
    }

    /// <summary>
    /// <b>The regression test for the central decision.</b> A fill replayed through <c>FuelEntryFactory</c>
    /// writes three rows and the file already contains all three, so an import built on the factories would
    /// double every mirrored expense and every derived mileage reading - silently, which is the workbook's
    /// doubled-litres defect in a new costume.
    /// </summary>
    [Fact]
    public async Task No_mirror_fires_twice()
    {
        var source = await SeedAccountAsync("mirror|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("mirror|destination");

        var original = await ExportAsync(source);
        await ImportAsync(destination, original);

        var file = JsonNode.Parse(original)!["vehicles"]![0]!;

        await using var context = NewContext(TestOwner.As(destination));
        var vehicleId = await context.Vehicles.Where(v => v.OwnerId == destination).Select(v => v.Id).SingleAsync();

        Assert.Equal(
            file["expenses"]!.AsArray().Count,
            await context.ExpenseEntries.CountAsync(e => e.VehicleId == vehicleId));
        Assert.Equal(
            file["mileageReadings"]!.AsArray().Count,
            await context.MileageReadings.CountAsync(m => m.VehicleId == vehicleId));

        // And the mirrors still point at the right rows, remapped rather than dropped: a clone whose expenses
        // had all lost their fuelEntryId would pass the count above and be wrong.
        var mirrored = await context.ExpenseEntries
            .Where(e => e.VehicleId == vehicleId && e.FuelEntryId != null)
            .Select(e => e.FuelEntryId!.Value)
            .ToListAsync();
        var fills = await context.FuelEntries.Where(f => f.VehicleId == vehicleId).Select(f => f.Id).ToListAsync();

        Assert.Equal(2, mirrored.Count);
        Assert.All(mirrored, id => Assert.Contains(id, fills));
    }

    /// <summary>
    /// The strongest single statement that the clone is faithful, and it costs one assertion: the figures are
    /// computed from the rows, so equal figures mean the rows they are computed from are the same rows.
    /// </summary>
    [Fact]
    public async Task Derived_figures_over_the_clone_match_the_source()
    {
        var source = await SeedAccountAsync("derived|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("derived|destination");

        await ImportAsync(destination, await ExportAsync(source));

        await using var context = NewContext();
        var metrics = new DerivedMetricsService(new VehicleMetricsLoader(context), new Clock(Clock));

        var sourceId = await context.Vehicles.Where(v => v.OwnerId == source).Select(v => v.Id).SingleAsync();
        var cloneId = await context.Vehicles.Where(v => v.OwnerId == destination).Select(v => v.Id).SingleAsync();

        var a = await metrics.GetVehicleSummaryAsync(sourceId);
        var b = await metrics.GetVehicleSummaryAsync(cloneId);

        // Compared as the payload a screen would receive, with the row ids stripped, rather than member by
        // member: several of these records hold a list or a dictionary, and a record's generated equality
        // compares those by reference - so `Assert.Equal(a.Spend, b.Spend)` fails on two identical summaries
        // and would have failed just as loudly on two different ones. Serialising also means a figure added to
        // the summary later is compared without anyone remembering to add a line here.
        Assert.Equal(ComparableFigures(a!), ComparableFigures(b!));
    }

    // ---- collisions ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_registration_you_already_own_is_previewed_as_a_copy()
    {
        var owner = await SeedAccountAsync("collide|owner", "BT53 AKJ", "K & P Motors");
        var file = await ExportAsync(owner);

        await using var context = NewContext(TestOwner.As(owner));
        var preview = await ImportFor(context, TestOwner.As(owner)).PreviewAsync(FileOf(file), AppVersion);

        var vehicle = Assert.Single(preview.Preview!.Vehicles);
        Assert.True(vehicle.Collides);
        Assert.Equal("BT53 AKJ-2", vehicle.ProposedRegistration);

        // First, always. Renaming on collision gave up the idempotency the uniqueness index would otherwise
        // have provided for free, so this sentence is what stops an accidental second import.
        Assert.Contains("already", preview.Preview.Warnings[0]);
        Assert.StartsWith("1 of 1", preview.Preview.Warnings[0]);
    }

    /// <summary>
    /// Importing the same file twice silently succeeds and produces a second complete car. That is the accepted
    /// cost of renaming rather than refusing, and the first vehicle is not touched by it.
    /// </summary>
    [Fact]
    public async Task Importing_into_your_own_account_leaves_the_original_alone()
    {
        var owner = await SeedAccountAsync("twice|owner", "BT53 AKJ", "K & P Motors");
        var file = await ExportAsync(owner);

        var report = (await ImportAsync(owner, file)).Report!;
        Assert.Equal("BT53 AKJ-2", Assert.Single(report.Vehicles).Registration);
        Assert.Equal("BT53 AKJ", Assert.Single(report.Vehicles).ImportedFrom);

        await using var context = NewContext(TestOwner.As(owner));

        var original = await context.Vehicles.SingleAsync(v => v.Registration == "BT53 AKJ");
        var copy = await context.Vehicles.SingleAsync(v => v.Registration == "BT53 AKJ-2");

        // The copy is complete...
        Assert.Equal(2, await context.FuelEntries.CountAsync(f => f.VehicleId == copy.Id));
        Assert.Equal(15, await context.CheckDefinitions.CountAsync(d => d.VehicleId == copy.Id));

        // ...and the original is exactly as it was, including the note the owner wrote and the default flag.
        Assert.Equal("K-series: watch the head gasket.", original.Notes);
        Assert.True(original.IsDefault);
        Assert.Equal(EntrySource.Web, original.Source);
        Assert.Equal(2, await context.FuelEntries.CountAsync(f => f.VehicleId == original.Id));

        // ix_vehicles_default is unique per owner where is_default, so the copy cannot claim it and must not try.
        Assert.False(copy.IsDefault);
    }

    [Fact]
    public async Task A_third_import_is_proposed_the_third_registration()
    {
        var owner = await SeedAccountAsync("thrice|owner", "BT53 AKJ", "K & P Motors");
        var file = await ExportAsync(owner);

        await ImportAsync(owner, file);
        var report = (await ImportAsync(owner, file)).Report!;

        Assert.Equal("BT53 AKJ-3", Assert.Single(report.Vehicles).Registration);
    }

    [Fact]
    public async Task An_override_registration_is_used_instead_of_the_proposal()
    {
        var owner = await SeedAccountAsync("override|owner", "BT53 AKJ", "K & P Motors");

        var report = (await ImportAsync(owner, await ExportAsync(owner),
            [new ImportVehicleDecision(0, Registration: "BT53 AKJ SPARE")])).Report!;

        Assert.Equal("BT53 AKJ SPARE", Assert.Single(report.Vehicles).Registration);
    }

    /// <summary>
    /// The override is re-checked at commit rather than trusted from the preview: minutes pass between the two
    /// calls and a vehicle can be added in them. And the refusal leaves the id standing, so correcting one
    /// plate does not cost a re-upload of the whole file.
    /// </summary>
    [Fact]
    public async Task An_override_that_collides_is_refused_and_the_upload_survives_the_refusal()
    {
        var owner = await SeedAccountAsync("clash|owner", "BT53 AKJ", "K & P Motors");
        var file = await ExportAsync(owner);

        string importId;
        await using (var context = NewContext(TestOwner.As(owner)))
        {
            var preview = await ImportFor(context, TestOwner.As(owner)).PreviewAsync(FileOf(file), AppVersion);
            importId = preview.Preview!.ImportId;
        }

        await using (var context = NewContext(TestOwner.As(owner)))
        {
            var refused = await ImportFor(context, TestOwner.As(owner))
                .CommitAsync(importId, [new ImportVehicleDecision(0, Registration: "BT53 AKJ")]);

            Assert.Equal(ImportOutcome.Collision, refused.Outcome);
            Assert.Contains("BT53 AKJ", refused.Detail);
        }

        await using (var context = NewContext(TestOwner.As(owner)))
        {
            Assert.Equal(1, await context.Vehicles.CountAsync(v => v.OwnerId == owner));

            // Same id, corrected plate.
            var second = await ImportFor(context, TestOwner.As(owner))
                .CommitAsync(importId, [new ImportVehicleDecision(0, Registration: "BT53 AKJ-9")]);

            Assert.Equal(ImportOutcome.Committed, second.Outcome);
        }
    }

    [Fact]
    public async Task A_vehicle_can_be_left_out()
    {
        var source = await SeedAccountAsync("exclude|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("exclude|destination");

        var report = (await ImportAsync(destination, await ExportAsync(source),
            [new ImportVehicleDecision(0, Include: false)])).Report!;

        Assert.Empty(report.Vehicles);

        await using var context = NewContext(TestOwner.As(destination));
        Assert.Equal(0, await context.Vehicles.CountAsync(v => v.OwnerId == destination));
    }

    // ---- reference lists ----------------------------------------------------------------------------------

    /// <summary>
    /// Merged by name and never overwritten. Letting an imported file rewrite the account's own reference data
    /// is the cross-tenant write DEC-018 closed, self-inflicted and through the front door.
    /// </summary>
    [Fact]
    public async Task A_garage_you_already_have_is_matched_rather_than_rewritten()
    {
        var source = await SeedAccountAsync("ref|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("ref|destination");

        await using (var context = NewContext(TestOwner.As(destination)))
        {
            await new ReferenceWriter(context, TestOwner.As(destination))
                .EnsureGarageAsync("K & P Motors", "07700 900000", "My own note of where it is", null);
            await context.SaveChangesAsync();
        }

        var report = (await ImportAsync(destination, await ExportAsync(source))).Report!;

        Assert.Equal(0, report.Reference.GaragesCreated);
        Assert.Equal(1, report.Reference.WashLocationsCreated);

        await using var check = NewContext(TestOwner.As(destination));
        var garage = await check.Garages.SingleAsync(g => g.OwnerId == destination && g.Name == "K & P Motors");

        Assert.Equal("07700 900000", garage.Contact);
        Assert.Equal("My own note of where it is", garage.Address);
    }

    [Fact]
    public async Task A_garage_you_do_not_have_arrives_with_its_details()
    {
        var source = await SeedAccountAsync("newref|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("newref|destination");

        var report = (await ImportAsync(destination, await ExportAsync(source))).Report!;

        Assert.Equal(1, report.Reference.GaragesCreated);

        await using var context = NewContext(TestOwner.As(destination));
        var garage = await context.Garages.SingleAsync(g => g.OwnerId == destination);

        Assert.Equal("K & P Motors", garage.Name);
        Assert.Equal("01234 567890", garage.Contact);
        Assert.Equal("Does the MOT.", garage.Notes);
    }

    /// <summary>The thirteen an account is provisioned with are already there, so nothing is created.</summary>
    [Fact]
    public async Task The_system_expense_categories_are_matched_not_duplicated()
    {
        var source = await SeedAccountAsync("cat|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("cat|destination");

        var report = (await ImportAsync(destination, await ExportAsync(source))).Report!;

        Assert.Equal(0, report.Reference.ExpenseCategoriesCreated);

        await using var context = NewContext(TestOwner.As(destination));
        Assert.Equal(13, await context.ExpenseCategories.CountAsync(c => c.OwnerId == destination));
    }

    // ---- what is not imported -----------------------------------------------------------------------------

    [Fact]
    public async Task Documents_anomalies_and_tokens_are_skipped_and_counted()
    {
        var source = await SeedAccountAsync("skip|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("skip|destination");

        var report = (await ImportAsync(destination, await ExportAsync(source))).Report!;

        Assert.Equal(1, report.Skipped.Documents);
        Assert.Equal(1, report.Skipped.Anomalies);
        Assert.Equal(1, report.Skipped.AssistantTokens);

        await using var context = NewContext(TestOwner.As(destination));
        var vehicleId = await context.Vehicles.Where(v => v.OwnerId == destination).Select(v => v.Id).SingleAsync();

        Assert.Equal(0, await context.Documents.CountAsync(d => d.VehicleId == vehicleId));
        Assert.Equal(0, await context.AssistantTokens.CountAsync(t => t.OwnerId == destination));
    }

    /// <summary>
    /// Flags are re-derived rather than copied, so the integrity queue describes this database rather than
    /// another one. The source's own flag was hand-planted and describes a reading that is not in the file;
    /// the clone's flags are whatever its rows actually justify.
    /// </summary>
    [Fact]
    public async Task Anomaly_flags_are_worked_out_again_from_the_rows_that_landed()
    {
        var source = await SeedAccountAsync("flags|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("flags|destination");

        await ImportAsync(destination, await ExportAsync(source));

        await using var context = NewContext(TestOwner.As(destination));
        var vehicleId = await context.Vehicles.Where(v => v.OwnerId == destination).Select(v => v.Id).SingleAsync();

        var flags = await context.DataAnomalies.Where(a => a.VehicleId == vehicleId).ToListAsync();

        Assert.DoesNotContain(flags, f => f.Detail == """{"mileage":83000,"currentMileage":80900}""");
        Assert.All(flags, f => Assert.Equal(EntrySource.Import, f.Source));
    }

    [Fact]
    public async Task Every_imported_row_says_it_was_imported()
    {
        var source = await SeedAccountAsync("stamp|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("stamp|destination");

        await ImportAsync(destination, await ExportAsync(source));

        await using var context = NewContext(TestOwner.As(destination));
        var vehicleId = await context.Vehicles.Where(v => v.OwnerId == destination).Select(v => v.Id).SingleAsync();

        Assert.All(await context.FuelEntries.Where(x => x.VehicleId == vehicleId).ToListAsync(),
            x => Assert.Equal(EntrySource.Import, x.Source));
        Assert.All(await context.ExpenseEntries.Where(x => x.VehicleId == vehicleId).ToListAsync(),
            x => Assert.Equal(EntrySource.Import, x.Source));
        Assert.All(await context.MileageReadings.Where(x => x.VehicleId == vehicleId).ToListAsync(),
            x => Assert.Equal(EntrySource.Import, x.Source));
        Assert.All(await context.CheckDefinitions.Where(x => x.VehicleId == vehicleId).ToListAsync(),
            x => Assert.Equal(EntrySource.Import, x.Source));
    }

    /// <summary>
    /// The provenance line is the only place the original plate survives a rename, which is the mitigation the
    /// whole rename rule leans on. The owner's own note is kept above it, because it is theirs.
    /// </summary>
    [Fact]
    public async Task The_vehicles_notes_record_where_it_came_from()
    {
        var owner = await SeedAccountAsync("notes|owner", "BT53 AKJ", "K & P Motors");

        await ImportAsync(owner, await ExportAsync(owner));

        await using var context = NewContext(TestOwner.As(owner));
        var copy = await context.Vehicles.SingleAsync(v => v.Registration == "BT53 AKJ-2");

        Assert.StartsWith("K-series: watch the head gasket.", copy.Notes);
        Assert.Contains("BT53 AKJ", copy.Notes!["K-series: watch the head gasket.".Length..]);
        Assert.Contains("2026-08-19", copy.Notes);
    }

    // ---- refusals -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_preview_writes_nothing()
    {
        var source = await SeedAccountAsync("nowrite|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("nowrite|destination");

        await using var context = NewContext(TestOwner.As(destination));
        var preview = await ImportFor(context, TestOwner.As(destination))
            .PreviewAsync(FileOf(await ExportAsync(source)), AppVersion);

        Assert.Equal(ImportOutcome.Previewed, preview.Outcome);

        await using var check = NewContext(TestOwner.As(destination));
        Assert.Equal(0, await check.Vehicles.CountAsync(v => v.OwnerId == destination));
        Assert.Equal(0, await check.Garages.CountAsync(g => g.OwnerId == destination));
        Assert.Equal(0, await check.WashLocations.CountAsync(w => w.OwnerId == destination));
    }

    [Fact]
    public async Task A_truncated_file_is_refused_and_writes_nothing()
    {
        var source = await SeedAccountAsync("trunc|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("trunc|destination");
        var file = await ExportAsync(source);

        await using var context = NewContext(TestOwner.As(destination));
        var refused = await ImportFor(context, TestOwner.As(destination))
            .PreviewAsync(FileOf(file[..(file.Length / 2)]), AppVersion);

        Assert.Equal(ImportOutcome.Unreadable, refused.Outcome);

        await using var check = NewContext(TestOwner.As(destination));
        Assert.Equal(0, await check.Vehicles.CountAsync(v => v.OwnerId == destination));
    }

    [Fact]
    public async Task A_file_whose_expense_mirrors_a_missing_fill_is_refused_by_name()
    {
        var source = await SeedAccountAsync("orphan|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("orphan|destination");

        var file = JsonNode.Parse(await ExportAsync(source))!;
        file["vehicles"]![0]!["expenses"]![0]!["fuelEntryId"] = 4242;

        await using var context = NewContext(TestOwner.As(destination));
        var refused = await ImportFor(context, TestOwner.As(destination))
            .PreviewAsync(FileOf(file.ToJsonString()), AppVersion);

        Assert.Equal(ImportOutcome.Invalid, refused.Outcome);
        Assert.Contains("vehicles[0].expenses[0].fuelEntryId", refused.Errors!.Keys);

        await using var check = NewContext(TestOwner.As(destination));
        Assert.Equal(0, await check.Vehicles.CountAsync(v => v.OwnerId == destination));
    }

    [Fact]
    public async Task A_file_with_two_purchase_rows_is_refused()
    {
        var source = await SeedAccountAsync("twobuys|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("twobuys|destination");

        var file = JsonNode.Parse(await ExportAsync(source))!;
        foreach (var expense in file["vehicles"]![0]!["expenses"]!.AsArray())
        {
            expense!["isVehiclePurchase"] = true;
        }

        await using var context = NewContext(TestOwner.As(destination));
        var refused = await ImportFor(context, TestOwner.As(destination))
            .PreviewAsync(FileOf(file.ToJsonString()), AppVersion);

        Assert.Equal(ImportOutcome.Invalid, refused.Outcome);
        Assert.Contains("vehicles[0].expenses", refused.Errors!.Keys);
    }

    [Fact]
    public async Task An_unknown_import_id_is_not_found()
    {
        var owner = await SeedEmptyAccountAsync("unknown|owner");

        await using var context = NewContext(TestOwner.As(owner));
        var refused = await ImportFor(context, TestOwner.As(owner)).CommitAsync("imp_nothing", null);

        Assert.Equal(ImportOutcome.NotFound, refused.Outcome);
    }

    /// <summary>
    /// Somebody else's id answers exactly as an expired one does. Telling them apart would confirm the id is
    /// real, which is the same shape a cross-owner vehicle takes: not found, because for this account it is not.
    /// </summary>
    [Fact]
    public async Task Another_owners_import_id_is_not_found_rather_than_forbidden()
    {
        var source = await SeedAccountAsync("foreign|source", "BT53 AKJ", "K & P Motors");
        var stranger = await SeedEmptyAccountAsync("foreign|stranger");

        string importId;
        await using (var context = NewContext(TestOwner.As(source)))
        {
            var preview = await ImportFor(context, TestOwner.As(source))
                .PreviewAsync(FileOf(await ExportAsync(source)), AppVersion);
            importId = preview.Preview!.ImportId;
        }

        await using (var context = NewContext(TestOwner.As(stranger)))
        {
            var refused = await ImportFor(context, TestOwner.As(stranger)).CommitAsync(importId, null);

            Assert.Equal(ImportOutcome.NotFound, refused.Outcome);
        }

        await using (var check = NewContext(TestOwner.As(stranger)))
        {
            Assert.Equal(0, await check.Vehicles.CountAsync(v => v.OwnerId == stranger));
        }
    }

    [Fact]
    public async Task A_decision_about_a_vehicle_the_preview_never_described_is_refused()
    {
        var source = await SeedAccountAsync("badindex|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("badindex|destination");

        string importId;
        await using (var context = NewContext(TestOwner.As(destination)))
        {
            var preview = await ImportFor(context, TestOwner.As(destination))
                .PreviewAsync(FileOf(await ExportAsync(source)), AppVersion);
            importId = preview.Preview!.ImportId;
        }

        await using (var context = NewContext(TestOwner.As(destination)))
        {
            var refused = await ImportFor(context, TestOwner.As(destination))
                .CommitAsync(importId, [new ImportVehicleDecision(7)]);

            Assert.Equal(ImportOutcome.Invalid, refused.Outcome);
            Assert.Contains("vehicles[7]", refused.Errors!.Keys);
        }
    }

    // ---- isolation ----------------------------------------------------------------------------------------

    /// <summary>
    /// An import reads and writes one account's rows and no other's. Built with pinned accessors rather than a
    /// bypass context, because a bypass context makes every ownership predicate match and turns an isolation
    /// test into a false green.
    /// </summary>
    [Fact]
    public async Task An_import_touches_no_other_account()
    {
        var source = await SeedAccountAsync("iso|source", "BT53 AKJ", "K & P Motors");
        var bystander = await SeedAccountAsync("iso|bystander", "KV02 XYZ", "Northgate Tyres");
        var destination = await SeedEmptyAccountAsync("iso|destination");

        int[] before;
        await using (var context = NewContext(TestOwner.As(bystander)))
        {
            before =
            [
                await context.Vehicles.CountAsync(v => v.OwnerId == bystander),
                await context.Garages.CountAsync(g => g.OwnerId == bystander),
                await context.WashLocations.CountAsync(w => w.OwnerId == bystander),
                await context.ExpenseCategories.CountAsync(c => c.OwnerId == bystander),
            ];
        }

        await ImportAsync(destination, await ExportAsync(source));

        await using (var context = NewContext(TestOwner.As(bystander)))
        {
            Assert.Equal(before[0], await context.Vehicles.CountAsync(v => v.OwnerId == bystander));
            Assert.Equal(before[1], await context.Garages.CountAsync(g => g.OwnerId == bystander));
            Assert.Equal(before[2], await context.WashLocations.CountAsync(w => w.OwnerId == bystander));
            Assert.Equal(before[3], await context.ExpenseCategories.CountAsync(c => c.OwnerId == bystander));

            // The bystander's garage of a different name is untouched, and the destination did not adopt it.
            var theirs = await context.Garages.SingleAsync(g => g.OwnerId == bystander);
            Assert.Equal("Northgate Tyres", theirs.Name);
        }

        await using (var context = NewContext(TestOwner.As(destination)))
        {
            Assert.Equal(1, await context.Vehicles.CountAsync(v => v.OwnerId == destination));
            Assert.DoesNotContain(
                await context.Garages.Where(g => g.OwnerId == destination).Select(g => g.Name).ToListAsync(),
                name => name == "Northgate Tyres");
        }
    }

    // ---- the version warning ------------------------------------------------------------------------------

    [Fact]
    public async Task A_file_from_a_later_version_is_warned_about_rather_than_refused()
    {
        var source = await SeedAccountAsync("newer|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("newer|destination");

        var file = JsonNode.Parse(await ExportAsync(source))!;
        file["schemaVersion"] = "99.0.0";

        await using var context = NewContext(TestOwner.As(destination));
        var preview = await ImportFor(context, TestOwner.As(destination))
            .PreviewAsync(FileOf(file.ToJsonString()), AppVersion);

        Assert.Equal(ImportOutcome.Previewed, preview.Outcome);
        Assert.True(preview.Preview!.Source.NewerThanThisApp);
        Assert.Contains(preview.Preview.Warnings, w => w.Contains("99.0.0"));
    }

    [Fact]
    public async Task The_account_block_is_provenance_and_is_written_nowhere()
    {
        var source = await SeedAccountAsync("prov|source", "BT53 AKJ", "K & P Motors");
        var destination = await SeedEmptyAccountAsync("prov|destination");

        await using var context = NewContext(TestOwner.As(destination));
        var preview = await ImportFor(context, TestOwner.As(destination))
            .PreviewAsync(FileOf(await ExportAsync(source)), AppVersion);

        Assert.Equal("prov.source@example.test", preview.Preview!.Source.Email);

        await ImportAsync(destination, await ExportAsync(source));

        await using var check = NewContext();
        var user = await check.Users.SingleAsync(u => u.Id == destination);

        Assert.Equal("prov.destination@example.test", user.Email);
        Assert.Equal("prov|destination", user.ExternalId);
    }

    // ---- normalisation ------------------------------------------------------------------------------------

    /// <summary>
    /// A derived summary with nothing in it that names a row rather than describing one.
    /// </summary>
    /// <remarks>
    /// The ids go for the reason they go everywhere else here. <c>notes</c> goes because the clone's vehicle
    /// carries the provenance line, which is prose rather than a figure and is asserted on its own; every
    /// stored note is compared by the round trip. <c>integrity</c> goes for a reason of its own, below.
    /// </remarks>
    private static string ComparableFigures(object summary)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(summary, AccountExportService.Json))!;

        // The integrity block is the one figure that is *meant* to differ, and it is the point rather than an
        // exception: flags are not imported, they are worked out again from the rows that landed. The source's
        // own open flag was planted by hand and describes a reading the file does not contain, so the clone
        // correctly raises nothing. Asserting equality here would be asserting the flags were copied.
        root.AsObject().Remove("integrity");

        StripNotes(root);
        Strip(root);

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void StripNotes(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("notes");
                foreach (var property in obj.ToList()) StripNotes(property.Value);
                break;

            case JsonArray array:
                foreach (var item in array) StripNotes(item);
                break;
        }
    }

    /// <summary>
    /// Every key that names a row rather than describing one. They are dropped wherever they appear, at any
    /// depth, because the clone's ids belong to this database and the file's belong to another.
    /// </summary>
    private static readonly HashSet<string> IdAndAuditKeys = new(StringComparer.Ordinal)
    {
        "id", "ownerId", "vehicleId", "checkDefinitionId", "fuelEntryId", "serviceRecordId",
        "equipmentItemId", "washEntryId", "issueId", "expenseEntryId", "tokenId",
        "createdAt", "updatedAt",
        // Stamped Import on every imported row, deliberately and by design, so comparing it would be asserting
        // that the feature did not do the one thing it is meant to.
        "source",
    };

    private static string Comparable(string export)
    {
        var root = JsonNode.Parse(export)!.AsObject();

        // The file's own header, and the account it came from. An import writes neither.
        foreach (var key in new[] { "exportedAt", "schemaVersion", "notes", "account", "assistantTokens", "assistantWriteAudit" })
        {
            root.Remove(key);
        }

        foreach (var vehicle in root["vehicles"]?.AsArray() ?? [])
        {
            var block = vehicle!.AsObject();

            // The two the spec names as re-derived or unrestorable rather than copied.
            block.Remove("documents");
            block.Remove("anomalies");

            // The provenance line, which is asserted on its own above.
            block["profile"]?.AsObject().Remove("notes");
        }

        Strip(root);

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(IdAndAuditKeys.Contains).ToList())
                {
                    obj.Remove(key);
                }

                foreach (var property in obj.ToList()) Strip(property.Value);
                break;

            case JsonArray array:
                foreach (var item in array) Strip(item);
                break;
        }
    }
}
