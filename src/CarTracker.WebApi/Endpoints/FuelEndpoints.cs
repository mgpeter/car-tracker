using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The fuel log — the daily loop's flagship write.
/// </summary>
public static class FuelEndpoints
{
    public static IEndpointRouteBuilder MapFuelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/fuel").WithTags("Fuel");

        group.MapGet("/", GetFuelAsync)
            .WithName("GetFuelLog")
            .WithSummary("Every fill with its computed MPG, newest last. MPG is derived per fill, never stored.");

        group.MapPatch("/{id:int}", UpdateFillAsync)
            .WithName("UpdateFill")
            .WithSummary("Corrects a fill and its shadows — the reading and the mirrored expense follow — then re-runs the detectors.");

        group.MapDelete("/{id:int}", DeleteFillAsync)
            .WithName("DeleteFill")
            .WithSummary("Removes a fill and its shadows — the mileage reading and the mirrored expense go with it.");

        group.MapPost("/", AddFillAsync)
            .WithName("AddFill")
            .WithSummary("Records a fill, its odometer reading and its mirrored expense, then re-runs the integrity detectors.");

        return app;
    }

    /// <remarks>
    /// Reads through <see cref="IDerivedMetricsService"/>, not the table. The per-fill MPG, the reliability
    /// flag and the fleet stats are all computed — a raw <c>FuelEntries</c> query would hand back rows with no
    /// MPG at all and invite the screen to work it out again, which is how two places start disagreeing.
    /// </remarks>
    private static async Task<Results<Ok<FuelEconomySummary>, NotFound<ProblemDetails>>> GetFuelAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary.Fuel);
    }

    /// <remarks>
    /// <para>
    /// An expense can be deleted and so can a service record; a fill could not, which was an asymmetry rather
    /// than a decision — and it meant a fill entered by mistake was permanent, moving the odometer and the MPG
    /// average forever. The shadows go with it: the mirrored expense cascades on its foreign key, and the
    /// mileage reading is removed here because nothing points at it and an orphan would keep moving the
    /// odometer.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The counterpart to <see cref="AddFillAsync"/>: the caller applies the field changes to the tracked
    /// entry, and <see cref="FuelEntryFactory.UpdateAsync"/> drags the mileage reading and the mirrored expense
    /// along — inside the execution strategy, so a transient retry moves all three or none. Correcting the
    /// litres re-derives MPG; correcting them past the plausibility band the scan raises a flag, and correcting
    /// them back clears it.
    /// </remarks>
    private static async Task<Results<Ok<AddFillResponse>, NotFound<ProblemDetails>, ValidationProblem>> UpdateFillAsync(
        string registration,
        int id,
        UpdateFillRequest request,
        CarTrackerDbContext context,
        FuelEntryFactory factory,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var entry = await context.FuelEntries
            .FirstOrDefaultAsync(f => f.Id == id && f.VehicleId == vehicleId.Value, cancellationToken);
        if (entry is null) return VehicleLookup.NotFound(registration);

        if (ValidateUpdate(request) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The reading has no FK back to the fill, so its old key must be captured before the edit moves it.
        var originalDate = entry.EntryDate;
        var originalMileage = entry.Mileage;

        entry.EntryDate = request.EntryDate ?? entry.EntryDate;
        entry.Mileage = request.Mileage ?? entry.Mileage;
        entry.Litres = request.Litres ?? entry.Litres;
        entry.PricePerLitre = request.PricePerLitre ?? entry.PricePerLitre;
        // The receipt still wins when given. Otherwise, if litres or price moved, the stored total is stale —
        // recompute it — but an untouched fill keeps its receipt figure to the penny.
        entry.TotalCost = request.TotalCost
            ?? (request.Litres is not null || request.PricePerLitre is not null
                ? decimal.Round(entry.Litres * entry.PricePerLitre, 2)
                : entry.TotalCost);
        entry.Station = request.Station ?? entry.Station;
        entry.FillLevel = request.FillLevel ?? entry.FillLevel;
        entry.Notes = request.Notes ?? entry.Notes;

        await factory.UpdateAsync(entry, originalDate, originalMileage, cancellationToken);

        var flags = await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Ok(new AddFillResponse(entry.Id, flags.ToFlags()));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteFillAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        FuelEntryFactory factory,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var entry = await context.FuelEntries
            .FirstOrDefaultAsync(f => f.Id == id && f.VehicleId == vehicleId.Value, cancellationToken);
        if (entry is null) return VehicleLookup.NotFound(registration);

        await factory.DeleteAsync(entry, cancellationToken);

        // Removing a fill can clear a flag it caused — an implausible MPG belongs to the interval, and the
        // interval is gone. Auto-reconcile closes it on this scan rather than leaving it orphaned.
        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.NoContent();
    }

    /// <remarks>
    /// <para>
    /// The response carries the flags the scan raised. A flag never blocks the save (§5.3) — the fill is
    /// recorded and then excluded from derived figures until the flag is resolved — but the caller has to be
    /// told, or the app has quietly accepted something it does not believe.
    /// </para>
    /// <para>
    /// Note what is <b>not</b> validated: fill level. The design's fuel sheet gates MPG on a full-to-full pair
    /// and hardcodes an 18–45 plausibility band; both predate the fuel-basis spec, which made litres the sole
    /// basis of MPG and left fill level as description. The band is the domain's (10–70) and lives in
    /// FuelEconomyCalculator. A partial fill is logged, and its MPG is computed like any other.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<AddFillResponse>, NotFound<ProblemDetails>, ValidationProblem>> AddFillAsync(
        string registration,
        AddFillRequest request,
        CarTrackerDbContext context,
        FuelEntryFactory factory,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (Validate(request) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var entry = new FuelEntry
        {
            VehicleId = vehicleId.Value,
            EntryDate = request.EntryDate,
            Mileage = request.Mileage,
            Litres = request.Litres,
            PricePerLitre = request.PricePerLitre,
            // The receipt is the authority, not litres x price. They disagree by pennies in real life —
            // rounding at the pump — and FuelCostDiscrepancy exists to notice when they disagree by more.
            TotalCost = request.TotalCost ?? decimal.Round(request.Litres * request.PricePerLitre, 2),
            Station = request.Station,
            FillLevel = request.FillLevel,
            Notes = request.Notes,
        };

        await factory.CreateAsync(entry, EntrySource.Web, cancellationToken);

        var flags = await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/fuel",
            new AddFillResponse(entry.Id, flags.ToFlags()));
    }

    /// <remarks>
    /// Refuses only what is meaningless, never what is merely surprising. Zero litres is not a fill; 90 litres
    /// in a 59-litre tank is odd but might be a jerrycan too, and that is a job for a detector and a flag —
    /// not a 400. Rejecting the odd-but-possible is how a tool teaches people to type whatever it will accept.
    /// </remarks>
    private static Dictionary<string, string[]> Validate(AddFillRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Litres <= 0)
            errors[nameof(request.Litres)] = ["A fill must have litres — they are the sole basis of MPG."];

        if (request.PricePerLitre <= 0)
            errors[nameof(request.PricePerLitre)] = ["Price per litre must be greater than zero."];

        if (request.Mileage <= 0)
            errors[nameof(request.Mileage)] = ["An odometer reading must be greater than zero."];

        if (request.TotalCost is <= 0)
            errors[nameof(request.TotalCost)] = ["A total must be greater than zero, or omitted to compute it."];

        return errors;
    }

    /// <remarks>Same spirit as <see cref="Validate"/>, but every field is optional — only what is supplied is
    /// checked, because null means "leave it".</remarks>
    private static Dictionary<string, string[]> ValidateUpdate(UpdateFillRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Litres is <= 0)
            errors[nameof(request.Litres)] = ["A fill must have litres — they are the sole basis of MPG."];

        if (request.PricePerLitre is <= 0)
            errors[nameof(request.PricePerLitre)] = ["Price per litre must be greater than zero."];

        if (request.Mileage is <= 0)
            errors[nameof(request.Mileage)] = ["An odometer reading must be greater than zero."];

        if (request.TotalCost is <= 0)
            errors[nameof(request.TotalCost)] = ["A total must be greater than zero, or omitted to compute it."];

        return errors;
    }
}

