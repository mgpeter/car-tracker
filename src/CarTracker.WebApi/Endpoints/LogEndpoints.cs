using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The three small logs: tyres, washes and equipment.
/// </summary>
/// <remarks>
/// One file because they are one shape — a vehicle-scoped list with a date and a create — and three files
/// would be three copies of the same twelve lines. Fuel, expenses and service each earn their own because each
/// has a factory, a mirror or a derivation behind it; these do not.
/// </remarks>
public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        MapTyres(app);
        MapWashes(app);
        MapEquipment(app);
        return app;
    }

    // ---- tyres -------------------------------------------------------------------------------------------

    private static void MapTyres(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/tyres").WithTags("Tyres");

        group.MapGet("/", async Task<Results<Ok<List<TyreReadingItem>>, NotFound<ProblemDetails>>> (
            string registration, CarTrackerDbContext context, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var items = await context.TyreReadings
                .Where(t => t.VehicleId == id.Value)
                .OrderBy(t => t.ReadingDate).ThenBy(t => t.Id)
                .Select(t => new TyreReadingItem(
                    t.Id, t.ReadingDate, t.Mileage,
                    t.PsiFrontLeft, t.PsiFrontRight, t.PsiRearLeft, t.PsiRearRight, t.PsiSpare,
                    t.TreadFrontLeft, t.TreadFrontRight, t.TreadRearLeft, t.TreadRearRight,
                    t.Location, t.Tool, t.Notes))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("GetTyreReadings").WithSummary("Pressure and tread by corner, oldest last.");

        group.MapPost("/", async Task<Results<Created<TyreReadingItem>, NotFound<ProblemDetails>>> (
            string registration, AddTyreReadingRequest request, CarTrackerDbContext context,
            AnomalyScanner scanner, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var reading = new TyreReading
            {
                VehicleId = id.Value,
                ReadingDate = request.ReadingDate,
                Mileage = request.Mileage,
                PsiFrontLeft = request.PsiFrontLeft,
                PsiFrontRight = request.PsiFrontRight,
                PsiRearLeft = request.PsiRearLeft,
                PsiRearRight = request.PsiRearRight,
                PsiSpare = request.PsiSpare,
                TreadFrontLeft = request.TreadFrontLeft,
                TreadFrontRight = request.TreadFrontRight,
                TreadRearLeft = request.TreadRearLeft,
                TreadRearRight = request.TreadRearRight,
                Location = request.Location,
                Tool = request.Tool,
                Notes = request.Notes,
                Source = EntrySource.Web,
            };

            context.TyreReadings.Add(reading);

            // A tyre check taken at a mileage is a mileage reading, like every other log that carries one.
            // Optional here, because a pressure check in the driveway often has no odometer attached.
            if (request.Mileage is { } miles)
            {
                context.MileageReadings.Add(new MileageReading
                {
                    VehicleId = id.Value,
                    ReadingDate = request.ReadingDate,
                    Mileage = miles,
                    Origin = MileageOrigin.Tyre,
                    Source = EntrySource.Web,
                });
            }

            await context.SaveChangesAsync(ct);
            if (request.Mileage is not null) await scanner.ScanAsync(id.Value, EntrySource.Web, ct);

            return TypedResults.Created(
                $"/api/vehicles/{registration}/tyres/{reading.Id}",
                new TyreReadingItem(
                    reading.Id, reading.ReadingDate, reading.Mileage,
                    reading.PsiFrontLeft, reading.PsiFrontRight, reading.PsiRearLeft, reading.PsiRearRight, reading.PsiSpare,
                    reading.TreadFrontLeft, reading.TreadFrontRight, reading.TreadRearLeft, reading.TreadRearRight,
                    reading.Location, reading.Tool, reading.Notes));
        }).WithName("AddTyreReading");
    }

    // ---- washes ------------------------------------------------------------------------------------------

    private static void MapWashes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/washes").WithTags("Wash");

        group.MapGet("/", async Task<Results<Ok<List<WashItem>>, NotFound<ProblemDetails>>> (
            string registration, CarTrackerDbContext context, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var items = await context.WashEntries
                .Where(w => w.VehicleId == id.Value)
                .OrderBy(w => w.WashDate).ThenBy(w => w.Id)
                .Select(w => new WashItem(w.Id, w.WashDate, w.Location, w.WashType, w.Cost, w.Mileage, w.Notes))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("GetWashes").WithSummary("The wash log, oldest last. Cadence is derived by the caller from the dates.");

        group.MapPost("/", async Task<Results<Created<WashItem>, NotFound<ProblemDetails>>> (
            string registration, AddWashRequest request, CarTrackerDbContext context,
            ReferenceWriter references, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            // Location is a foreign key to a keyed table, not the free text it looks like — the same trap as
            // ServiceRecord.Garage, which was a 500 the first time anyone typed a new name.
            await references.EnsureWashLocationAsync(request.Location, ct);

            var wash = new WashEntry
            {
                VehicleId = id.Value,
                WashDate = request.WashDate,
                Location = request.Location,
                WashType = request.WashType,
                Cost = request.Cost,
                Mileage = request.Mileage,
                Notes = request.Notes,
                Source = EntrySource.Web,
            };

            context.WashEntries.Add(wash);
            await context.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/vehicles/{registration}/washes/{wash.Id}",
                new WashItem(wash.Id, wash.WashDate, wash.Location, wash.WashType, wash.Cost, wash.Mileage, wash.Notes));
        }).WithName("AddWash");
    }

    // ---- equipment ---------------------------------------------------------------------------------------

    private static void MapEquipment(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/equipment").WithTags("Equipment");

        group.MapGet("/", async Task<Results<Ok<List<EquipmentItemDto>>, NotFound<ProblemDetails>>> (
            string registration, CarTrackerDbContext context, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var items = await context.EquipmentItems
                .Where(e => e.VehicleId == id.Value)
                .OrderBy(e => e.Status).ThenBy(e => e.Category).ThenBy(e => e.Name)
                .Select(e => new EquipmentItemDto(
                    e.Id, e.Name, e.Category, e.PurchasedDate, e.SourceVendor, e.Cost, e.StoredAt, e.Status, e.Notes))
                .ToListAsync(ct);

            return TypedResults.Ok(items);
        }).WithName("GetEquipment").WithSummary("The kit inventory — what is owned, on order, and still to buy.");

        group.MapPost("/", async Task<Results<Created<EquipmentItemDto>, NotFound<ProblemDetails>, ValidationProblem>> (
            string registration, AddEquipmentRequest request, CarTrackerDbContext context, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["An equipment item needs a name."],
                });
            }

            var item = new EquipmentItem
            {
                VehicleId = id.Value,
                Name = request.Name.Trim(),
                Category = request.Category,
                PurchasedDate = request.PurchasedDate,
                SourceVendor = request.SourceVendor,
                Cost = request.Cost,
                StoredAt = request.StoredAt,
                Status = request.Status,
                Notes = request.Notes,
                Source = EntrySource.Web,
            };

            context.EquipmentItems.Add(item);
            await context.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/vehicles/{registration}/equipment/{item.Id}",
                new EquipmentItemDto(
                    item.Id, item.Name, item.Category, item.PurchasedDate, item.SourceVendor,
                    item.Cost, item.StoredAt, item.Status, item.Notes));
        }).WithName("AddEquipment");

        group.MapPatch("/{id:int}", async Task<Results<Ok<EquipmentItemDto>, NotFound<ProblemDetails>>> (
            string registration, int id, UpdateEquipmentRequest request, CarTrackerDbContext context,
            CancellationToken ct) =>
        {
            var vehicleId = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (vehicleId is null) return VehicleLookup.NotFound(registration);

            var item = await context.EquipmentItems
                .FirstOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId.Value, ct);
            if (item is null) return VehicleLookup.NotFound(registration);

            item.Name = request.Name ?? item.Name;
            item.Category = request.Category ?? item.Category;
            item.PurchasedDate = request.PurchasedDate ?? item.PurchasedDate;
            item.SourceVendor = request.SourceVendor ?? item.SourceVendor;
            item.Cost = request.Cost ?? item.Cost;
            item.StoredAt = request.StoredAt ?? item.StoredAt;
            item.Status = request.Status ?? item.Status;
            item.Notes = request.Notes ?? item.Notes;

            await context.SaveChangesAsync(ct);

            return TypedResults.Ok(new EquipmentItemDto(
                item.Id, item.Name, item.Category, item.PurchasedDate, item.SourceVendor,
                item.Cost, item.StoredAt, item.Status, item.Notes));
        }).WithName("UpdateEquipment");
    }
}

