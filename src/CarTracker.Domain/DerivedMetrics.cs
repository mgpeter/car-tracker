using CarTracker.Data;
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
        var fuel = FuelEconomyCalculator.Calculate(data.FuelEntries);

        return new VehicleSummary(
            VehicleId: data.Vehicle.Id,
            Registration: data.Vehicle.Registration,
            Name: $"{data.Vehicle.Make} {data.Vehicle.Model}".Trim(),
            AsOfDate: referenceDate,
            Identity: IdentityOf(data.Vehicle, mileage, referenceDate),
            Mileage: mileage,
            Renewals: RenewalCalculator.Calculate(data.Vehicle, data.ServiceRecords, referenceDate, mileage.CurrentMileage),
            Spend: SpendCalculator.Calculate(data.ExpenseEntries, data.Vehicle.PurchaseDate, referenceDate, mileage.MilesSincePurchase),
            Fuel: fuel,
            Checks: CheckStatusCalculator.Calculate(data.CheckDefinitions, data.CheckLogs, referenceDate),
            Integrity: IntegrityOf(data.OpenAnomalies),
            FullTankRangeMiles: FullTankRange(data.Vehicle.Fluids.FuelTankCapacityLitres, fuel.AverageMpg));
    }

    /// <remarks>
    /// Full-tank range, not "remaining": tank level is not tracked (the fuel-basis spec made FillLevel
    /// descriptive), so remaining is unknowable and would be a guess. This is average MPG x the tank in imperial
    /// gallons. Null unless both a real capacity and a measurable average MPG exist — "0 range" is a claim the
    /// data does not support, exactly like a null MilesPerDay on the day of purchase.
    /// </remarks>
    private static decimal? FullTankRange(decimal? tankCapacityLitres, decimal? averageMpg) =>
        tankCapacityLitres is { } litres && averageMpg is { } mpg
            ? mpg * litres / Units.LitresPerImperialGallon
            : null;

    /// <remarks>
    /// The worst severity is Error &lt; Warning &lt; Info by enum value, so <c>Min</c> is the most severe.
    /// Null severity when there are no open flags — a headline of "0 flags, and nothing" rather than "0 flags,
    /// severity Info".
    /// </remarks>
    private static IntegritySummary IntegrityOf(IReadOnlyCollection<DataAnomaly> open) =>
        new(
            OpenCount: open.Count,
            HighestSeverity: open.Count == 0 ? null : open.Min(a => a.Severity));

    /// <remarks>
    /// Days owned and miles per day are computed here rather than stored, for the same reason as every other
    /// figure on the summary: a stored "owned 122 days" is wrong by morning.
    /// </remarks>
    private static VehicleIdentity IdentityOf(Vehicle vehicle, MileageResult mileage, DateOnly referenceDate)
    {
        // Never negative. A purchase date in the future is a typo, and "owned -6 days" renders that typo as
        // though it were a fact about the car.
        var daysOwned = Math.Max(0, referenceDate.DayNumber - vehicle.PurchaseDate.DayNumber);

        // Null rather than a division by zero on the day of purchase. Also null when there are no readings:
        // "0.0 mi/day" is a claim about usage, and we would be making it up.
        var milesPerDay = daysOwned > 0 && mileage.MilesSincePurchase is { } miles
            ? Math.Round((decimal)miles / daysOwned, 1)
            : (decimal?)null;

        return new VehicleIdentity(
            Variant: vehicle.Variant,
            Year: vehicle.Year,
            Colour: vehicle.Colour,
            Drivetrain: vehicle.Drivetrain,
            Transmission: vehicle.Transmission,
            EngineCode: vehicle.EngineCode,
            PurchaseDate: vehicle.PurchaseDate,
            DaysOwned: daysOwned,
            MilesPerDay: milesPerDay,
            DefaultGarage: vehicle.DefaultGarage);
    }

    public static BudgetSummary ComputeBudget(VehicleMetricsData data, BudgetPeriod period, DateOnly referenceDate) =>
        BudgetCalculator.Calculate(
            data.BudgetCategories, data.ExpenseEntries, period, data.Vehicle.PurchaseDate, referenceDate);
}
