using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class CheckStatusCalculatorTests
{
    private static readonly DateOnly Reference = new(2026, 7, 14);

    private static CheckDefinition Definition(int id, string name, int intervalDays, bool isActive = true) =>
        new()
        {
            Id = id,
            VehicleId = 1,
            Name = name,
            CadenceLabel = intervalDays == 7 ? "Weekly" : $"Every {intervalDays} days",
            IntervalDays = intervalDays,
            DisplayOrder = id,
            IsActive = isActive,
            Source = EntrySource.Import,
        };

    private static CheckLog Log(int definitionId, string date, CheckResult? result = null, int id = 0) =>
        new()
        {
            Id = id,
            CheckDefinitionId = definitionId,
            PerformedOn = DateOnly.Parse(date),
            Result = result,
            Source = EntrySource.Import,
        };

    /// <summary>
    /// The fourth state, and why it exists.
    /// </summary>
    /// <remarks>
    /// The workbook has 18 check definitions but its Dashboard counts 17: "Spare tyre pressure" has never been
    /// logged and falls out of the OK/due-soon/overdue buckets entirely. Never-logged is not OK.
    /// </remarks>
    [Fact]
    public void A_never_logged_check_is_its_own_state_not_ok()
    {
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "Spare tyre pressure", 30)],
            logs: [],
            Reference);

        var check = result.Checks.Single();
        Assert.Equal(CheckStatus.NeverLogged, check.Status);
        Assert.NotEqual(CheckStatus.Ok, check.Status);
        Assert.Null(check.LastPerformedOn);
        Assert.Null(check.NextDue);
        Assert.Null(check.DaysRemaining);
    }

    [Fact]
    public void The_counts_sum_to_eighteen_not_seventeen()
    {
        // The real BT53 AKJ distribution at the reference date: 7 overdue, 3 due soon, 7 OK, 1 never logged.
        var definitions = new List<CheckDefinition>();
        var logs = new List<CheckLog>();

        for (var i = 1; i <= 7; i++) // overdue: weekly, last done 19 days ago
        {
            definitions.Add(Definition(i, $"overdue-{i}", 7));
            logs.Add(Log(i, "2026-06-25"));
        }

        for (var i = 8; i <= 10; i++) // due soon: 30-day interval, window is 6 days; 27 days ago -> 3 left
        {
            definitions.Add(Definition(i, $"soon-{i}", 30));
            logs.Add(Log(i, "2026-06-17"));
        }

        for (var i = 11; i <= 17; i++) // ok: 90-day interval, done recently
        {
            definitions.Add(Definition(i, $"ok-{i}", 90));
            logs.Add(Log(i, "2026-07-01"));
        }

        definitions.Add(Definition(18, "Spare tyre pressure", 30)); // never logged

        var result = CheckStatusCalculator.Calculate(definitions, logs, Reference);

        Assert.Equal(7, result.OverdueCount);
        Assert.Equal(3, result.DueSoonCount);
        Assert.Equal(7, result.OkCount);
        Assert.Equal(1, result.NeverLoggedCount);
        Assert.Equal(18, result.TotalCount);
        Assert.Equal(18, result.Checks.Count);
    }

    [Fact]
    public void An_overdue_check_reports_how_far_past_due_it_is()
    {
        // The K-series head-gasket early warning: weekly, last done 18 June, so 19 days overdue.
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "Oil filler cap (mayo residue)", 7)],
            [Log(1, "2026-06-18")],
            Reference);

        var check = result.Checks.Single();
        Assert.Equal(CheckStatus.Overdue, check.Status);
        Assert.Equal(new DateOnly(2026, 6, 25), check.NextDue);
        Assert.Equal(-19, check.DaysRemaining);
    }

    [Theory]
    // Weekly: window = max(1, ceil(7 * 0.2)) = 2 days.
    [InlineData(7, "2026-07-14", 7, CheckStatus.Ok)]        // done today, 7 days left
    [InlineData(7, "2026-07-10", 3, CheckStatus.Ok)]        // 3 left, outside the 2-day window
    [InlineData(7, "2026-07-09", 2, CheckStatus.DueSoon)]   // exactly at the window
    [InlineData(7, "2026-07-08", 1, CheckStatus.DueSoon)]
    [InlineData(7, "2026-07-07", 0, CheckStatus.DueSoon)]   // due today is not yet overdue
    [InlineData(7, "2026-07-06", -1, CheckStatus.Overdue)]
    // Annual: window = ceil(365 * 0.2) = 73 days.
    [InlineData(365, "2025-08-01", 18, CheckStatus.DueSoon)]
    [InlineData(365, "2026-01-01", 171, CheckStatus.Ok)]
    public void Status_follows_the_scaled_due_soon_window(int interval, string lastDone, int expectedDays, CheckStatus expected)
    {
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "check", interval)],
            [Log(1, lastDone)],
            Reference);

        var check = result.Checks.Single();
        Assert.Equal(expectedDays, check.DaysRemaining);
        Assert.Equal(expected, check.Status);
    }

    [Fact]
    public void The_due_soon_window_scales_with_the_interval()
    {
        // A fixed window is wrong at both ends: 7 days' notice on a weekly check means always-due-soon, and
        // on an annual check it is useless. At 3 days remaining, weekly is fine but monthly is due soon.
        var weekly = CheckStatusCalculator.Calculate(
            [Definition(1, "weekly", 7)], [Log(1, "2026-07-10")], Reference).Checks.Single();

        var monthly = CheckStatusCalculator.Calculate(
            [Definition(1, "monthly", 30)], [Log(1, "2026-06-17")], Reference).Checks.Single();

        Assert.Equal(3, weekly.DaysRemaining);
        Assert.Equal(3, monthly.DaysRemaining);
        Assert.Equal(CheckStatus.Ok, weekly.Status);
        Assert.Equal(CheckStatus.DueSoon, monthly.Status);
    }

    [Fact]
    public void The_latest_log_wins()
    {
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "check", 7)],
            [Log(1, "2026-06-01"), Log(1, "2026-07-13"), Log(1, "2026-06-20")],
            Reference);

        Assert.Equal(new DateOnly(2026, 7, 13), result.Checks.Single().LastPerformedOn);
        Assert.Equal(CheckStatus.Ok, result.Checks.Single().Status);
    }

    [Theory]
    [InlineData(CheckResult.Failed)]
    [InlineData(CheckResult.Attention)]
    public void A_bad_verdict_overrides_the_date_and_carries_it(CheckResult verdict)
    {
        // Done today, so by date this would be Ok (7 days left on a weekly cadence). The verdict overrides that:
        // a check flagged on its last log needs action whatever the cadence says — the whole reported bug.
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "Coolant colour", 7)],
            [Log(1, "2026-07-14", verdict)],
            Reference);

        var check = result.Checks.Single();
        Assert.Equal(CheckStatus.Attention, check.Status);
        Assert.Equal(verdict, check.Result);
        Assert.Equal(1, result.AttentionCount);
        Assert.Equal(0, result.OkCount);
        // The date math is still returned so the row can show the check is also in-interval.
        Assert.Equal(7, check.DaysRemaining);
    }

    [Fact]
    public void An_ok_verdict_leaves_the_date_status_alone()
    {
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "Coolant colour", 7)],
            [Log(1, "2026-07-14", CheckResult.OK)],
            Reference);

        var check = result.Checks.Single();
        Assert.Equal(CheckStatus.Ok, check.Status);
        Assert.Equal(CheckResult.OK, check.Result);
        Assert.Equal(0, result.AttentionCount);
    }

    [Fact]
    public void A_later_clean_log_clears_an_earlier_flag()
    {
        // Failed on the 10th, then re-checked OK on the 13th. Only the latest log counts, so it is no longer
        // flagged — re-logging is how you clear the attention state.
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "Coolant colour", 7)],
            [Log(1, "2026-07-10", CheckResult.Failed), Log(1, "2026-07-13", CheckResult.OK)],
            Reference);

        var check = result.Checks.Single();
        Assert.Equal(CheckStatus.Ok, check.Status);
        Assert.Equal(CheckResult.OK, check.Result);
        Assert.Equal(0, result.AttentionCount);
    }

    [Fact]
    public void On_the_same_day_the_last_entered_log_wins_the_verdict()
    {
        // Two logs the same day: Failed entered first (lower id), then OK. The id tiebreak picks the OK, so the
        // check clears — a correction entered after a mistake takes effect even without a later date.
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "Coolant colour", 7)],
            [Log(1, "2026-07-14", CheckResult.Failed, id: 1), Log(1, "2026-07-14", CheckResult.OK, id: 2)],
            Reference);

        Assert.Equal(CheckStatus.Ok, result.Checks.Single().Status);
        Assert.Equal(CheckResult.OK, result.Checks.Single().Result);
    }

    [Fact]
    public void Retired_checks_are_excluded_but_their_history_is_not_destroyed()
    {
        var result = CheckStatusCalculator.Calculate(
            [Definition(1, "active", 7), Definition(2, "retired", 7, isActive: false)],
            [Log(1, "2026-07-13"), Log(2, "2026-01-01")],
            Reference);

        Assert.Single(result.Checks);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("active", result.Checks.Single().Name);
    }

    [Fact]
    public void No_definitions_yields_an_empty_summary()
    {
        var result = CheckStatusCalculator.Calculate([], [], Reference);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Checks);
    }

    [Fact]
    public void Checks_come_back_in_display_order()
    {
        var result = CheckStatusCalculator.Calculate(
            [Definition(3, "third", 7), Definition(1, "first", 7), Definition(2, "second", 7)],
            [],
            Reference);

        Assert.Equal(["first", "second", "third"], result.Checks.Select(c => c.Name));
    }
}
