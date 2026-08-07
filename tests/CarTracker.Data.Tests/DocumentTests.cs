using System.Security.Cryptography;
using System.Text;
using CarTracker.Domain;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Documents — the papers and photo sets, and the only feature in the app that puts bytes on a disk.
/// </summary>
/// <remarks>
/// Against a real database and a real temp directory, because the claims are about both at once: a row and a
/// file, and the ways they can disagree. File storage is deliberately <b>not</b> transactional with the
/// database (DEC-005 names that as its cost), so the interesting cases are the seams — a duplicate, a delete
/// that must free bytes, a row whose file has gone.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private int _ownerId;
    private string _root = string.Empty;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock);

    private DocumentStore NewStore() => new(new DocumentStorageOptions(_root));

    private DocumentService NewDocuments(CarTrackerDbContext context) => new(context, NewStore());

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_documents");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-docs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<int> NewVehicleAsync(CarTrackerDbContext context, string registration)
    {
        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    private static Stream Bytes(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static string Sha256Of(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private async Task<WriteResult<DocumentItem>> FileAsync(
        CarTrackerDbContext context,
        int vehicleId,
        string content,
        string title,
        DocumentType type = DocumentType.MOT,
        string contentType = "application/pdf",
        int? serviceRecordId = null,
        int? expenseEntryId = null,
        int? issueId = null)
    {
        await using var stream = Bytes(content);
        var stored = await NewStore().SaveAsync(vehicleId, stream, contentType);
        return await NewDocuments(context).RecordAsync(
            vehicleId, stored!, contentType, type, title, new DateOnly(2026, 7, 8),
            serviceRecordId, expenseEntryId, issueId, null, EntrySource.Web);
    }

    // ---- storage ------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_upload_writes_one_file_named_for_its_own_hash()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 001");

        await using var stream = Bytes("%PDF-1.4 the MOT certificate");
        var stored = await NewStore().SaveAsync(vehicleId, stream, "application/pdf");

        Assert.NotNull(stored);
        // Content-addressed: the name is the hash, so two files called scan.pdf cannot collide and a
        // client-supplied filename never becomes a path component.
        Assert.Equal(Sha256Of("%PDF-1.4 the MOT certificate"), stored!.Sha256);
        Assert.Equal($"{vehicleId}/{stored.Sha256}.pdf", stored.RelativePath);
        // From what actually arrived, not from a client-declared length.
        Assert.Equal(Encoding.UTF8.GetByteCount("%PDF-1.4 the MOT certificate"), stored.SizeBytes);
        Assert.False(stored.AlreadyExisted);

        var onDisk = Directory.GetFiles(Path.Combine(_root, vehicleId.ToString()));
        Assert.Single(onDisk);
    }

    [Fact]
    public async Task Identical_bytes_resolve_to_the_one_file_rather_than_a_second_copy()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 002");

        await using (var first = Bytes("same bytes")) await NewStore().SaveAsync(vehicleId, first, "application/pdf");
        await using var second = Bytes("same bytes");
        var again = await NewStore().SaveAsync(vehicleId, second, "application/pdf");

        Assert.True(again!.AlreadyExisted);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, vehicleId.ToString())));
    }

    [Fact]
    public async Task An_oversize_upload_is_refused_and_leaves_nothing_behind()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 003");

        // The cap is enforced while reading, not from a Content-Length header — the point of a cap is the case
        // where the client's claim about the size is wrong.
        await using var huge = new MemoryStream(new byte[DocumentStore.MaxSizeBytes + 1]);
        Assert.Null(await NewStore().SaveAsync(vehicleId, huge, "application/pdf"));

        var folder = Path.Combine(_root, vehicleId.ToString());
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void Only_pdfs_and_photos_may_be_filed()
    {
        Assert.True(DocumentStore.IsAllowed("application/pdf"));
        Assert.True(DocumentStore.IsAllowed("image/jpeg"));
        // An allow-list, not a deny-list: the set of things safe to serve back to a browser is small and known.
        Assert.False(DocumentStore.IsAllowed("text/html"));
        Assert.False(DocumentStore.IsAllowed("application/x-msdownload"));
        Assert.False(DocumentStore.IsAllowed(null));
    }

    // ---- filing -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_byte_identical_refile_is_refused_and_names_what_it_duplicates()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 004");

        Assert.Equal(WriteStatus.Created,
            (await FileAsync(context, vehicleId, "the certificate", "MOT certificate — pass")).Status);

        var again = await FileAsync(context, vehicleId, "the certificate", "MOT cert (copy)");
        Assert.Equal(WriteStatus.Validation, again.Status);
        // Actionable: it says which document it already is, rather than "duplicate".
        Assert.Contains("MOT certificate — pass", again.Errors!["File"][0]);
    }

    [Fact]
    public async Task Papers_and_photos_are_split_for_the_two_halves_of_the_screen()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 005");

        await FileAsync(context, vehicleId, "cert", "MOT certificate", DocumentType.MOT);
        await FileAsync(context, vehicleId, "v5c", "V5C registration certificate", DocumentType.V5C);
        await FileAsync(context, vehicleId, "photo", "Front ¾ · baseline", DocumentType.Photo, "image/jpeg");

        var log = await NewDocuments(context).GetLogAsync(vehicleId);

        Assert.Equal(2, log.Papers.Count);
        Assert.Equal("Front ¾ · baseline", log.Photos.Single().Title);
        Assert.Equal(3, log.TotalCount);
    }

    // ---- links --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_document_links_to_one_record_and_the_chip_names_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 006");

        var issue = await new IssueService(context, new Clock(Clock)).AddAsync(
            vehicleId, new IssueInput("Rear tyre cracking", new DateOnly(2026, 3, 14)), EntrySource.Web);

        var filed = await FileAsync(
            context, vehicleId, "photo bytes", "Rear tyre cracking", DocumentType.Photo, "image/jpeg",
            issueId: issue.Value!.Id);

        Assert.Equal(WriteStatus.Created, filed.Status);
        var link = filed.Value!.LinkedTo;
        Assert.Equal(DocumentLinkKind.Issue, link!.Kind);
        Assert.Equal("Rear tyre cracking", link.Label);
    }

    [Fact]
    public async Task A_document_attaches_to_one_record_not_several()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 007");

        var issue = await new IssueService(context, new Clock(Clock)).AddAsync(
            vehicleId, new IssueInput("Sills", new DateOnly(2026, 3, 14)), EntrySource.Web);
        var expense = await new CarTracker.Domain.Expenses.ExpenseService(
                context, new AnomalyScanner(context, new VehicleMetricsLoader(context), Clock))
            .AddAsync(vehicleId, new ExpenseInput(new DateOnly(2026, 7, 8), "Repair", 129.99m), EntrySource.Web);

        var filed = await FileAsync(
            context, vehicleId, "invoice", "Wiper motor repair invoice", DocumentType.Receipt,
            issueId: issue.Value!.Id, expenseEntryId: expense.Value!.Id);

        // Two links would make "open the record this belongs to" ambiguous, and the chip row would need a rule
        // about precedence for a case nothing needs.
        Assert.Equal(WriteStatus.Validation, filed.Status);
        Assert.Contains("Link", filed.Errors!.Keys);
    }

    [Fact]
    public async Task A_cross_vehicle_link_is_refused()
    {
        await using var context = NewContext();
        var mine = await NewVehicleAsync(context, "DOC 008");
        var theirs = await NewVehicleAsync(context, "DOC 009");

        var theirIssue = await new IssueService(context, new Clock(Clock)).AddAsync(
            theirs, new IssueInput("Their issue", new DateOnly(2026, 3, 14)), EntrySource.Web);

        var filed = await FileAsync(
            context, mine, "photo", "Not mine", DocumentType.Photo, "image/jpeg", issueId: theirIssue.Value!.Id);

        // The FKs enforce that the target exists, not that it belongs to the car whose folder the file is in.
        Assert.Equal(WriteStatus.Validation, filed.Status);
        Assert.Contains("IssueId", filed.Errors!.Keys);
    }

    [Fact]
    public async Task Deleting_the_linked_record_severs_the_link_and_keeps_the_document()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 010");

        var issue = await new IssueService(context, new Clock(Clock)).AddAsync(
            vehicleId, new IssueInput("Headlamp haze", new DateOnly(2026, 3, 14)), EntrySource.Web);
        var filed = await FileAsync(
            context, vehicleId, "haze photo", "Headlamp haze", DocumentType.Photo, "image/jpeg",
            issueId: issue.Value!.Id);

        context.Issues.Remove(await context.Issues.SingleAsync(i => i.Id == issue.Value!.Id));
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var document = await reader.Documents.SingleAsync(d => d.Id == filed.Value!.Id);
        // SetNull, not cascade. The evidence outlives its subject — that is what makes a baseline photo worth
        // keeping once the issue it documented is closed.
        Assert.Null(document.IssueId);
        Assert.Equal("Headlamp haze", document.Title);
    }

    [Fact]
    public async Task Re_tagging_moves_the_link_and_clearing_detaches_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 011");
        var issue = await new IssueService(context, new Clock(Clock)).AddAsync(
            vehicleId, new IssueInput("Sills — rust watch", new DateOnly(2026, 3, 14)), EntrySource.Web);

        var filed = await FileAsync(context, vehicleId, "sill photo", "Sills", DocumentType.Photo, "image/jpeg");
        var id = filed.Value!.Id;

        var linked = await NewDocuments(context).UpdateAsync(
            vehicleId, id, new DocumentPatch(IssueId: issue.Value!.Id, Title: "Sills — rust watch"));
        Assert.Equal(WriteStatus.Updated, linked.Status);
        Assert.Equal(DocumentLinkKind.Issue, linked.Value!.LinkedTo!.Kind);

        // ClearLink exists because null on the id fields already means "leave it alone" — without it there
        // would be no way to say "attached to nothing".
        var cleared = await NewDocuments(context).UpdateAsync(vehicleId, id, new DocumentPatch(ClearLink: true));
        Assert.Null(cleared.Value!.LinkedTo);
    }

    // ---- delete -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_document_removes_the_row_and_frees_the_bytes()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 012");
        var filed = await FileAsync(context, vehicleId, "some bytes", "Purchase receipt", DocumentType.Receipt);

        var folder = Path.Combine(_root, vehicleId.ToString());
        Assert.Single(Directory.GetFiles(folder));

        Assert.Equal(WriteStatus.Updated, (await NewDocuments(context).DeleteAsync(vehicleId, filed.Value!.Id)).Status);

        await using var reader = NewContext();
        Assert.False(await reader.Documents.AnyAsync(d => d.Id == filed.Value!.Id));
        // Nothing else in the app deletes from the volume, so if this did not, the volume grows orphans.
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Deleting_one_of_two_documents_sharing_bytes_keeps_the_file()
    {
        await using var context = NewContext();
        var mine = await NewVehicleAsync(context, "DOC 013");
        var theirs = await NewVehicleAsync(context, "DOC 014");

        // Same bytes, different vehicles — each gets its own folder, so this is really two files. The guard
        // matters for the case where a path IS shared, which content-addressing makes possible.
        var a = await FileAsync(context, mine, "shared bytes", "Mine");
        await FileAsync(context, theirs, "shared bytes", "Theirs");

        var document = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == a.Value!.Id);
        var path = document.FilePath;

        // Point a second row at the same path, which is what a future share would look like.
        context.Documents.Add(new Document
        {
            VehicleId = mine, Type = DocumentType.Other, Title = "Same file, filed twice",
            FilePath = path, ContentType = "application/pdf", SizeBytes = document.SizeBytes,
            Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            Source = EntrySource.Web,
        });
        await context.SaveChangesAsync();

        await NewDocuments(context).DeleteAsync(mine, a.Value!.Id);

        // Disposed, or the handle keeps the temp root locked and the fixture cannot clean up after itself.
        await using var survivor = NewStore().OpenRead(path);
        Assert.NotNull(survivor);
    }

    [Fact]
    public async Task A_row_whose_file_has_gone_reads_back_as_missing_rather_than_throwing()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "DOC 015");
        var filed = await FileAsync(context, vehicleId, "bytes that vanish", "V5C");

        var document = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Value!.Id);
        File.Delete(Path.Combine(_root, document.FilePath));

        // The DB and the volume are backed up separately, so a restore can produce exactly this. The endpoint
        // turns null into a 404 that says which of the two is missing.
        Assert.Null(NewStore().OpenRead(document.FilePath));
    }

    [Fact]
    public void A_path_escaping_the_root_resolves_to_nothing()
    {
        // The paths this resolves are ones the store generated, so traversal should be impossible — but this is
        // the one code path turning database content into a file read, and the check costs nothing.
        Assert.Null(NewStore().OpenRead("../../etc/passwd"));
    }
}
