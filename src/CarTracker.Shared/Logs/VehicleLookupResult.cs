namespace CarTracker.Shared.Logs;

/// <summary>
/// What a registration resolves to upstream — un-persisted facts for the owner to confirm in the add-car sheet.
/// </summary>
/// <remarks>
/// <para>
/// Every field is nullable because a reg may resolve <i>partially</i>: VES knows a brand-new car that DVSA has
/// no MOT for, VES returns make and colour but frequently not model, and a partial record is a normal answer
/// rather than an error. The sheet pre-fills what came back and leaves every field editable.
/// </para>
/// <para>
/// <b><see cref="MotExpiry"/> is a seed, not an answer.</b> MOT expiry is derived everywhere in this app — from
/// the latest MOT <c>ServiceRecord.NextDueDate</c> — and a stored copy is the first of the five defects this
/// project exists to fix: the workbook showed a red 23-day countdown for a test that had already passed. This
/// date lands on <c>Vehicle.MotExpirySeed</c>, the documented fallback read only while no MOT record exists, so
/// the first logged pass supersedes it. It must never become a figure the dashboard trusts as final.
/// </para>
/// </remarks>
/// <param name="Source">
/// Provenance. "dvla" when the facts came from the upstreams — present so a reader (and a future assistant) can
/// tell a looked-up value from a typed one without guessing.
/// </param>
public sealed record VehicleLookupResult(
    string Registration,
    string? Make,
    string? Model,
    int? Year,
    string? Colour,
    int? EngineSizeCc,
    FuelType? FuelType,
    DateOnly? MotExpiry,
    string? MotStatus,
    string? TaxStatus,
    DateOnly? VedExpiry,
    string Source);
