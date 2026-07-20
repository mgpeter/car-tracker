using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Logs;

/// <summary>
/// The task read + add + complete paths the REST endpoints and the MCP tools share. Promotion to a service record
/// stays on <see cref="TaskPromoter"/> (a factory of its own); edit and delete stay in the endpoint.
/// </summary>
public sealed class TaskService(CarTrackerDbContext context, ReferenceWriter references, TimeProvider timeProvider)
{
    public async Task<WriteResult<TaskItem>> AddAsync(
        int vehicleId, TaskInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return WriteResult<TaskItem>.Invalid("Title", "A task needs a title.");

        // AssignedGarage is a keyed FK, created on first use (else an FK 500 the first time it is typed).
        await references.EnsureGarageAsync(input.AssignedGarage, cancellationToken);

        var task = new MaintenanceTask
        {
            VehicleId = vehicleId,
            Kind = input.Kind,
            Priority = input.Priority,
            Title = input.Title.Trim(),
            Description = input.Description,
            EstimatedCost = input.EstimatedCost,
            Status = input.Status,
            TargetDate = input.TargetDate,
            TargetService = input.TargetService,
            AssignedGarage = input.AssignedGarage,
            Notes = input.Notes,
            Source = source,
        };

        context.MaintenanceTasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<TaskItem>.Created(ToItem(task));
    }

    /// <summary>Marks a task Done and stamps its completed date (defaulting to today). A safe update.</summary>
    public async Task<WriteResult<TaskItem>> CompleteAsync(
        int vehicleId, int taskId, DateOnly? completedDate, EntrySource source, CancellationToken cancellationToken = default)
    {
        var task = await context.MaintenanceTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.VehicleId == vehicleId, cancellationToken);
        if (task is null) return WriteResult<TaskItem>.NotFound();

        task.Status = MaintenanceTaskStatus.Done;
        task.CompletedDate = completedDate ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<TaskItem>.Updated(ToItem(task));
    }

    private static TaskItem ToItem(MaintenanceTask t) => new(
        t.Id, t.Kind, t.Priority, t.Title, t.Description, t.EstimatedCost, t.Status,
        t.TargetDate, t.TargetService, t.CompletedDate, t.AssignedGarage, t.ServiceRecordId, t.Notes);
}
