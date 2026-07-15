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

    /// <summary>
    /// Every vehicle, as the garage shows it.
    /// </summary>
    /// <remarks>
    /// The one method here that is not keyed by vehicle id, because it is the screen you are on *before*
    /// choosing a vehicle. Each item is a projection of that vehicle's <see cref="VehicleSummary"/> — never a
    /// second computation, so a card cannot disagree with the dashboard it links to.
    /// </remarks>
    Task<IReadOnlyList<GarageItem>> GetGarageAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads a vehicle's inputs. Separated from the calculators so the computation stays a pure function.
/// </summary>
/// <remarks>
/// Every read the service needs goes through here, including the two the garage added. Reaching past it into
/// the DbContext would give the service a second data path and leave a test's fake loader covering only one
/// of them — the seam would be there in name and not in fact.
/// </remarks>
public interface IVehicleMetricsLoader
{
    Task<VehicleMetricsData?> LoadAsync(int vehicleId, CancellationToken cancellationToken = default);

    /// <summary>Every vehicle id, default first, then in the order they were added.</summary>
    Task<IReadOnlyList<int>> ListVehicleIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Open anomalies per vehicle, for the garage's integrity pill.
    /// </summary>
    /// <remarks>
    /// Open only. An Accepted or Dismissed flag has been decided, and "needs attention" is about what has not.
    /// Anomalies are not derived — they are records with a lifecycle — which is why they arrive alongside the
    /// computed summary rather than inside it.
    /// </remarks>
    Task<IReadOnlyDictionary<int, int>> CountOpenAnomaliesAsync(CancellationToken cancellationToken = default);
}
