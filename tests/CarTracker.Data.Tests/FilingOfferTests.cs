using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Documents;
using CarTracker.ModelContextProtocol;
using CarTracker.ModelContextProtocol.Tools;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;

namespace CarTracker.Data.Tests;

/// <summary>
/// <c>offer_to_file</c>: a proposal checked against the vehicle, returned with a link, and nothing written.
/// </summary>
/// <remarks>
/// Called directly, as <c>McpWriteSurfaceTests</c> calls <c>add_task</c>, with the context pinned to an owner so
/// the query filter is real. The "writes nothing" claim is asserted by counting every table in the schema before
/// and after, so a future audit row or a stray document would fail it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class FilingOfferTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

    private string _connectionString = string.Empty;
    private string _root = string.Empty;
    private int _owner;
    private int _stranger;

    private Seeded _seed = null!;

    private sealed record Seeded(
        int VehicleId, int OtherVehicleId, int ExpenseId, int OtherVehicleExpenseId, int ServiceId, int IssueId,
        int StrangersIssueId);

    private CarTrackerDbContext NewContext(int? owner = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Time,
            owner is { } id ? TestOwner.As(id) : null);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_filing_offer");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-filing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();
        _owner = await TestOwner.SeedAsync(seed, "test|filing-owner");
        _stranger = await TestOwner.SeedAsync(seed, "test|filing-stranger");

        _seed = await SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Find-or-create, because the database outlives each test's fixture instance.</summary>
    private async Task<Seeded> SeedAsync()
    {
        await using var mine = NewContext(_owner);
        var vehicle = await VehicleAsync(mine, _owner, "FO01 AAA", isDefault: true);
        var other = await VehicleAsync(mine, _owner, "FO02 BBB", isDefault: false);

        var expense = await mine.ExpenseEntries.FirstOrDefaultAsync(e => e.VehicleId == vehicle && e.Vendor == "Euro Car Parts");
        if (expense is null)
        {
            expense = new ExpenseEntry
            {
                VehicleId = vehicle, EntryDate = new DateOnly(2026, 11, 14), Category = "Parts", Vendor = "Euro Car Parts",
                Amount = 63.20m, Source = EntrySource.Web,
            };
            mine.ExpenseEntries.Add(expense);
        }

        var otherExpense = await mine.ExpenseEntries.FirstOrDefaultAsync(e => e.VehicleId == other && e.Vendor == "GSF");
        if (otherExpense is null)
        {
            otherExpense = new ExpenseEntry
            {
                VehicleId = other, EntryDate = new DateOnly(2026, 11, 1), Category = "Parts", Vendor = "GSF",
                Amount = 12.00m, Source = EntrySource.Web,
            };
            mine.ExpenseEntries.Add(otherExpense);
        }

        var service = await mine.ServiceRecords.FirstOrDefaultAsync(s => s.VehicleId == vehicle);
        if (service is null)
        {
            service = new ServiceRecord
            {
                VehicleId = vehicle, ServiceDate = new DateOnly(2026, 6, 27), Mileage = 80_100, Type = "Oil change",
                Source = EntrySource.Web,
            };
            mine.ServiceRecords.Add(service);
        }

        var issue = await mine.Issues.FirstOrDefaultAsync(i => i.VehicleId == vehicle);
        if (issue is null)
        {
            issue = new Issue
            {
                VehicleId = vehicle, Title = "Rear tyre cracking", Severity = Severity.Medium,
                FirstNoted = new DateOnly(2026, 10, 2), Status = IssueStatus.Monitoring, Source = EntrySource.Web,
            };
            mine.Issues.Add(issue);
        }

        await mine.SaveChangesAsync();

        await using var theirs = NewContext(_stranger);
        var strangersVehicle = await VehicleAsync(theirs, _stranger, "FO03 ZZZ", isDefault: true);
        var strangersIssue = await theirs.Issues.FirstOrDefaultAsync(i => i.VehicleId == strangersVehicle);
        if (strangersIssue is null)
        {
            strangersIssue = new Issue
            {
                VehicleId = strangersVehicle, Title = "Someone else's dent", Severity = Severity.Low,
                FirstNoted = new DateOnly(2026, 9, 1), Status = IssueStatus.Monitoring, Source = EntrySource.Web,
            };
            theirs.Issues.Add(strangersIssue);
            await theirs.SaveChangesAsync();
        }

        return new Seeded(vehicle, other, expense.Id, otherExpense.Id, service.Id, issue.Id, strangersIssue.Id);
    }

    private static async Task<int> VehicleAsync(CarTrackerDbContext context, int owner, string registration, bool isDefault)
    {
        var existing = await context.Vehicles.FirstOrDefaultAsync(v => v.Registration == registration);
        if (existing is not null) return existing.Id;

        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, FuelType = FuelType.Petrol,
            Source = EntrySource.Web, IsDefault = isDefault,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, owner, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    /// <summary>Runs the tool as the signed-in owner, with the plan and the origin a test wants.</summary>
    private async Task<McpResult<FilingOffer>> OfferAsync(
        DocumentType type,
        string title,
        string? vehicle = null,
        DateOnly? documentDate = null,
        int? serviceRecordId = null,
        int? expenseEntryId = null,
        int? issueId = null,
        int? attachment = null,
        IAccountEntitlements? plan = null,
        string? origin = "https://cambelt.app")
    {
        await using var context = NewContext(_owner);
        var resolver = new VehicleResolver(context, new VehicleMetricsLoader(context));
        var documents = new DocumentService(context, new DocumentStore(new DocumentStorageOptions(_root)), plan ?? TestEntitlements.Pro);

        return await FilingTools.OfferToFile(
            resolver, documents, new AppOptions { PublicOrigin = origin }, type, title, vehicle, documentDate,
            serviceRecordId, expenseEntryId, issueId, attachment);
    }

    /// <summary>Every base table in the schema and its row count, read from the catalogue so none is missed.</summary>
    private async Task<Dictionary<string, long>> CountEveryTableAsync()
    {
        await using var context = NewContext();
        var tables = await context.Database
            .SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables "
                + "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")
            .ToListAsync();

        Assert.Contains("documents", tables);
        Assert.Contains("assistant_write_audits", tables);

        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            // The names come from the catalogue query above, not from input.
            var sql = "SELECT count(*) AS \"Value\" FROM \"" + table + "\"";
            counts[table] = await context.Database.SqlQueryRaw<long>(sql).SingleAsync();
        }

        return counts;
    }

    private async Task AssertWritesNothingAsync(Func<Task> call)
    {
        var before = await CountEveryTableAsync();
        try
        {
            await call();
        }
        catch (McpException)
        {
            // A refusal writes nothing either; the counts below are the assertion.
        }

        Assert.Equal(before, await CountEveryTableAsync());
    }

    [Fact]
    public async Task A_parts_receipt_linked_to_the_vehicles_expense_returns_the_whole_proposal()
    {
        var result = await OfferAsync(
            DocumentType.PartsReceipt, "  Euro Car Parts - coolant  ", vehicle: "FO01 AAA",
            documentDate: new DateOnly(2026, 11, 14), expenseEntryId: _seed.ExpenseId, attachment: 1);

        var offer = result.Data;
        Assert.Equal("FO01 AAA", offer.Registration);
        Assert.Equal(DocumentType.PartsReceipt, offer.Type);
        Assert.Equal(DocumentCategories.Kind(DocumentType.PartsReceipt), offer.Kind);
        Assert.Equal(DocumentGroup.WorkAndParts, offer.Group);
        Assert.Equal("Parts receipt", offer.Label);
        Assert.Equal("Euro Car Parts - coolant", offer.Title);
        Assert.Equal(new DateOnly(2026, 11, 14), offer.DocumentDate);
        // The chip the filed document will carry, so the card and the Documents screen read the same.
        Assert.Equal(new DocumentLink(DocumentLinkKind.Expense, _seed.ExpenseId, "Parts · £63.20"), offer.LinkedTo);
        Assert.Equal(1, offer.Attachment);

        await using var context = NewContext(_owner);
        var capacity = await new DocumentService(context, new DocumentStore(new DocumentStorageOptions(_root)), TestEntitlements.Pro)
            .CapacityAsync();
        Assert.Equal(new FilingAllowance(capacity.Used, capacity.Limit), offer.Allowance);

        Assert.Equal(
            FilingLink.PathFor("FO01 AAA", DocumentType.PartsReceipt, "Euro Car Parts - coolant",
                new DateOnly(2026, 11, 14), null, _seed.ExpenseId, null),
            offer.Path);
        Assert.Equal("https://cambelt.app" + offer.Path, offer.Url);
        Assert.Equal($"Ready to file as Parts receipt, linked to Parts · £63.20. Open: {offer.Url}", result.Summary);
    }

    [Fact]
    public async Task A_service_record_link_is_described_like_a_filed_document()
    {
        var offer = (await OfferAsync(DocumentType.ServiceInvoice, "Oil change invoice", serviceRecordId: _seed.ServiceId)).Data;

        Assert.Equal(new DocumentLink(DocumentLinkKind.ServiceRecord, _seed.ServiceId, "Oil change · 27 Jun 2026"), offer.LinkedTo);
        Assert.Contains($"serviceRecordId={_seed.ServiceId}", offer.Path);
    }

    [Fact]
    public async Task An_issue_link_is_described_by_the_issue_title()
    {
        var offer = (await OfferAsync(DocumentType.ConditionPhoto, "Cracking, November", issueId: _seed.IssueId)).Data;

        Assert.Equal(new DocumentLink(DocumentLinkKind.Issue, _seed.IssueId, "Rear tyre cracking"), offer.LinkedTo);
        Assert.Equal(DocumentKind.Photo, offer.Kind);
        Assert.Equal(DocumentGroup.Photos, offer.Group);
    }

    [Fact]
    public async Task No_link_is_fine_and_an_omitted_vehicle_is_the_default()
    {
        var result = await OfferAsync(DocumentType.CarPhoto, "After the wash");

        Assert.Equal("FO01 AAA", result.Data.Registration);
        Assert.Null(result.Data.LinkedTo);
        Assert.Null(result.Data.DocumentDate);
        Assert.Null(result.Data.Attachment);
        Assert.Equal("Ready to file as Car photo. Open: https://cambelt.app" + result.Data.Path, result.Summary);
    }

    [Fact]
    public async Task A_blank_origin_gives_a_path_only_link_and_says_where_to_go()
    {
        var result = await OfferAsync(DocumentType.CarPhoto, "After the wash", origin: "  ");

        Assert.Null(result.Data.Url);
        Assert.StartsWith("/file?", result.Data.Path);
        Assert.Equal($"Ready to file as Car photo. Open Cambelt and go to {result.Data.Path}", result.Summary);
    }

    [Fact]
    public async Task Another_owners_issue_is_refused_as_not_this_vehicles()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() =>
            OfferAsync(DocumentType.ConditionPhoto, "Dent", issueId: _seed.StrangersIssueId));

        Assert.Contains($"Issue {_seed.StrangersIssueId} is not this vehicle's", refusal.Message);
    }

    [Fact]
    public async Task The_same_owners_other_vehicles_expense_is_refused()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() =>
            OfferAsync(DocumentType.PartsReceipt, "GSF", vehicle: "FO01 AAA", expenseEntryId: _seed.OtherVehicleExpenseId));

        Assert.Contains($"Expense {_seed.OtherVehicleExpenseId} is not this vehicle's", refusal.Message);
    }

    [Fact]
    public async Task Two_links_are_refused()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() =>
            OfferAsync(DocumentType.PartsReceipt, "Both", expenseEntryId: _seed.ExpenseId, issueId: _seed.IssueId));

        Assert.Contains("attaches to one record", refusal.Message);
    }

    [Fact]
    public async Task A_title_of_160_characters_is_accepted_and_161_refused()
    {
        var accepted = await OfferAsync(DocumentType.Other, new string('a', 160));
        Assert.Equal(160, accepted.Data.Title.Length);

        var refusal = await Assert.ThrowsAsync<McpException>(() => OfferAsync(DocumentType.Other, new string('a', 161)));
        Assert.Contains("160 characters", refusal.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_title_is_refused(string title)
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() => OfferAsync(DocumentType.Other, title));

        Assert.Contains("needs a title", refusal.Message);
    }

    [Fact]
    public async Task An_unknown_vehicle_is_refused_with_the_list_vehicles_hint()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() => OfferAsync(DocumentType.CarPhoto, "x", vehicle: "NO53 CAR"));

        Assert.Contains("list_vehicles", refusal.Message);
    }

    [Fact]
    public async Task Another_owners_vehicle_is_refused_like_an_unknown_one()
    {
        var refusal = await Assert.ThrowsAsync<McpException>(() => OfferAsync(DocumentType.CarPhoto, "x", vehicle: "FO03 ZZZ"));

        Assert.Contains("No vehicle matches", refusal.Message);
    }

    [Fact]
    public async Task A_full_allowance_still_offers_and_says_to_remove_a_document_first()
    {
        var result = await OfferAsync(DocumentType.CarPhoto, "After the wash", plan: TestEntitlements.With(maxDocuments: 0));

        Assert.Equal(result.Data.Allowance.Used, result.Data.Allowance.Limit);
        Assert.Contains("Remove one to file another", result.Summary);
        Assert.Contains(result.Data.Url!, result.Summary);
    }

    [Fact]
    public async Task A_valid_offer_writes_nothing()
    {
        await AssertWritesNothingAsync(() =>
            OfferAsync(DocumentType.PartsReceipt, "Euro Car Parts", expenseEntryId: _seed.ExpenseId, attachment: 2));
    }

    [Fact]
    public async Task A_refused_offer_writes_nothing()
    {
        await AssertWritesNothingAsync(() =>
            OfferAsync(DocumentType.ConditionPhoto, "Dent", issueId: _seed.StrangersIssueId));
        await AssertWritesNothingAsync(() =>
            OfferAsync(DocumentType.PartsReceipt, "Both", expenseEntryId: _seed.ExpenseId, issueId: _seed.IssueId));
        await AssertWritesNothingAsync(() => OfferAsync(DocumentType.Other, new string('a', 161)));
    }
}
