namespace CarTracker.Shared.Metrics;

/// <summary>Why an interval has no MPG at all. Structural, not a judgement of quality.</summary>
public enum MpgUnreliableReason
{
    /// <summary>The first fill has no predecessor, so there is no interval to measure.</summary>
    NoPreviousFill = 1,

    /// <summary>The odometer did not advance between the two fills. Never divide by this.</summary>
    NonMonotonicMileage = 3,

    /// <summary>
    /// A partial fill (Half/Quarter): the tank was not brought back to a known level, so this fill measures
    /// nothing on its own. Its litres are not discarded — they accumulate into the span measured at the next
    /// fill to full. Distinct from value <c>2</c>, the retired <c>PartialFill</c>, which meant the opposite
    /// ("discarded because an endpoint wasn't full").
    /// </summary>
    AwaitingFullTank = 4,
}

/// <summary>
/// Metrics for one fill, relative to the fill before it.
/// </summary>
/// <remarks>
/// <b>MPG rests on litres and miles — both recorded exactly.</b> Litres is a receipt figure to 2dp.
/// <see cref="FillLevel"/> is load-bearing for grouping (not gating): Full or unrecorded <i>closes</i> the
/// tank and measures the open segment; Half/Quarter <i>defers</i> the figure to the next fill to full (see
/// <see cref="MpgUnreliableReason.AwaitingFullTank"/>). Whether a computed figure can be trusted is still
/// decided by <paramref name="IsPlausible"/> — the number's own physics.
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
/// Load-bearing for grouping. Full or unrecorded (null) <b>closes the tank</b> — this fill measures MPG across
/// the open segment since the last closing fill. Half/Quarter mark a <b>partial</b> that defers its figure to
/// the next closing fill (<see cref="MpgUnreliableReason.AwaitingFullTank"/>); its litres are not lost, they
/// count in the next measured span. Only "closes vs not" is read — Half vs Quarter is never read arithmetically.
/// Null is treated as closing (a driver leaving the field alone asserts the normal, filled-to-full case).
/// </param>
/// <param name="MilesSinceLast">
/// Odometer delta from the <b>previous row</b>, always shown in the "Miles" column. Distinct from
/// <paramref name="SegmentMiles"/>, the distance the MPG figure actually covers.
/// </param>
/// <param name="SegmentMiles">
/// The distance the MPG figure covers — the denominator's miles. Equals <paramref name="MilesSinceLast"/> for
/// an ungrouped (single-tank) fill; larger for a fill that closed a multi-fill segment. Null when there is no
/// figure.
/// </param>
/// <param name="SpannedFillCount">
/// How many fills the figure covers. 1 for an ordinary tank-to-tank figure; ≥2 when the closing fill grouped
/// one or more deferred partials. 0 on a fill with no figure.
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
    MpgUnreliableReason? UnreliableReason,
    int? SegmentMiles,
    int SpannedFillCount);

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
/// <param name="PendingFillCount">
/// Trailing partial fills since the last closing fill — the open tank in progress. <c>0</c> when the last fill
/// closed the tank (the normal state), in which case <paramref name="PendingLitres"/> is 0 and
/// <paramref name="PendingMiles"/> is null.
/// </param>
/// <param name="PendingLitres">Litres pumped into the open tank so far. <c>0</c> when the tank is closed.</param>
/// <param name="PendingMiles">
/// Miles from the last closing fill to the latest fill's odometer. Null when there is no closing anchor yet
/// (e.g. only partial fills exist) or when the tank is closed.
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
    IReadOnlyList<FuelEntryMetrics> Entries,
    int PendingFillCount,
    decimal PendingLitres,
    int? PendingMiles);
