using System.Text;
using CarTracker.Domain;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The car's cover photo: which documents may be one, and what happens to it when its document is deleted or
/// re-tagged as a paper.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CoverPhotoTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private string _connectionString = string.Empty;
    private int _ownerId;
    private int _otherOwnerId;
    private string _root = string.Empty;

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext OwnedContext(int ownerId) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock,
            TestOwner.As(ownerId));

    private CarTrackerDbContext Context() => OwnedContext(_ownerId);

    private DocumentService NewDocuments(CarTrackerDbContext context) =>
        new(context, new DocumentStore(new DocumentStorageOptions(_root)), TestEntitlements.Pro);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_coverphoto");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-cover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var context = new CarTrackerDbContext(
            new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock);
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
        _otherOwnerId = await TestOwner.SeedAsync(context, "test|cover-other");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static async Task<int> NewVehicleAsync(CarTrackerDbContext context, int ownerId, string registration)
    {
        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    private async Task<int> FileAsync(CarTrackerDbContext context, int vehicleId, DocumentType type, string content)
    {
        var contentType = DocumentCategories.Kind(type) == DocumentKind.Photo ? "image/jpeg" : "application/pdf";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var stored = await new DocumentStore(new DocumentStorageOptions(_root)).SaveAsync(vehicleId, stream, contentType);
        var result = await NewDocuments(context).RecordAsync(
            vehicleId, stored!, contentType, type, content, null, null, null, null, null, EntrySource.Web);
        return result.Value!.Id;
    }

    private static async Task<int?> CoverOf(CarTrackerDbContext context, int vehicleId) =>
        await context.Vehicles.AsNoTracking().Where(v => v.Id == vehicleId).Select(v => v.CoverPhotoId).SingleAsync();

    [Theory]
    [InlineData(DocumentType.CarPhoto)]
    [InlineData(DocumentType.ConditionPhoto)]
    public async Task Either_photo_category_can_be_the_cover(DocumentType type)
    {
        await using var context = Context();
        var vehicleId = await NewVehicleAsync(context, _ownerId, $"CVR {(int)type:000}");
        var photo = await FileAsync(context, vehicleId, type, $"cover {type}");

        var result = await NewDocuments(context).SetCoverAsync(vehicleId, photo);

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Equal(photo, await CoverOf(context, vehicleId));
    }

    [Fact]
    public async Task A_paper_cannot_be_the_cover()
    {
        await using var context = Context();
        var vehicleId = await NewVehicleAsync(context, _ownerId, "CVR 100");
        var invoice = await FileAsync(context, vehicleId, DocumentType.ServiceInvoice, "an invoice");

        var result = await NewDocuments(context).SetCoverAsync(vehicleId, invoice);

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Contains("documentId", result.Errors!.Keys);
        Assert.Null(await CoverOf(context, vehicleId));
    }

    [Fact]
    public async Task Another_vehicles_photo_cannot_be_the_cover()
    {
        await using var context = Context();
        var mine = await NewVehicleAsync(context, _ownerId, "CVR 101");
        var other = await NewVehicleAsync(context, _ownerId, "CVR 102");
        var theirPhoto = await FileAsync(context, other, DocumentType.CarPhoto, "the other car");

        var result = await NewDocuments(context).SetCoverAsync(mine, theirPhoto);

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.Contains("documentId", result.Errors!.Keys);
        Assert.Null(await CoverOf(context, mine));
    }

    [Fact]
    public async Task Another_owners_photo_cannot_be_the_cover_and_their_car_cannot_be_reached()
    {
        int theirVehicle, theirPhoto;
        await using (var theirs = OwnedContext(_otherOwnerId))
        {
            theirVehicle = await NewVehicleAsync(theirs, _otherOwnerId, "CVR 103");
            theirPhoto = await FileAsync(theirs, theirVehicle, DocumentType.CarPhoto, "someone else's car");
        }

        await using var context = Context();
        var mine = await NewVehicleAsync(context, _ownerId, "CVR 104");

        var foreignDocument = await NewDocuments(context).SetCoverAsync(mine, theirPhoto);
        Assert.Equal(WriteStatus.Validation, foreignDocument.Status);
        Assert.Contains("documentId", foreignDocument.Errors!.Keys);

        // Handed their vehicle id directly, as a caller that skipped the registration lookup would be.
        var foreignVehicle = await NewDocuments(context).SetCoverAsync(theirVehicle, theirPhoto);
        Assert.Equal(WriteStatus.NotFound, foreignVehicle.Status);

        await using var check = OwnedContext(_otherOwnerId);
        Assert.Null(await CoverOf(check, theirVehicle));
    }

    [Fact]
    public async Task Null_clears_the_cover()
    {
        await using var context = Context();
        var vehicleId = await NewVehicleAsync(context, _ownerId, "CVR 105");
        var photo = await FileAsync(context, vehicleId, DocumentType.CarPhoto, "cleared");
        await NewDocuments(context).SetCoverAsync(vehicleId, photo);

        var result = await NewDocuments(context).SetCoverAsync(vehicleId, null);

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Null(await CoverOf(context, vehicleId));
    }

    [Fact]
    public async Task Deleting_the_cover_document_leaves_the_car_without_a_cover()
    {
        int vehicleId, photo;
        await using (var context = Context())
        {
            vehicleId = await NewVehicleAsync(context, _ownerId, "CVR 106");
            photo = await FileAsync(context, vehicleId, DocumentType.CarPhoto, "deleted cover");
            await NewDocuments(context).SetCoverAsync(vehicleId, photo);
            Assert.Equal(WriteStatus.Updated, (await NewDocuments(context).DeleteAsync(vehicleId, photo)).Status);
        }

        await using var reload = Context();
        Assert.Null(await CoverOf(reload, vehicleId));
    }

    [Fact]
    public async Task Re_tagging_the_cover_as_a_paper_clears_it()
    {
        await using var context = Context();
        var vehicleId = await NewVehicleAsync(context, _ownerId, "CVR 107");
        var photo = await FileAsync(context, vehicleId, DocumentType.CarPhoto, "photographed invoice");
        await NewDocuments(context).SetCoverAsync(vehicleId, photo);

        var result = await NewDocuments(context).UpdateAsync(
            vehicleId, photo, new DocumentPatch(Type: DocumentType.ServiceInvoice));

        Assert.Equal(WriteStatus.Updated, result.Status);
        Assert.Null(await CoverOf(context, vehicleId));
    }

    [Fact]
    public async Task Re_tagging_the_cover_as_another_photo_category_keeps_it()
    {
        await using var context = Context();
        var vehicleId = await NewVehicleAsync(context, _ownerId, "CVR 108");
        var photo = await FileAsync(context, vehicleId, DocumentType.CarPhoto, "still a photo");
        await NewDocuments(context).SetCoverAsync(vehicleId, photo);

        await NewDocuments(context).UpdateAsync(vehicleId, photo, new DocumentPatch(Type: DocumentType.ConditionPhoto));

        Assert.Equal(photo, await CoverOf(context, vehicleId));
    }

    [Fact]
    public async Task Re_tagging_a_photo_that_is_not_the_cover_leaves_the_cover_alone()
    {
        await using var context = Context();
        var vehicleId = await NewVehicleAsync(context, _ownerId, "CVR 109");
        var cover = await FileAsync(context, vehicleId, DocumentType.CarPhoto, "the cover");
        var other = await FileAsync(context, vehicleId, DocumentType.CarPhoto, "not the cover");
        await NewDocuments(context).SetCoverAsync(vehicleId, cover);

        await NewDocuments(context).UpdateAsync(vehicleId, other, new DocumentPatch(Type: DocumentType.Receipt));

        Assert.Equal(cover, await CoverOf(context, vehicleId));
    }
}
