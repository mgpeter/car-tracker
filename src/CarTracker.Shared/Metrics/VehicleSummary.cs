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
    VehicleIdentity Identity,
    MileageResult Mileage,
    RenewalSummary Renewals,
    SpendSummary Spend,
    FuelEconomySummary Fuel,
    CheckStatusSummary Checks,
    IntegritySummary Integrity,
    /// <summary>
    /// Estimated distance on a full tank — average MPG x tank capacity — derived, never stored. Null when the
    /// tank capacity is unrecorded or the average MPG is unknown (one fill, or none): a full-tank estimate the
    /// data can support, not a live "remaining" gauge, and no guess when it cannot be given honestly.
    /// </summary>
    decimal? FullTankRangeMiles);
