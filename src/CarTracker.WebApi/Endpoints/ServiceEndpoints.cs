using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Service history — and the two workbook defects that could not be shown without it.
/// </summary>
/// <remarks>
/// The MOT expiry derives from the latest <c>Type = "MOT"</c> record here, so until this endpoint existed a
/// vehicle's MOT could only ever read "not set". And the workbook's 27 Jun 2026 row logging 83,000 mi against
/// a current 80,712 lives on this sheet: it is the row that trips the mileage detector, and there was nowhere
/// to enter it.
/// </remarks>
public static class ServiceEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/service").WithTags("Service");

        group.MapGet("/", GetServiceAsync)
            .WithName("GetServiceHistory")
            .WithSummary("Every service record, newest last, with the derived next-service figures.");

        group.MapPost("/", AddServiceAsync)
            .WithName("AddServiceRecord")
            .WithSummary("Records a service, its odometer reading and its mirrored expense, then re-runs the detectors.");

        group.MapPatch("/{id:int}", UpdateServiceAsync)
            .WithName("UpdateServiceRecord")
            .WithSummary("Corrects a record and its shadows, then re-runs the detectors.");

        group.MapDelete("/{id:int}", DeleteServiceAsync)
            .WithName("DeleteServiceRecord")
            .WithSummary("Removes a record; its mirrored reading and expense go with it.");

        return app;
    }

    private static async Task<Results<Ok<ServiceLog>, NotFound<ProblemDetails>>> GetServiceAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        if (summary is null) return VehicleLookup.NotFound(registration);

        var records = await context.ServiceRecords
            .Where(r => r.VehicleId == vehicleId.Value)
            .OrderBy(r => r.ServiceDate).ThenBy(r => r.Id)
            .Select(r => new ServiceRecordItem(
                r.Id, r.ServiceDate, r.Type, r.Mileage, r.Garage, r.WorkDone, r.PartsReplaced,
                r.Cost, r.NextDueDate, r.NextDueMileage, r.Notes))
            .ToListAsync(cancellationToken);

        // The derived half comes from the summary, not from a second pass over these rows: the MOT expiry and
        // the next-service date are RenewalCalculator's answer, and computing them again here is how two
        // surfaces start to disagree.
        return TypedResults.Ok(new ServiceLog(
            records,
            summary.Renewals.Mot,
            summary.Renewals.NextServiceDate,
            summary.Renewals.NextServiceMiles));
    }

    private static async Task<Results<Created<AddServiceResponse>, NotFound<ProblemDetails>, ValidationProblem>> AddServiceAsync(
        string registration,
        AddServiceRequest request,
        CarTrackerDbContext context,
        ServiceRecordFactory factory,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["type"] = ["A service record needs a type. \"MOT\" is matched exactly and is what the expiry derives from."],
            });
        }

        var record = new ServiceRecord
        {
            VehicleId = vehicleId.Value,
            ServiceDate = request.ServiceDate,
            Type = request.Type.Trim(),
            Mileage = request.Mileage,
            Garage = request.Garage,
            WorkDone = request.WorkDone,
            PartsReplaced = request.PartsReplaced,
            Cost = request.Cost,
            NextDueDate = request.NextDueDate,
            NextDueMileage = request.NextDueMileage,
            Notes = request.Notes,
        };

        await factory.CreateAsync(record, EntrySource.Web, cancellationToken);

        // Never a gate. §5.3: the 83,000 mi row saves, and then it is flagged.
        var flags = await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/service/{record.Id}",
            new AddServiceResponse(record.Id, ToFlags(flags)));
    }

    private static async Task<Results<Ok<ServiceRecordItem>, NotFound<ProblemDetails>>> UpdateServiceAsync(
        string registration,
        int id,
        UpdateServiceRequest request,
        CarTrackerDbContext context,
        ServiceRecordFactory factory,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var record = await context.ServiceRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.VehicleId == vehicleId.Value, cancellationToken);
        if (record is null) return VehicleLookup.NotFound(registration);

        // Captured before the edit: the reading carries no FK back, so its old (date, mileage) key is how the
        // factory finds it. This is also the fix for a latent bug in the old inline handler, which matched the
        // reading by the *new* date after mutating it — so moving a service's date silently orphaned its reading.
        var originalDate = record.ServiceDate;
        var originalMileage = record.Mileage;

        record.ServiceDate = request.ServiceDate ?? record.ServiceDate;
        record.Type = request.Type ?? record.Type;
        record.Mileage = request.Mileage ?? record.Mileage;
        record.Garage = request.Garage ?? record.Garage;
        record.WorkDone = request.WorkDone ?? record.WorkDone;
        record.PartsReplaced = request.PartsReplaced ?? record.PartsReplaced;
        record.Cost = request.Cost ?? record.Cost;
        record.NextDueDate = request.NextDueDate ?? record.NextDueDate;
        record.NextDueMileage = request.NextDueMileage ?? record.NextDueMileage;
        record.Notes = request.Notes ?? record.Notes;

        // The shadows follow their source, inside the execution strategy — a record whose mileage is corrected
        // but whose reading is not would leave the odometer deriving from the old figure.
        await factory.UpdateAsync(record, originalDate, originalMileage, cancellationToken);

        // Re-scan: editing a mileage down can clear an anomaly, and editing one up can raise one. A flag that
        // outlives the condition that caused it is a nag.
        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Ok(new ServiceRecordItem(
            record.Id, record.ServiceDate, record.Type, record.Mileage, record.Garage, record.WorkDone,
            record.PartsReplaced, record.Cost, record.NextDueDate, record.NextDueMileage, record.Notes));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteServiceAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        ServiceRecordFactory factory,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var record = await context.ServiceRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.VehicleId == vehicleId.Value, cancellationToken);
        if (record is null) return VehicleLookup.NotFound(registration);

        // The expense cascades on the FK; the reading has none, so the factory removes it — a shadow cannot
        // outlive its source, and an orphaned reading would keep moving the odometer.
        await factory.DeleteAsync(record, cancellationToken);

        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.NoContent();
    }

    private static List<AnomalyFlag> ToFlags(IReadOnlyList<DataAnomaly> flags) =>
        [.. flags.Select(f => new AnomalyFlag(f.Id, f.Kind, f.Severity, f.Message, f.Detail))];
}

