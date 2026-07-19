using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>The outcome of a promotion, mapped to HTTP by the endpoint.</summary>
public enum PromoteStatus
{
    Ok,
    TaskNotFound,
    /// <summary>Only Workshop work is a service; DIY is added as a DIY record directly.</summary>
    NotWorkshop,
    /// <summary>A job still open has no completion date to become the service date.</summary>
    NotDone,
    /// <summary>Promoting twice would create a second record and orphan the first.</summary>
    AlreadyPromoted,
    /// <summary>A service record needs a type ("MOT" is matched exactly for the expiry).</summary>
    TypeRequired,
}

public sealed record PromoteResult(PromoteStatus Status, int ServiceRecordId = 0);

/// <summary>
/// README §3.3's one-click promotion: a completed Workshop task becomes a <see cref="ServiceRecord"/>, and the
/// task keeps the record's id.
/// </summary>
/// <remarks>
/// Wiring, not modelling — <see cref="MaintenanceTask.ServiceRecordId"/> already round-trips read-only; this is
/// the write that sets it. The record is created through <see cref="ServiceRecordFactory"/>, the same path
/// AddService uses (record + mileage reading + mirrored expense, one transaction), so a promoted cost moves the
/// spend rollup and a reading is written with no second three-row path. A task carries no odometer — it records
/// what to do, not the reading it was done at — so the caller supplies the mileage; the estimate is only a
/// default for the cost, because an estimate is not a receipt.
/// </remarks>
public sealed class TaskPromoter(CarTrackerDbContext context, ServiceRecordFactory factory)
{
    public async Task<PromoteResult> PromoteAsync(
        int vehicleId,
        int taskId,
        int mileage,
        string type,
        decimal? cost,
        string? notes,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        var task = await context.MaintenanceTasks
            .SingleOrDefaultAsync(t => t.Id == taskId && t.VehicleId == vehicleId, cancellationToken);
        if (task is null) return new PromoteResult(PromoteStatus.TaskNotFound);

        if (task.Kind != MaintenanceTaskKind.Workshop) return new PromoteResult(PromoteStatus.NotWorkshop);
        if (task.Status != MaintenanceTaskStatus.Done || task.CompletedDate is not { } completedDate)
            return new PromoteResult(PromoteStatus.NotDone);
        if (task.ServiceRecordId is not null) return new PromoteResult(PromoteStatus.AlreadyPromoted);
        if (string.IsNullOrWhiteSpace(type)) return new PromoteResult(PromoteStatus.TypeRequired);

        var workDone = string.IsNullOrWhiteSpace(task.Description) ? task.Title : $"{task.Title} — {task.Description}";
        var record = new ServiceRecord
        {
            VehicleId = vehicleId,
            ServiceDate = completedDate,
            Type = type.Trim(),
            Mileage = mileage,
            Garage = task.AssignedGarage,
            WorkDone = workDone,
            Cost = cost ?? task.EstimatedCost,
            Notes = notes ?? $"Converted from workshop task #{task.Id}",
        };

        await factory.CreateAsync(record, source, cancellationToken);

        // The record's id now exists; stamp the link so the history gains a row and the task keeps its provenance.
        task.ServiceRecordId = record.Id;
        await context.SaveChangesAsync(cancellationToken);

        return new PromoteResult(PromoteStatus.Ok, record.Id);
    }
}
