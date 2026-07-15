namespace CarTracker.Shared;

/// <summary>
/// What kind of data problem was found.
/// </summary>
/// <remarks>
/// Three kinds, not six. `SupersededByMirror`, `UnparseableValue` and `MissingReference` were importer-only
/// and died with it (DEC-008). What remains are the flags a live write can raise — which is why anomalies
/// outlived the importer at all: README §5.3 makes flagging a write-path obligation.
/// </remarks>
public enum AnomalyKind
{
    /// <summary>
    /// A reading below an earlier-dated one. The schema permits this deliberately — §5.3 requires flagging
    /// anomalies, not rejecting the data.
    /// </summary>
    MileageNonMonotonic = 1,

    /// <summary>
    /// The receipt total disagrees with litres × price by more than 2p. Forecourt rounding is about a penny;
    /// more suggests a transcription error.
    /// </summary>
    FuelCostDiscrepancy = 2,

    /// <summary>
    /// A computed MPG outside the physical band. Usually a missed fill or a mistyped odometer, not economy.
    /// </summary>
    ImplausibleMpg = 3,
}

/// <summary>Separates "this is wrong" from "expected, but worth knowing".</summary>
public enum AnomalySeverity
{
    Error = 1,
    Warning = 2,
    Info = 3,
}

/// <summary>
/// Where an anomaly is in its life.
/// </summary>
/// <remarks>
/// Not decoration. The 83,000 mi row sits Open until the owner decides, and there are three legitimate
/// endings: it was a typo (Corrected), the odometer really did read that (Accepted), or it is not worth
/// pursuing (Dismissed). Without a lifecycle the integrity screen is either permanently noisy or gets cleared
/// by deletion — which destroys the record that the data was ever questioned.
/// </remarks>
public enum AnomalyStatus
{
    Open = 1,
    Accepted = 2,
    Corrected = 3,
    Dismissed = 4,
}
