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
    FluidsPatch? Fluids = null,
    TyresPatch? Tyres = null);

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

/// <summary>
/// A partial edit to a vehicle's fluid/consumable reference block — the "at the pump" facts <c>get_reference</c>
/// reads back ("what oil", "what coolant", "which oil filter"). Every field optional and <b>merged</b> (a null
/// leaves the stored value), the same rule the rest of <see cref="VehiclePatch"/> follows — so setting the oil
/// spec cannot wipe the coolant spec. <c>CoolantSpec</c> is where BT53's OAT-only requirement lives.
/// </summary>
/// <param name="FuelTankCapacityLitres">
/// Usable tank capacity, the one fluid figure the dashboard reads (for full-tank range). Merged like the rest:
/// a null leaves it. (Before Phase 4 this field cleared on a present block; it now follows the uniform
/// merge rule, so an omitted value never clears the range.)
/// </param>
public sealed record FluidsPatch(
    decimal? FuelTankCapacityLitres = null,
    string? OilSpec = null,
    decimal? OilCapacityLitres = null,
    string? CoolantSpec = null,
    decimal? CoolantCapacityLitres = null,
    string? BrakeFluidSpec = null,
    string? TransmissionOilSpec = null,
    string? SparkPlugPart = null,
    string? OilFilterPart = null,
    string? AirFilterPart = null,
    string? FuelFilterPart = null,
    string? CabinFilterPart = null);

/// <summary>
/// A partial edit to a vehicle's tyre reference block — size, the manufacturer's cold pressures (normal and
/// laden) and the minimum legal tread. These are the specs <c>get_reference</c> answers "what pressure for a
/// full load" with — not a reading (that is the tyre log). Merged per field, a null leaves the stored value.
/// </summary>
public sealed record TyresPatch(
    string? TyreSize = null,
    decimal? PressureFrontPsi = null,
    decimal? PressureRearPsi = null,
    decimal? PressureFrontLadenPsi = null,
    decimal? PressureRearLadenPsi = null,
    decimal? MinTreadMm = null);
