using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The head-gasket watch: an issue naming the regular checks that are its early warning.
/// </summary>
/// <remarks>
/// BT53's K-series head gasket is resolved off a compression test and a CO₂ sniff, and the weekly
/// oil-filler-cap and coolant-colour checks are what keep it resolved. These tests pin the one rule this adds —
/// what counts as a lapse — and, more importantly, pin that it adds no arithmetic: every status here comes from
/// <see cref="CheckStatusCalculator"/>, so the dashboard's named watch and the checks screen cannot disagree.
/// </remarks>
public sealed class WatchCalculatorTests
{
    private static readonly DateOnly Reference = new(2026, 7, 14);

    private const int OilFillerCap = 1;
    private const int CoolantColour = 2;
    private const int SpareTyre = 3;

    private static CheckDefinition Definition(int id, string name, int intervalDays = 7, bool isActive = true) =>
        new()
        {
            Id = id,
            VehicleId = 1,
            Name = name,
            CadenceLabel = "Weekly",
            IntervalDays = intervalDays,
            DisplayOrder = id,
            IsActive = isActive,
            Source = EntrySource.Import,
        };

    private static CheckLog Log(int definitionId, string date, CheckResult? result = null) =>
        new()
        {
            CheckDefinitionId = definitionId,
            PerformedOn = DateOnly.Parse(date),
            Result = result,
            Source = EntrySource.Import,
        };

    private static Issue HeadGasket(IssueStatus status = IssueStatus.Resolved, int id = 10) =>
        new()
        {
            Id = id,
            VehicleId = 1,
            Title = "Head gasket — K-series risk",
            Severity = Severity.Critical,
            FirstNoted = new DateOnly(2026, 3, 14),
            Status = status,
            Source = EntrySource.Import,
        };

    private static IReadOnlyCollection<CheckState> StatesFor(
        IReadOnlyCollection<CheckDefinition> definitions, IReadOnlyCollection<CheckLog> logs) =>
        CheckStatusCalculator.Calculate(definitions, logs, Reference).Checks;

    private static readonly IssueWatchCheck[] TheTwoWeeklyChecks =
    [
        new() { IssueId = 10, CheckDefinitionId = OilFillerCap },
        new() { IssueId = 10, CheckDefinitionId = CoolantColour },
    ];

    [Fact]
    public void Both_checks_current_means_the_watch_has_not_lapsed()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside"), Definition(CoolantColour, "Coolant reservoir colour") };
        // Two days ago on a weekly cadence: five days of cover left, outside the due-soon window.
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-07-12"), Log(CoolantColour, "2026-07-12")]);

        var watch = WatchCalculator.Calculate([HeadGasket()], TheTwoWeeklyChecks, states).Single();

