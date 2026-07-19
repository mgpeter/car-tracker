using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Reminders;

/// <summary>
/// A delivery channel for fired reminders — the adapter seam DEC-006 requires.
/// </summary>
/// <remarks>
/// "Channels are adapters. Triggers fire regardless; today they only surface in-app. Wiring a channel later
/// changes delivery, not logic." The trigger evaluation (<see cref="ReminderEvaluator"/>) is separate from the
/// delivery, so choosing between email, push and the in-app badge later costs a registration, not a rewrite.
/// Channels are registered as a collection and the dispatcher hands fired items to every enabled one.
/// </remarks>
public interface INotificationChannel
{
    /// <summary>A stable identifier for logs and diagnostics — "in-app", "email", "ntfy".</summary>
    string Name { get; }

    /// <summary>Whether this channel is currently delivering. A disabled channel is skipped, not removed.</summary>
    bool Enabled { get; }

    /// <summary>Deliver the reminders that fired for one vehicle. Never called with an empty list.</summary>
    Task NotifyAsync(int vehicleId, IReadOnlyList<ReminderItem> fired, CancellationToken cancellationToken = default);
}
