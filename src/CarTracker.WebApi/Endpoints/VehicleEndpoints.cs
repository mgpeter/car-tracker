using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Vehicles;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using CarTracker.Domain.Lookup;
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

        group.MapGet("/lookup/{registration}", LookupVehicleAsync)
            .WithName("LookupVehicle")
            .WithSummary("Resolves a registration to un-persisted DVLA/DVSA facts for the add-car form. Creates nothing.");

        group.MapGet("/{registration}", GetVehicleAsync)
            .WithName("GetVehicle")
            .WithSummary("The stored reference facts — specs, fluids, tyre pressures, policies. The only screen that is not derived.");

        group.MapGet("/{registration}/summary", GetSummaryAsync)
            .WithName("GetVehicleSummary")
            .WithSummary("Every derived figure for one vehicle, computed on read. Registration is matched ignoring case and spacing.");

        group.MapPatch("/{registration}", UpdateVehicleAsync)
            .WithName("UpdateVehicle")
            .WithSummary("Edits the stored inputs — identity, statutory dates and the insurance policy. MOT expiry is derived and cannot be set here.");

        group.MapGet("/{registration}/deletion-summary", GetDeletionSummaryAsync)
            .WithName("GetVehicleDeletionSummary")
            .WithSummary("What deleting this vehicle would destroy. The weight the confirmation states before it arms.");

        group.MapDelete("/{registration}", DeleteVehicleAsync)
            .WithName("DeleteVehicle")
            .WithSummary("Destroys the vehicle and every row filed under it. Irreversible, and gated on typing the registration.");

        return app;
    }

    /// <summary>
    /// Resolves a registration to un-persisted facts for the add-car form. Reads only.
    /// </summary>
    /// <remarks>
    /// The create stays the separate, deliberate <c>POST /api/vehicles</c> — there is no "look up and create"
    /// shortcut, because the design's whole promise is "you confirm before anything is created" and an
    /// auto-create could persist a wrong-vehicle match. Every failure is ProblemDetails rather than an
    /// anonymous shape, so the sheet can show the reason and keep manual entry open.
    /// </remarks>
    private static async Task<Results<Ok<VehicleLookupResult>, NotFound<ProblemDetails>, ProblemHttpResult>>
        LookupVehicleAsync(
            string registration,
            IVehicleLookupService lookup,
            VehicleLookupQuota quota,
            CancellationToken cancellationToken)
    {
        // Before the call, so a refusal spends none of the upstream quota it exists to protect.
        if (await quota.CheckAsync(cancellationToken) is { } refused)
        {
            return TypedResults.Problem(
                title: "Daily lookup limit reached",
                detail: refused.Limit <= 0
                    ? "This account's plan does not include registration lookups. Type the details in instead; "
                      + "nothing else on the add-car form depends on this."
                    : $"This account has used all {refused.Limit} of today's registration lookups. The "
                      + $"allowance resets at {refused.ResetsAt:HH:mm} on {refused.ResetsAt:d MMMM}. Type the "
                      + "details in meanwhile.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var result = await lookup.LookupAsync(registration, cancellationToken);

        // Charged for a call that actually reached DVLA, and for nothing else. A NotFound did reach it and
        // spent the upstream quota, so it counts; a 503 from an unconfigured deployment and a 502 from an
        // outage consumed nothing and must not cost somebody their third lookup of the day.
        if (result.Outcome is LookupOutcome.Found or LookupOutcome.NotFound)
            await quota.RecordAsync(cancellationToken);

        return result.Outcome switch
        {
            LookupOutcome.Found => TypedResults.Ok(result.Result!),

            LookupOutcome.NotFound => TypedResults.NotFound(new ProblemDetails
            {
                Title = "No record for that registration",
                Detail = result.Detail,
                Status = StatusCodes.Status404NotFound,
            }),

            // 503, not 502: a deployment with no key is not a broken gateway, it is a capability this instance
            // does not have. Distinct from NotFound so the sheet says "type it in" rather than "no such car".
            LookupOutcome.NotConfigured => TypedResults.Problem(
                title: "Lookup is not configured",
                detail: result.Detail,
                statusCode: StatusCodes.Status503ServiceUnavailable),

            _ => TypedResults.Problem(
                title: "Lookup unavailable",
                detail: result.Detail,
                statusCode: StatusCodes.Status502BadGateway),
        };
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
        VehicleUpdateService updates,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        // The merge (and the "no MOT expiry setter" rule) lives in the shared service, so the MCP settings tools
        // apply the exact same edit — one write path.
        var patch = new VehiclePatch(
            request.Colour, request.Vin, request.BodyStyle, request.Seller, request.DefaultGarage, request.Notes,
            request.Status, request.IsDefault, request.MotExpirySeed, request.VedExpiry, request.VedAnnualCost,
            request.UlezCompliant, request.Insurance, request.Fluids, request.Tyres, request.PurchasePrice,
            request.Breakdown);

        var result = await updates.ApplyAsync(vehicleId.Value, patch, cancellationToken);
        return result.Status switch
        {
            WriteStatus.Validation => TypedResults.ValidationProblem(result.Errors!),
            WriteStatus.NotFound => VehicleLookup.NotFound(registration),
            _ => TypedResults.Ok(result.Value!),
        };
    }

    private static async Task<Results<Ok<VehicleDeletionSummary>, NotFound<ProblemDetails>>> GetDeletionSummaryAsync(
        string registration,
        CarTrackerDbContext context,
        VehicleDeletionService deletions,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await deletions.GetSummaryAsync(vehicleId.Value, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary);
    }

    /// <remarks>
    /// <para>
    /// A shell, like the account-deletion handler and for the same reason: every refusal - the typed
    /// confirmation, the not-found - is decided in <see cref="VehicleDeletionService"/>, because there is no
    /// <c>CarTracker.WebApi.Tests</c> project and this is the second most destructive thing the app does.
    /// </para>
    /// <para>
    /// The body is required even though the UI already asks for the registration. The client is not the only
    /// possible caller, and a vehicle-deleting <c>DELETE</c> that succeeded on an empty body is one mis-wired
    /// button away from destroying four years of history.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<VehicleDeletedResponse>, NotFound<ProblemDetails>, ValidationProblem>>
        DeleteVehicleAsync(
            string registration,
            // Empty bodies allowed through to the service, so a DELETE with nothing in it is refused by the
            // confirmation rule with a field error rather than by the framework with a shape complaint. The
            // refusal is the same either way; only one of them names what was missing.
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
            DeleteVehicleRequest? request,
            CarTrackerDbContext context,
            VehicleDeletionService deletions,
            CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await deletions.DeleteAsync(vehicleId.Value, request?.ConfirmRegistration, cancellationToken);

        return result.Outcome switch
        {
            // 200 with a body rather than 204, because the caller needs to know whether another vehicle
            // became the default - the garage reorders under them otherwise with nothing saying why.
            VehicleDeletionOutcome.Deleted =>
                TypedResults.Ok(new VehicleDeletedResponse(registration, result.PromotedRegistration)),

            // A per-field RFC 9457 errors map, so the sheet marks the field the way every other form does.
            VehicleDeletionOutcome.ConfirmationMismatch => TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [result.Field ?? "confirmRegistration"] =
                        [result.Detail ?? "The confirmation does not match."],
                }),

            _ => VehicleLookup.NotFound(registration),
        };
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
    private static async Task<Results<Created<CreateVehicleResponse>, Conflict<ProblemDetails>, UnauthorizedHttpResult>> CreateVehicleAsync(
        CreateVehicleRequest request,
        VehicleFactory factory,
        CarTrackerDbContext context,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        // The signed-in user owns what they create. No resolved user means an unauthenticated or key-only
        // principal reached here, which the fallback policy should already refuse — belt and braces.
        if (currentUser.OwnerId is not int ownerId)
        {
            return TypedResults.Unauthorized();
        }

        // Scoped to this owner by the vehicle query filter, so it detects a duplicate the SAME user already has
        // while letting a different user register the same plate.
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
            EngineSizeCc = request.EngineSizeCc,
            // A seed and a stored input respectively — see the request record for why those are different
            // things, and why only one of them is a date the dashboard is allowed to trust as final.
            MotExpirySeed = request.MotExpirySeed,
            VedExpiry = request.VedExpiry,
        };

        try
        {
            // Never construct-and-Add inline: VehicleFactory is the only thing that guarantees the opening
            // MileageReading, without which every derived figure reports null until the first log.
            await factory.CreateAsync(
                vehicle,
                ownerId,
                EntrySource.Web,
                request.CheckSource ?? CheckSource.GenericStarterSet,
                request.CopyChecksFromVehicleId,
                request.SelectedCheckNames,
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

    /// <remarks>
    /// The one read that is honestly <b>stored</b>, and it is worth being explicit about why that is fine: an
    /// oil spec is not a measurement, it is what the manual says. Nothing here can drift out of step with a log
    /// because no log produces it. The renewals ARE derived and deliberately live on the summary instead — the
    /// policy dates here are inputs to that, not answers.
    /// </remarks>
    private static async Task<Results<Ok<VehicleDetail>, NotFound<ProblemDetails>>> GetVehicleAsync(
        string registration,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicle = await VehicleLookup.FindAsync(context, registration, cancellationToken);
        if (vehicle is null) return VehicleLookup.NotFound(registration);

        return TypedResults.Ok(new VehicleDetail(
            vehicle.Registration,
            $"{vehicle.Make} {vehicle.Model}".Trim(),
            vehicle.Variant,
            vehicle.Year,
            vehicle.Colour,
            vehicle.BodyStyle,
            vehicle.Vin,
            vehicle.EngineCode,
            vehicle.EngineSizeCc,
            vehicle.FuelType,
            vehicle.Transmission,
            vehicle.Drivetrain,
            vehicle.PurchaseDate,
            vehicle.PurchasePrice,
            vehicle.PurchaseMileage,
            vehicle.Seller,
            vehicle.DefaultGarage,
            vehicle.UlezCompliant,
            vehicle.VedAnnualCost,
            vehicle.Fluids,
            vehicle.Tyres,
            vehicle.Insurance,
            vehicle.Breakdown,
            vehicle.Notes,
            vehicle.Status,
            vehicle.IsDefault));
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
    int? CopyChecksFromVehicleId = null,
    /// <summary>
    /// Which generic starter checks to apply, by name — the add-car toggle selection. Only meaningful with
    /// <see cref="CheckSource.GenericStarterSet"/>; ignored otherwise. Null (the default) applies the whole
    /// set, so an omitting client is unchanged; an empty list applies none, the deselect-all case.
    /// </summary>
    IReadOnlyList<string>? SelectedCheckNames = null,
    /// <summary>Engine capacity in cc — pre-filled by a registration lookup, editable before submit.</summary>
    int? EngineSizeCc = null,
    /// <summary>
    /// The MOT expiry a registration lookup found, as a <b>seed</b>. Read only while the vehicle has no MOT
    /// record; the first logged pass supersedes it. There is deliberately no settable MOT <i>expiry</i> — a
    /// stored one is how the spreadsheet showed a red countdown for a test that had already passed.
    /// </summary>
    DateOnly? MotExpirySeed = null,
    /// <summary>
    /// VED expiry, from the lookup's tax due date. Unlike MOT this is a legitimately stored <i>input</i> — the
    /// renewal calculator reads it directly, because nothing in the app logs a road-tax payment.
    /// </summary>
    DateOnly? VedExpiry = null);

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
    InsurancePatch? Insurance = null,
    FluidsPatch? Fluids = null,
    TyresPatch? Tyres = null,
    /// <summary>
    /// What the car cost. Correctable because it is load-bearing: it mirrors into a Purchase expense, so it
    /// moves total outlay and cost-per-mile. Purchase date and mileage stay create-only — they are the vehicle's
    /// founding facts, and the odometer one also seeded a MileageReading.
    /// </summary>
    decimal? PurchasePrice = null,
    /// <summary>Breakdown cover - provider, policy number, expiry. Stored, because no log produces it.</summary>
    BreakdownPatch? Breakdown = null);

/// <param name="Fluids">
/// Specs, not measurements: what the manual says goes in. BT53's coolant must be OAT — red/pink, never mixed
/// with IAT — and the K-series head gasket is the reason that matters enough to have a field.
/// </param>
public sealed record VehicleDetail(
    string Registration,
    string Name,
    string? Variant,
    int Year,
    string? Colour,
    string? BodyStyle,
    string? Vin,
    string? EngineCode,
    int? EngineSizeCc,
    FuelType FuelType,
    string? Transmission,
    string? Drivetrain,
    DateOnly PurchaseDate,
    decimal? PurchasePrice,
    int PurchaseMileage,
    string? Seller,
    string? DefaultGarage,
    bool? UlezCompliant,
    decimal? VedAnnualCost,
    FluidSpecs Fluids,
    TyreSpecs Tyres,
    InsurancePolicy Insurance,
    BreakdownCover Breakdown,
    string? Notes,
    /// <summary>
    /// Where the car is in its life: Active, Sold or SORN. A stored input like every other field here, and it
    /// belongs on this payload for the same reason they all do - the screen that edits stored inputs has to be
    /// able to read them. It was absent until the vehicle screen gained a control for it, which is why nothing
    /// noticed: no screen could set it, so every car was Active and the field answered itself.
    /// </summary>
    VehicleStatus Status,
    /// <summary>
    /// Whether this is the account's default vehicle - the one the garage lists first and the one the
    /// assistant resolves when a tool omits a registration. Read-only here today: setting it needs
    /// <c>VehicleUpdateService</c> to demote the incumbent first, which it does not, and
    /// <c>ix_vehicles_default</c> is unique per owner.
    /// </summary>
    bool IsDefault);

/// <param name="ConfirmRegistration">
/// The vehicle's own registration, typed out. Matched through the same normalisation the database's unique
/// index uses, so "bt53akj" confirms "BT53 AKJ" - a gate that disagreed with the app's own idea of a plate
/// would teach nothing. A second gate behind the UI's typed confirmation, because the UI is not the only
/// thing that can call this.
/// </param>
public sealed record DeleteVehicleRequest(string? ConfirmRegistration);

/// <param name="PromotedRegistration">
/// The vehicle that became the default because the deleted one was, or null when nothing changed. Stated
/// rather than left to be noticed: the garage reorders around the default, and a silent reorder reads as a bug.
/// </param>
public sealed record VehicleDeletedResponse(string Registration, string? PromotedRegistration);
