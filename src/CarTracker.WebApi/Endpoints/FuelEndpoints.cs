using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

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
            new AddFillResponse(entry.Id, [.. flags.Select(ToFlag)]));
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

    internal static AnomalyFlag ToFlag(DataAnomaly a) =>
        new(a.Id, a.Kind, a.Severity, a.Message, a.Detail);
}

/// <param name="TotalCost">
/// The receipt total. Omit it and litres x price is used — but when present it wins, because the receipt is
/// what was actually paid.
/// </param>
/// <param name="FillLevel">
/// Descriptive only. It does NOT gate MPG: the fuel-basis spec made litres the sole basis, and the design's
/// full-to-full rule predates that.
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

public sealed record AnomalyFlag(int Id, AnomalyKind Kind, AnomalySeverity Severity, string Message, string? Detail);
