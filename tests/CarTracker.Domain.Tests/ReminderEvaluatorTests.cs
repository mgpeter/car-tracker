using CarTracker.Domain;
using CarTracker.Domain.Reminders;
using CarTracker.Domain.Tests.Workbook;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class ReminderEvaluatorTests
{
    private static Renewal Rnl(string name, int? days, RenewalUrgency? urgency) =>
        new(name, ExpiryDate: null, DaysRemaining: days, Urgency: urgency, Source: null);

    private static CheckState Chk(string name, CheckStatus status, int? days, int interval = 30) =>
        new(CheckDefinitionId: 1, Name: name, CadenceLabel: "cadence", IntervalDays: interval,
            LastPerformedOn: null, NextDue: null, DaysRemaining: days, Status: status);

    private static CheckStatusSummary Checks(params CheckState[] checks) =>
        new(
            OkCount: checks.Count(c => c.Status is CheckStatus.Ok),
            DueSoonCount: checks.Count(c => c.Status is CheckStatus.DueSoon),
            OverdueCount: checks.Count(c => c.Status is CheckStatus.Overdue),
            NeverLoggedCount: checks.Count(c => c.Status is CheckStatus.NeverLogged),
            Checks: checks);

    /// <summary>A real summary with its renewal and check state swapped for the scenario under test.</summary>
    private static VehicleSummary Summary(RenewalSummary renewals, CheckStatusSummary checks) =>
        DerivedMetrics.Compute(WorkbookFixture.Data(), WorkbookFixture.ReferenceDate) with
        {
            Renewals = renewals,
            Checks = checks,
        };

    private static RenewalSummary AllRenewalsOk(int? nextServiceMiles = null) =>
        new(
            Mot: Rnl("MOT", 359, RenewalUrgency.Ok),
            Insurance: Rnl("Insurance", 244, RenewalUrgency.Ok),
            RoadTax: Rnl("Road tax", 229, RenewalUrgency.Ok),
            NextServiceDate: Rnl("Next service", 302, RenewalUrgency.Ok),
            NextServiceMiles: nextServiceMiles);

    [Fact]
    public void It_fires_on_overdue_checks_and_stays_quiet_on_the_wash_and_the_mot()
    {
        // BT53's shape: seven ordinary checks overdue plus the tyre tread check overdue, the wash still inside
        // its window (14 days left of 28), one check never logged, and the MOT 359 days out.
        var overdue = Enumerable.Range(1, 7).Select(i => Chk($"Check {i}", CheckStatus.Overdue, -5)).ToArray();
        var checks = Checks(
            overdue
                .Append(Chk("Tread depth, all 4 tyres", CheckStatus.Overdue, -22, interval: 30))
                .Append(Chk("Wash & underbody rinse", CheckStatus.Ok, 14, interval: 28))
                .Append(Chk("Spare tyre pressure", CheckStatus.NeverLogged, null))
                .ToArray());

        var result = ReminderEvaluator.Evaluate(Summary(AllRenewalsOk(), checks));

        // Eight overdue checks fire; the wash, the MOT and the never-logged spare do not.
        Assert.Equal(8, result.FiringCount);
        Assert.All(result.Items, i => Assert.True(i.Firing));
        Assert.DoesNotContain(result.Items, i => i.Subject == "Wash & underbody rinse");
        Assert.DoesNotContain(result.Items, i => i.Subject == "MOT");
        Assert.DoesNotContain(result.Items, i => i.Subject == "Spare tyre pressure");
        Assert.Contains(result.Items, i => i.Subject == "Tread depth, all 4 tyres" && i.Reason.Contains("Overdue — 52 days, target 30"));
    }

    [Fact]
    public void A_never_logged_check_is_never_a_reminder_even_with_include_quiet()
    {
        var checks = Checks(Chk("Spare tyre pressure", CheckStatus.NeverLogged, null));

        var quiet = ReminderEvaluator.Evaluate(Summary(AllRenewalsOk(), checks), includeQuiet: true);

        // A cadence that never started has not lapsed — it is the fourth state, not on the due axis.
        Assert.DoesNotContain(quiet.Items, i => i.Subject == "Spare tyre pressure");
    }

    [Fact]
    public void A_red_renewal_fires_and_an_amber_one_stays_quiet()
    {
        var renewals = new RenewalSummary(
            Mot: Rnl("MOT", -3, RenewalUrgency.Red),            // expired: fires
            Insurance: Rnl("Insurance", 45, RenewalUrgency.Amber), // under 60: quiet
            RoadTax: Rnl("Road tax", 200, RenewalUrgency.Ok),
            NextServiceDate: Rnl("Next service", 300, RenewalUrgency.Ok),
            NextServiceMiles: null);

        var firing = ReminderEvaluator.Evaluate(Summary(renewals, Checks()));
        Assert.Equal(1, firing.FiringCount);
        Assert.Contains(firing.Items, i => i.Subject == "MOT" && i.Reason.Contains("Expired 3 days ago"));

        var quiet = ReminderEvaluator.Evaluate(Summary(renewals, Checks()), includeQuiet: true);
        var insurance = Assert.Single(quiet.Items, i => i.Subject == "Insurance");
        Assert.False(insurance.Firing);
        Assert.Equal(ReminderSeverity.DueSoon, insurance.Severity);
    }

    [Fact]
    public void Service_fires_by_mileage_only_once_it_is_reached()
    {
        Assert.Equal(0, ReminderEvaluator.Evaluate(Summary(AllRenewalsOk(nextServiceMiles: 400), Checks())).FiringCount);

        var overdue = ReminderEvaluator.Evaluate(Summary(AllRenewalsOk(nextServiceMiles: -120), Checks()));
        var item = Assert.Single(overdue.Items, i => i.Kind == ReminderKind.Service && i.Subject.Contains("mileage"));
        Assert.True(item.Firing);
        Assert.Contains("Overdue by 120 miles", item.Reason);
    }

    [Fact]
    public void Include_quiet_lists_the_evaluated_but_quiet_triggers()
    {
        var checks = Checks(
            Chk("Oil check", CheckStatus.Overdue, -5),
            Chk("Wash & underbody rinse", CheckStatus.Ok, 14, interval: 28));

        var firing = ReminderEvaluator.Evaluate(Summary(AllRenewalsOk(), checks));
        var quiet = ReminderEvaluator.Evaluate(Summary(AllRenewalsOk(), checks), includeQuiet: true);

        // Default view: only the firing oil check. Quiet view: the wash and the OK renewals appear too, marked
        // not-firing, but the firing count is unchanged.
        Assert.Single(firing.Items);
        Assert.Equal(firing.FiringCount, quiet.FiringCount);
        Assert.Contains(quiet.Items, i => i.Subject == "Wash & underbody rinse" && !i.Firing);
        Assert.Contains(quiet.Items, i => i.Subject == "MOT" && !i.Firing && i.Reason.Contains("OK"));
    }

    /// <summary>
    /// The parity claim: the firing set is exactly the dashboard's own renewal and check state, re-expressed.
    /// The reminder count and the dashboard cannot disagree because both read one derived summary.
    /// </summary>
    [Fact]
    public void The_firing_set_equals_what_the_summary_already_implies()
    {
        var renewals = new RenewalSummary(
            Mot: Rnl("MOT", -1, RenewalUrgency.Red),
            Insurance: Rnl("Insurance", 20, RenewalUrgency.Red),
            RoadTax: Rnl("Road tax", 200, RenewalUrgency.Ok),
            NextServiceDate: Rnl("Next service", 300, RenewalUrgency.Ok),
            NextServiceMiles: null);
        var checks = Checks(
            Chk("A", CheckStatus.Overdue, -5),
            Chk("B", CheckStatus.Overdue, -1),
            Chk("C", CheckStatus.DueSoon, 3),
            Chk("D", CheckStatus.Ok, 20),
            Chk("E", CheckStatus.NeverLogged, null));
        var summary = Summary(renewals, checks);

        var result = ReminderEvaluator.Evaluate(summary);

        var redRenewals = new[] { summary.Renewals.Mot, summary.Renewals.Insurance, summary.Renewals.RoadTax, summary.Renewals.NextServiceDate }
            .Count(r => r.Urgency is RenewalUrgency.Red);
        Assert.Equal(redRenewals + summary.Checks.OverdueCount, result.FiringCount);
    }
}
