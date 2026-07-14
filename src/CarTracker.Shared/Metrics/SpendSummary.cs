namespace CarTracker.Shared.Metrics;

/// <summary>Dashboard spend groups per README §3.1.</summary>
public enum SpendGroup
{
    Fuel = 1,
    ServiceAndRepairs = 2,
    Statutory = 3,
}

/// <param name="CostPerMile">Null when the vehicle has covered no miles — not zero, and not infinity.</param>
/// <param name="TotalSincePurchaseExcludingPurchase">
/// The purchase price is a real cost and belongs in the total, but "what does it cost me to run" is the more
/// useful question. Both are legitimate; expose both rather than choosing for the reader.
/// </param>
public sealed record SpendSummary(
    decimal FuelYtd,
    decimal ServiceAndRepairsYtd,
    decimal StatutoryYtd,
    decimal TotalYtd,
    decimal TotalSincePurchase,
    decimal TotalSincePurchaseExcludingPurchase,
    decimal? MonthlyAverage,
    decimal? CostPerMile,
    decimal? CostPerMileExcludingPurchase,
    IReadOnlyDictionary<string, decimal> YtdByCategory);
