using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The three small logs: tyres, washes and equipment.
/// </summary>
/// <remarks>
/// The list and add halves live in <see cref="LogQueryService"/> and <see cref="LogWriteService"/> so the MCP
/// tools share them; edit and delete stay here (the assistant does neither). The tyre edit/delete use the shared
/// <see cref="OdometerShadow"/> helper the add path uses.
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
            string registration, CarTrackerDbContext context, LogQueryService queries, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);
            return TypedResults.Ok(await queries.ListTyresAsync(id.Value, ct));
        }).WithName("GetTyreReadings").WithSummary("Pressure and tread by corner, oldest last.");

        group.MapPost("/", async Task<Results<Created<TyreReadingItem>, NotFound<ProblemDetails>>> (
            string registration, AddTyreReadingRequest request, CarTrackerDbContext context,
            LogWriteService writes, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var input = new TyreInput(
                request.ReadingDate, request.Mileage,
                request.PsiFrontLeft, request.PsiFrontRight, request.PsiRearLeft, request.PsiRearRight, request.PsiSpare,
                request.TreadFrontLeft, request.TreadFrontRight, request.TreadRearLeft, request.TreadRearRight,
                request.Location, request.Tool, request.Notes);

            var result = await writes.AddTyreAsync(id.Value, input, EntrySource.Web, ct);
            return TypedResults.Created($"/api/vehicles/{registration}/tyres/{result.Value!.Id}", result.Value);
        }).WithName("AddTyreReading");

        group.MapPatch("/{id:int}", async Task<Results<Ok<TyreReadingItem>, NotFound<ProblemDetails>>> (
            string registration, int id, UpdateTyreReadingRequest request, CarTrackerDbContext context,
            AnomalyScanner scanner, CancellationToken ct) =>
        {
            var vehicleId = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (vehicleId is null) return VehicleLookup.NotFound(registration);

            var reading = await context.TyreReadings
                .FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId.Value, ct);
            if (reading is null) return VehicleLookup.NotFound(registration);

            var originalDate = reading.ReadingDate;
            var originalMileage = reading.Mileage;

            reading.ReadingDate = request.ReadingDate ?? reading.ReadingDate;
            reading.Mileage = request.Mileage ?? reading.Mileage;
            reading.PsiFrontLeft = request.PsiFrontLeft ?? reading.PsiFrontLeft;
            reading.PsiFrontRight = request.PsiFrontRight ?? reading.PsiFrontRight;
            reading.PsiRearLeft = request.PsiRearLeft ?? reading.PsiRearLeft;
            reading.PsiRearRight = request.PsiRearRight ?? reading.PsiRearRight;
            reading.PsiSpare = request.PsiSpare ?? reading.PsiSpare;
            reading.TreadFrontLeft = request.TreadFrontLeft ?? reading.TreadFrontLeft;
            reading.TreadFrontRight = request.TreadFrontRight ?? reading.TreadFrontRight;
            reading.TreadRearLeft = request.TreadRearLeft ?? reading.TreadRearLeft;
            reading.TreadRearRight = request.TreadRearRight ?? reading.TreadRearRight;
            reading.Location = request.Location ?? reading.Location;
            reading.Tool = request.Tool ?? reading.Tool;
            reading.Notes = request.Notes ?? reading.Notes;

            await OdometerShadow.SyncAsync(
                context, vehicleId.Value, MileageOrigin.Tyre,
                originalDate, originalMileage, reading.ReadingDate, reading.Mileage, EntrySource.Web, ct);

            await context.SaveChangesAsync(ct);
            await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, ct);

            return TypedResults.Ok(new TyreReadingItem(
                reading.Id, reading.ReadingDate, reading.Mileage,
                reading.PsiFrontLeft, reading.PsiFrontRight, reading.PsiRearLeft, reading.PsiRearRight, reading.PsiSpare,
                reading.TreadFrontLeft, reading.TreadFrontRight, reading.TreadRearLeft, reading.TreadRearRight,
                reading.Location, reading.Tool, reading.Notes));
        }).WithName("UpdateTyreReading").WithSummary("Corrects a tyre reading; its odometer shadow follows, then the detectors re-run.");

        group.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound<ProblemDetails>>> (
            string registration, int id, CarTrackerDbContext context, AnomalyScanner scanner, CancellationToken ct) =>
        {
            var vehicleId = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (vehicleId is null) return VehicleLookup.NotFound(registration);

            var reading = await context.TyreReadings
                .FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId.Value, ct);
            if (reading is null) return VehicleLookup.NotFound(registration);

            await OdometerShadow.SyncAsync(
                context, vehicleId.Value, MileageOrigin.Tyre,
                reading.ReadingDate, reading.Mileage, reading.ReadingDate, newMileage: null, EntrySource.Web, ct);

            context.TyreReadings.Remove(reading);
            await context.SaveChangesAsync(ct);
            if (reading.Mileage is not null) await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, ct);

            return TypedResults.NoContent();
        }).WithName("DeleteTyreReading").WithSummary("Removes a tyre reading and its odometer shadow.");
    }

    // ---- washes ------------------------------------------------------------------------------------------

    private static void MapWashes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/washes").WithTags("Wash");

        group.MapGet("/", async Task<Results<Ok<List<WashItem>>, NotFound<ProblemDetails>>> (
            string registration, CarTrackerDbContext context, LogQueryService queries, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);
            return TypedResults.Ok(await queries.ListWashesAsync(id.Value, ct));
        }).WithName("GetWashes").WithSummary("The wash log, oldest last. Cadence is derived by the caller from the dates.");

        group.MapPost("/", async Task<Results<Created<WashItem>, NotFound<ProblemDetails>>> (
            string registration, AddWashRequest request, CarTrackerDbContext context,
            LogWriteService writes, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var input = new WashInput(request.WashDate, request.Location, request.WashType, request.Cost, request.Mileage, request.Notes);
            var result = await writes.AddWashAsync(id.Value, input, EntrySource.Web, ct);
            return TypedResults.Created($"/api/vehicles/{registration}/washes/{result.Value!.Id}", result.Value);
        }).WithName("AddWash");

        group.MapPatch("/{id:int}", async Task<Results<Ok<WashItem>, NotFound<ProblemDetails>>> (
            string registration, int id, UpdateWashRequest request, CarTrackerDbContext context,
            ReferenceWriter references, CancellationToken ct) =>
        {
            var vehicleId = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (vehicleId is null) return VehicleLookup.NotFound(registration);

            var wash = await context.WashEntries
                .FirstOrDefaultAsync(w => w.Id == id && w.VehicleId == vehicleId.Value, ct);
            if (wash is null) return VehicleLookup.NotFound(registration);

            if (request.Location is not null) await references.EnsureWashLocationAsync(request.Location, ct);

            wash.WashDate = request.WashDate ?? wash.WashDate;
            wash.Location = request.Location ?? wash.Location;
            wash.WashType = request.WashType ?? wash.WashType;
            wash.Cost = request.Cost ?? wash.Cost;
            wash.Mileage = request.Mileage ?? wash.Mileage;
            wash.Notes = request.Notes ?? wash.Notes;

            await context.SaveChangesAsync(ct);

            return TypedResults.Ok(
                new WashItem(wash.Id, wash.WashDate, wash.Location, wash.WashType, wash.Cost, wash.Mileage, wash.Notes));
        }).WithName("UpdateWash").WithSummary("Corrects a wash; a new location name is created on first use.");

        group.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound<ProblemDetails>>> (
            string registration, int id, CarTrackerDbContext context, CancellationToken ct) =>
        {
            var vehicleId = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (vehicleId is null) return VehicleLookup.NotFound(registration);

            var wash = await context.WashEntries
                .FirstOrDefaultAsync(w => w.Id == id && w.VehicleId == vehicleId.Value, ct);
            if (wash is null) return VehicleLookup.NotFound(registration);

            context.WashEntries.Remove(wash);
            await context.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        }).WithName("DeleteWash").WithSummary("Removes a wash entry.");
    }

    // ---- equipment ---------------------------------------------------------------------------------------

    private static void MapEquipment(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/equipment").WithTags("Equipment");

        group.MapGet("/", async Task<Results<Ok<List<EquipmentItemDto>>, NotFound<ProblemDetails>>> (
            string registration, CarTrackerDbContext context, LogQueryService queries, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);
            return TypedResults.Ok(await queries.ListEquipmentAsync(id.Value, ct));
        }).WithName("GetEquipment").WithSummary("The kit inventory — what is owned, on order, and still to buy.");

        group.MapPost("/", async Task<Results<Created<EquipmentItemDto>, NotFound<ProblemDetails>, ValidationProblem>> (
            string registration, AddEquipmentRequest request, CarTrackerDbContext context,
            LogWriteService writes, CancellationToken ct) =>
        {
            var id = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (id is null) return VehicleLookup.NotFound(registration);

            var input = new EquipmentInput(
                request.Name, request.Status, request.Category, request.PurchasedDate,
                request.SourceVendor, request.Cost, request.StoredAt, request.Notes);

            var result = await writes.AddEquipmentAsync(id.Value, input, EntrySource.Web, ct);
            if (result is { Status: WriteStatus.Validation, Errors: { } errors })
                return TypedResults.ValidationProblem(errors);

            return TypedResults.Created($"/api/vehicles/{registration}/equipment/{result.Value!.Id}", result.Value);
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

        group.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound<ProblemDetails>>> (
            string registration, int id, CarTrackerDbContext context, CancellationToken ct) =>
        {
            var vehicleId = await VehicleLookup.FindIdAsync(context, registration, ct);
            if (vehicleId is null) return VehicleLookup.NotFound(registration);

            var item = await context.EquipmentItems
                .FirstOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId.Value, ct);
            if (item is null) return VehicleLookup.NotFound(registration);

            context.EquipmentItems.Remove(item);
            await context.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        }).WithName("DeleteEquipment").WithSummary("Removes an equipment item.");
    }
}

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

/// <summary>Every field optional: null leaves the tyre reading's value untouched. A mileage supplied for the
/// first time creates the odometer shadow; one already present moves with the edit.</summary>
public sealed record UpdateTyreReadingRequest(
    DateOnly? ReadingDate = null,
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

public sealed record AddWashRequest(
    DateOnly WashDate,
    string? Location = null,
    string? WashType = null,
    decimal? Cost = null,
    int? Mileage = null,
    string? Notes = null);

/// <summary>Every field optional: null leaves the wash's value untouched.</summary>
public sealed record UpdateWashRequest(
    DateOnly? WashDate = null,
    string? Location = null,
    string? WashType = null,
    decimal? Cost = null,
    int? Mileage = null,
    string? Notes = null);

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
