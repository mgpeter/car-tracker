using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
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
        LogQueryService queries,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        if (summary is null) return VehicleLookup.NotFound(registration);

        // Rows from the shared query (so list_mileage matches); the derived half from the summary, not recomputed
        // — current mileage is the newest reading BY DATE, not MAX(mileage), and the 83,000 mi row is why they differ.
        var readings = await queries.ListMileageAsync(vehicleId.Value, cancellationToken);
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
        LogWriteService writes,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await writes.AddMileageAsync(
            vehicleId.Value, new MileageInput(request.ReadingDate, request.Mileage, request.Notes), EntrySource.Web, cancellationToken);

        if (result is { Status: WriteStatus.Validation, Errors: { } errors })
            return TypedResults.ValidationProblem(errors);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/mileage",
            new AddReadingResponse(result.Value!.Id, result.Flags));
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
        LogWriteService writes,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        // One path with the MCP update_mileage_reading tool: the "only a Manual reading is editable" rule, the
        // re-scan and the shadow conflict all live in the shared service.
        var result = await writes.UpdateMileageAsync(
            vehicleId.Value, id, new MileagePatch(request.ReadingDate, request.Mileage, request.Notes),
            EntrySource.Web, cancellationToken);

        return result.Status switch
        {
            WriteStatus.Updated => TypedResults.Ok(result.Value!),
            WriteStatus.Validation => TypedResults.ValidationProblem(result.Errors!),
            WriteStatus.Conflict => ShadowConflict(result),
            _ => ReadingNotFound(id, registration),
        };
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> DeleteReadingAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        LogWriteService writes,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await writes.DeleteMileageAsync(vehicleId.Value, id, EntrySource.Web, cancellationToken);

        return result.Status switch
        {
            WriteStatus.Updated => TypedResults.NoContent(),
            WriteStatus.Conflict => ShadowConflict(result),
            _ => ReadingNotFound(id, registration),
        };
    }

    private static NotFound<ProblemDetails> ReadingNotFound(int id, string registration) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Reading not found",
            Detail = $"No mileage reading {id} on '{registration}'.",
            Status = StatusCodes.Status404NotFound,
        });

    private static Conflict<ProblemDetails> ShadowConflict<T>(WriteResult<T> result) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = result.ConflictTitle,
            Detail = result.ConflictDetail,
            Status = StatusCodes.Status409Conflict,
        });
}

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
