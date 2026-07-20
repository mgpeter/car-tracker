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
/// Regular checks: the definitions, and the logs that give them a status.
/// </summary>
/// <remarks>
/// This closes a structural gap, not a missing endpoint. <see cref="CheckDefinition"/> is vehicle-scoped, is
/// not seeded (DEC-007 forbids seeding anything vehicle-scoped), and until now nothing in the codebase
/// constructed one — so the checks screen rendered 0 of 18 for every vehicle, forever. The definitions arrive
/// two ways now: <see cref="VehicleFactory"/>'s starter set at create time, and here.
/// </remarks>
public static class ChecksEndpoints
{
    public static IEndpointRouteBuilder MapChecksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/checks").WithTags("Checks");

        group.MapGet("/", GetChecksAsync)
            .WithName("GetChecks")
            .WithSummary("Every check with its computed status. Status is never stored — it derives from the last log and the interval.");

        group.MapGet("/definitions", GetDefinitionsAsync)
            .WithName("GetCheckDefinitions")
            .WithSummary("Every definition — including retired ones — with its guidance, order and active flag, for the settings editor. The status summary above carries only active checks and no guidance.");

        group.MapPost("/definitions", AddDefinitionAsync)
            .WithName("AddCheckDefinition")
            .WithSummary("Adds a check definition.");

        group.MapPost("/definitions/add-set", AddCheckSetAsync)
            .WithName("AddCheckSet")
            .WithSummary("Adds a set of checks — the generic starter set, or a copy of another vehicle's active checks — appending only the ones this vehicle does not already have.");

        group.MapPatch("/definitions/{id:int}", UpdateDefinitionAsync)
            .WithName("UpdateCheckDefinition")
            .WithSummary("Edits a definition's name, cadence, interval, guidance, order or active flag.");

        group.MapDelete("/definitions/{id:int}", DeleteDefinitionAsync)
            .WithName("DeleteCheckDefinition")
            .WithSummary("Deletes a definition and its logs. Deactivating is usually what you want instead.");

        group.MapPost("/logs", LogChecksAsync)
            .WithName("LogChecks")
            .WithSummary("Marks one or more checks done. The batch is the weekly walk-around: one action, not five.");

