using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;

namespace CarTracker.Domain;

/// <summary>
/// Finds data problems worth flagging, per README §5.3: validate and flag, never silently accept.
/// </summary>
/// <remarks>
/// <para>
/// Pure — data and existing anomalies in, anomalies to raise out. Persisting them is the caller's job, which
/// keeps this testable against the workbook fixture without a database.
/// </para>
/// <para>
/// <b>No production caller yet.</b> Phase 2's write paths (fuel quick-add, mileage entry) are where this gets
/// invoked, and Phase 4's MCP write tools after that. It is proven against the real history today.
/// </para>
/// </remarks>
public static class AnomalyDetector
{
    /// <summary>Forecourt rounding is about a penny; more than 2p suggests a transcription error.</summary>
    private const decimal FuelCostToleranceGbp = 0.02m;

    /// <param name="existing">
    /// Anomalies already recorded. Only the <see cref="AnomalyStatus.Open"/> ones suppress a re-raise: once
    /// something is Corrected, Accepted or Dismissed, a fresh occurrence is news again.
    /// </param>
    public static IReadOnlyList<DataAnomaly> Detect(
        VehicleMetricsData data,
        IReadOnlyCollection<DataAnomaly> existing)
    {
        var open = existing
            .Where(a => a.Status == AnomalyStatus.Open)
            .Select(a => (a.Kind, a.EntityType, a.EntityId))
            .ToHashSet();

        var found = new List<DataAnomaly>();

        found.AddRange(DetectNonMonotonicMileage(data));
        found.AddRange(DetectFuelCostDiscrepancies(data));
        found.AddRange(DetectImplausibleMpg(data));

        // Idempotent: a caller that runs twice must not bury the integrity screen in duplicates.
        return found
            .Where(a => !open.Contains((a.Kind, a.EntityType, a.EntityId)))
            .ToList();
    }

    /// <summary>
    /// A reading that exceeds the current mileage — the workbook's 83,000 mi service row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The outlier is the reading above current, not the readings below it.</b> A first attempt walked the
    /// history forward flagging anything below the highest seen so far — which flagged the 83,000 row's
    /// innocent successors (80,705 and 80,712) and left the culprit unmarked. One bad row would have flooded
    /// the screen with three flags and named the wrong rows.
    /// </para>
    /// <para>
    /// Reuses <see cref="MileageCalculator"/> rather than re-deriving "which reading is current". Two
    /// implementations of that question would be two chances to disagree, and it is the same rule the
    /// Dashboard shows.
    /// </para>
    /// </remarks>
    private static IEnumerable<DataAnomaly> DetectNonMonotonicMileage(VehicleMetricsData data)
    {
        var mileage = MileageCalculator.Calculate(data.MileageReadings, data.Vehicle.PurchaseMileage);

        if (!mileage.HasNonMonotonicHistory || mileage.CurrentMileage is null)
        {
            yield break;
        }

        var current = mileage.CurrentMileage.Value;

        foreach (var reading in data.MileageReadings.Where(m => m.Mileage > current))
        {
            yield return New(
                data, AnomalyKind.MileageNonMonotonic, AnomalySeverity.Error,
                nameof(MileageReading), reading.Id,
                $"Reading of {reading.Mileage:N0} mi on {reading.ReadingDate:d MMM yyyy} is above the current " +
                $"{current:N0} mi from {mileage.AsOfDate:d MMM yyyy}. An odometer only advances, so this " +
                "reading cannot be right.",
                $$"""{"mileage":{{reading.Mileage}},"currentMileage":{{current}}}""");
        }
    }

    /// <summary>
    /// The receipt total disagreeing with litres x price.
    /// </summary>
    /// <remarks>
    /// Both are stored deliberately — the receipt is authoritative and forecourt rounding makes the product
    /// differ by a penny. No CHECK enforces agreement, because that would reject legitimate receipts; this
    /// flags a gap too large to be rounding.
    /// </remarks>
    private static IEnumerable<DataAnomaly> DetectFuelCostDiscrepancies(VehicleMetricsData data)
    {
        foreach (var fill in data.FuelEntries)
        {
            var computed = fill.Litres * fill.PricePerLitre;
            var difference = Math.Abs(fill.TotalCost - computed);

            if (difference > FuelCostToleranceGbp)
            {
                yield return New(
                    data, AnomalyKind.FuelCostDiscrepancy, AnomalySeverity.Warning,
                    nameof(FuelEntry), fill.Id,
                    $"Receipt total £{fill.TotalCost:N2} differs from {fill.Litres:N2} L x " +
                    $"£{fill.PricePerLitre:N3} = £{computed:N2} by £{difference:N2}, beyond forecourt rounding.",
                    $$"""{"totalCost":{{fill.TotalCost}},"computed":{{Math.Round(computed, 2)}}}""");
            }
        }
    }

    /// <summary>
    /// An MPG outside the physical band.
    /// </summary>
    /// <remarks>
    /// Reuses <c>FuelEntryMetrics.IsPlausible</c> rather than re-deriving it. Two implementations of "is this
    /// figure believable" would be two chances to disagree — the defect class this project exists to prevent.
    /// </remarks>
    private static IEnumerable<DataAnomaly> DetectImplausibleMpg(VehicleMetricsData data)
    {
        var economy = FuelEconomyCalculator.Calculate(data.FuelEntries);

        foreach (var entry in economy.Entries.Where(e => e is { Mpg: not null, IsPlausible: false }))
        {
            yield return New(
                data, AnomalyKind.ImplausibleMpg, AnomalySeverity.Warning,
                nameof(FuelEntry), entry.FuelEntryId,
                $"Computed {entry.Mpg:N1} mpg over {entry.MilesSinceLast:N0} miles on {entry.Litres:N2} L is " +
                $"outside {FuelEconomyCalculator.MinPlausibleMpg}-{FuelEconomyCalculator.MaxPlausibleMpg} mpg. " +
                "Usually a partial fill or a mistyped odometer.",
                $$"""{"mpg":{{Math.Round(entry.Mpg!.Value, 2)}},"miles":{{entry.MilesSinceLast}}}""");
        }
    }

    private static DataAnomaly New(
        VehicleMetricsData data,
        AnomalyKind kind,
        AnomalySeverity severity,
        string entityType,
        int? entityId,
        string message,
        string? detail) =>
        new()
        {
            VehicleId = data.Vehicle.Id,
            Kind = kind,
            Severity = severity,
            EntityType = entityType,
            EntityId = entityId,
            Message = message,
            Detail = detail,
            Status = AnomalyStatus.Open,
            Source = EntrySource.Web,
        };
}
