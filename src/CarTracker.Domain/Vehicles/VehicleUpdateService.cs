using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Vehicles;

/// <summary>
/// Applies a partial edit to a vehicle's stored inputs — the identity/statutory/insurance/fuel-tank fields the
/// dashboard's renewal countdowns and full-tank range read. The REST <c>PATCH /vehicles/{reg}</c> and the MCP
/// settings tools both call this, so there is one merge and one "no MOT expiry setter" rule.
/// </summary>
/// <remarks>
/// Returns the recomputed <see cref="VehicleSummary"/> on success, because the whole reason to write these is what
/// they do to the countdowns — a caller reads the new renewal straight back rather than deriving it again.
/// </remarks>
public sealed class VehicleUpdateService(
    CarTrackerDbContext context, IDerivedMetricsService metrics, ReferenceWriter references)
{
    /// <summary>Matches the <c>insurance_cover_type varchar(40)</c> column; a longer value is a DbUpdateException.</summary>
    private const int CoverTypeMaxLength = 40;

    public async Task<WriteResult<VehicleSummary>> ApplyAsync(
        int vehicleId, VehiclePatch patch, CancellationToken cancellationToken = default)
    {
        var vehicle = await context.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        if (vehicle is null) return WriteResult<VehicleSummary>.NotFound();

        if (patch.Insurance is { PeriodStart: { } start, PeriodEnd: { } end } && end < start)
            return WriteResult<VehicleSummary>.Invalid("Insurance.PeriodEnd", "A policy cannot end before it starts.");

        // Guard the column length here with a plain message, rather than letting the varchar(40) constraint throw a
        // bare DbUpdateException the MCP layer would surface as an opaque "An error occurred". The value is a short
        // label ("Comprehensive", "Third party") — 40 is generous.
        if (patch.Insurance?.CoverType is { Length: > CoverTypeMaxLength })
            return WriteResult<VehicleSummary>.Invalid(
                "coverType", $"Cover type must be {CoverTypeMaxLength} characters or fewer.");

        // Identity — a null leaves the field, so "set the colour" cannot wipe the notes.
        vehicle.Colour = patch.Colour ?? vehicle.Colour;
        vehicle.Vin = patch.Vin ?? vehicle.Vin;
        vehicle.BodyStyle = patch.BodyStyle ?? vehicle.BodyStyle;
        vehicle.Seller = patch.Seller ?? vehicle.Seller;
        // DefaultGarage is a foreign key to a keyed table, not the free text it looks like (see ReferenceWriter).
        // Setting it to a garage that has never been seen is an FK violation unless the row is created first — the
        // same trap ServiceRecordFactory guards. The single SaveChangesAsync below persists both in one transaction.
        if (patch.DefaultGarage is { } garage)
        {
            await references.EnsureGarageAsync(garage, cancellationToken);
            vehicle.DefaultGarage = garage;
        }
        vehicle.Notes = patch.Notes ?? vehicle.Notes;
        vehicle.Status = patch.Status ?? vehicle.Status;
        vehicle.IsDefault = patch.IsDefault ?? vehicle.IsDefault;

        // Statutory — these feed the dashboard's renewal countdowns. MotExpirySeed is only ever a fallback for a
        // vehicle with no MOT record; a logged pass always wins in RenewalCalculator.
        vehicle.MotExpirySeed = patch.MotExpirySeed ?? vehicle.MotExpirySeed;
        vehicle.VedExpiry = patch.VedExpiry ?? vehicle.VedExpiry;
        vehicle.VedAnnualCost = patch.VedAnnualCost ?? vehicle.VedAnnualCost;
        vehicle.UlezCompliant = patch.UlezCompliant ?? vehicle.UlezCompliant;

        if (patch.Insurance is { } insurance)
        {
            vehicle.Insurance ??= new InsurancePolicy();
            vehicle.Insurance.Insurer = insurance.Insurer ?? vehicle.Insurance.Insurer;
            vehicle.Insurance.PolicyNumber = insurance.PolicyNumber ?? vehicle.Insurance.PolicyNumber;
            vehicle.Insurance.PeriodStart = insurance.PeriodStart ?? vehicle.Insurance.PeriodStart;
            vehicle.Insurance.PeriodEnd = insurance.PeriodEnd ?? vehicle.Insurance.PeriodEnd;
            vehicle.Insurance.CoverType = insurance.CoverType ?? vehicle.Insurance.CoverType;
            vehicle.Insurance.Premium = insurance.Premium ?? vehicle.Insurance.Premium;
            vehicle.Insurance.ExcessCompulsory = insurance.ExcessCompulsory ?? vehicle.Insurance.ExcessCompulsory;
            vehicle.Insurance.ExcessVoluntary = insurance.ExcessVoluntary ?? vehicle.Insurance.ExcessVoluntary;
            vehicle.Insurance.NcbYears = insurance.NcbYears ?? vehicle.Insurance.NcbYears;
        }

        // Fluids/consumables — the "at the pump" reference block get_reference reads. Merged per field like the
        // rest of the patch (a null leaves the stored value), so setting the oil spec cannot wipe the coolant.
        if (patch.Fluids is { } fluids)
        {
            vehicle.Fluids.FuelTankCapacityLitres = fluids.FuelTankCapacityLitres ?? vehicle.Fluids.FuelTankCapacityLitres;
            vehicle.Fluids.OilSpec = fluids.OilSpec ?? vehicle.Fluids.OilSpec;
            vehicle.Fluids.OilCapacityLitres = fluids.OilCapacityLitres ?? vehicle.Fluids.OilCapacityLitres;
            vehicle.Fluids.CoolantSpec = fluids.CoolantSpec ?? vehicle.Fluids.CoolantSpec;
            vehicle.Fluids.CoolantCapacityLitres = fluids.CoolantCapacityLitres ?? vehicle.Fluids.CoolantCapacityLitres;
            vehicle.Fluids.BrakeFluidSpec = fluids.BrakeFluidSpec ?? vehicle.Fluids.BrakeFluidSpec;
            vehicle.Fluids.TransmissionOilSpec = fluids.TransmissionOilSpec ?? vehicle.Fluids.TransmissionOilSpec;
            vehicle.Fluids.SparkPlugPart = fluids.SparkPlugPart ?? vehicle.Fluids.SparkPlugPart;
            vehicle.Fluids.OilFilterPart = fluids.OilFilterPart ?? vehicle.Fluids.OilFilterPart;
            vehicle.Fluids.AirFilterPart = fluids.AirFilterPart ?? vehicle.Fluids.AirFilterPart;
            vehicle.Fluids.FuelFilterPart = fluids.FuelFilterPart ?? vehicle.Fluids.FuelFilterPart;
            vehicle.Fluids.CabinFilterPart = fluids.CabinFilterPart ?? vehicle.Fluids.CabinFilterPart;
        }

        // Tyre reference specs — size, cold pressures (normal + laden) and minimum tread. Merged per field.
        if (patch.Tyres is { } tyres)
        {
            vehicle.Tyres.TyreSize = tyres.TyreSize ?? vehicle.Tyres.TyreSize;
            vehicle.Tyres.PressureFrontPsi = tyres.PressureFrontPsi ?? vehicle.Tyres.PressureFrontPsi;
            vehicle.Tyres.PressureRearPsi = tyres.PressureRearPsi ?? vehicle.Tyres.PressureRearPsi;
            vehicle.Tyres.PressureFrontLadenPsi = tyres.PressureFrontLadenPsi ?? vehicle.Tyres.PressureFrontLadenPsi;
            vehicle.Tyres.PressureRearLadenPsi = tyres.PressureRearLadenPsi ?? vehicle.Tyres.PressureRearLadenPsi;
            vehicle.Tyres.MinTreadMm = tyres.MinTreadMm ?? vehicle.Tyres.MinTreadMm;
        }

        await context.SaveChangesAsync(cancellationToken);

        var summary = await metrics.GetVehicleSummaryAsync(vehicle.Id, cancellationToken);
        return summary is null
            ? WriteResult<VehicleSummary>.NotFound()
            : WriteResult<VehicleSummary>.Updated(summary);
    }
}
