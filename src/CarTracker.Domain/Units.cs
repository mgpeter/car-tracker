namespace CarTracker.Domain;

/// <summary>
/// Exact unit conversions. Both are definitions, not approximations — never inline them, and never round them.
/// </summary>
public static class Units
{
    /// <summary>1 imperial gallon = 4.54609 L exactly. UK MPG depends on this being the imperial gallon.</summary>
    public const decimal LitresPerImperialGallon = 4.54609m;

    /// <summary>1 mile = 1.609344 km exactly.</summary>
    public const decimal KmPerMile = 1.609344m;

    /// <summary>
    /// The invariant tying MPG and L/100km together: their product is constant for any non-zero interval.
    /// </summary>
    /// <remarks>
    /// 4.54609 × 100 ÷ 1.609344 ≈ 282.4809. Worth a property test over the whole fuel history — it catches a
    /// transposed constant or an inverted formula in either direction, which hand-picked examples can miss.
    /// </remarks>
    public const decimal MpgTimesLitresPer100Km = LitresPerImperialGallon * 100m / KmPerMile;
}
