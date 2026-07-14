namespace CarTracker.Shared.Metrics;

/// <summary>
/// How pressing a renewal is. Thresholds per README §3.1: red under 30 days, amber under 60.
/// </summary>
/// <remarks>
/// Urgency, not colour. Mapping this to <c>--rust</c> / <c>#C79A22</c> / <c>--green</c> is the UI's job; a
/// domain service that knows hex codes has the layering wrong.
/// </remarks>
public enum RenewalUrgency
{
    Ok = 1,
    Amber = 2,
    Red = 3,
}

/// <param name="DaysRemaining">
/// Negative when expired, and the negative is kept: "expired 12 days ago" is actionable in a way that a
/// clamped zero is not.
/// </param>
/// <param name="Source">Where the date came from — a derived record, or a seeded fallback.</param>
public sealed record Renewal(
    string Name,
    DateOnly? ExpiryDate,
    int? DaysRemaining,
    RenewalUrgency? Urgency,
    string? Source);

/// <param name="Mot">
/// Derived from the latest <c>type = 'MOT'</c> service record's next-due date, falling back to
/// <c>Vehicle.MotExpirySeed</c> only when no such record exists. The workbook's Dashboard stores it and reads
/// 6 Aug 2026 / 23 days — a red countdown for a test already passed on 8 Jul 2026, whose certificate runs to
/// 8 Jul 2027.
/// </param>
/// <param name="NextServiceMiles">Miles remaining until the next service is due, from the latest record.</param>
public sealed record RenewalSummary(
    Renewal Mot,
    Renewal Insurance,
    Renewal RoadTax,
    Renewal NextServiceDate,
    int? NextServiceMiles);
