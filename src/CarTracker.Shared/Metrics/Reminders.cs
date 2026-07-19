namespace CarTracker.Shared.Metrics;

/// <summary>
/// Which axis a reminder sits on. A reminder is a due-status thing (README §3.1), never the blue data-integrity
/// axis and never the orange structural accent — keeping those apart is <c>lib/status.ts</c>'s whole job.
/// </summary>
public enum ReminderSeverity
{
    /// <summary>Evaluated and clear. Only ever appears in the <c>includeQuiet</c> view.</summary>
    Ok = 1,

    /// <summary>Approaching — an amber renewal, or a check due soon. Shown quiet by default.</summary>
    DueSoon = 2,

    /// <summary>Past its threshold: a red renewal, an overdue check, a service due by miles. This fires.</summary>
    Overdue = 3,
}

/// <summary>What kind of thing is due. The subject carries the specific name.</summary>
public enum ReminderKind
{
    /// <summary>A statutory renewal — MOT, insurance, road tax.</summary>
    Renewal = 1,

    /// <summary>The next service, due by date or by mileage.</summary>
    Service = 2,

    /// <summary>A regular check past its cadence — including the wash and tyre checks, which are check definitions.</summary>
    Check = 3,
}

/// <summary>
/// One evaluated reminder: the derived due state re-expressed, with a human reason. Not a stored fact — it is
/// read off <c>VehicleSummary</c>, so it cannot disagree with the dashboard that computed the same state.
/// </summary>
/// <param name="Reason">A human sentence — "Overdue — 52 days, target 30", "359 days remaining, OK".</param>
/// <param name="Firing">
/// True when this reminder is currently sounding (the badge counts these). False for a trigger that was
/// evaluated and found clear, which only appears when <c>includeQuiet</c> is set.
/// </param>
/// <param name="DaysRemaining">Days to the due date, negative when overdue. Null for a mileage-only service trigger.</param>
public sealed record ReminderItem(
    ReminderKind Kind,
    string Subject,
    string Reason,
    ReminderSeverity Severity,
    bool Firing,
    int? DaysRemaining);

/// <summary>
/// The reminders for one vehicle. <see cref="FiringCount"/> is what the shell badge shows; it equals the number
/// of firing items, so the badge and this list can never disagree.
/// </summary>
public sealed record ReminderList(
    int FiringCount,
    IReadOnlyList<ReminderItem> Items);