/// <param name="PsiSpare">
/// The workbook's eighteenth check is "Spare tyre pressure" and it has never been logged — which is why its
/// Dashboard counts 17 of 18. Nullable here for the same reason: not checked is not zero.
/// </param>
public sealed record TyreReadingItem(
    int Id,
    DateOnly ReadingDate,
    int? Mileage,
    decimal? PsiFrontLeft,
    decimal? PsiFrontRight,
    decimal? PsiRearLeft,
    decimal? PsiRearRight,
    decimal? PsiSpare,
    decimal? TreadFrontLeft,
    decimal? TreadFrontRight,
    decimal? TreadRearLeft,
    decimal? TreadRearRight,
    string? Location,
    string? Tool,
    string? Notes);

public sealed record AddTyreReadingRequest(
    DateOnly ReadingDate,
    int? Mileage = null,
    decimal? PsiFrontLeft = null,
    decimal? PsiFrontRight = null,
    decimal? PsiRearLeft = null,
    decimal? PsiRearRight = null,
    decimal? PsiSpare = null,
    decimal? TreadFrontLeft = null,
    decimal? TreadFrontRight = null,
    decimal? TreadRearLeft = null,
    decimal? TreadRearRight = null,
    string? Location = null,
    string? Tool = null,
    string? Notes = null);

public sealed record WashItem(
    int Id,
    DateOnly WashDate,
    string? Location,
    string? WashType,
    decimal? Cost,
    int? Mileage,
    string? Notes);

public sealed record AddWashRequest(
    DateOnly WashDate,
    string? Location = null,
    string? WashType = null,
    decimal? Cost = null,
    int? Mileage = null,
    string? Notes = null);

public sealed record EquipmentItemDto(
    int Id,
    string Name,
    string? Category,
    DateOnly? PurchasedDate,
    string? SourceVendor,
    decimal? Cost,
    string? StoredAt,
    EquipmentStatus Status,
    string? Notes);

public sealed record AddEquipmentRequest(
    string Name,
    EquipmentStatus Status = EquipmentStatus.Owned,
    string? Category = null,
    DateOnly? PurchasedDate = null,
    string? SourceVendor = null,
    decimal? Cost = null,
    string? StoredAt = null,
    string? Notes = null);

public sealed record UpdateEquipmentRequest(
    string? Name = null,
    EquipmentStatus? Status = null,
    string? Category = null,
    DateOnly? PurchasedDate = null,
    string? SourceVendor = null,
    decimal? Cost = null,
    string? StoredAt = null,
    string? Notes = null);
