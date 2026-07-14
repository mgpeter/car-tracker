namespace CarTracker.Shared.Metrics;

/// <summary>
/// Current mileage and what is known about the readings behind it.
/// </summary>
/// <param name="CurrentMileage">
/// The mileage of the <b>most recent reading by date</b> — not <c>MAX(mileage)</c>. README §2 says
/// "max/most-recent", and in the real data those disagree: a service record dated 27 Jun 2026 logs 83,000 mi
/// against a latest reading of 80,712 on 8 Jul. An odometer only advances, so when the two disagree one row is
/// wrong, and a historical typo must not inflate current mileage forever — which is exactly what MAX would do.
/// </param>
/// <param name="AsOfDate">The date of the reading <paramref name="CurrentMileage"/> came from.</param>
/// <param name="MilesSincePurchase">Null when the vehicle has no readings at all.</param>
/// <param name="HasNonMonotonicHistory">
/// True when some earlier reading exceeds <paramref name="CurrentMileage"/>. Lets the Dashboard show a
/// data-integrity flag (the blue axis, distinct from due-status) instead of silently picking a number.
/// </param>
/// <param name="HighestRecordedMileage">
/// Present only when it exceeds <paramref name="CurrentMileage"/> — i.e. only when there is a disagreement
/// worth showing.
/// </param>
public sealed record MileageResult(
    int? CurrentMileage,
    DateOnly? AsOfDate,
    int? MilesSincePurchase,
    bool HasNonMonotonicHistory,
    int? HighestRecordedMileage);