/// <summary>Every field optional: null leaves the fill's value untouched. The receipt total still wins when
/// given, and is recomputed from litres x price only when one of those moves.</summary>
public sealed record UpdateFillRequest(
    DateOnly? EntryDate = null,
    int? Mileage = null,
    decimal? Litres = null,
    decimal? PricePerLitre = null,
    decimal? TotalCost = null,
    string? Station = null,
    FillLevel? FillLevel = null,
    string? Notes = null);

/// <param name="TotalCost">
/// The receipt total. Omit it and litres x price is used — but when present it wins, because the receipt is
/// what was actually paid.
/// </param>
/// <param name="FillLevel">
/// Full or unrecorded (null) closes the tank and measures MPG across the segment since the last full fill;
/// Half/Quarter mark a partial that defers MPG to your next full fill, its litres accumulating into that span.
/// Never a reason to reject a save — a partial fill is always accepted and simply defers its figure. Only
/// "closes vs not" is read; Half vs Quarter is not distinguished arithmetically.
/// </param>
public sealed record AddFillRequest(
    DateOnly EntryDate,
    int Mileage,
    decimal Litres,
    decimal PricePerLitre,
    decimal? TotalCost = null,
    string? Station = null,
    FillLevel? FillLevel = null,
    string? Notes = null);

/// <param name="Flags">What the detectors raised. Empty is the normal case; a flag never blocked the save.</param>
public sealed record AddFillResponse(int Id, IReadOnlyList<AnomalyFlag> Flags);
