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

        group.MapPatch("/{registration}", UpdateVehicleAsync)
            .WithName("UpdateVehicle")
            .WithSummary("Edits the stored inputs — identity, statutory dates and the insurance policy. MOT expiry is derived and cannot be set here.");

        return app;
    }

    /// <remarks>
    /// <para>
    /// This exists because <see cref="CreateVehicleRequest"/> reaches 11 of the Vehicle's ~30 fields, and
    /// <c>RenewalCalculator</c> reads exactly four things — <c>Insurance.Insurer</c>,
    /// <c>Insurance.PeriodEnd</c>, <c>MotExpirySeed</c> and <c>VedExpiry</c> — none of which were writable.
    /// A freshly-created vehicle could therefore never show a non-null renewal: the dashboard's entire
    /// RENEWALS panel had no path to being populated.
    /// </para>
    /// <para>
    /// <b>MOT expiry is not settable, and that is the point.</b> It derives from the latest MOT pass record
    /// (<c>MotExpirySeed</c> is a fallback for a car with no record yet, never an override). A stored MOT
    /// expiry is exactly how the spreadsheet came to show a red 23-day countdown for a test that had already
    /// passed — the first of the five defects. Making it writable here would rebuild that.
    /// </para>
    /// <para>
    /// Omitted fields are untouched, not cleared. <c>PATCH</c> means "change these"; a body that nulled
    /// everything absent would turn "rename the car" into "delete its insurance".
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<VehicleSummary>, NotFound<ProblemDetails>, ValidationProblem>> UpdateVehicleAsync(
        string registration,
        UpdateVehicleRequest request,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicle = await context.Vehicles
            .SingleOrDefaultAsync(
                v => EF.Property<string>(v, "RegistrationNormalized") == VehicleLookup.Normalize(registration),
                cancellationToken);

        if (vehicle is null) return VehicleLookup.NotFound(registration);

        if (request.Insurance is { PeriodStart: { } start, PeriodEnd: { } end } && end < start)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Insurance.PeriodEnd"] = ["A policy cannot end before it starts."],
            });
        }

        vehicle.Colour = request.Colour ?? vehicle.Colour;
        vehicle.Vin = request.Vin ?? vehicle.Vin;
        vehicle.BodyStyle = request.BodyStyle ?? vehicle.BodyStyle;
        vehicle.Seller = request.Seller ?? vehicle.Seller;
        vehicle.DefaultGarage = request.DefaultGarage ?? vehicle.DefaultGarage;
        vehicle.Notes = request.Notes ?? vehicle.Notes;
        vehicle.Status = request.Status ?? vehicle.Status;
        vehicle.IsDefault = request.IsDefault ?? vehicle.IsDefault;

        // Statutory. These feed the dashboard's renewal countdowns.
        vehicle.MotExpirySeed = request.MotExpirySeed ?? vehicle.MotExpirySeed;
        vehicle.VedExpiry = request.VedExpiry ?? vehicle.VedExpiry;
        vehicle.VedAnnualCost = request.VedAnnualCost ?? vehicle.VedAnnualCost;
        vehicle.UlezCompliant = request.UlezCompliant ?? vehicle.UlezCompliant;

        if (request.Insurance is { } insurance)
        {
            vehicle.Insurance ??= new InsurancePolicy();
            vehicle.Insurance.Insurer = insurance.Insurer ?? vehicle.Insurance.Insurer;
            vehicle.Insurance.PolicyNumber = insurance.PolicyNumber ?? vehicle.Insurance.PolicyNumber;
            vehicle.Insurance.PeriodStart = insurance.PeriodStart ?? vehicle.Insurance.PeriodStart;
            vehicle.Insurance.PeriodEnd = insurance.PeriodEnd ?? vehicle.Insurance.PeriodEnd;
            vehicle.Insurance.CoverType = insurance.CoverType ?? vehicle.Insurance.CoverType;
            vehicle.Insurance.Premium = insurance.Premium ?? vehicle.Insurance.Premium;
            vehicle.Insurance.ExcessCompulsory = insurance.ExcessCompulsory ?? vehicle.Insurance.ExcessCompulsory;
            vehicle.Insurance.ExcessVoluntary = insurance.ExcessVoluntary ?? vehicle.Insurance.ExcessVoluntary;
            vehicle.Insurance.NcbYears = insurance.NcbYears ?? vehicle.Insurance.NcbYears;
        }

        await context.SaveChangesAsync(cancellationToken);

        // The recomputed summary, because the whole reason to write these is what they do to the countdowns.
        var summary = await metrics.GetVehicleSummaryAsync(vehicle.Id, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary);
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
            await factory.CreateAsync(
                vehicle,
                EntrySource.Web,
                request.CheckSource ?? CheckSource.GenericStarterSet,
                request.CopyChecksFromVehicleId,
                cancellationToken);
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
    string? EngineCode = null,
    /// <summary>
    /// Where the vehicle's regular checks come from. Defaults to the generic starter set: CheckDefinition is
    /// vehicle-scoped and nothing else creates one, so a car created with none has a permanently empty checks
    /// screen. The set is owned by the vehicle the moment it lands.
    /// </summary>
    CheckSource? CheckSource = null,
    int? CopyChecksFromVehicleId = null);

public sealed record CreateVehicleResponse(int Id, string Registration);

/// <summary>
/// The stored inputs. Everything downstream — countdowns, MPG, cost-per-mile, budget variance, check status —
/// is computed from the logs and is not settable anywhere.
/// </summary>
/// <remarks>
/// Every field optional: an omitted field is untouched, not cleared. There is deliberately no
/// <c>MotExpiry</c> — it derives from the latest MOT pass record, and a stored copy is what made the
/// spreadsheet show a red countdown for a test that had already passed.
/// </remarks>
public sealed record UpdateVehicleRequest(
    string? Colour = null,
    string? Vin = null,
    string? BodyStyle = null,
    string? Seller = null,
    string? DefaultGarage = null,
    string? Notes = null,
    VehicleStatus? Status = null,
    bool? IsDefault = null,
    /// <summary>Only used while the vehicle has no MOT record. A pass record always wins.</summary>
    DateOnly? MotExpirySeed = null,
    DateOnly? VedExpiry = null,
    decimal? VedAnnualCost = null,
    bool? UlezCompliant = null,
    InsurancePatch? Insurance = null);

public sealed record InsurancePatch(
    string? Insurer = null,
    string? PolicyNumber = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    string? CoverType = null,
    decimal? Premium = null,
    decimal? ExcessCompulsory = null,
    decimal? ExcessVoluntary = null,
    int? NcbYears = null);
