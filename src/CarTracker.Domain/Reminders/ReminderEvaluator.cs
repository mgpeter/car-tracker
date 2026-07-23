using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Reminders;

/// <summary>
/// Turns a vehicle's already-derived renewal and check state into reminders. A pure function over
/// <see cref="VehicleSummary"/> — it computes no dates and reads no table, because <c>IDerivedMetricsService</c>
/// already did, and doing it again is how the badge and the dashboard would drift apart (DEC-002, README §4).
/// </summary>
/// <remarks>
/// Firing mirrors the dashboard's "needs attention" panel exactly: a statutory renewal fires when its urgency
/// is <see cref="RenewalUrgency.Red"/> (under 30 days, expired included), and a check fires when it is
/// <see cref="CheckStatus.Overdue"/> or <see cref="CheckStatus.Attention"/> (its latest log recorded a bad
/// verdict, which is on the due axis now, not a display flag). Amber renewals and due-soon checks are quiet — they surface
/// only under <c>includeQuiet</c>. A <see cref="CheckStatus.NeverLogged"/> check is not a reminder: a cadence
/// that never started has not lapsed, the same fourth-state distinction the integrity axis keeps blue off due.
/// The wash and tyre triggers the design lists separately are check definitions here, so they fall out of the
/// same check loop rather than needing calculators of their own.
/// </remarks>
public static class ReminderEvaluator
{
    public static ReminderList Evaluate(VehicleSummary summary, bool includeQuiet = false)
    {
        var items = new List<ReminderItem>();

        void Add(ReminderItem item)
        {
            if (item.Firing || includeQuiet)
            {
                items.Add(item);
            }
        }

        var renewals = summary.Renewals;
        Add(RenewalItem(renewals.Mot, ReminderKind.Renewal));
        Add(RenewalItem(renewals.Insurance, ReminderKind.Renewal));
        Add(RenewalItem(renewals.RoadTax, ReminderKind.Renewal));
        Add(RenewalItem(renewals.NextServiceDate, ReminderKind.Service));

        if (ServiceMileageItem(renewals.NextServiceMiles) is { } mileageItem)
        {
            Add(mileageItem);
        }

        foreach (var check in summary.Checks.Checks)
        {
            // Never-logged never fires and is never listed — it is not on the due axis (see remarks).
            if (check.Status is CheckStatus.NeverLogged)
            {
                continue;
            }

            Add(CheckItem(check));
        }

        return new ReminderList(items.Count(i => i.Firing), items);
    }

    private static ReminderItem RenewalItem(Renewal renewal, ReminderKind kind)
    {
        var firing = renewal.Urgency is RenewalUrgency.Red;
        var severity = renewal.Urgency switch
        {
            RenewalUrgency.Red => ReminderSeverity.Overdue,
            RenewalUrgency.Amber => ReminderSeverity.DueSoon,
            _ => ReminderSeverity.Ok,
        };

        return new ReminderItem(kind, renewal.Name, RenewalReason(renewal), severity, firing, renewal.DaysRemaining);
    }

    private static string RenewalReason(Renewal renewal)
    {
        if (renewal.DaysRemaining is not { } days)
        {
            return renewal.ExpiryDate is null ? "No date recorded" : "No countdown";
        }

        return days switch
        {
            < 0 => $"Expired {-days} days ago",
            _ when renewal.Urgency is RenewalUrgency.Red => $"{days} days remaining — under 30, renew now",
            _ when renewal.Urgency is RenewalUrgency.Amber => $"{days} days remaining — under 60",
            _ => $"{days} days remaining, OK",
        };
    }

    /// <summary>
    /// Service due by mileage. Fires only when it has actually been reached (miles remaining ≤ 0); a positive
    /// figure is a quiet countdown. Null (no next-service mileage recorded) produces no item at all.
    /// </summary>
    private static ReminderItem? ServiceMileageItem(int? nextServiceMiles)
    {
        if (nextServiceMiles is not { } miles)
        {
            return null;
        }

        return miles <= 0
            ? new ReminderItem(ReminderKind.Service, "Next service (by mileage)",
                $"Overdue by {-miles} miles", ReminderSeverity.Overdue, Firing: true, DaysRemaining: null)
            : new ReminderItem(ReminderKind.Service, "Next service (by mileage)",
                $"{miles} miles remaining, OK", ReminderSeverity.Ok, Firing: false, DaysRemaining: null);
    }

    private static ReminderItem CheckItem(CheckState check)
    {
        // A flagged check (Attention/Failed on its latest log) fires like an overdue one: it is on the due
        // axis now, not merely a display flag, so the badge and the dashboard raise it either way.
        var firing = check.Status is CheckStatus.Overdue or CheckStatus.Attention;
        var severity = check.Status switch
        {
            CheckStatus.Overdue or CheckStatus.Attention => ReminderSeverity.Overdue,
            CheckStatus.DueSoon => ReminderSeverity.DueSoon,
            _ => ReminderSeverity.Ok,
        };

        return new ReminderItem(ReminderKind.Check, check.Name, CheckReason(check), severity, firing, check.DaysRemaining);
    }

    private static string CheckReason(CheckState check)
    {
        // The verdict outranks the countdown: a flagged check is raised for what it found, not how long ago.
        if (check.Status is CheckStatus.Attention)
        {
            var verdict = check.Result is CheckResult.Failed ? "Failed" : "Attention";
            return $"Flagged {verdict} on the last check — needs looking at";
        }

        if (check.DaysRemaining is not { } days)
        {
            return $"Never logged — due every {check.IntervalDays} days";
        }

        // How long since the last log: the interval, less what is left on the clock (negative days-remaining
        // adds). Matches the design's "Overdue — 52 days, target 30".
        var sinceLast = check.IntervalDays - days;

        return check.Status switch
        {
            CheckStatus.Overdue => $"Overdue — {sinceLast} days, target {check.IntervalDays}",
            CheckStatus.DueSoon => $"Due soon — {days} days left of {check.IntervalDays}",
            _ => $"OK — {days} days left of {check.IntervalDays}",
        };
    }
}
