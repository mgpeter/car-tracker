using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The DIY and Workshop to-do lists — two workbook sheets, one entity.
/// </summary>
/// <remarks>
/// The workbook keeps them as separate sheets, which is why the same job appears on whichever one someone
/// happened to open. <see cref="MaintenanceTask.Kind"/> is the distinction, and it is a column rather than a
/// table because "do it myself or pay someone" is a property of a task, not a different kind of thing — and
/// tasks move between them.
/// </remarks>
public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/tasks").WithTags("Tasks");

        group.MapGet("/", GetTasksAsync).WithName("GetTasks")
            .WithSummary("Every task with its derived bundle total — the cost of the jobs waiting on one garage visit.");
        group.MapPost("/", AddTaskAsync).WithName("AddTask");
        group.MapPatch("/{id:int}", UpdateTaskAsync).WithName("UpdateTask");
        group.MapDelete("/{id:int}", DeleteTaskAsync).WithName("DeleteTask");
        group.MapPost("/{id:int}/promote", PromoteTaskAsync).WithName("PromoteTask")
            .WithSummary("Turns a done Workshop task into a service record through ServiceRecordFactory, and links the task to it.");

        return app;
    }

    /// <remarks>
    /// README §3.3's one-click promotion, wired: a completed Workshop task becomes a <see cref="ServiceRecord"/>
    /// carrying its date, garage and cost, and the task keeps the new record's id so the history gains a row and
    /// the to-do list keeps its provenance. It goes through <see cref="ServiceRecordFactory"/> — the same path
    /// AddService uses, writing the record, its mileage reading and its mirrored expense in one transaction —
    /// never a second three-row path. The odometer at completion is not on the task (a task records what to do,
    /// not the reading it was done at), so the request supplies it; cost and type are confirmed because an
    /// estimate is not a receipt and "MOT" must match exactly for the expiry derivation.
    /// </remarks>
    private static async Task<Results<Created<PromoteTaskResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> PromoteTaskAsync(
        string registration,
        int id,
        PromoteTaskRequest request,
        CarTrackerDbContext context,
        TaskPromoter promoter,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await promoter.PromoteAsync(
            vehicleId.Value, id, request.Mileage, request.Type, request.Cost, request.Notes, EntrySource.Web, cancellationToken);

        // Each precondition is its own refusal, so the screen can say which one failed rather than "cannot promote".
        switch (result.Status)
        {
            case PromoteStatus.TaskNotFound:
                return NotFoundProblem("Task not found", $"No task {id} for {registration}.");
            case PromoteStatus.NotWorkshop:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["kind"] = ["Only a Workshop task promotes to a service record. DIY work is added as a DIY record directly."],
                });
            case PromoteStatus.NotDone:
                return Conflict("The task is not done yet — a job still open has no completion date to become the service date.");
            case PromoteStatus.AlreadyPromoted:
                return Conflict("Already promoted to a service record. Promoting again would create a second record and orphan the first.");
            case PromoteStatus.TypeRequired:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["type"] = ["A service record needs a type. \"MOT\" is matched exactly and is what the expiry derives from."],
                });
        }

        // Never a gate (§5.3): a promoted mileage above the current reading is flagged, not refused.
        var flags = await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/service/{result.ServiceRecordId}",
            new PromoteTaskResponse(result.ServiceRecordId, ToFlags(flags)));
    }

    private static List<AnomalyFlag> ToFlags(IReadOnlyList<DataAnomaly> flags) =>
        [.. flags.Select(f => new AnomalyFlag(f.Id, f.Kind, f.Severity, f.Message, f.Detail))];

    private static NotFound<ProblemDetails> NotFoundProblem(string title, string detail) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Title = title,
            Status = StatusCodes.Status404NotFound,
            Detail = detail,
        });

    private static Conflict<ProblemDetails> Conflict(string detail) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Title = "Conflict",
            Status = StatusCodes.Status409Conflict,
            Detail = detail,
        });

    private static async Task<Results<Ok<TaskLog>, NotFound<ProblemDetails>>> GetTasksAsync(
        string registration,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var tasks = await context.MaintenanceTasks
            .Where(t => t.VehicleId == vehicleId.Value)
            .OrderBy(t => t.Status).ThenBy(t => t.Priority).ThenBy(t => t.TargetDate)
            .Select(t => new TaskItem(
                t.Id, t.Kind, t.Priority, t.Title, t.Description, t.EstimatedCost, t.Status,
                t.TargetDate, t.TargetService, t.CompletedDate, t.AssignedGarage, t.ServiceRecordId, t.Notes))
            .ToListAsync(cancellationToken);

        // Derived, not stored. The design shows "Bundle for next garage visit → £150 · 1 job" as a hardcoded
        // string; it is the sum of the open Workshop tasks' estimates, and it moves when one is added.
        var bundle = tasks
            .Where(t => t.Kind == MaintenanceTaskKind.Workshop && t.Status != MaintenanceTaskStatus.Done)
            .ToList();

        return TypedResults.Ok(new TaskLog(
            tasks,
            BundleCost: bundle.Sum(t => t.EstimatedCost ?? 0m),
            BundleCount: bundle.Count,
            // The worst case if every open job is done. The design calls it "worst case" on the issues panel
            // and hardcodes £730; it is the same arithmetic and it belongs here.
            OpenEstimateTotal: tasks.Where(t => t.Status != MaintenanceTaskStatus.Done).Sum(t => t.EstimatedCost ?? 0m)));
    }

    private static async Task<Results<Created<TaskItem>, NotFound<ProblemDetails>, ValidationProblem>> AddTaskAsync(
        string registration,
        AddTaskRequest request,
        CarTrackerDbContext context,
        ReferenceWriter references,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["A task needs a title."],
            });
        }

        // AssignedGarage is a foreign key to a keyed table, not the free text it looks like.
        await references.EnsureGarageAsync(request.AssignedGarage, cancellationToken);

        var task = new MaintenanceTask
        {
            VehicleId = vehicleId.Value,
            Kind = request.Kind,
            Priority = request.Priority,
            Title = request.Title.Trim(),
            Description = request.Description,
            EstimatedCost = request.EstimatedCost,
            Status = request.Status,
            TargetDate = request.TargetDate,
            TargetService = request.TargetService,
            AssignedGarage = request.AssignedGarage,
            Notes = request.Notes,
            Source = EntrySource.Web,
        };

        context.MaintenanceTasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/vehicles/{registration}/tasks/{task.Id}", ToItem(task));
    }

    private static async Task<Results<Ok<TaskItem>, NotFound<ProblemDetails>>> UpdateTaskAsync(
        string registration,
        int id,
        UpdateTaskRequest request,
        CarTrackerDbContext context,
        ReferenceWriter references,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var task = await context.MaintenanceTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId.Value, cancellationToken);
        if (task is null) return VehicleLookup.NotFound(registration);

        await references.EnsureGarageAsync(request.AssignedGarage, cancellationToken);

        task.Kind = request.Kind ?? task.Kind;
        task.Priority = request.Priority ?? task.Priority;
        task.Title = request.Title ?? task.Title;
        task.Description = request.Description ?? task.Description;
        task.EstimatedCost = request.EstimatedCost ?? task.EstimatedCost;
        task.TargetDate = request.TargetDate ?? task.TargetDate;
        task.TargetService = request.TargetService ?? task.TargetService;
        task.AssignedGarage = request.AssignedGarage ?? task.AssignedGarage;
        task.Notes = request.Notes ?? task.Notes;

        if (request.Status is { } status && status != task.Status)
        {
            task.Status = status;
            // Done stamps a date; moving back off Done clears it. A completed date on an open task is the kind
            // of contradiction the workbook is full of.
            task.CompletedDate = status == MaintenanceTaskStatus.Done
                ? DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime)
                : null;
        }

        await context.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToItem(task));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteTaskAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var task = await context.MaintenanceTasks
            .FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId.Value, cancellationToken);
        if (task is null) return VehicleLookup.NotFound(registration);

        context.MaintenanceTasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static TaskItem ToItem(MaintenanceTask t) => new(
        t.Id, t.Kind, t.Priority, t.Title, t.Description, t.EstimatedCost, t.Status,
        t.TargetDate, t.TargetService, t.CompletedDate, t.AssignedGarage, t.ServiceRecordId, t.Notes);
}

