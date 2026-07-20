namespace CarTracker.Shared.Logs;

/// <summary>
/// A partial edit to a vehicle's stored inputs. Every field optional: an omitted field is untouched, not cleared.
/// The REST endpoint maps its request body to this; the MCP settings tools build one with just their fields set —
/// both then call the one <c>VehicleUpdateService</c>, so the merge (and the "no MOT expiry" rule) cannot fork.
/// </summary>
/// <remarks>
/// There is deliberately no derived figure here and no settable current mileage or MOT expiry — MOT expiry
/// derives from the latest MOT pass record, and a stored copy is the first of the five defects this project fixes.
/// </remarks>
public sealed record VehiclePatch(
    string? Colour = null,
    string? Vin = null,
    string? BodyStyle = null,
    string? Seller = null,
    string? DefaultGarage = null,
    string? Notes = null,
    VehicleStatus? Status = null,
    bool? IsDefault = null,
    /// <summary>Only used while the vehicle has no MOT record. A pass record always wins.</summary>
    DateOnly? MotExpirySeed = null,
    DateOnly? VedExpiry = null,
    decimal? VedAnnualCost = null,
    bool? UlezCompliant = null,
    InsurancePatch? Insurance = null,
    FluidsPatch? Fluids = null);

public sealed record InsurancePatch(
    string? Insurer = null,
    string? PolicyNumber = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    string? CoverType = null,
    decimal? Premium = null,
    decimal? ExcessCompulsory = null,
    decimal? ExcessVoluntary = null,
    int? NcbYears = null);

/// <param name="FuelTankCapacityLitres">
/// Usable tank capacity, the one fluid figure the dashboard reads (for full-tank range). A <c>fluids</c> block
/// sets this authoritatively — including to <c>null</c> to clear it — so the derived range disappears rather than
/// falling back to a guessed size. Omit the block entirely to leave it untouched.
/// </param>
public sealed record FluidsPatch(
    decimal? FuelTankCapacityLitres = null);
