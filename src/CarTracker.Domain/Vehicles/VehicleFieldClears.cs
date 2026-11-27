using CarTracker.Data;
using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Vehicles;

/// <summary>
/// What each <see cref="VehicleField"/> means to <see cref="VehicleUpdateService"/>: the web form key and label
/// its errors carry, whether a patch gives it a value, and how to clear it.
/// </summary>
/// <remarks>
/// One switch arm per member. A new member without an arm throws, and the enumerated tests in
/// <c>VehicleUpdateServiceTests</c> run every member, so it fails there before it ships.
/// </remarks>
internal static class VehicleFieldClears
{
    /// <param name="Key">
    /// The web form's field key (<c>oilSpec</c>, <c>premium</c>), so <c>reportApiError</c> lands a conflict on the
    /// input. Insurance and breakdown both use <c>policyNumber</c>; only one sheet is open at a time.
    /// </param>
    /// <param name="Label">Plain words for a sentence, without the unit suffixes the form labels carry.</param>
    /// <param name="IsSet">Whether the patch carries a value for this field.</param>
    /// <param name="Clear">Sets the column to null. Never called for an insurance or breakdown block that is null.</param>
    internal sealed record Rule(string Key, string Label, Func<VehiclePatch, bool> IsSet, Action<Vehicle> Clear);

    internal static Rule For(VehicleField field) => field switch
    {
        VehicleField.Colour => new("colour", "Colour", p => p.Colour is not null, v => v.Colour = null),
        VehicleField.Vin => new("vin", "VIN", p => p.Vin is not null, v => v.Vin = null),
        VehicleField.BodyStyle => new("bodyStyle", "Body style", p => p.BodyStyle is not null, v => v.BodyStyle = null),
        VehicleField.Seller => new("seller", "Seller", p => p.Seller is not null, v => v.Seller = null),
        VehicleField.DefaultGarage => new(
            "defaultGarage", "Default garage", p => p.DefaultGarage is not null, v => v.DefaultGarage = null),
        VehicleField.Notes => new("notes", "Notes", p => p.Notes is not null, v => v.Notes = null),
        VehicleField.MotExpirySeed => new(
            "motExpirySeed", "MOT expiry", p => p.MotExpirySeed is not null, v => v.MotExpirySeed = null),
        VehicleField.VedExpiry => new(
            "vedExpiry", "Road tax expiry", p => p.VedExpiry is not null, v => v.VedExpiry = null),
        VehicleField.VedAnnualCost => new(
            "vedAnnualCost", "Road tax annual cost", p => p.VedAnnualCost is not null, v => v.VedAnnualCost = null),
        VehicleField.UlezCompliant => new(
            "ulezCompliant", "ULEZ", p => p.UlezCompliant is not null, v => v.UlezCompliant = null),
        VehicleField.PurchasePrice => new(
            "purchasePrice", "Purchase price", p => p.PurchasePrice is not null, v => v.PurchasePrice = null),

        VehicleField.InsuranceInsurer => Insurance("insurer", "Insurer", i => i.Insurer, i => i.Insurer = null),
        VehicleField.InsurancePolicyNumber => Insurance(
            "policyNumber", "Insurance policy number", i => i.PolicyNumber, i => i.PolicyNumber = null),
        VehicleField.InsurancePeriodStart => Insurance(
            "periodStart", "Cover from", i => i.PeriodStart, i => i.PeriodStart = null),
        VehicleField.InsurancePeriodEnd => Insurance("periodEnd", "Cover to", i => i.PeriodEnd, i => i.PeriodEnd = null),
        VehicleField.InsuranceCoverType => Insurance(
            "coverType", "Cover type", i => i.CoverType, i => i.CoverType = null),
        VehicleField.InsurancePremium => Insurance("premium", "Premium", i => i.Premium, i => i.Premium = null),
        VehicleField.InsuranceExcessCompulsory => Insurance(
            "excessCompulsory", "Compulsory excess", i => i.ExcessCompulsory, i => i.ExcessCompulsory = null),
        VehicleField.InsuranceExcessVoluntary => Insurance(
            "excessVoluntary", "Voluntary excess", i => i.ExcessVoluntary, i => i.ExcessVoluntary = null),
        VehicleField.InsuranceNcbYears => Insurance(
            "ncbYears", "No-claims years", i => i.NcbYears, i => i.NcbYears = null),

        VehicleField.BreakdownProvider => Breakdown(
            "provider", "Breakdown provider", b => b.Provider, b => b.Provider = null),
        VehicleField.BreakdownPolicyNumber => Breakdown(
            "policyNumber", "Breakdown policy number", b => b.PolicyNumber, b => b.PolicyNumber = null),
        VehicleField.BreakdownExpiry => Breakdown("expiry", "Breakdown expiry", b => b.Expiry, b => b.Expiry = null),

        VehicleField.FluidsFuelTankCapacityLitres => Fluids(
            "fuelTankCapacityLitres", "Fuel tank capacity",
            f => f.FuelTankCapacityLitres, f => f.FuelTankCapacityLitres = null),
        VehicleField.FluidsOilSpec => Fluids("oilSpec", "Engine oil", f => f.OilSpec, f => f.OilSpec = null),
        VehicleField.FluidsOilCapacityLitres => Fluids(
            "oilCapacityLitres", "Oil capacity", f => f.OilCapacityLitres, f => f.OilCapacityLitres = null),
        VehicleField.FluidsCoolantSpec => Fluids(
            "coolantSpec", "Coolant", f => f.CoolantSpec, f => f.CoolantSpec = null),
        VehicleField.FluidsCoolantCapacityLitres => Fluids(
            "coolantCapacityLitres", "Coolant capacity",
            f => f.CoolantCapacityLitres, f => f.CoolantCapacityLitres = null),
        VehicleField.FluidsBrakeFluidSpec => Fluids(
            "brakeFluidSpec", "Brake fluid", f => f.BrakeFluidSpec, f => f.BrakeFluidSpec = null),
        VehicleField.FluidsTransmissionOilSpec => Fluids(
            "transmissionOilSpec", "Transmission oil", f => f.TransmissionOilSpec, f => f.TransmissionOilSpec = null),
        VehicleField.FluidsSparkPlugPart => Fluids(
            "sparkPlugPart", "Spark plugs", f => f.SparkPlugPart, f => f.SparkPlugPart = null),
        VehicleField.FluidsOilFilterPart => Fluids(
            "oilFilterPart", "Oil filter", f => f.OilFilterPart, f => f.OilFilterPart = null),
        VehicleField.FluidsAirFilterPart => Fluids(
            "airFilterPart", "Air filter", f => f.AirFilterPart, f => f.AirFilterPart = null),
        VehicleField.FluidsFuelFilterPart => Fluids(
            "fuelFilterPart", "Fuel filter", f => f.FuelFilterPart, f => f.FuelFilterPart = null),
        VehicleField.FluidsCabinFilterPart => Fluids(
            "cabinFilterPart", "Cabin filter", f => f.CabinFilterPart, f => f.CabinFilterPart = null),

        VehicleField.TyresTyreSize => Tyres("tyreSize", "Tyre size", t => t.TyreSize, t => t.TyreSize = null),
        VehicleField.TyresPressureFrontPsi => Tyres(
            "pressureFrontPsi", "Front tyre pressure", t => t.PressureFrontPsi, t => t.PressureFrontPsi = null),
        VehicleField.TyresPressureRearPsi => Tyres(
            "pressureRearPsi", "Rear tyre pressure", t => t.PressureRearPsi, t => t.PressureRearPsi = null),
        VehicleField.TyresPressureFrontLadenPsi => Tyres(
            "pressureFrontLadenPsi", "Front tyre pressure (laden)",
            t => t.PressureFrontLadenPsi, t => t.PressureFrontLadenPsi = null),
        VehicleField.TyresPressureRearLadenPsi => Tyres(
            "pressureRearLadenPsi", "Rear tyre pressure (laden)",
            t => t.PressureRearLadenPsi, t => t.PressureRearLadenPsi = null),
        VehicleField.TyresMinTreadMm => Tyres(
            "minTreadMm", "Minimum tread", t => t.MinTreadMm, t => t.MinTreadMm = null),

        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a VehicleField member."),
    };

