using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Odometer readings — the spine every other log writes into.
/// </summary>
public static class MileageEndpoints
{
    public static IEndpointRouteBuilder MapMileageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/mileage").WithTags("Mileage");

        group.MapGet("/", GetReadingsAsync)
            .WithName("GetMileageReadings")
            .WithSummary("Every reading, newest first, with its origin. The odometer derives from the newest valid one.");

        group.MapPost("/", AddReadingAsync)
            .WithName("AddMileageReading")
            .WithSummary("Records a manual reading, then re-runs the integrity detectors. A reading below the odometer is flagged, never rejected.");

        group.MapPatch("/{id:int}", UpdateReadingAsync)
            .WithName("UpdateMileageReading")
            .WithSummary("Corrects a manual reading, then re-runs the detectors. A fuel/service shadow reading is edited via its source.");

        group.MapDelete("/{id:int}", DeleteReadingAsync)
            .WithName("DeleteMileageReading")
            .WithSummary("Removes a manual reading, then re-runs the detectors. The odometer re-derives from the newest remaining reading.");

        return app;
    }

    private static async Task<Results<Ok<MileageLog>, NotFound<ProblemDetails>>> GetReadingsAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        if (summary is null) return VehicleLookup.NotFound(registration);

        var readings = await context.MileageReadings
            .AsNoTracking()
            .Where(m => m.VehicleId == vehicleId.Value)
            .OrderByDescending(m => m.ReadingDate)
            .ThenByDescending(m => m.Id)
            .Select(m => new MileageReadingItem(m.Id, m.ReadingDate, m.Mileage, m.Origin, m.Notes))
            .ToListAsync(cancellationToken);

        // The derived half comes from the summary, not recomputed here — current mileage is the newest reading
        // BY DATE, not MAX(mileage), and the 83,000 mi row is exactly why those differ.
        return TypedResults.Ok(new MileageLog(summary.Mileage, readings));
    }

    /// <remarks>
    /// <para>
    /// A reading below the current odometer is <b>recorded and flagged</b>, never refused. That is spec §5.3
    /// and it is the product's whole thesis: the workbook silently accepted a service record of 83,000 mi
    /// against a real odometer of 80,712 — almost certainly 80,300 mistyped — and every figure downstream
    /// inherited it. Refusing the save would just push the owner into editing the number until the app takes
    /// it, which is the same outcome with more steps.
    /// </para>
    /// <para>
    /// So the flag is the answer, not the 400. The reading lands, the detector raises it, the odometer ignores
    /// it, and a human decides.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<AddReadingResponse>, NotFound<ProblemDetails>, ValidationProblem>> AddReadingAsync(
        string registration,
        AddReadingRequest request,
        CarTrackerDbContext context,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (request.Mileage <= 0)
        {
            // Not a judgement about the value, just that it is not a reading.
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Mileage)] = ["An odometer reading must be greater than zero."],
            });
        }

        var reading = new MileageReading
        {
            VehicleId = vehicleId.Value,
            ReadingDate = request.ReadingDate,
            Mileage = request.Mileage,
            Origin = MileageOrigin.Manual,
            Notes = request.Notes,
            Source = EntrySource.Web,
        };

        context.MileageReadings.Add(reading);
        await context.SaveChangesAsync(cancellationToken);

        var flags = await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/mileage",
            new AddReadingResponse(reading.Id, [.. flags.Select(FuelEndpoints.ToFlag)]));
    }

    /// <remarks>
    /// Only a <see cref="MileageOrigin.Manual"/> reading is editable here. The rest are shadows — a fill, a
    /// service, a tyre check or a wash wrote them — and a shadow is corrected through its source, or the two
    /// drift. The founding <see cref="MileageOrigin.Purchase"/> reading is a shadow too, which is why it cannot
    /// be edited or deleted away. A single-table write, so no execution strategy: the retrying strategy only
    /// balks at user-initiated transactions, which this is not.
    /// </remarks>
    private static async Task<Results<Ok<MileageReadingItem>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> UpdateReadingAsync(
        string registration,
        int id,
        UpdateReadingRequest request,
        CarTrackerDbContext context,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var reading = await context.MileageReadings
            .FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicleId.Value, cancellationToken);
        if (reading is null) return ReadingNotFound(id, registration);
        if (reading.Origin != MileageOrigin.Manual) return ShadowReading(id, reading.Origin);

        if (request.Mileage is <= 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Mileage)] = ["An odometer reading must be greater than zero."],
            });
        }

        reading.ReadingDate = request.ReadingDate ?? reading.ReadingDate;
        reading.Mileage = request.Mileage ?? reading.Mileage;
        reading.Notes = request.Notes ?? reading.Notes;

        await context.SaveChangesAsync(cancellationToken);

        // Editing a reading down can clear a non-monotonic flag; editing one up can raise one. Auto-reconcile
        // closes a flag whose cause the edit removed.
        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Ok(new MileageReadingItem(reading.Id, reading.ReadingDate, reading.Mileage, reading.Origin, reading.Notes));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> DeleteReadingAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var reading = await context.MileageReadings
            .FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicleId.Value, cancellationToken);
        if (reading is null) return ReadingNotFound(id, registration);
        if (reading.Origin != MileageOrigin.Manual) return ShadowReading(id, reading.Origin);

        context.MileageReadings.Remove(reading);
        await context.SaveChangesAsync(cancellationToken);

        // The odometer re-derives from the newest remaining reading by date; deleting a non-latest one moves
        // nothing. A non-monotonic flag whose reading this was auto-reconciles on the scan.
        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.NoContent();
    }

    private static NotFound<ProblemDetails> ReadingNotFound(int id, string registration) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Reading not found",
            Detail = $"No mileage reading {id} on '{registration}'.",
            Status = StatusCodes.Status404NotFound,
        });

    private static Conflict<ProblemDetails> ShadowReading(int id, MileageOrigin origin) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = "Reading mirrors another log",
            Detail = $"Reading {id} was written by the {origin} log. Edit or remove that entry and this reading "
                   + "follows — a shadow cannot be changed apart from its source.",
            Status = StatusCodes.Status409Conflict,
        });
}

public sealed record MileageReadingItem(int Id, DateOnly ReadingDate, int Mileage, MileageOrigin Origin, string? Notes);

/// <param name="Derived">
/// The computed half — current odometer, miles since purchase, and whether the history is non-monotonic.
/// Never recomputed from <paramref name="Readings"/> by a caller: current mileage is the newest reading by
/// DATE, not the largest, and the two differ precisely when it matters.
/// </param>
public sealed record MileageLog(MileageResult Derived, IReadOnlyList<MileageReadingItem> Readings);

public sealed record AddReadingRequest(DateOnly ReadingDate, int Mileage, string? Notes = null);

public sealed record AddReadingResponse(int Id, IReadOnlyList<AnomalyFlag> Flags);

/// <summary>Every field optional: null leaves the reading's value untouched.</summary>
public sealed record UpdateReadingRequest(DateOnly? ReadingDate = null, int? Mileage = null, string? Notes = null);