        Assert.Equal("Head gasket — K-series risk", watch.IssueTitle);
        Assert.Equal(2, watch.TotalCheckCount);
        Assert.Equal(0, watch.LapsedCheckCount);
    }

    [Fact]
    public void One_overdue_check_lapses_one_of_two()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside"), Definition(CoolantColour, "Coolant reservoir colour") };
        // The design's scenario: last done 18 June, weekly cadence, 19 days ago.
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-06-18"), Log(CoolantColour, "2026-07-12")]);

        var watch = WatchCalculator.Calculate([HeadGasket()], TheTwoWeeklyChecks, states).Single();

        Assert.Equal(2, watch.TotalCheckCount);
        Assert.Equal(1, watch.LapsedCheckCount);
    }

    /// <summary>
    /// A never-done early-warning check is not reassurance.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the fourth check state itself: the workbook silently dropped its never-logged row
    /// out of the buckets and reported 17 of 18. A watch that treated NeverLogged as fine would report a healthy
    /// head-gasket watch that had never once been performed.
    /// </remarks>
    [Fact]
    public void A_never_logged_watched_check_counts_as_lapsed()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside"), Definition(CoolantColour, "Coolant reservoir colour") };
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-07-12")]);

        var watch = WatchCalculator.Calculate([HeadGasket()], TheTwoWeeklyChecks, states).Single();

        Assert.Equal(1, watch.LapsedCheckCount);
        Assert.True(WatchCalculator.IsLapsed(CheckStatus.NeverLogged));
    }

    /// <summary>A check logged in date but flagged Attention/Failed is the alarm going off, not silence.</summary>
    [Fact]
    public void A_watched_check_flagged_attention_counts_as_lapsed_even_though_it_is_in_date()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside"), Definition(CoolantColour, "Coolant reservoir colour") };
        var states = StatesFor(definitions,
            [Log(OilFillerCap, "2026-07-12", CheckResult.Failed), Log(CoolantColour, "2026-07-12")]);

        var watch = WatchCalculator.Calculate([HeadGasket()], TheTwoWeeklyChecks, states).Single();

        // Logged two days ago, so the date says it is fine; the verdict says mayonnaise on the filler cap.
        Assert.Equal(CheckStatus.Attention, states.Single(s => s.CheckDefinitionId == OilFillerCap).Status);
        Assert.Equal(1, watch.LapsedCheckCount);
    }

    [Fact]
    public void A_check_merely_due_soon_is_still_watching()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside") };
        // Six days into a seven-day cadence: due soon, not overdue.
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-07-08")]);

        Assert.Equal(CheckStatus.DueSoon, states.Single().Status);
        Assert.False(WatchCalculator.IsLapsed(CheckStatus.DueSoon));

        var watch = WatchCalculator.Calculate(
            [HeadGasket()], [new IssueWatchCheck { IssueId = 10, CheckDefinitionId = OilFillerCap }], states).Single();
        Assert.Equal(0, watch.LapsedCheckCount);
    }

    /// <summary>
    /// The status is not reopened, and this is the whole point of the design's wording.
    /// </summary>
    /// <remarks>
    /// "Resolved conditionally — the two weekly checks are what keep it that way." The watch surfaces the
    /// contingency; flipping the issue back to Monitoring would be the app overruling the owner, which is the
    /// same rule the anomaly lifecycle follows.
    /// </remarks>
    [Fact]
    public void A_lapsed_watch_reports_the_issues_own_status_unchanged()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside"), Definition(CoolantColour, "Coolant reservoir colour") };
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-06-18"), Log(CoolantColour, "2026-06-18")]);

        var watch = WatchCalculator.Calculate([HeadGasket(IssueStatus.Resolved)], TheTwoWeeklyChecks, states).Single();

        Assert.Equal(IssueStatus.Resolved, watch.IssueStatus);
        Assert.Equal(2, watch.LapsedCheckCount);
    }

    [Fact]
    public void An_issue_watching_nothing_is_absent_rather_than_present_with_zeroes()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside") };
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-07-12")]);

        Assert.Empty(WatchCalculator.Calculate([HeadGasket()], [], states));
    }

    /// <summary>
    /// A retired definition drops out of the watch rather than showing as unknown.
    /// </summary>
    /// <remarks>
    /// <c>CheckStatusCalculator</c> only evaluates active definitions, so a retired one has no state to report.
    /// A retired check genuinely stops watching anything — the honest reading is "watches fewer checks", not
    /// "watches one whose status we cannot determine".
    /// </remarks>
    [Fact]
    public void A_retired_watched_check_falls_out_of_the_contingency()
    {
        var definitions = new[]
        {
            Definition(OilFillerCap, "Oil filler cap underside"),
            Definition(CoolantColour, "Coolant reservoir colour", isActive: false),
        };
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-07-12"), Log(CoolantColour, "2026-06-18")]);

        var watch = WatchCalculator.Calculate([HeadGasket()], TheTwoWeeklyChecks, states).Single();

        Assert.Equal(1, watch.TotalCheckCount);
        Assert.Equal(0, watch.LapsedCheckCount);
    }

    [Fact]
    public void Watches_are_ranked_worst_first()
    {
        var definitions = new[]
        {
            Definition(OilFillerCap, "Oil filler cap underside"),
            Definition(CoolantColour, "Coolant reservoir colour"),
            Definition(SpareTyre, "Spare tyre pressure", intervalDays: 30),
        };
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-06-18"), Log(CoolantColour, "2026-06-18")]);

        var brakePipe = new Issue
        {
            Id = 11, VehicleId = 1, Title = "Brake pipe corrosion", Severity = Severity.Medium,
            FirstNoted = new DateOnly(2026, 4, 1), Status = IssueStatus.Monitoring, Source = EntrySource.Import,
        };

        var watches = WatchCalculator.Calculate(
            [brakePipe, HeadGasket()],
            [.. TheTwoWeeklyChecks, new IssueWatchCheck { IssueId = 11, CheckDefinitionId = SpareTyre }],
            states);

        // Head gasket has two lapsed against the brake pipe's one, so it leads regardless of input order.
        Assert.Equal("Head gasket — K-series risk", watches[0].IssueTitle);
        Assert.Equal(2, watches[0].LapsedCheckCount);
        Assert.Equal("Brake pipe corrosion", watches[1].IssueTitle);
    }

    [Fact]
    public void The_per_issue_check_list_carries_each_status_and_its_lapse_flag()
    {
        var definitions = new[] { Definition(OilFillerCap, "Oil filler cap underside"), Definition(CoolantColour, "Coolant reservoir colour") };
        var states = StatesFor(definitions, [Log(OilFillerCap, "2026-06-18"), Log(CoolantColour, "2026-07-12")]);

        var checks = WatchCalculator.ChecksFor(10, TheTwoWeeklyChecks, states);

        var lapsed = checks.Single(c => c.IsLapsed);
        Assert.Equal("Oil filler cap underside", lapsed.Name);
        Assert.Equal(CheckStatus.Overdue, lapsed.Status);
        // The days figure comes straight off the check state — the screen shows "19 days overdue" without
        // recomputing it from the log date.
        Assert.Equal(-19, lapsed.DaysRemaining);

        var healthy = checks.Single(c => !c.IsLapsed);
        Assert.Equal("Coolant reservoir colour", healthy.Name);
    }
}
