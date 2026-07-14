namespace CarTracker.Shared.Metrics;

/// <summary>
/// The Dashboard's headline (README §3.1), and what <c>get_vehicle_summary</c> returns over MCP.
/// </summary>
/// <remarks>
/// One type serving both surfaces is the point of README §4: the web UI and the assistant cannot disagree
/// about a figure because they are reading the same object, computed once.
/// </remarks>
public sealed record VehicleSummary(
    int VehicleId,
    string Registration,
    string Name,
    DateOnly AsOfDate,
    MileageResult Mileage,
    RenewalSummary Renewals,
    SpendSummary Spend,
    FuelEconomySummary Fuel,
    CheckStatusSummary Checks);