    // Both sides of an owned block are null-checked: the patch block may be absent, and an insurance or breakdown
    // block whose columns are all null can materialise as null. A clear has nothing to put in a block, so it
    // never creates one (the merge's `??=` would).

    private static Rule Insurance(
        string key, string label, Func<InsurancePatch, object?> value, Action<InsurancePolicy> clear) =>
        Block(key, label, p => p.Insurance, value, v => v.Insurance, clear);

    private static Rule Breakdown(
        string key, string label, Func<BreakdownPatch, object?> value, Action<BreakdownCover> clear) =>
        Block(key, label, p => p.Breakdown, value, v => v.Breakdown, clear);

    private static Rule Fluids(string key, string label, Func<FluidsPatch, object?> value, Action<FluidSpecs> clear) =>
        Block(key, label, p => p.Fluids, value, v => v.Fluids, clear);

    private static Rule Tyres(string key, string label, Func<TyresPatch, object?> value, Action<TyreSpecs> clear) =>
        Block(key, label, p => p.Tyres, value, v => v.Tyres, clear);

    private static Rule Block<TPatch, TBlock>(
        string key,
        string label,
        Func<VehiclePatch, TPatch?> patchBlock,
        Func<TPatch, object?> value,
        Func<Vehicle, TBlock?> vehicleBlock,
        Action<TBlock> clear)
        where TPatch : class
        where TBlock : class =>
        new(key, label,
            p => patchBlock(p) is { } b && value(b) is not null,
            v => { if (vehicleBlock(v) is { } b) clear(b); });
}
