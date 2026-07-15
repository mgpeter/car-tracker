using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Per-fill and fleet fuel economy. UK (imperial) MPG throughout.
/// </summary>
/// <remarks>
/// Every figure rests on litres and miles — both recorded exactly. <c>FuelEntry.FillLevel</c> is descriptive
/// and is deliberately not read here; <see cref="NoFillLevelInCalculationsTests"/> enforces that.
/// </remarks>
public static class FuelEconomyCalculator
{
    /// <summary>
    /// Below this, an interval implies a fuel leak or a missed odometer entry rather than economy.
    /// </summary>
    /// <remarks>
    /// Bounds are for a petrol K-series Freelander, whose real intervals run 25.4-32.2 mpg. Wide enough that
    /// tripping one is a genuine anomaly rather than a nag. A per-vehicle band belongs with the second vehicle.
    /// </remarks>
    public const decimal MinPlausibleMpg = 10m;

    /// <summary>Above this, an interval implies a partial fill or a mistyped mileage.</summary>
    public const decimal MaxPlausibleMpg = 70m;

    public static FuelEconomySummary Calculate(IReadOnlyCollection<FuelEntry> fills)
    {
        if (fills.Count == 0)
        {
            return new FuelEconomySummary(
                null, null, null, null, 0m, 0m, null, null,
                FillCount: 0, MeasuredIntervalCount: 0, ImplausibleCount: 0, Entries: []);
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

        // Plausible figures only. A 272 mpg splash is arithmetically correct and physically meaningless;
        // unguarded it becomes "best MPG" and sits on the Dashboard as good news.
        var usable = entries
            .Where(e => e is { Mpg: not null, IsPlausible: true })
            .Select(e => e.Mpg!.Value)
            .ToList();

        var totalLitres = ordered.Sum(f => f.Litres);
        var totalCost = ordered.Sum(f => f.TotalCost);

        return new FuelEconomySummary(
            AverageMpg: CumulativeMpg(ordered),
            PerFillAverageMpg: usable.Count > 0 ? usable.Average() : null,
            BestMpg: usable.Count > 0 ? usable.Max() : null,
            WorstMpg: usable.Count > 0 ? usable.Min() : null,
            TotalLitres: totalLitres,
            TotalCost: totalCost,
            AveragePricePerLitre: totalLitres > 0 ? totalCost / totalLitres : null,
            LastFillDate: ordered[^1].EntryDate,
            FillCount: ordered.Count,
            MeasuredIntervalCount: entries.Count(e => e.Mpg is not null),
            ImplausibleCount: entries.Count(e => e is { Mpg: not null, IsPlausible: false }),
            Entries: entries);
    }

    /// <summary>
    /// Total distance across the span, over the litres burned within it.
    /// </summary>
    /// <remarks>
    /// The strongest reading of "based on actual litres": every litre pumped, over every mile driven. Needs
    /// nothing about tank level, because the error is one tank's variation spread across the whole span.
    ///
    /// The first fill's litres are excluded: that fuel filled a tank already burned before recording began.
    /// What was burned *across* the span is what was pumped after the opening fill.
    /// </remarks>
    private static decimal? CumulativeMpg(List<FuelEntry> ordered)
    {
        if (ordered.Count < 2)
        {
            return null;
        }

        var span = ordered[^1].Mileage - ordered[0].Mileage;
        var litresBurned = ordered.Skip(1).Sum(f => f.Litres);

        if (span <= 0 || litresBurned <= 0)
        {
            return null;
        }

        return span * Units.LitresPerImperialGallon / litresBurned;
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
            return Unmeasurable(current, milesSinceLast, MpgUnreliableReason.NonMonotonicMileage);
        }

        var miles = (decimal)milesSinceLast;
        var mpg = miles * Units.LitresPerImperialGallon / current.Litres;
        var litresPer100Km = current.Litres * 100m / (miles * Units.KmPerMile);

        return new FuelEntryMetrics(
            FuelEntryId: current.Id,
            EntryDate: current.EntryDate,
            Mileage: current.Mileage,
            Litres: current.Litres,
            PricePerLitre: current.PricePerLitre,
            TotalCost: current.TotalCost,
            Station: current.Station,
            FillLevel: current.FillLevel,
            Notes: current.Notes,
            MilesSinceLast: milesSinceLast,
            Mpg: mpg,
            LitresPer100Km: litresPer100Km,
            IsReliable: true,
            // Judged on the number's own physics — which also catches a mistyped odometer with a full tank at
            // both ends, the case the old fill-level gate waved straight through.
            IsPlausible: mpg >= MinPlausibleMpg && mpg <= MaxPlausibleMpg,
            UnreliableReason: null);
    }

    private static FuelEntryMetrics Unmeasurable(FuelEntry current, int? milesSinceLast, MpgUnreliableReason reason) =>
        new(
            FuelEntryId: current.Id,
            EntryDate: current.EntryDate,
            Mileage: current.Mileage,
            Litres: current.Litres,
            PricePerLitre: current.PricePerLitre,
            TotalCost: current.TotalCost,
            Station: current.Station,
            FillLevel: current.FillLevel,
            Notes: current.Notes,
            MilesSinceLast: milesSinceLast,
            Mpg: null,
            LitresPer100Km: null,
            IsReliable: false,
            // No figure, so nothing to judge. Not "implausible" — absent.
            IsPlausible: true,
            UnreliableReason: reason);
}
