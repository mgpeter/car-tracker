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

        return app;
    }

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