/// <param name="ServiceRecordId">Set when a task was promoted to a service record. Promotion itself is M2.</param>
public sealed record TaskItem(
    int Id,
    MaintenanceTaskKind Kind,
    Priority Priority,
    string Title,
    string? Description,
    decimal? EstimatedCost,
    MaintenanceTaskStatus Status,
    DateOnly? TargetDate,
    string? TargetService,
    DateOnly? CompletedDate,
    string? AssignedGarage,
    int? ServiceRecordId,
    string? Notes);

/// <param name="BundleCost">
/// The cost of the open Workshop jobs — what the next garage visit is worth if they all go in together. The
/// design hardcodes "£150 · 1 job"; this is that figure computed, so it moves when a task is added.
/// </param>
public sealed record TaskLog(
    IReadOnlyList<TaskItem> Tasks,
    decimal BundleCost,
    int BundleCount,
    decimal OpenEstimateTotal);

public sealed record AddTaskRequest(
    string Title,
    MaintenanceTaskKind Kind = MaintenanceTaskKind.DIY,
    Priority Priority = Priority.Medium,
    MaintenanceTaskStatus Status = MaintenanceTaskStatus.Open,
    string? Description = null,
    decimal? EstimatedCost = null,
    DateOnly? TargetDate = null,
    string? TargetService = null,
    string? AssignedGarage = null,
    string? Notes = null);

public sealed record UpdateTaskRequest(
    string? Title = null,
    MaintenanceTaskKind? Kind = null,
    Priority? Priority = null,
    MaintenanceTaskStatus? Status = null,
    string? Description = null,
    decimal? EstimatedCost = null,
    DateOnly? TargetDate = null,
    string? TargetService = null,
    string? AssignedGarage = null,
    string? Notes = null);

/// <param name="Mileage">The odometer at completion — a task carries no reading, so promotion asks for one.</param>
/// <param name="Type">The service type. Free text; "MOT" is matched exactly for the expiry. Defaults to "Service".</param>
/// <param name="Cost">The amount actually paid. Defaults to the task's estimate when omitted.</param>
public sealed record PromoteTaskRequest(
    int Mileage,
    string Type = ServiceRecordFactory.ServiceCategory,
    decimal? Cost = null,
    string? Notes = null);

public sealed record PromoteTaskResponse(int ServiceRecordId, IReadOnlyList<AnomalyFlag> Flags);
