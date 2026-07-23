using CarTracker.Domain;
using CarTracker.Domain.Reminders;
using CarTracker.Domain.Tests.Workbook;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class ReminderDispatcherTests
{
    /// <summary>Serves fixed summaries by id, standing in for the derived-metrics service.</summary>
    private sealed class StubMetrics(IReadOnlyDictionary<int, VehicleSummary> summaries) : IDerivedMetricsService
    {
        public Task<VehicleSummary?> GetVehicleSummaryAsync(int vehicleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(summaries.GetValueOrDefault(vehicleId));

        public Task<BudgetSummary?> GetBudgetSummaryAsync(int vehicleId, BudgetPeriod period = BudgetPeriod.CalendarYear, CancellationToken cancellationToken = default) =>
            Task.FromResult<BudgetSummary?>(null);

        public Task<IReadOnlyList<GarageItem>> GetGarageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GarageItem>>([]);
    }

    private sealed class StubLoader(IReadOnlyList<int> ids) : IVehicleMetricsLoader
    {
        public Task<VehicleMetricsData?> LoadAsync(int vehicleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<VehicleMetricsData?>(null);

        public Task<IReadOnlyList<int>> ListVehicleIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ids);

        public Task<IReadOnlyDictionary<int, int>> CountOpenAnomaliesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>());
    }

    private sealed class RecordingChannel(string name, bool enabled = true) : INotificationChannel
    {
        public string Name => name;
        public bool Enabled => enabled;
        public List<(int VehicleId, int FiredCount)> Received { get; } = [];

        public Task NotifyAsync(int vehicleId, IReadOnlyList<ReminderItem> fired, CancellationToken cancellationToken = default)
        {
            Received.Add((vehicleId, fired.Count));
            return Task.CompletedTask;
        }
    }

    private static VehicleSummary WithOverdueChecks(int vehicleId, int overdue)
    {
        var checks = Enumerable.Range(1, overdue)
            .Select(i => new CheckState(i, $"Check {i}", "cadence", 30, null, null, -5, CheckStatus.Overdue, null))
            .ToList();
        return DerivedMetrics.Compute(WorkbookFixture.Data(), WorkbookFixture.ReferenceDate) with
        {
            Checks = new CheckStatusSummary(0, 0, overdue, 0, 0, checks),
        };
    }

    [Fact]
    public async Task A_pass_evaluates_every_vehicle_and_reaches_every_enabled_channel()
    {
        var summaries = new Dictionary<int, VehicleSummary>
        {
            [1] = WithOverdueChecks(1, 3),
            [2] = WithOverdueChecks(2, 1),
        };
        var badge = new RecordingChannel("in-app");
        var stub = new RecordingChannel("stub-second"); // the throwaway second adapter the spec calls for
        var dispatcher = new ReminderDispatcher(new StubLoader([1, 2]), new StubMetrics(summaries), [badge, stub]);

        var totalFired = await dispatcher.RunOnceAsync();

        // 3 + 1 firing reminders dispatched across the fleet.
        Assert.Equal(4, totalFired);
        // Both channels received both vehicles' fired items, unchanged — the adapter seam works with no change
        // to the trigger logic.
        Assert.Equal([(1, 3), (2, 1)], badge.Received);
        Assert.Equal([(1, 3), (2, 1)], stub.Received);
    }

    [Fact]
    public async Task A_disabled_channel_is_skipped()
    {
        var badge = new RecordingChannel("in-app");
        var off = new RecordingChannel("email", enabled: false);
        var dispatcher = new ReminderDispatcher(
            new StubLoader([1]),
            new StubMetrics(new Dictionary<int, VehicleSummary> { [1] = WithOverdueChecks(1, 2) }),
            [badge, off]);

        await dispatcher.RunOnceAsync();

        Assert.Single(badge.Received);
        Assert.Empty(off.Received);
    }

    [Fact]
    public async Task A_vehicle_with_nothing_firing_dispatches_nothing()
    {
        var clean = DerivedMetrics.Compute(WorkbookFixture.Data(), WorkbookFixture.ReferenceDate) with
        {
            Renewals = new RenewalSummary(
                new Renewal("MOT", null, 359, RenewalUrgency.Ok, null),
                new Renewal("Insurance", null, 244, RenewalUrgency.Ok, null),
                new Renewal("Road tax", null, 229, RenewalUrgency.Ok, null),
                new Renewal("Next service", null, 302, RenewalUrgency.Ok, null),
                null),
            Checks = new CheckStatusSummary(3, 0, 0, 1, 0, []),
        };
        var badge = new RecordingChannel("in-app");
        var dispatcher = new ReminderDispatcher(
            new StubLoader([1]),
            new StubMetrics(new Dictionary<int, VehicleSummary> { [1] = clean }),
            [badge]);

        var total = await dispatcher.RunOnceAsync();

        Assert.Equal(0, total);
        Assert.Empty(badge.Received); // never called with an empty list
    }
}