        return app;
    }

    private static async Task<Results<Ok<CheckStatusSummary>, NotFound<ProblemDetails>>> GetChecksAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary.Checks);
    }

    /// <remarks>
    /// All definitions in display order, retired included — the settings panel manages <c>IsActive</c>,
    /// guidance and order, none of which the status summary carries (it only lists active checks). Reads the
    /// table directly rather than the derived service, because this is the stored definition, not its status.
    /// </remarks>
    private static async Task<Results<Ok<List<CheckDefinitionResponse>>, NotFound<ProblemDetails>>> GetDefinitionsAsync(
        string registration,
        CarTrackerDbContext context,
        LogQueryService queries,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        return TypedResults.Ok(await queries.ListCheckDefinitionsAsync(vehicleId.Value, cancellationToken));
    }

    private static async Task<Results<Created<CheckDefinitionResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> AddDefinitionAsync(
        string registration,
        CheckDefinitionRequest request,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (Validate(request) is { Count: > 0 } errors) return TypedResults.ValidationProblem(errors);

        var exists = await context.CheckDefinitions
            .AnyAsync(d => d.VehicleId == vehicleId.Value && d.Name == request.Name, cancellationToken);

        if (exists)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Check already defined",
                Detail = $"This vehicle already has a check named '{request.Name}'.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        // Append by default. The order is the sequence the checks screen renders, and a new check has no claim
        // to a place in the middle of someone's routine.
        var nextOrder = request.DisplayOrder ?? await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId.Value)
            .Select(d => (int?)d.DisplayOrder)
            .MaxAsync(cancellationToken) + 1 ?? 1;

        var definition = new CheckDefinition
        {
            VehicleId = vehicleId.Value,
            Name = request.Name,
            CadenceLabel = request.CadenceLabel,
            IntervalDays = request.IntervalDays,
            Guidance = request.Guidance,
            DisplayOrder = nextOrder,
            IsActive = request.IsActive ?? true,
            Source = EntrySource.Web,
        };

        context.CheckDefinitions.Add(definition);
        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/checks",
            ToResponse(definition));
    }

    /// <remarks>
    /// The bulk counterpart to <see cref="AddDefinitionAsync"/>, for adding the generic set or copying another
    /// vehicle's checks onto a car that already exists. Resolution and the append-and-diff both live in the
    /// domain (<see cref="CheckSetAdder"/> over <see cref="CheckSetResolver"/>) so this stays what it says: a
    /// thin caller. Names the vehicle already has are skipped, not rejected — the unique index makes a re-add
    /// impossible, and reporting them back lets the UI say "N added, M already present".
    /// </remarks>
    private static async Task<Results<Ok<AddCheckSetResponse>, NotFound<ProblemDetails>, ValidationProblem>> AddCheckSetAsync(
        string registration,
        AddCheckSetRequest request,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (request.Source == CheckSource.CopyFromVehicle)
        {
            if (request.CopyFromVehicleId is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.CopyFromVehicleId)] = ["Copying needs a source vehicle."],
                });
            }

            if (request.CopyFromVehicleId == vehicleId.Value)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.CopyFromVehicleId)] = ["A vehicle cannot copy checks from itself."],
                });
            }
        }

        var result = await new CheckSetAdder(context).AddSetAsync(
            vehicleId.Value, request.Source, request.CopyFromVehicleId, request.SelectedCheckNames,
            EntrySource.Web, cancellationToken);

        return TypedResults.Ok(new AddCheckSetResponse(
            [.. result.Added.Select(ToResponse)],
            result.Skipped));
    }

    private static async Task<Results<Ok<CheckDefinitionResponse>, NotFound<ProblemDetails>, ValidationProblem>> UpdateDefinitionAsync(
        string registration,
        int id,
        CheckDefinitionPatch request,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        // Scoped to the vehicle in the URL, not just by id: without it, a definition id from one car could be
        // edited through another car's route.
        var definition = await context.CheckDefinitions
            .SingleOrDefaultAsync(d => d.Id == id && d.VehicleId == vehicleId.Value, cancellationToken);

        if (definition is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Check not found",
                Detail = $"No check definition {id} on '{registration}'.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        if (request.IntervalDays is <= 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.IntervalDays)] = ["An interval must be at least one day."],
            });
        }

        // Only what was sent. A PATCH that nulled every omitted field would make "rename this check" erase its
        // guidance and cadence.
        definition.Name = request.Name ?? definition.Name;
        definition.CadenceLabel = request.CadenceLabel ?? definition.CadenceLabel;
        definition.IntervalDays = request.IntervalDays ?? definition.IntervalDays;
        definition.Guidance = request.Guidance ?? definition.Guidance;
        definition.DisplayOrder = request.DisplayOrder ?? definition.DisplayOrder;
        definition.IsActive = request.IsActive ?? definition.IsActive;

        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(definition));
    }

    /// <remarks>
    /// A real delete, and it takes the logs with it (cascade). That is why the checks screen offers Active as
    /// a toggle: deactivating keeps the history and stops the nagging, which is what someone almost always
    /// means. Deleting is for a check that should never have existed.
    /// </remarks>
    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteDefinitionAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var definition = await context.CheckDefinitions
            .SingleOrDefaultAsync(d => d.Id == id && d.VehicleId == vehicleId.Value, cancellationToken);

        if (definition is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Check not found",
                Detail = $"No check definition {id} on '{registration}'.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        context.CheckDefinitions.Remove(definition);
        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <remarks>
    /// Takes a list because the real action is "I did the weekly walk-around" — five checks, one moment, one
    /// button. Five requests would be five chances to half-succeed, and would put the owner's five checks at
    /// five different timestamps for no reason.
    /// </remarks>
    private static async Task<Results<Ok<CheckStatusSummary>, NotFound<ProblemDetails>, ValidationProblem>> LogChecksAsync(
        string registration,
        LogChecksRequest request,
        CarTrackerDbContext context,
        CheckService checks,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await checks.MarkDoneAsync(
            vehicleId.Value, request.CheckDefinitionIds, request.PerformedOn, request.Result, request.Notes,
            EntrySource.Web, cancellationToken);

        return result.Status switch
        {
            WriteStatus.Validation => TypedResults.ValidationProblem(result.Errors!),
            WriteStatus.NotFound => VehicleLookup.NotFound(registration),
            _ => TypedResults.Ok(result.Value!),
        };
    }

    private static Dictionary<string, string[]> Validate(CheckDefinitionRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
            errors[nameof(request.Name)] = ["A check needs a name."];

        if (string.IsNullOrWhiteSpace(request.CadenceLabel))
            errors[nameof(request.CadenceLabel)] = ["A check needs a cadence label — it is what the screen shows."];

        if (request.IntervalDays <= 0)
            errors[nameof(request.IntervalDays)] = ["An interval must be at least one day."];

        return errors;
    }

    private static CheckDefinitionResponse ToResponse(CheckDefinition d) =>
        new(d.Id, d.Name, d.CadenceLabel, d.IntervalDays, d.Guidance, d.DisplayOrder, d.IsActive);
}

public sealed record CheckDefinitionRequest(
    string Name,
    string CadenceLabel,
    int IntervalDays,
    string? Guidance = null,
    int? DisplayOrder = null,
    bool? IsActive = null);

/// <remarks>Every field optional: an omitted field is untouched, not cleared.</remarks>
public sealed record CheckDefinitionPatch(
    string? Name = null,
    string? CadenceLabel = null,
    int? IntervalDays = null,
    string? Guidance = null,
    int? DisplayOrder = null,
    bool? IsActive = null);

/// <param name="Source">GenericStarterSet or CopyFromVehicle. None adds nothing.</param>
/// <param name="SelectedCheckNames">
/// Which of the source's checks to add, by name — the toggle selection. Null adds all of them (the whole
/// generic set / every active check on the source vehicle); an empty list adds none.
/// </param>
/// <param name="CopyFromVehicleId">The source vehicle's id — required for CopyFromVehicle, ignored otherwise.</param>
public sealed record AddCheckSetRequest(
    CheckSource Source,
    IReadOnlyList<string>? SelectedCheckNames = null,
    int? CopyFromVehicleId = null);

/// <param name="Added">The definitions actually created, appended in order.</param>
/// <param name="Skipped">Names the vehicle already had (active or retired), left untouched.</param>
public sealed record AddCheckSetResponse(
    IReadOnlyList<CheckDefinitionResponse> Added,
    IReadOnlyList<string> Skipped);

/// <param name="CheckDefinitionIds">One for a single check; five for the weekly walk-around.</param>
/// <param name="Result">
/// The outcome — OK, Attention or Failed. Typed, not free text: "mayo under the oil filler cap" is an
/// Attention that the head-gasket watch depends on noticing, and a prose note would make that unqueryable.
/// Null means logged without a verdict, which is the ordinary "did it, all fine" case the batch uses.
/// </param>
public sealed record LogChecksRequest(
    IReadOnlyList<int> CheckDefinitionIds,
    DateOnly PerformedOn,
    CheckResult? Result = null,
    string? Notes = null);
