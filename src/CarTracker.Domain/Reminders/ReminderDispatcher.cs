using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Reminders;

/// <summary>
/// One evaluation pass: enumerate the vehicles, read each one's derived summary, evaluate the reminders, and
/// hand what fired to every enabled channel.
/// </summary>
/// <remarks>
/// The testable core of the hosted background service. Kept scoped and pure of timers so it can be exercised
/// with stub channels and a fake loader, while the <c>BackgroundService</c> in the web app supplies only the
/// clock and the per-tick DI scope. It reads <see cref="IDerivedMetricsService"/> exactly as the dashboard and
/// the reminders endpoint do — never a second query.
/// </remarks>
public sealed class ReminderDispatcher(
    IVehicleMetricsLoader loader,
    IDerivedMetricsService metrics,
    IEnumerable<INotificationChannel> channels)
{
    /// <summary>
    /// Evaluate every vehicle and dispatch its fired reminders. Returns the total number of firing reminders
    /// dispatched across the fleet, so a caller (or a test) can see the pass did work.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var enabled = channels.Where(c => c.Enabled).ToList();
        var totalFired = 0;

        var vehicleIds = await loader.ListVehicleIdsAsync(cancellationToken);
        foreach (var vehicleId in vehicleIds)
        {
            var summary = await metrics.GetVehicleSummaryAsync(vehicleId, cancellationToken);
            if (summary is null)
            {
                continue;
            }

            var fired = ReminderEvaluator.Evaluate(summary).Items.Where(i => i.Firing).ToList();
            if (fired.Count == 0)
            {
                continue;
            }

            totalFired += fired.Count;

            // Every enabled channel, so adding a second adapter reaches it with no change here — the seam.
            foreach (var channel in enabled)
            {
                await channel.NotifyAsync(vehicleId, fired, cancellationToken);
            }
        }

        return totalFired;
    }
}
