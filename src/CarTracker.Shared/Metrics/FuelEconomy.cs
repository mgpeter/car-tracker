namespace CarTracker.Shared.Metrics;

/// <summary>Why a computed MPG cannot be trusted. Absent when the figure is reliable.</summary>
public enum MpgUnreliableReason
{
    /// <summary>The first fill has no predecessor, so there is no interval to measure.</summary>
    NoPreviousFill = 1,

    /// <summary>
    /// The tank was not full at both ends. The litres added only equal the fuel consumed between two full
    /// fills; otherwise the figure is arithmetic without meaning.
    /// </summary>
    PartialFill = 2,

    /// <summary>The odometer did not advance between the two fills. Never divide by this.</summary>
    NonMonotonicMileage = 3,
}

/// <summary>
/// Metrics for one fill, relative to the fill before it.
/// </summary>
/// <param name="Mpg">UK (imperial) MPG. Null when there is no measurable interval.</param>
/// <param name="LitresPer100Km">Null under the same conditions as <paramref name="Mpg"/>.</param>
/// <param name="MilesSinceLast">Null when there is no previous fill.</param>
/// <param name="IsReliable">
/// False does not mean the figure is absent — a partial fill still produces arithmetic. It means the UI must
/// say so rather than present it as fact, and fleet aggregates must exclude it.
/// </param>
public sealed record FuelEntryMetrics(
    int FuelEntryId,
    DateOnly EntryDate,
    int Mileage,
    decimal Litres,
    decimal TotalCost,
    int? MilesSinceLast,
    decimal? Mpg,
    decimal? LitresPer100Km,
    bool IsReliable,
    MpgUnreliableReason? UnreliableReason);

/// <summary>
/// Fleet-level fuel figures.
/// </summary>
/// <param name="AverageMpg">
/// Over reliable intervals only. A partial fill yields an inflated figure that would otherwise land on the
/// Dashboard as good news — the single most likely way this feature could lie.
/// </param>
/// <param name="TotalLitres">
/// The plain sum. The workbook's Dashboard says 1,112.94 against a real 556.47 — exactly 2.0000×, because the
/// summary counts all 13 fills twice.
/// </param>
/// <param name="AveragePricePerLitre">
/// Volume-weighted: <c>SUM(totalCost) / SUM(litres)</c>. A 50 L fill at £1.40 and a 10 L fill at £1.60 average
/// to £1.433, not £1.50 — the latter is what a plain mean of the price column gives, and it answers a question
/// nobody asked.
/// </param>
public sealed record FuelEconomySummary(
    decimal? AverageMpg,
    decimal? BestMpg,
    decimal? WorstMpg,
    decimal TotalLitres,
    decimal TotalCost,
    decimal? AveragePricePerLitre,
    DateOnly? LastFillDate,
    int FillCount,
    int ReliableIntervalCount,
    IReadOnlyList<FuelEntryMetrics> Entries);
