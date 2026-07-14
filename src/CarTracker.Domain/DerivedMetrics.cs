using CarTracker.Domain.Calculators;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain;

/// <summary>
/// Composes the calculators into the one summary README §4 requires.
/// </summary>
/// <remarks>
/// Pure and static: data and a reference date in, summary out. The web API and the MCP server both reach this
/// through <see cref="IDerivedMetricsService"/>, so a metric cannot disagree with itself across surfaces —
/// there is only one place it is computed.
/// </remarks>
public static class DerivedMetrics
{
    public static VehicleSummary Compute(VehicleMetricsData data, DateOnly referenceDate)
    {
        var mileage = MileageCalculator.Calculate(data.MileageReadings, data.Vehicle.PurchaseMileage);

        return new VehicleSummary(
            VehicleId: data.Vehicle.Id,
            Registration: data.Vehicle.Registration,
            Name: $"{data.Vehicle.Make} {data.Vehicle.Model}".Trim(),
            AsOfDate: referenceDate,
            Mileage: mileage,
            Renewals: RenewalCalculator.Calculate(data.Vehicle, data.ServiceRecords, referenceDate, mileage.CurrentMileage),
            Spend: SpendCalculator.Calculate(data.ExpenseEntries, data.Vehicle.PurchaseDate, referenceDate, mileage.MilesSincePurchase),
            Fuel: FuelEconomyCalculator.Calculate(data.FuelEntries),
            Checks: CheckStatusCalculator.Calculate(data.CheckDefinitions, data.CheckLogs, referenceDate));
    }

    public static BudgetSummary ComputeBudget(VehicleMetricsData data, BudgetPeriod period, DateOnly referenceDate) =>
        BudgetCalculator.Calculate(
            data.BudgetCategories, data.ExpenseEntries, period, data.Vehicle.PurchaseDate, referenceDate);
}
