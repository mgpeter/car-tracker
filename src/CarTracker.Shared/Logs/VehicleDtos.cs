namespace CarTracker.Shared.Logs;

/// <summary>
/// A partial edit to a vehicle's stored inputs. Every field optional: an omitted or null field is untouched. To
/// set a stored value back to null, name it in <see cref="Clear"/>. The REST endpoint maps its request body to this; the MCP settings tools build one with just their fields set —
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
    TyresPatch? Tyres = null,
    /// <summary>
    /// What the car cost. Correctable because it is load-bearing: it mirrors into a Purchase expense and so
    /// moves total outlay and cost-per-mile — a typo here used to be cosmetic and is not any more. Appended
    /// last to keep every existing positional construction of this record valid.
    /// </summary>
    decimal? PurchasePrice = null,
    /// <summary>
    /// Breakdown cover. Appended last for the same reason <c>PurchasePrice</c> was: every existing positional
    /// construction of this record stays valid. Stored and never derived - nothing logs a recovery callout,
    /// so the expiry here is the only thing that knows when the cover runs out.
    /// </summary>
    BreakdownPatch? Breakdown = null,
    /// <summary>
    /// Fields to set to null, applied after the merge. A null anywhere else still means "unchanged", which is
    /// what the MCP settings tools rely on when they leave a parameter out. Naming a field here and giving it a
    /// value in the same patch is a validation error; duplicates are harmless. Appended last for the same reason
    /// as <c>PurchasePrice</c>.
    /// </summary>
    IReadOnlyList<VehicleField>? Clear = null);

/// <summary>
/// One member per nullable stored input on the vehicle, for <see cref="VehiclePatch.Clear"/>. Prefixed by the
/// block the field lives in, so <c>InsurancePolicyNumber</c> and <c>BreakdownPolicyNumber</c> cannot be confused.
/// </summary>
/// <remarks>
/// <c>Status</c> and <c>IsDefault</c> are not members: their columns are required. Create-only facts (make,
/// model, purchase date) are not members because they are not on the patch at all.
/// </remarks>
public enum VehicleField
{
    Colour,
    Vin,
    BodyStyle,
    Seller,
    DefaultGarage,
    Notes,
    MotExpirySeed,
    VedExpiry,
    VedAnnualCost,
    UlezCompliant,
    PurchasePrice,

    InsuranceInsurer,
    InsurancePolicyNumber,
    InsurancePeriodStart,
    InsurancePeriodEnd,
    InsuranceCoverType,
    InsurancePremium,
    InsuranceExcessCompulsory,
    InsuranceExcessVoluntary,
    InsuranceNcbYears,

    BreakdownProvider,
    BreakdownPolicyNumber,
    BreakdownExpiry,

    FluidsFuelTankCapacityLitres,
    FluidsOilSpec,
    FluidsOilCapacityLitres,
    FluidsCoolantSpec,
    FluidsCoolantCapacityLitres,
    FluidsBrakeFluidSpec,
    FluidsTransmissionOilSpec,
    FluidsSparkPlugPart,
    FluidsOilFilterPart,
    FluidsAirFilterPart,
    FluidsFuelFilterPart,
    FluidsCabinFilterPart,

    TyresTyreSize,
    TyresPressureFrontPsi,
    TyresPressureRearPsi,
    TyresPressureFrontLadenPsi,
    TyresPressureRearLadenPsi,
    TyresMinTreadMm,
}

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
/// A partial edit to a vehicle's breakdown cover - provider, policy number and expiry. Merged per field, a null
/// leaves the stored value, the same rule the rest of <see cref="VehiclePatch"/> follows. To clear one, name it
/// in <see cref="VehiclePatch.Clear"/>.
/// </summary>
/// <remarks>
/// Unlike the MOT, this expiry is a legitimately stored input: no log produces it. Unlike insurance, it drives
/// no dashboard countdown today - <c>RenewalCalculator</c> reads MOT, insurance and road tax only - so it is
/// reference data the owner can correct rather than something the app acts on.
/// </remarks>
public sealed record BreakdownPatch(
    string? Provider = null,
    string? PolicyNumber = null,
    DateOnly? Expiry = null);

/// <summary>
/// A partial edit to a vehicle's fluid/consumable reference block — the "at the pump" facts <c>get_reference</c>
/// reads back ("what oil", "what coolant", "which oil filter"). Every field optional and <b>merged</b> (a null
/// leaves the stored value), the same rule the rest of <see cref="VehiclePatch"/> follows — so setting the oil
/// spec cannot wipe the coolant spec. Clearing a stored value goes through <see cref="VehiclePatch.Clear"/>. <c>CoolantSpec</c> is where BT53's OAT-only requirement lives.
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
/// full load" with — not a reading (that is the tyre log). Merged per field, a null leaves the stored value; to
/// clear one, name it in <see cref="VehiclePatch.Clear"/>.
/// </summary>
public sealed record TyresPatch(
    string? TyreSize = null,
    decimal? PressureFrontPsi = null,
    decimal? PressureRearPsi = null,
    decimal? PressureFrontLadenPsi = null,
    decimal? PressureRearLadenPsi = null,
    decimal? MinTreadMm = null);
