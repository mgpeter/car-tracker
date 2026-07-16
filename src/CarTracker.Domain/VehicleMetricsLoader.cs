using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Loads one vehicle's inputs from the database.
/// </summary>
/// <remarks>
/// The only part of the metrics stack that touches EF. Everything downstream is a pure function of what this
/// returns, which is why the workbook fixture can be a C# constant.
/// </remarks>
public sealed class VehicleMetricsLoader(CarTrackerDbContext context) : IVehicleMetricsLoader
{
    public async Task<VehicleMetricsData?> LoadAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        // Metrics compute for any vehicle regardless of lifecycle status (DEC-007): a Sold car's history
        // still answers questions. Filtering Sold/SORN is presentation, done by the garage surfaces.
        var vehicle = await context.Vehicles
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);

        if (vehicle is null)
        {
            return null;
        }

        var checkDefinitions = await context.CheckDefinitions
            .AsNoTracking()
            .Where(d => d.VehicleId == vehicleId)
            .ToListAsync(cancellationToken);

        var definitionIds = checkDefinitions.Select(d => d.Id).ToList();

        return new VehicleMetricsData(
            Vehicle: vehicle,
            MileageReadings: await context.MileageReadings.AsNoTracking()
                .Where(m => m.VehicleId == vehicleId).ToListAsync(cancellationToken),
            FuelEntries: await context.FuelEntries.AsNoTracking()
                .Where(f => f.VehicleId == vehicleId).ToListAsync(cancellationToken),
            ExpenseEntries: await context.ExpenseEntries.AsNoTracking()
                .Where(e => e.VehicleId == vehicleId).ToListAsync(cancellationToken),
            ServiceRecords: await context.ServiceRecords.AsNoTracking()
                .Where(s => s.VehicleId == vehicleId).ToListAsync(cancellationToken),
            CheckDefinitions: checkDefinitions,
            // Check logs are scoped through their definition, not by vehicle id — the definition is already
            // vehicle-scoped and a second path would let the two disagree.
            CheckLogs: await context.CheckLogs.AsNoTracking()
                .Where(l => definitionIds.Contains(l.CheckDefinitionId)).ToListAsync(cancellationToken),
            BudgetCategories: await context.BudgetCategories.AsNoTracking()
                .Where(b => b.VehicleId == vehicleId).ToListAsync(cancellationToken),
            // Open flags only. The summary reports a headline (count + worst severity); the full queue with
            // each flag's detail is the anomalies endpoint's job, not the metrics stack's.
            OpenAnomalies: await context.DataAnomalies.AsNoTracking()
                .Where(a => a.VehicleId == vehicleId && a.Status == AnomalyStatus.Open).ToListAsync(cancellationToken));
    }

    /// <remarks>
    /// Every vehicle regardless of status, for the same reason <see cref="LoadAsync"/> ignores it: a Sold
    /// car's history still answers questions (DEC-007). Hiding Sold or SORN is presentation, and the garage
    /// screen's filter — not a decision to bury in a loader.
    /// </remarks>
    public async Task<IReadOnlyList<int>> ListVehicleIdsAsync(CancellationToken cancellationToken = default)
    {
        return await context.Vehicles
            .AsNoTracking()
            // Default first, then oldest first. The garage is a short list a human reads top to bottom.
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, int>> CountOpenAnomaliesAsync(
        CancellationToken cancellationToken = default)
    {
        return await context.DataAnomalies
            .AsNoTracking()
            .Where(a => a.Status == AnomalyStatus.Open)
            .GroupBy(a => a.VehicleId)
            .Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VehicleId, x => x.Count, cancellationToken);
    }
}
