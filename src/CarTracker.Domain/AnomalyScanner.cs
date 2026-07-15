using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Runs the detectors after a write and persists what they find.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AnomalyDetector"/> is pure — it takes loaded data plus the open flags and returns new ones,
/// leaving persistence to a caller. Until now it had no caller at all: the class was built, tested against the
/// real workbook history, and invoked from nowhere. This is it.
/// </para>
/// <para>
/// One scanner rather than detection inlined into each endpoint, for two reasons. The detectors reason about a
/// vehicle's <em>whole</em> history — the 83,000 mi service record is only wrong relative to every other
/// reading — so there is no such thing as validating one row in isolation. And an endpoint that forgot to scan
/// would silently accept bad data, which is the failure the spreadsheet already has and the one thing this
/// product exists to stop.
/// </para>
/// <para>
/// Spec §5.3: a flag never blocks a write. The reading is recorded, then excluded from derived figures until
/// the flag is resolved. Refusing the save would just push the owner into "correcting" the number at the
/// keyboard to make the app accept it — which is exactly how a spreadsheet ends up with 80,300 typed as
/// 83,000 and nobody any the wiser.
/// </para>
/// </remarks>
public sealed class AnomalyScanner(CarTrackerDbContext context, IVehicleMetricsLoader loader)
{
    /// <summary>
    /// Re-detects for one vehicle and saves any newly-found flags.
    /// </summary>
    /// <returns>The flags raised by this scan. Empty is the normal case.</returns>
    public async Task<IReadOnlyList<DataAnomaly>> ScanAsync(
        int vehicleId,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        var data = await loader.LoadAsync(vehicleId, cancellationToken);

        if (data is null)
        {
            // The vehicle went away between the write and the scan. Nothing to say about it.
            return [];
        }

        // Only the open ones: AnomalyDetector de-duplicates against these, so a flag the owner has already
        // Accepted or Dismissed must not be raised again on the next write. Resolved means decided.
        var existing = await context.DataAnomalies
            .AsNoTracking()
            .Where(a => a.VehicleId == vehicleId)
            .ToListAsync(cancellationToken);

        var found = AnomalyDetector.Detect(data, existing);

        if (found.Count == 0)
        {
            return [];
        }

        foreach (var anomaly in found)
        {
            anomaly.Source = source;
        }

        context.DataAnomalies.AddRange(found);
        await context.SaveChangesAsync(cancellationToken);

        return found;
    }
}
