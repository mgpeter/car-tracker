using System.Text;
using System.Text.Json;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Logs;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The account export (UK GDPR Art. 15 and Art. 20) — what it carries, and far more importantly what it does not.
/// </summary>
/// <remarks>
/// <para>
/// Three claims, and each of them is a different kind of failure. <b>No derived figure</b>, because an export
/// carrying stored MPG or a stored cost-per-mile would reproduce the five workbook defects in the one artefact
/// nobody can recompute later. <b>No token secret</b>, because the file is downloaded, mailed and left in
/// folders. <b>No other account's rows</b>, because a subject access response that includes a stranger's data
/// is a breach in the act of complying with the law.
/// </para>
/// <para>
/// The derived-key assertion walks every property name in the payload rather than checking a handful of known
/// places. A new screen wrapper serialised into the export by accident is exactly the mistake that would pass a
/// spot check — this fails on the name wherever it appears, at any depth.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AccountExportTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private string _root = string.Empty;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock, accessor);

    private DocumentStore NewStore() => new(new DocumentStorageOptions(_root));

    private AccountExportService ExportFor(CarTrackerDbContext context) => new(
        context,
        new LogQueryService(context, new Clock(Clock)),
        new ExpenseService(context, new AnomalyScanner(context, new VehicleMetricsLoader(context), Clock, new Clock(Clock))),
        new DocumentService(context, NewStore(), TestEntitlements.Pro),
        Clock);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_accountexport");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-exp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// Every name <see cref="IDerivedMetricsService"/> and the log wrappers put on the wire.
    /// </summary>
    /// <remarks>
    /// Not an exhaustive list of everything derived — it cannot be, since the point of derived figures is that
    /// there can always be another one. It is the set that would actually arrive if someone serialised a screen's
    /// read into the export: the fleet fuel stats, the spend rollups, the check statuses, and the four wrappers
    /// (<c>TaskLog</c>, <c>IssueLog</c>, <c>DocumentLog</c>, and the reference lists' counts) whose rows this
    /// export deliberately unwraps.
    /// </remarks>
    private static readonly HashSet<string> DerivedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "mpg", "milesPerGallon", "averageMpg", "bestMpg", "worstMpg", "litresPer100Km", "fullTankRangeMiles",
        "costPerMile", "costPerMileExcludingPurchase", "totalSincePurchase", "totalSincePurchaseExcludingPurchase",
        "monthlyAverage", "monthlyAverageExcludingPurchase", "yearToDate", "rollups", "spend",
        "checkStatus", "checksStatus", "overallStatus", "attentionCount", "neverLoggedCount", "overdueCount",
        "daysUntil", "daysRemaining", "renewals", "watches", "watch", "isLapsed",
        "bundleCost", "bundleCount", "openEstimateTotal", "worstCaseCost", "monitoringCount", "resolvedCount",
        "totalCount", "totalSizeBytes", "papers", "photos", "linkedTo", "referenceCount",
    };

    // ---- fixture ------------------------------------------------------------------------------------------

    private sealed record Account(int OwnerId, int VehicleId, string TokenHash);

    /// <summary>An account with a row in every table the export walks, so an omission shows up as an empty array.</summary>
    private async Task<Account> SeedAccountAsync(string externalId, string registration, string garage)
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
        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web);
        var vehicleId = vehicle.Id;

        await new ReferenceWriter(context, accessor).EnsureGarageAsync(garage);

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 7, 8), Mileage = 80_705,
            Origin = MileageOrigin.Manual, Source = EntrySource.Web,
        });
        // A half fill: the one row whose stored field the whole MPG segment rule hangs off, and the one an
        // export that flattened its columns would quietly make un-recomputable.
        context.FuelEntries.Add(new FuelEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 4, 2), Mileage = 77_881,
            Litres = 44.02m, PricePerLitre = 1.599m, TotalCost = 70.39m, Station = "Applegreen",
            FillLevel = FillLevel.Half, Source = EntrySource.Web,
        });
        context.ExpenseEntries.Add(new ExpenseEntry
        {
            VehicleId = vehicleId, EntryDate = new DateOnly(2026, 7, 8), Category = "Repair",
            Amount = 129.99m, Source = EntrySource.Web,
        });
        context.ServiceRecords.Add(new ServiceRecord
        {
            VehicleId = vehicleId, ServiceDate = new DateOnly(2026, 7, 8), Type = "MOT", Mileage = 80_705,
            Garage = garage, Source = EntrySource.Web,
        });
        context.TyreReadings.Add(new TyreReading
        {
            VehicleId = vehicleId, ReadingDate = new DateOnly(2026, 6, 1), PsiFrontLeft = 30m, Source = EntrySource.Web,
        });
        context.WashEntries.Add(new WashEntry
        {
            VehicleId = vehicleId, WashDate = new DateOnly(2026, 6, 20), Cost = 4.50m, Source = EntrySource.Web,
        });
        context.MaintenanceTasks.Add(new MaintenanceTask
        {
            VehicleId = vehicleId, Title = $"Wiper blades for {registration}", Kind = MaintenanceTaskKind.DIY,
            Priority = Priority.Low, Status = MaintenanceTaskStatus.Open, EstimatedCost = 18m,
            Source = EntrySource.Web,
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
            Detail = """{"mileage":83000,"currentMileage":80900}""",
            Status = AnomalyStatus.Open, CreatedAt = Clock.GetUtcNow(), Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        var definitionId = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId).OrderBy(d => d.DisplayOrder).Select(d => d.Id).FirstAsync();
        context.CheckLogs.Add(new CheckLog
        {
            CheckDefinitionId = definitionId, PerformedOn = new DateOnly(2026, 7, 1),
            Result = CheckResult.Attention, Source = EntrySource.Web,
        });

        // No budget group is added by hand: VehicleFactory seeds the four default groups with their category
        // memberships, and ix_budget_group_category_vehicle_category is unique per vehicle, so a second "Repair"
        // would collide with the one the template already placed.

        // A real hash: ix_assistant_tokens_hash is unique across every account, and it also gives the
        // "no secret in the payload" assertion below a distinctive string to look for.
        var tokenHash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(externalId)));
        var token = new AssistantToken
        {
            OwnerId = ownerId, Name = $"Claude Desktop ({registration})", TokenHash = tokenHash,
            Scope = AssistantScope.ReadWrite, CreatedAt = Clock.GetUtcNow(),
        };
        context.AssistantTokens.Add(token);
        await context.SaveChangesAsync();

        context.AssistantWriteAudits.Add(new AssistantWriteAudit
        {
            TokenId = token.Id, Tool = "log_fuel_fillup", VehicleId = vehicleId,
            Summary = $"Logged 44.02 L for {registration}", TimestampUtc = Clock.GetUtcNow(),
        });
        await context.SaveChangesAsync();

        var issues = new IssueService(context, new Clock(Clock));
        var issueId = (await issues.AddAsync(
            vehicleId,
            new IssueInput("Head gasket — K-series risk", new DateOnly(2026, 3, 14), Severity.Critical),
            EntrySource.Web)).Value!.Id;
        await issues.SetWatchAsync(vehicleId, issueId, [definitionId]);

        await using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes($"certificate for {registration}")))
        {
            var stored = await NewStore().SaveAsync(vehicleId, bytes, "application/pdf");
            await new DocumentService(context, NewStore(), TestEntitlements.Pro).RecordAsync(
                vehicleId, stored!, "application/pdf", DocumentType.MOT, $"MOT certificate — {registration}",
                new DateOnly(2026, 7, 8), null, null, null, null, EntrySource.Web);
        }

        return new Account(ownerId, vehicleId, tokenHash);
    }

    private async Task<(string Text, JsonDocument Json)> ExportAsync(Account account)
    {
        await using var context = NewContext(TestOwner.As(account.OwnerId));
        using var buffer = new MemoryStream();
        await ExportFor(context).WriteAsync(account.OwnerId, "0.13.0", buffer);

        var text = Encoding.UTF8.GetString(buffer.ToArray());
        return (text, JsonDocument.Parse(text));
    }

    /// <summary>Every property name in the document, at every depth.</summary>
    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in PropertyNames(property.Value)) yield return nested;
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in PropertyNames(item)) yield return nested;

                break;
        }
    }

    // ---- the three claims ---------------------------------------------------------------------------------

    [Fact]
    public async Task No_derived_figure_appears_anywhere_in_the_payload()
    {
        var account = await SeedAccountAsync("exp|derived", "EXP 001", "K & P Motors");
        var (_, json) = await ExportAsync(account);
        using var _json = json;

        var offenders = PropertyNames(json.RootElement).Where(DerivedKeys.Contains).Distinct().ToList();

        Assert.True(offenders.Count == 0,
            $"the export carries computed figures it must not: {string.Join(", ", offenders)}");

        // And the payload says so itself, because an absence is otherwise indistinguishable from an oversight.
        var notes = json.RootElement.GetProperty("notes").EnumerateArray().Select(n => n.GetString()!).ToList();
        Assert.Contains(notes, n => n.Contains("never stored", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notes, n => n.Contains("Document files are not included", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_token_secret_appears_and_the_audit_trail_does()
    {
        var account = await SeedAccountAsync("exp|tokens", "EXP 002", "Halfords Autocentre");
        var (text, json) = await ExportAsync(account);
        using var _json = json;

        // The stored hash is not a secret, but it is the nearest thing to one in the schema and there is no
        // reason for it to travel. Asserted on the raw text, so a nested shape cannot smuggle it past a
        // property-name check.
        Assert.DoesNotContain(account.TokenHash, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tokenHash", PropertyNames(json.RootElement));

        var token = Assert.Single(json.RootElement.GetProperty("assistantTokens").EnumerateArray().ToList());
        Assert.Equal("Claude Desktop (EXP 002)", token.GetProperty("name").GetString());
        // An enum as a string, like every other payload the app produces — a hand-rolled writer inherits nothing
        // from ConfigureHttpJsonOptions, so this is the assertion that the settings were actually stated.
        Assert.Equal("ReadWrite", token.GetProperty("scope").GetString());

        // Included deliberately: a record of changes made to this person's data is part of what they are owed,
        // and leaving it out silently is the failure the notes array exists to prevent.
        var write = Assert.Single(json.RootElement.GetProperty("assistantWriteAudit").EnumerateArray().ToList());
        Assert.Equal("log_fuel_fillup", write.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task Not_one_row_of_another_account_is_in_it()
    {
        var mine = await SeedAccountAsync("exp|mine", "EXP 003", "K & P Motors");
        var theirs = await SeedAccountAsync("exp|theirs", "EXP 004", "Kwik Fit Bangor");

        var (text, json) = await ExportAsync(mine);
        using var _json = json;

        // Every one of these is the other account's and every one of them is reached by a different route —
        // the vehicle by the owner filter, the garage by a shared-name reference list, the token by owner id,
        // and the task title through a per-vehicle log read.
        Assert.DoesNotContain("EXP 004", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Kwik Fit Bangor", text, StringComparison.Ordinal);
        Assert.DoesNotContain(theirs.TokenHash, text, StringComparison.Ordinal);

        var vehicle = Assert.Single(json.RootElement.GetProperty("vehicles").EnumerateArray().ToList());
        Assert.Equal("EXP 003", vehicle.GetProperty("registration").GetString());
        Assert.Equal("exp|mine", json.RootElement.GetProperty("account").GetProperty("externalId").GetString());
    }

    // ---- completeness -------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_table_the_account_owns_is_present_and_populated()
    {
        var account = await SeedAccountAsync("exp|complete", "EXP 005", "K & P Motors");
        var (_, json) = await ExportAsync(account);
        using var _json = json;

        var root = json.RootElement;
        Assert.Equal("0.13.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(Clock.GetUtcNow(), root.GetProperty("exportedAt").GetDateTimeOffset());

        Assert.Equal("K & P Motors",
            root.GetProperty("reference").GetProperty("garages")[0].GetProperty("name").GetString());
        // Created as used, so an account that has never washed a car has an empty list rather than a missing key.
        Assert.Equal(JsonValueKind.Array, root.GetProperty("reference").GetProperty("washLocations").ValueKind);
        Assert.Equal(13, root.GetProperty("reference").GetProperty("expenseCategories").GetArrayLength());

        var vehicle = root.GetProperty("vehicles")[0];

        // Named one at a time rather than counted: the failure this guards against is a table that was never
        // added here, and a count would move with it.
        string[] tables =
        [
            "mileageReadings", "fuelEntries", "expenses", "serviceRecords", "tyreReadings", "washEntries",
            "checkDefinitions", "checkLogs", "tasks", "issues", "issueWatchChecks", "equipment",
            "budgetGroups", "documents", "anomalies",
        ];
        foreach (var table in tables)
        {
            Assert.True(vehicle.TryGetProperty(table, out var rows), $"the export has no '{table}'");
            Assert.True(rows.GetArrayLength() > 0, $"'{table}' came back empty, so nothing about it was tested");
        }

        // The stored fields, unrounded and unflattened: a half fill is still half, an unset verdict is still
        // absent, and the anomaly's JSON detail survives as the string the detector wrote.
        Assert.Equal("Half", vehicle.GetProperty("fuelEntries")[0].GetProperty("fillLevel").GetString());
        Assert.Equal("Attention", vehicle.GetProperty("checkLogs")[0].GetProperty("result").GetString());
        Assert.Contains("83000", vehicle.GetProperty("anomalies")[0].GetProperty("detail").GetString());

        // The whole vehicle row, owned blocks included — the profile is the entity rather than a hand-listed
        // projection precisely so a column added later cannot fall out of the export unnoticed.
        var profile = vehicle.GetProperty("profile");
        Assert.Equal(1_700m, profile.GetProperty("purchasePrice").GetDecimal());
        Assert.Equal("Petrol", profile.GetProperty("fuelType").GetString());
        Assert.True(profile.TryGetProperty("fluids", out _));
        Assert.True(profile.TryGetProperty("insurance", out _));

        // The bytes are not here, and the file they refer to is named so the two can be reconciled.
        var document = vehicle.GetProperty("documents")[0];
        Assert.Equal("MOT certificate — EXP 005", document.GetProperty("title").GetString());
        Assert.StartsWith($"{account.VehicleId}/", document.GetProperty("filePath").GetString());
    }

    [Fact]
    public async Task An_owner_id_that_names_no_account_fails_loudly_rather_than_exporting_nothing()
    {
        await using var context = NewContext();

        // An empty file would be indistinguishable from an account with nothing in it, which is the one answer
        // this endpoint must never give by accident.
        using var buffer = new MemoryStream();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExportFor(context).WriteAsync(-1, "0.13.0", buffer));

        Assert.Contains("no such user row", thrown.Message);
    }

    /// <summary>
    /// The export must never write to its destination synchronously — Kestrel refuses one, and the whole
    /// endpoint 500s when it happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test that was missing, and its absence is why a shipped release could not export.</b>
    /// Every other test here writes to a <see cref="MemoryStream"/>, which permits synchronous writes, so the
    /// offending call — <c>JsonSerializer.Serialize(Utf8JsonWriter, …)</c> flushes the writer when it returns,
    /// synchronously, with no async overload — succeeded in the suite and threw on the NAS with
    /// <i>"Synchronous operations are disallowed. Call WriteAsync or set AllowSynchronousIO to true instead."</i>
    /// The gap was not that the assertion was weak; it was that no destination in the suite could tell the
    /// difference.
    /// </para>
    /// <para>
    /// So the destination here refuses a synchronous write exactly as <c>HttpResponseStream</c> does. It is a
    /// property of the writer rather than of the payload, so it needs no seeded rows to be worth asserting —
    /// but it gets a real account anyway, because the failure was on the <i>first</i> property written and a
    /// fixture with nothing in it would pass a version of this code that still had the bug for every row.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Export_never_writes_synchronously_to_its_destination()
    {
        var account = await SeedAccountAsync("exp|syncio", "EXP 006", "K & P Motors");

        await using var context = NewContext(TestOwner.As(account.OwnerId));
        await using var destination = new AsyncOnlyStream();

        await ExportFor(context).WriteAsync(account.OwnerId, "0.13.0", destination);

        // And it is a whole document, not merely an unthrown one: a drain that never ran would leave this empty.
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(destination.Written.ToArray()));
        Assert.Equal("EXP 006", json.RootElement.GetProperty("vehicles")[0].GetProperty("registration").GetString());
    }

    /// <summary>
    /// A destination that refuses synchronous writes, the way Kestrel's response body does.
    /// </summary>
    /// <remarks>
    /// It throws the same <see cref="InvalidOperationException"/> with the same wording, so a future failure
    /// reads identically in the test output and in a production log — the point of a fake being that you
    /// recognise the real thing when you meet it.
    /// </remarks>
    private sealed class AsyncOnlyStream : Stream
    {
        public MemoryStream Written { get; } = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written.Length;

        public override long Position
        {
            get => Written.Position;
            set => throw new NotSupportedException();
        }

        private static Exception Disallowed() => new InvalidOperationException(
            "Synchronous operations are disallowed. Call WriteAsync or set AllowSynchronousIO to true instead.");

        public override void Write(byte[] buffer, int offset, int count) => throw Disallowed();

        public override void Write(ReadOnlySpan<byte> buffer) => throw Disallowed();

        public override void WriteByte(byte value) => throw Disallowed();

        public override void Flush() => throw Disallowed();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            Written.WriteAsync(buffer, offset, count, ct);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            Written.WriteAsync(buffer, ct);

        public override Task FlushAsync(CancellationToken ct) => Written.FlushAsync(ct);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) Written.Dispose();
            base.Dispose(disposing);
        }
    }
}
