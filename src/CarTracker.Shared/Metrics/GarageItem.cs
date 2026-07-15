namespace CarTracker.Shared.Metrics;

/// <summary>
/// One vehicle as the garage shows it.
/// </summary>
/// <remarks>
/// <para>
/// A projection of <see cref="VehicleSummary"/>, not a second computation. Every figure here is lifted from
/// the summary the dashboard renders, so the card and the dashboard cannot disagree — spec §4's whole point.
/// If this recomputed anything, "80,712 on the card, 80,705 on the dashboard" becomes possible, which is the
/// class of bug the workbook already has.
/// </para>
/// <para>
/// It is a slimmer shape rather than the summary itself because the summary carries every fill
/// (<c>Fuel.Entries</c>): a garage of five cars would ship five complete fuel logs to render five cards.
/// </para>
/// </remarks>
/// <param name="OpenAnomalyCount">
/// The integrity pill. Not on <see cref="VehicleSummary"/> because anomalies are not derived — they are
/// records with a lifecycle, and the summary is only ever computed.
/// </param>
/// <param name="LatestMpg">
/// The most recent fill's MPG. Null when there are no fills, or when the last fill's MPG is not measurable —
/// the card must say so rather than show the average and imply it is current.
/// </param>
public sealed record GarageItem(
    int VehicleId,
    string Registration,
    string Name,
    VehicleStatus Status,
    bool IsDefault,
    int? CurrentMileage,
    int? MilesSincePurchase,
    decimal? CostPerMile,
    decimal? MonthlyAverage,
    decimal? AverageMpg,
    decimal? LatestMpg,
    Renewal Mot,
    int OverdueCheckCount,
    int NeverLoggedCheckCount,
    int OpenAnomalyCount)
{
    /// <summary>True when no renewal is amber or red — the card's "Renewals OK" pill.</summary>
    public required bool RenewalsOk { get; init; }
}
