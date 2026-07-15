namespace CarTracker.Shared.Metrics;

/// <summary>
/// What the vehicle <i>is</i>, as opposed to how it is doing.
/// </summary>
/// <remarks>
/// <para>
/// Everything else on <see cref="VehicleSummary"/> is a measurement. This is the subject of those
/// measurements, and until now the summary carried only <c>Name</c> — so the dashboard's dossier header had no
/// path to its own chips, and <c>get_vehicle_summary</c> could tell an assistant that a car had done 3,513
/// miles at 28.7 mpg without being able to say what car it was.
/// </para>
/// <para>
/// Stored facts, with one exception. <paramref name="DaysOwned"/> is derived from the reference date and must
/// be: it changes every midnight, and a stored copy would be the workbook's stale-dashboard failure in
/// miniature — an "owned 122 days" that was true once.
/// </para>
/// </remarks>
/// <param name="DaysOwned">
/// Whole days from purchase to the summary's reference date. Zero on the day of purchase, and never negative:
/// a future purchase date is a data-entry error, not a car owned for minus six days.
/// </param>
/// <param name="MilesPerDay">
/// Average miles per day since purchase. Null on the day of purchase — the divisor is zero, and "infinity
/// miles a day" is not a figure worth rendering. The dashboard's "≈ 206 days at 33 mi/day" rests on this.
/// </param>
public sealed record VehicleIdentity(
    string? Variant,
    int Year,
    string? Colour,
    string? Drivetrain,
    string? Transmission,
    string? EngineCode,
    DateOnly PurchaseDate,
    int DaysOwned,
    decimal? MilesPerDay,
    string? DefaultGarage);
