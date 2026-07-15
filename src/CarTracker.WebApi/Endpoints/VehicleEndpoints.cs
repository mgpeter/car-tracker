using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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

        group.MapGet("/", GetGarageAsync)
            .WithName("GetGarage")
            .WithSummary("Every vehicle, with the figures the garage card shows. Each is projected from that vehicle's summary, never recomputed.");

        group.MapPost("/", CreateVehicleAsync)
            .WithName("CreateVehicle")
            .WithSummary("Adds a vehicle, together with its opening odometer reading.");

        group.MapGet("/{registration}/summary", GetSummaryAsync)
            .WithName("GetVehicleSummary")
            .WithSummary("Every derived figure for one vehicle, computed on read. Registration is matched ignoring case and spacing.");

        return app;
    }

    /// <remarks>
    /// An empty garage is <c>200 []</c>, not <c>404</c>. "You have no cars yet" is a state the app is designed
    /// for — it is what the add-car flow exists to answer — not a missing resource.
    /// </remarks>
    private static async Task<Ok<IReadOnlyList<GarageItem>>> GetGarageAsync(
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await metrics.GetGarageAsync(cancellationToken));
    }

    /// <remarks>
    /// Typed results, not <see cref="IResult"/>. Two reasons, and the second is why this changed: the compiler
    /// checks that every returned shape is one the signature admits; and OpenAPI can only describe a response
    /// it can see. With a bare <c>Results.Ok(summary)</c> the emitted document said <c>200: OK</c> and nothing
    /// more, so the generated TypeScript for the one endpoint that returns real derived figures was
    /// <c>unknown</c> — the codegen loop silently buying us nothing exactly where it matters most.
    /// </remarks>
    private static async Task<Results<Created<CreateVehicleResponse>, Conflict<ProblemDetails>>> CreateVehicleAsync(
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

        return TypedResults.Created(
            $"/api/vehicles/{VehicleLookup.Normalize(vehicle.Registration)}/summary",
            new CreateVehicleResponse(vehicle.Id, vehicle.Registration));
    }

    private static async Task<Results<Ok<VehicleSummary>, NotFound<ProblemDetails>>> GetSummaryAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);

        if (vehicleId is null)
        {
            return VehicleLookup.NotFound(registration);
        }

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);

        // Null here means the row vanished between the id lookup and the load — rare, but it is a 404 for the
        // same reason the lookup miss is: the caller asked about a vehicle that is not there.
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary);
    }

    private static Task<bool> RegistrationExistsAsync(
        CarTrackerDbContext context,
        string registration,
        CancellationToken cancellationToken)
    {
        var normalized = VehicleLookup.Normalize(registration);

        return context.Vehicles
            .AsNoTracking()
            .AnyAsync(v => EF.Property<string>(v, "RegistrationNormalized") == normalized, cancellationToken);
    }

    /// <remarks>
    /// ProblemDetails rather than an anonymous <c>{ message }</c>: an anonymous type has no schema, so it
    /// generates as <c>unknown</c> and the front end cannot read the reason it was refused. RFC 9457 is what
    /// the platform already speaks.
    /// </remarks>
    private static Conflict<ProblemDetails> Conflict(string registration) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = "Registration already exists",
            Detail = $"A vehicle with registration '{registration}' already exists.",
            Status = StatusCodes.Status409Conflict,
        });
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
