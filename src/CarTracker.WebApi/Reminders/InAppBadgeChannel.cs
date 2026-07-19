using CarTracker.Domain.Reminders;
using CarTracker.Shared.Metrics;

namespace CarTracker.WebApi.Reminders;

/// <summary>
/// The in-app badge channel — the only adapter built for this cut (DEC-006 leaves the rest open).
/// </summary>
/// <remarks>
/// In-app delivery is a <b>read</b>: the badge is derived from the reminders endpoint whenever the UI is open,
/// so this adapter has nothing to send. It exists so the dispatch loop has exactly one enabled member and the
/// seam is exercised — a background pass runs, evaluates, and reaches a real channel — while email, push and
/// the Assistant·MCP adapter stay named-but-unbuilt registration points rather than assumed-present. When a
/// push channel lands it plugs in beside this one with no change to the engine.
/// </remarks>
public sealed class InAppBadgeChannel(ILogger<InAppBadgeChannel> logger) : INotificationChannel
{
    public string Name => "in-app";

    public bool Enabled => true;

    public Task NotifyAsync(int vehicleId, IReadOnlyList<ReminderItem> fired, CancellationToken cancellationToken = default)
    {
        // Nothing to push — the badge reads the same derived state on demand. Logged at Debug so a background
        // pass is visible in the dev log without pretending a delivery happened.
        logger.LogDebug(
            "Reminders evaluated for vehicle {VehicleId}: {Count} firing (surfaced in-app, nothing pushed).",
            vehicleId, fired.Count);
        return Task.CompletedTask;
    }
}
