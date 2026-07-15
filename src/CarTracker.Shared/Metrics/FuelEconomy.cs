namespace CarTracker.Shared.Metrics;

/// <summary>Why an interval has no MPG at all. Structural, not a judgement of quality.</summary>
public enum MpgUnreliableReason
{
    /// <summary>The first fill has no predecessor, so there is no interval to measure.</summary>
    NoPreviousFill = 1,

    /// <summary>The odometer did not advance between the two fills. Never divide by this.</summary>
    NonMonotonicMileage = 3,
}

/// <summary>
/// Metrics for one fill, relative to the fill before it.
/// </summary>
/// <remarks>
/// <b>MPG rests on litres alone.</b> Litres is a receipt figure to 2dp; the fill level is a glance at a needle,
/// and is descriptive only. Whether a figure can be trusted is decided by
/// <paramref name="IsPlausible"/> — by the number's own physics, not by what was said about the tank.
/// </remarks>
/// <param name="Mpg">UK (imperial) MPG. Null only when there is no measurable interval.</param>
/// <param name="IsReliable">
/// True whenever an MPG exists. It answers "is there a figure?", which is a different question from
/// <paramref name="IsPlausible"/>'s "does the figure make sense?" — conflating the two is what the fill-level
/// gate used to do.
/// </param>
/// <param name="IsPlausible">
/// False when the figure falls outside the physical band for this vehicle. A five-litre splash after 300 miles
/// computes to 272 mpg — from exact litres, correctly, and it is not a real figure. Excluded from the
/// aggregates but kept on the entry: marked, not deleted.
/// </param>
/// <param name="PricePerLitre">
/// As printed on the receipt, per entry. Distinct from <see cref="FuelEconomySummary.AveragePricePerLitre"/>,
/// which is volume-weighted across the log (DEC-011) and answers a different question.
/// </param>
/// <param name="FillLevel">
/// Descriptive, and nothing depends on it. The fuel-basis spec made litres the sole basis of MPG, so this is
/// a note about the tank rather than an input — see <paramref name="IsPlausible"/>, which judges the figure by
/// its own physics instead. Null where it was not recorded, which is not the same as Full.
/// </param>
public sealed record FuelEntryMetrics(
    int FuelEntryId,
    DateOnly EntryDate,
    int Mileage,
    decimal Litres,
    decimal PricePerLitre,
    decimal TotalCost,
    string? Station,
    FillLevel? FillLevel,
    string? Notes,
    int? MilesSinceLast,
    decimal? Mpg,
    decimal? LitresPer100Km,
    bool IsReliable,
    bool IsPlausible,
    MpgUnreliableReason? UnreliableReason);

/// <summary>
/// Fleet-level fuel figures.
/// </summary>
/// <param name="AverageMpg">
/// <b>Cumulative</b>: total distance across the span, over the litres actually pumped within it. Needs nothing
/// about tank level — the error is bounded by one tank's variation spread across thousands of miles. On the
/// real history 3,175 mi / 494.47 L = 29.19 mpg, against 29.14 for <paramref name="PerFillAverageMpg"/>; the
/// two agreeing to 0.05 is that noise washing out.
/// </param>
/// <param name="PerFillAverageMpg">
/// The mean of the plausible per-fill figures. Exposed alongside because a divergence from
/// <paramref name="AverageMpg"/> would be a real signal that something is wrong.
/// </param>
/// <param name="TotalLitres">
/// The plain sum. The workbook's Dashboard says 1,112.94 against a real 556.47 — exactly 2.0000x, because the
/// summary counts all 13 fills twice.
/// </param>
/// <param name="AveragePricePerLitre">
/// Volume-weighted: <c>SUM(totalCost) / SUM(litres)</c>. A 50 L fill at £1.40 and a 10 L fill at £1.60 average
/// to £1.433, not £1.50 — the latter is what a plain mean of the price column gives, and is what the workbook's
/// Dashboard reports.
/// </param>
public sealed record FuelEconomySummary(
    decimal? AverageMpg,
    decimal? PerFillAverageMpg,
    decimal? BestMpg,
    decimal? WorstMpg,
    decimal TotalLitres,
    decimal TotalCost,
    decimal? AveragePricePerLitre,
    DateOnly? LastFillDate,
    int FillCount,
    int MeasuredIntervalCount,
    int ImplausibleCount,
    IReadOnlyList<FuelEntryMetrics> Entries);