public sealed record ServiceRecordItem(
    int Id,
    DateOnly ServiceDate,
    string Type,
    int Mileage,
    string? Garage,
    string? WorkDone,
    string? PartsReplaced,
    decimal? Cost,
    DateOnly? NextDueDate,
    int? NextDueMileage,
    string? Notes);

/// <param name="Mot">
/// The derived MOT renewal, carried here so the screen can say which record it came from without deriving it
/// again. The whole point of the screen: this is what stops reading "not set".
/// </param>
public sealed record ServiceLog(
    IReadOnlyList<ServiceRecordItem> Records,
    Renewal Mot,
    Renewal NextServiceDate,
    int? NextServiceMiles);

/// <param name="Type">
/// Free text, and <c>"MOT"</c> is matched exactly by the expiry derivation. "MOT test" derives nothing and
/// fails silently, which is why the UI offers it as a choice rather than trusting it to be typed.
/// </param>
public sealed record AddServiceRequest(
    DateOnly ServiceDate,
    string Type,
    int Mileage,
    string? Garage = null,
    string? WorkDone = null,
    string? PartsReplaced = null,
    decimal? Cost = null,
    DateOnly? NextDueDate = null,
    int? NextDueMileage = null,
    string? Notes = null);

public sealed record UpdateServiceRequest(
    DateOnly? ServiceDate = null,
    string? Type = null,
    int? Mileage = null,
    string? Garage = null,
    string? WorkDone = null,
    string? PartsReplaced = null,
    decimal? Cost = null,
    DateOnly? NextDueDate = null,
    int? NextDueMileage = null,
    string? Notes = null);

public sealed record AddServiceResponse(int Id, IReadOnlyList<AnomalyFlag> Flags);
