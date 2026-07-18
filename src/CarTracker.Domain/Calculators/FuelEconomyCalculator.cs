using CarTracker.Data;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Per-fill and fleet fuel economy. UK (imperial) MPG throughout.
/// </summary>
/// <remarks>
/// Every figure rests on litres and miles — both recorded exactly. <c>FuelEntry.FillLevel</c> is read only as a
/// hard binary through <see cref="ClosesTank"/>: Full or unrecorded closes the tank and measures the open
/// segment; Half/Quarter defer the figure to the next fill to full, accumulating their litres into it. Nothing
/// reads how partial a partial is.
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

    /// <summary>
    /// The only question the arithmetic asks of <see cref="FillLevel"/>. Full or unrecorded (null) brings the
    /// tank back to a known level and closes the open segment; Half/Quarter leave it open. Null is closing on
    /// purpose: it reproduces every historical figure (the workbook fills are all Full) and matches the
    /// add-fill sheet's default-to-Full, where an untouched field asserts the normal case.
    /// </summary>
    private static bool ClosesTank(FuelEntry f) => f.FillLevel is null or FillLevel.Full;

    public static FuelEconomySummary Calculate(IReadOnlyCollection<FuelEntry> fills)
    {
        if (fills.Count == 0)
        {
            return new FuelEconomySummary(
                null, null, null, null, 0m, 0m, null, null,
                FillCount: 0, MeasuredIntervalCount: 0, ImplausibleCount: 0, Entries: [],
                PendingFillCount: 0, PendingLitres: 0m, PendingMiles: null);
        }

        var ordered = fills
            .OrderBy(f => f.EntryDate)
            .ThenBy(f => f.Mileage)
            .ToList();

        var entries = new List<FuelEntryMetrics>(ordered.Count);

        // The open segment: the last closing fill and the litres/fills piled up since it. A partial defers its
        // figure and keeps its litres here; the next closing fill measures the whole span at once. On all-full
        // history every segment is exactly one fill, so this reduces byte-for-byte to the old pairwise measure.
        int? anchorMileage = null;
        var segmentLitres = 0m;
        var segmentFills = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var f = ordered[i];
            int? milesSinceLast = i == 0 ? null : f.Mileage - ordered[i - 1].Mileage;

            if (anchorMileage is null)
            {
                // No closing baseline yet — nothing to measure this fill against.
                entries.Add(Unmeasurable(f, milesSinceLast, MpgUnreliableReason.NoPreviousFill));
                if (ClosesTank(f))
                {
                    anchorMileage = f.Mileage;
                    segmentLitres = 0m;
                    segmentFills = 0;
                }

                continue;
            }

            segmentLitres += f.Litres;
            segmentFills += 1;

            if (!ClosesTank(f))
            {
                // Partial: defer. Its litres stay in the open segment for the next closing fill.
                entries.Add(Unmeasurable(f, milesSinceLast, MpgUnreliableReason.AwaitingFullTank));
                continue;
            }

            // Closing fill: measure the whole open segment.
            var segmentMiles = f.Mileage - anchorMileage.Value;

            if (segmentMiles <= 0)
            {
                entries.Add(Unmeasurable(f, milesSinceLast, MpgUnreliableReason.NonMonotonicMileage));
            }
            else
            {
                var miles = (decimal)segmentMiles;
                var mpg = miles * Units.LitresPerImperialGallon / segmentLitres;
                var litresPer100Km = segmentLitres * 100m / (miles * Units.KmPerMile);

                entries.Add(Measured(
                    f,
                    milesSinceLast,
                    mpg,
                    litresPer100Km,
                    segmentMiles,
                    segmentFills));
            }

            // This fill is the new anchor whether or not it measured — a non-monotonic closing fill still closes.
            anchorMileage = f.Mileage;
            segmentLitres = 0m;
            segmentFills = 0;
        }

        // Plausible figures only. A 272 mpg splash is arithmetically correct and physically meaningless;
        // unguarded it becomes "best MPG" and sits on the Dashboard as good news.
        var usable = entries
            .Where(e => e is { Mpg: not null, IsPlausible: true })
            .Select(e => e.Mpg!.Value)
            .ToList();

        var totalLitres = ordered.Sum(f => f.Litres);
        var totalCost = ordered.Sum(f => f.TotalCost);

        // Whatever the walk left open after the last fill: 0/0/null when the last fill closed the tank.
        var pendingFillCount = segmentFills;
        var pendingLitres = segmentLitres;
        int? pendingMiles = anchorMileage is not null && segmentFills > 0
            ? ordered[^1].Mileage - anchorMileage.Value
            : null;

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
            Entries: entries,
            PendingFillCount: pendingFillCount,
            PendingLitres: pendingLitres,
            PendingMiles: pendingMiles);
    }

    /// <summary>
    /// Total distance across the span, over the litres burned within it.
    /// </summary>
    /// <remarks>
    /// The strongest reading of "based on actual litres": every litre pumped, over every mile driven. Needs
    /// nothing about tank level, because the error is one tank's variation spread across the whole span.
    ///
    /// The first fill's litres are excluded: that fuel filled a tank already burned before recording began.
    /// The span ends at the <b>last closing fill</b>, and litres are summed to it — a trailing open tank (a
    /// partial with no full fill after it) is dropped, exactly as the per-fill path drops it, so the aggregate
    /// and the per-fill figures tell the same story. On all-full history the last fill is closing, so this is
    /// identical to measuring to the final fill.
    /// </remarks>
    private static decimal? CumulativeMpg(List<FuelEntry> ordered)
    {
        if (ordered.Count < 2)
        {
            return null;
        }

        var lastClosing = -1;
        for (var i = ordered.Count - 1; i >= 1; i--)
        {
            if (ClosesTank(ordered[i]))
            {
                lastClosing = i;
                break;
            }
        }

        if (lastClosing < 1)
        {
            return null;
        }

        var span = ordered[lastClosing].Mileage - ordered[0].Mileage;
        var litresBurned = ordered.Skip(1).Take(lastClosing).Sum(f => f.Litres);

        if (span <= 0 || litresBurned <= 0)
        {
            return null;
        }

        return span * Units.LitresPerImperialGallon / litresBurned;
    }

    private static FuelEntryMetrics Measured(
        FuelEntry current,
        int? milesSinceLast,
        decimal mpg,
        decimal litresPer100Km,
        int segmentMiles,
        int spannedFillCount) =>
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
            Mpg: mpg,
            LitresPer100Km: litresPer100Km,
            IsReliable: true,
            // Judged on the number's own physics — which also catches a mistyped odometer with a full tank at
            // both ends, the case the old fill-level gate waved straight through.
            IsPlausible: mpg >= MinPlausibleMpg && mpg <= MaxPlausibleMpg,
            UnreliableReason: null,
            SegmentMiles: segmentMiles,
            SpannedFillCount: spannedFillCount);

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
            UnreliableReason: reason,
            SegmentMiles: null,
            SpannedFillCount: 0);
}
