using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Vehicle creation and the derived summary.
/// </summary>
/// <remarks>
/// The API half of Phase 2's add-car flow and Dashboard, landed early because without it nothing the domain
/// computes is observable outside the test suite.
/// </remarks>
public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles").WithTags("Vehicles");

        group.MapPost("/", CreateVehicleAsync)
            .WithName("CreateVehicle")
            .WithSummary("Adds a vehicle, together with its opening odometer reading.");

        group.MapGet("/{registration}/summary", GetSummaryAsync)
            .WithName("GetVehicleSummary")
            .WithSummary("Every derived figure for one vehicle, computed on read. Registration is matched ignoring case and spacing.");

        return app;
    }

    private static async Task<IResult> CreateVehicleAsync(
        CreateVehicleRequest request,
        VehicleFactory factory,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        if (await RegistrationExistsAsync(context, request.Registration, cancellationToken))
        {
            return Conflict(request.Registration);
        }

        var vehicle = new Vehicle
        {
            Registration = request.Registration.Trim(),
            Make = request.Make,
            Model = request.Model,
            Variant = request.Variant,
            Year = request.Year,
            Colour = request.Colour,
            PurchaseDate = request.PurchaseDate,
            PurchaseMileage = request.PurchaseMileage,
            PurchasePrice = request.PurchasePrice,
            FuelType = request.FuelType,
            EngineCode = request.EngineCode,
        };

        try
        {
            // Never construct-and-Add inline: VehicleFactory is the only thing that guarantees the opening
            // MileageReading, without which every derived figure reports null until the first log.
            await factory.CreateAsync(vehicle, EntrySource.Web, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The check above answers the ordinary case cleanly; this catches the race where two requests pass
            // it together and the normalised unique index rejects the loser. The database is the arbiter — the
            // pre-check only exists to avoid answering with an exception in the common case.
            return Conflict(request.Registration);
        }

        return Results.Created(
            $"/api/vehicles/{Normalize(vehicle.Registration)}/summary",
            new CreateVehicleResponse(vehicle.Id, vehicle.Registration));
    }

    private static async Task<IResult> GetSummaryAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await FindVehicleIdAsync(context, registration, cancellationToken);

        if (vehicleId is null)
        {
            return Results.NotFound(new { message = $"No vehicle with registration '{registration}'." });
        }

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);

        return summary is null ? Results.NotFound() : Results.Ok(summary);
    }

    /// <summary>
    /// Resolves a registration to an id.
    /// </summary>
    /// <remarks>
    /// Deliberately the endpoint's job. <see cref="IDerivedMetricsService"/> takes a vehicle id and stays a
    /// pure id-keyed API — the MCP server will resolve registrations its own way (README §5.2), and pushing
    /// lookup into the domain would give two callers two different ideas of what a registration means.
    /// </remarks>
    private static Task<int?> FindVehicleIdAsync(
        CarTrackerDbContext context,
        string registration,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(registration);

        return context.Vehicles
            .AsNoTracking()
            // The shadow generated column the unique index is built on, so this matches exactly what the
            // database considers a duplicate: "bt53akj" and "BT53 AKJ" are the same vehicle.
            .Where(v => EF.Property<string>(v, "RegistrationNormalized") == normalized)
            .Select(v => (int?)v.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Task<bool> RegistrationExistsAsync(
        CarTrackerDbContext context,
        string registration,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(registration);

        return context.Vehicles
            .AsNoTracking()
            .AnyAsync(v => EF.Property<string>(v, "RegistrationNormalized") == normalized, cancellationToken);
    }

    private static IResult Conflict(string registration) =>
        Results.Conflict(new { message = $"A vehicle with registration '{registration}' already exists." });

    /// <summary>Mirrors the database's <c>upper(replace(registration, ' ', ''))</c> generated column.</summary>
    private static string Normalize(string registration) =>
        registration.Replace(" ", string.Empty).ToUpperInvariant();
}

public sealed record CreateVehicleRequest(
    string Registration,
    string Make,
    string Model,
    int Year,
    DateOnly PurchaseDate,
    int PurchaseMileage,
    FuelType FuelType,
    string? Variant = null,
    string? Colour = null,
    decimal? PurchasePrice = null,
    string? EngineCode = null);

public sealed record CreateVehicleResponse(int Id, string Registration);
