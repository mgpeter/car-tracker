using CarTracker.Shared.Metrics;

namespace CarTracker.Domain;

/// <summary>
/// Loads, then computes. Holds no logic of its own — every figure comes from <see cref="DerivedMetrics"/>.
/// </summary>
public sealed class DerivedMetricsService(IVehicleMetricsLoader loader, Clock clock) : IDerivedMetricsService
{
    public async Task<VehicleSummary?> GetVehicleSummaryAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var data = await loader.LoadAsync(vehicleId, cancellationToken);

        // Null, not an empty summary: "this vehicle does not exist" and "this vehicle has no data" are
        // different answers, and the caller needs to tell them apart.
        return data is null ? null : DerivedMetrics.Compute(data, clock.Today());
    }

    public async Task<BudgetSummary?> GetBudgetSummaryAsync(
        int vehicleId,
        BudgetPeriod period = BudgetPeriod.CalendarYear,
        CancellationToken cancellationToken = default)
    {
        var data = await loader.LoadAsync(vehicleId, cancellationToken);

        return data is null ? null : DerivedMetrics.ComputeBudget(data, period, clock.Today());
    }
}
