using CarTracker.Shared.Metrics;

namespace CarTracker.Domain;

/// <summary>
/// The single public entry point for every derived figure (README §4).
/// </summary>
/// <remarks>
/// Both the web API and the MCP server call this. Neither computes anything itself — that is the whole point:
/// "what's my MPG" and the Dashboard cannot disagree, because there is one implementation.
/// </remarks>
public interface IDerivedMetricsService
{
    /// <summary>Everything the Dashboard shows, for one vehicle, as of now.</summary>
    Task<VehicleSummary?> GetVehicleSummaryAsync(int vehicleId, CancellationToken cancellationToken = default);

    Task<BudgetSummary?> GetBudgetSummaryAsync(
        int vehicleId,
        BudgetPeriod period = BudgetPeriod.CalendarYear,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads a vehicle's inputs. Separated from the calculators so the computation stays a pure function.
/// </summary>
public interface IVehicleMetricsLoader
{
    Task<VehicleMetricsData?> LoadAsync(int vehicleId, CancellationToken cancellationToken = default);
}
