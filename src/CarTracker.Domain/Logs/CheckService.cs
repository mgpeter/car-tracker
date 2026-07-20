using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Logs;

/// <summary>
/// Marking regular checks done — the shared path behind the web batch (the weekly walk-around) and the MCP
/// <c>mark_check_done</c> tool. A check's status is never stored; the recomputed <see cref="CheckStatusSummary"/>
/// comes straight back so a caller does not have to guess what changed.
/// </summary>
public sealed class CheckService(CarTrackerDbContext context, IDerivedMetricsService metrics)
{
    /// <summary>Marks one or more checks (by definition id) done. Every id must belong to the vehicle.</summary>
    public async Task<WriteResult<CheckStatusSummary>> MarkDoneAsync(
        int vehicleId,
        IReadOnlyList<int> checkDefinitionIds,
        DateOnly performedOn,
        CheckResult? result,
        string? notes,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        if (checkDefinitionIds.Count == 0)
            return WriteResult<CheckStatusSummary>.Invalid("CheckDefinitionIds", "Name at least one check.");

        var owned = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId && checkDefinitionIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var unknown = checkDefinitionIds.Except(owned).ToList();
        if (unknown.Count > 0)
            return WriteResult<CheckStatusSummary>.Invalid("CheckDefinitionIds", $"Not checks on this vehicle: {string.Join(", ", unknown)}.");

        foreach (var id in owned)
        {
            context.CheckLogs.Add(new CheckLog
            {
                CheckDefinitionId = id,
                PerformedOn = performedOn,
                Result = result,
                Notes = notes,
                Source = source,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId, cancellationToken);
        return summary is null
            ? WriteResult<CheckStatusSummary>.NotFound()
            : WriteResult<CheckStatusSummary>.Updated(summary.Checks);
    }

    /// <summary>Marks a single check done by (active) name — what the MCP <c>mark_check_done</c> tool receives.</summary>
    public async Task<WriteResult<CheckStatusSummary>> MarkDoneByNameAsync(
        int vehicleId,
        string checkName,
        DateOnly performedOn,
        CheckResult? result,
        string? notes,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        var normalized = checkName.Trim();
        var id = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId && d.IsActive && d.Name.ToLower() == normalized.ToLower())
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (id is null)
            return WriteResult<CheckStatusSummary>.Invalid("checkName", $"No active check named '{checkName}' on this vehicle. Use get_check_status to see the names.");

        return await MarkDoneAsync(vehicleId, [id.Value], performedOn, result, notes, source, cancellationToken);
    }
}
