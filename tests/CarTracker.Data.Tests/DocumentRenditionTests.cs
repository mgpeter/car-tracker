using System.Security.Cryptography;
using System.Text;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Photo renditions: the thumbnail and display copy the browser makes at upload or later, stored beside the
/// original under the same content-addressed scheme.
/// </summary>
/// <remarks>
/// Against a real database and a real temp directory, as <see cref="DocumentTests"/> is, because the rules are
/// about a row and up to three files together. There is no WebApi test project, so the rendition rules live in
/// <see cref="RenditionSet"/>, <see cref="DocumentStore"/> and <see cref="DocumentService"/>, and the endpoint
/// only maps their results.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentRenditionTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private int _ownerId;
    private int _otherOwnerId;
    private string _root = string.Empty;

    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Clock = new(Now);

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock);

    private CarTrackerDbContext OwnedContext(int ownerId) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock,
            TestOwner.As(ownerId));

    private DocumentStore NewStore() => new(new DocumentStorageOptions(_root));

    private DocumentService NewDocuments(CarTrackerDbContext context, IAccountEntitlements? plan = null) =>
        new(context, NewStore(), plan ?? TestEntitlements.Pro);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_renditions");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-renditions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
        _otherOwnerId = await TestOwner.SeedAsync(context, "test|other-owner");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<int> NewVehicleAsync(CarTrackerDbContext context, string registration, int? ownerId = null)
    {
        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, ownerId ?? _ownerId, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    private static RenditionUpload Upload(
        string thumbnail = "thumbnail bytes",
        string display = "display bytes",
        string thumbnailType = "image/webp",
        string displayType = "image/webp",
        int width = 4032,
        int height = 3024) =>
        new(Bytes(thumbnail), thumbnailType, Bytes(display), displayType, width, height);

    private string[] FilesOf(int vehicleId) =>
        Directory.Exists(Path.Combine(_root, vehicleId.ToString()))
            ? Directory.GetFiles(Path.Combine(_root, vehicleId.ToString())).Order().ToArray()
            : [];

    private bool OnDisk(string relativePath) => File.Exists(Path.Combine(_root, relativePath));

    /// <summary>Files a document, with renditions when <paramref name="renditions"/> is given.</summary>
    private async Task<WriteResult<DocumentItem>> FileAsync(
        CarTrackerDbContext context,
        int vehicleId,
        string content,
        string title,
        DocumentType type = DocumentType.CarPhoto,
        string contentType = "image/jpeg",
        RenditionUpload? renditions = null)
    {
        await using var stream = Bytes(content);
        var stored = await NewStore().SaveAsync(vehicleId, stream, contentType);
        var documents = NewDocuments(context);

        StoredRenditions? saved = null;
        if (renditions is not null)
        {
            var result = await documents.SaveRenditionsAsync(vehicleId, renditions);
            Assert.Equal(WriteStatus.Created, result.Status);
            saved = result.Value;
        }

        return await documents.RecordAsync(
            vehicleId, stored!, contentType, type, title, new DateOnly(2026, 11, 20),
            null, null, null, null, EntrySource.Web, saved);
    }

    // ---- the set ------------------------------------------------------------------------------------------

    private static readonly RenditionPart Webp = new("image/webp");
    private static readonly RenditionPart Jpeg = new("image/jpeg");

    [Fact]
    public void A_complete_set_of_webp_or_jpeg_is_accepted()
    {
        Assert.Null(new RenditionSet(Webp, Webp, 4032, 3024).Validate());
        Assert.Null(new RenditionSet(Jpeg, Jpeg, 1, 1).Validate());
        Assert.Null(new RenditionSet(Webp, Jpeg, 3024, 4032).Validate());
        Assert.True(new RenditionSet(null, null, null, null).IsEmpty);
        Assert.Null(new RenditionSet(null, null, null, null).Validate());
    }

    [Fact]
    public void A_partial_set_is_refused()
    {
        // A thumbnail without a display copy, and the reverse.
        Assert.Equal("Display", new RenditionSet(Webp, null, 4032, 3024).Validate()?.Field);
        Assert.Equal("Thumbnail", new RenditionSet(null, Webp, 4032, 3024).Validate()?.Field);
        // Dimensions without renditions.
        Assert.Equal("Thumbnail", new RenditionSet(null, null, 4032, 3024).Validate()?.Field);
        // Renditions without dimensions, or with half of them.
        Assert.Equal("Width", new RenditionSet(Webp, Webp, null, null).Validate()?.Field);
        Assert.Equal("Height", new RenditionSet(Webp, Webp, 4032, null).Validate()?.Field);
    }

    [Theory]
    [InlineData(0, 3024)]
    [InlineData(4032, 0)]
    [InlineData(-1, 3024)]
    [InlineData(4032, -5)]
    public void A_zero_or_negative_dimension_is_refused(int width, int height)
    {
        Assert.NotNull(new RenditionSet(Webp, Webp, width, height).Validate());
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/heic")]
    [InlineData("application/pdf")]
    public void A_rendition_that_is_not_webp_or_jpeg_is_refused(string contentType)
    {
        Assert.Equal("Thumbnail", new RenditionSet(new(contentType), Webp, 10, 10).Validate()?.Field);
        Assert.Equal("Display", new RenditionSet(Webp, new(contentType), 10, 10).Validate()?.Field);
    }

    [Fact]
    public void A_declared_length_over_the_cap_is_refused_before_a_byte_is_read()
    {
        Assert.Equal("Thumbnail",
            new RenditionSet(new("image/webp", DocumentStore.MaxThumbnailBytes + 1), Webp, 10, 10).Validate()?.Field);
        Assert.Equal("Display",
            new RenditionSet(Webp, new("image/webp", DocumentStore.MaxDisplayBytes + 1), 10, 10).Validate()?.Field);
    }

    // ---- the store ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_store_refuses_an_oversize_rendition_and_leaves_nothing_behind()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 001");

        await using (var thumb = new MemoryStream(new byte[301 * 1024]))
            Assert.Null(await NewStore().SaveAsync(vehicleId, thumb, "image/webp", DocumentStore.MaxThumbnailBytes));

        await using (var display = new MemoryStream(new byte[DocumentStore.MaxDisplayBytes + 1]))
            Assert.Null(await NewStore().SaveAsync(vehicleId, display, "image/webp", DocumentStore.MaxDisplayBytes));

        Assert.Empty(FilesOf(vehicleId));

        // At the cap is fine: the cap is a maximum, not a bound to stay under.
        await using var exact = new MemoryStream(new byte[DocumentStore.MaxThumbnailBytes]);
        Assert.NotNull(await NewStore().SaveAsync(vehicleId, exact, "image/webp", DocumentStore.MaxThumbnailBytes));
    }

    // ---- filing with renditions ---------------------------------------------------------------------------

    [Fact]
    public async Task Filing_with_renditions_stores_three_files_and_fills_all_four_columns()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 002");

        var filed = await FileAsync(context, vehicleId, "original heic bytes", "On the drive",
            contentType: "image/heic", renditions: Upload(thumbnailType: "image/jpeg"));

        Assert.Equal(WriteStatus.Created, filed.Status);
        Assert.Equal(3, FilesOf(vehicleId).Length);

        var row = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Value!.Id);
        Assert.NotNull(row.ThumbnailPath);
        Assert.NotNull(row.DisplayPath);
        Assert.EndsWith(".jpg", row.ThumbnailPath);
        Assert.EndsWith(".webp", row.DisplayPath);
        Assert.Equal(4032, row.Width);
        Assert.Equal(3024, row.Height);
        Assert.True(OnDisk(row.FilePath) && OnDisk(row.ThumbnailPath!) && OnDisk(row.DisplayPath!));

        var item = filed.Value!;
        Assert.True(item.HasThumbnail);
        Assert.True(item.HasDisplay);
        Assert.Equal(4032, item.Width);
        Assert.Equal(3024, item.Height);
        Assert.Equal(DocumentKind.Photo, item.Kind);
        Assert.Equal(DocumentGroup.Photos, item.Group);
        Assert.Equal(Now, item.CreatedAt);
    }

    [Fact]
    public async Task Filing_without_renditions_leaves_the_columns_null()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 003");

        var filed = await FileAsync(context, vehicleId, "%PDF the invoice", "Cambelt invoice",
            DocumentType.ServiceInvoice, "application/pdf");

        var item = filed.Value!;
        Assert.False(item.HasThumbnail);
        Assert.False(item.HasDisplay);
        Assert.Null(item.Width);
        Assert.Null(item.Height);
        Assert.Equal(DocumentKind.Paper, item.Kind);
        Assert.Equal(DocumentGroup.WorkAndParts, item.Group);
        Assert.Single(FilesOf(vehicleId));
    }

    [Fact]
    public async Task A_rendition_over_its_cap_is_refused_by_the_save_and_leaves_no_rendition_behind()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 004");

        var oversize = new RenditionUpload(
            Bytes("thumb"), "image/webp", new MemoryStream(new byte[DocumentStore.MaxDisplayBytes + 1]), "image/webp",
            10, 10);
        var result = await NewDocuments(context).SaveRenditionsAsync(vehicleId, oversize);

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Contains("Display", result.Errors!.Keys);
        // The thumbnail saved before the display copy was refused is taken back off the volume.
        Assert.Empty(FilesOf(vehicleId));
    }

    [Fact]
    public async Task Renditions_do_not_count_toward_the_document_allowance()
    {
        await using var context = OwnedContext(_ownerId);
        var before = (await NewDocuments(context).CapacityAsync()).Used;
        var vehicleId = await NewVehicleAsync(context, "REN 005");

        await FileAsync(context, vehicleId, "allowance photo", "Allowance", renditions: Upload("t5", "d5"));

        Assert.Equal(before + 1, (await NewDocuments(context).CapacityAsync()).Used);
    }

    // ---- deleting -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_removes_the_original_and_both_renditions()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 006");
        var filed = await FileAsync(context, vehicleId, "photo to delete", "Delete me", renditions: Upload("t6", "d6"));

        Assert.Equal(3, FilesOf(vehicleId).Length);
        await NewDocuments(context).DeleteAsync(vehicleId, filed.Value!.Id);

        Assert.Empty(FilesOf(vehicleId));
    }

    /// <summary>
    /// Content-addressing shares one file between rows with identical bytes, and a rendition can be
    /// byte-identical to another row's original or rendition. Whichever column the other row names it in, the
    /// file stays.
    /// </summary>
    [Theory]
    [InlineData("FilePath", "Display")]
    [InlineData("ThumbnailPath", "Original")]
    [InlineData("DisplayPath", "Thumbnail")]
    public async Task A_file_another_row_still_references_survives_the_delete(string otherColumn, string sharedFile)
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, $"REN 1{otherColumn.Length:00}");

        var mine = (await FileAsync(context, vehicleId, $"mine {otherColumn}", "Mine",
            renditions: Upload($"t {otherColumn}", $"d {otherColumn}"))).Value!;
        var theirs = (await FileAsync(context, vehicleId, $"theirs {otherColumn}", "Theirs")).Value!;

        var mineRow = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == mine.Id);
        var shared = sharedFile switch
        {
            "Original" => mineRow.FilePath,
            "Display" => mineRow.DisplayPath!,
            _ => mineRow.ThumbnailPath!,
        };

        var theirsRow = await context.Documents.SingleAsync(d => d.Id == theirs.Id);
        switch (otherColumn)
        {
            case "FilePath":
                theirsRow.FilePath = shared;
                break;
            case "ThumbnailPath":
                theirsRow.ThumbnailPath = shared;
                theirsRow.DisplayPath = theirsRow.FilePath;
                theirsRow.Width = theirsRow.Height = 1;
                break;
            default:
                theirsRow.DisplayPath = shared;
                theirsRow.ThumbnailPath = theirsRow.FilePath;
                theirsRow.Width = theirsRow.Height = 1;
                break;
        }
        await context.SaveChangesAsync();

        await NewDocuments(context).DeleteAsync(vehicleId, mine.Id);

        Assert.True(OnDisk(shared));
        foreach (var path in new[] { mineRow.FilePath, mineRow.ThumbnailPath!, mineRow.DisplayPath! }.Where(p => p != shared))
            Assert.False(OnDisk(path));
    }

    // ---- backfill -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Backfill_fills_null_columns_and_never_touches_the_original()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 007");
        var filed = (await FileAsync(context, vehicleId, "filed from chrome as heic", "HEIC from desktop",
            contentType: "image/heic")).Value!;
        var original = (await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id)).FilePath;
        var originalHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, original))));

        var result = await NewDocuments(context).AddRenditionsAsync(vehicleId, filed.Id, Upload("t7", "d7", width: 3024, height: 4032));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.True(result.Value!.HasThumbnail);
        Assert.Equal(3024, result.Value.Width);

        var row = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id);
        Assert.Equal(original, row.FilePath);
        Assert.True(OnDisk(row.ThumbnailPath!) && OnDisk(row.DisplayPath!));
        Assert.Equal(originalHash,
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(_root, original)))));
        Assert.Equal(originalHash, row.Sha256);
    }

    [Fact]
    public async Task Backfill_is_a_no_op_when_renditions_already_exist()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 008");
        var filed = (await FileAsync(context, vehicleId, "already rendered", "Rendered", renditions: Upload("t8", "d8"))).Value!;
        var before = FilesOf(vehicleId);
        var rowBefore = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id);

        var result = await NewDocuments(context).AddRenditionsAsync(
            vehicleId, filed.Id, Upload("different thumb", "different display", width: 10, height: 10));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Equal(4032, result.Value!.Width);
        Assert.Equal(before, FilesOf(vehicleId));

        var rowAfter = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id);
        Assert.Equal(rowBefore.ThumbnailPath, rowAfter.ThumbnailPath);
        Assert.Equal(rowBefore.DisplayPath, rowAfter.DisplayPath);
    }

    [Fact]
    public async Task Backfill_refuses_a_pdf()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 009");
        var filed = (await FileAsync(context, vehicleId, "%PDF manual", "Manual", DocumentType.WorkshopManual,
            "application/pdf")).Value!;

        var result = await NewDocuments(context).AddRenditionsAsync(vehicleId, filed.Id, Upload("t9", "d9"));

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Single(FilesOf(vehicleId));
    }

    [Fact]
    public async Task Backfill_refuses_an_oversize_rendition_and_leaves_the_columns_null()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 010");
        var filed = (await FileAsync(context, vehicleId, "big one", "Big")).Value!;

        var oversize = new RenditionUpload(
            new MemoryStream(new byte[DocumentStore.MaxThumbnailBytes + 1]), "image/webp", Bytes("d10"), "image/webp",
            10, 10);
        var result = await NewDocuments(context).AddRenditionsAsync(vehicleId, filed.Id, oversize);

        Assert.Equal(WriteStatus.Validation, result.Status);
        var row = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id);
        Assert.Null(row.ThumbnailPath);
        Assert.Null(row.Width);
        Assert.Single(FilesOf(vehicleId));
    }

    [Fact]
    public async Task Backfill_on_another_vehicles_document_is_not_found()
    {
        await using var context = NewContext();
        var mine = await NewVehicleAsync(context, "REN 011");
        var other = await NewVehicleAsync(context, "REN 012");
        var filed = (await FileAsync(context, other, "elsewhere", "Elsewhere")).Value!;

        var result = await NewDocuments(context).AddRenditionsAsync(mine, filed.Id, Upload("t11", "d11"));

        Assert.Equal(WriteStatus.NotFound, result.Status);
    }

    // ---- reading the bytes --------------------------------------------------------------------------------

    [Fact]
    public async Task Each_variant_resolves_to_its_own_file_and_content_type()
    {
        await using var context = OwnedContext(_ownerId);
        var vehicleId = await NewVehicleAsync(context, "REN 013");
        var filed = (await FileAsync(context, vehicleId, "variant photo", "Variants", contentType: "image/heic",
            renditions: Upload("t13", "d13", thumbnailType: "image/jpeg"))).Value!;
        var row = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id);
        var documents = NewDocuments(context);

        var original = await documents.FindFileAsync(vehicleId, filed.Id, DocumentVariant.Original);
        Assert.Equal(row.FilePath, original!.RelativePath);
        Assert.Equal("image/heic", original.ContentType);
        Assert.Equal("Variants", original.Title);

        var display = await documents.FindFileAsync(vehicleId, filed.Id, DocumentVariant.Display);
        Assert.Equal(row.DisplayPath, display!.RelativePath);
        Assert.Equal("image/webp", display.ContentType);
        // The ETag is the file's own hash, which content-addressing put in its name.
        Assert.Equal(Path.GetFileNameWithoutExtension(row.DisplayPath), display.Hash);

        var thumbnail = await documents.FindFileAsync(vehicleId, filed.Id, DocumentVariant.Thumbnail);
        Assert.Equal(row.ThumbnailPath, thumbnail!.RelativePath);
        Assert.Equal("image/jpeg", thumbnail.ContentType);
    }

    [Fact]
    public async Task A_variant_that_does_not_exist_is_not_found()
    {
        await using var context = OwnedContext(_ownerId);
        var vehicleId = await NewVehicleAsync(context, "REN 014");
        var pdf = (await FileAsync(context, vehicleId, "%PDF a pdf", "A PDF", DocumentType.V5C, "application/pdf")).Value!;
        var documents = NewDocuments(context);

        Assert.NotNull(await documents.FindFileAsync(vehicleId, pdf.Id, DocumentVariant.Original));
        Assert.Null(await documents.FindFileAsync(vehicleId, pdf.Id, DocumentVariant.Display));
        Assert.Null(await documents.FindFileAsync(vehicleId, pdf.Id, DocumentVariant.Thumbnail));
        Assert.Null(await documents.FindFileAsync(vehicleId, pdf.Id + 10_000, DocumentVariant.Original));
    }

    [Theory]
    [InlineData(DocumentVariant.Original)]
    [InlineData(DocumentVariant.Display)]
    [InlineData(DocumentVariant.Thumbnail)]
    public async Task Another_owner_cannot_read_any_variant(DocumentVariant variant)
    {
        int vehicleId, documentId;
        await using (var mine = OwnedContext(_ownerId))
        {
            vehicleId = await NewVehicleAsync(mine, $"ISO {(int)variant:000}");
            documentId = (await FileAsync(mine, vehicleId, $"isolated {variant}", "Isolated",
                renditions: Upload($"ti{variant}", $"di{variant}"))).Value!.Id;
            Assert.NotNull(await NewDocuments(mine).FindFileAsync(vehicleId, documentId, variant));
        }

        // Handed this owner's vehicle id directly, as a caller that skipped the registration lookup would be.
        await using var theirs = OwnedContext(_otherOwnerId);
        Assert.Null(await NewDocuments(theirs).FindFileAsync(vehicleId, documentId, variant));
    }

    // ---- the log and the export ---------------------------------------------------------------------------

    [Fact]
    public async Task Both_photo_categories_are_photos_and_an_image_paper_is_a_paper()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 015");

        await FileAsync(context, vehicleId, "car photo", "After a wash", DocumentType.CarPhoto);
        await FileAsync(context, vehicleId, "condition photo", "Sill corrosion", DocumentType.ConditionPhoto);
        await FileAsync(context, vehicleId, "photographed invoice", "Invoice photo", DocumentType.ServiceInvoice);

        var log = await NewDocuments(context).GetLogAsync(vehicleId);

        Assert.Equal(["After a wash", "Sill corrosion"], log.Photos.Select(p => p.Title).Order());
        Assert.Equal("Invoice photo", log.Papers.Single().Title);
    }

    [Fact]
    public async Task The_export_rows_carry_the_rendition_columns()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "REN 016");
        var filed = (await FileAsync(context, vehicleId, "export photo", "Export", renditions: Upload("t16", "d16"))).Value!;
        var row = await context.Documents.AsNoTracking().SingleAsync(d => d.Id == filed.Id);

        var exported = (await NewDocuments(context).ListRowsAsync(vehicleId)).Single();

        Assert.Equal(row.ThumbnailPath, exported.ThumbnailPath);
        Assert.Equal(row.DisplayPath, exported.DisplayPath);
        Assert.Equal(4032, exported.Width);
        Assert.Equal(3024, exported.Height);
    }
}
