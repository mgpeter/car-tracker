using CarTracker.Data;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Per-fill and fleet fuel economy. UK (imperial) MPG throughout.
/// </summary>
public static class FuelEconomyCalculator
{
    public static FuelEconomySummary Calculate(IReadOnlyCollection<FuelEntry> fills)
    {
        if (fills.Count == 0)
        {
            return new FuelEconomySummary(
                null, null, null, 0m, 0m, null, null, FillCount: 0, ReliableIntervalCount: 0, Entries: []);
        }

        var ordered = fills
            .OrderBy(f => f.EntryDate)
            .ThenBy(f => f.Mileage)
            .ToList();

        var entries = new List<FuelEntryMetrics>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            entries.Add(Measure(ordered[i], i == 0 ? null : ordered[i - 1]));
        }

        // Reliable intervals only. A partial fill still produces arithmetic — a 5 L splash after 300 miles
        // computes to ~273 mpg — and including it would put a fabricated figure on the Dashboard as "best".
        var reliableMpg = entries
            .Where(e => e is { IsReliable: true, Mpg: not null })
            .Select(e => e.Mpg!.Value)
            .ToList();

        var totalLitres = ordered.Sum(f => f.Litres);
        var totalCost = ordered.Sum(f => f.TotalCost);

        return new FuelEconomySummary(
            // Averaged unrounded; rounding each interval first gives a different answer.
            AverageMpg: reliableMpg.Count > 0 ? reliableMpg.Average() : null,
            BestMpg: reliableMpg.Count > 0 ? reliableMpg.Max() : null,
            WorstMpg: reliableMpg.Count > 0 ? reliableMpg.Min() : null,
            TotalLitres: totalLitres,
            TotalCost: totalCost,
            // Volume-weighted: a 50 L fill at £1.40 and a 10 L fill at £1.60 cost £1.433/L, not £1.50. Uses
            // the receipt totals, which are authoritative — forecourt rounding makes litres x price differ.
            AveragePricePerLitre: totalLitres > 0 ? totalCost / totalLitres : null,
            LastFillDate: ordered[^1].EntryDate,
            FillCount: ordered.Count,
            ReliableIntervalCount: reliableMpg.Count,
            Entries: entries);
    }

    private static FuelEntryMetrics Measure(FuelEntry current, FuelEntry? previous)
    {
        if (previous is null)
        {
            return Unmeasurable(current, null, MpgUnreliableReason.NoPreviousFill);
        }

        var milesSinceLast = current.Mileage - previous.Mileage;

        if (milesSinceLast <= 0)
        {
            // Never divide by this, and never report a negative MPG as economy.
            return Unmeasurable(current, milesSinceLast, MpgUnreliableReason.NonMonotonicMileage);
        }

        var miles = (decimal)milesSinceLast;
        var mpg = miles * Units.LitresPerImperialGallon / current.Litres;
        var litresPer100Km = current.Litres * 100m / (miles * Units.KmPerMile);

        // Only full-to-full measures anything: the litres added equal the fuel consumed since the last fill
        // exactly when the tank was full at both ends.
        var isReliable = previous.FillLevel == FillLevel.Full && current.FillLevel == FillLevel.Full;

        return new FuelEntryMetrics(
            FuelEntryId: current.Id,
            EntryDate: current.EntryDate,
            Mileage: current.Mileage,
            Litres: current.Litres,
            TotalCost: current.TotalCost,
            MilesSinceLast: milesSinceLast,
            Mpg: mpg,
            LitresPer100Km: litresPer100Km,
            IsReliable: isReliable,
            UnreliableReason: isReliable ? null : MpgUnreliableReason.PartialFill);
    }

    private static FuelEntryMetrics Unmeasurable(FuelEntry current, int? milesSinceLast, MpgUnreliableReason reason) =>
        new(
            FuelEntryId: current.Id,
            EntryDate: current.EntryDate,
            Mileage: current.Mileage,
            Litres: current.Litres,
            TotalCost: current.TotalCost,
            MilesSinceLast: milesSinceLast,
            Mpg: null,
            LitresPer100Km: null,
            IsReliable: false,
            UnreliableReason: reason);
}
