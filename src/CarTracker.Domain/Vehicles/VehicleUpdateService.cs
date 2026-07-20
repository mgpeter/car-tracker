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
public sealed class VehicleUpdateService(CarTrackerDbContext context, IDerivedMetricsService metrics)
{
    public async Task<WriteResult<VehicleSummary>> ApplyAsync(
        int vehicleId, VehiclePatch patch, CancellationToken cancellationToken = default)
    {
        var vehicle = await context.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        if (vehicle is null) return WriteResult<VehicleSummary>.NotFound();

        if (patch.Insurance is { PeriodStart: { } start, PeriodEnd: { } end } && end < start)
            return WriteResult<VehicleSummary>.Invalid("Insurance.PeriodEnd", "A policy cannot end before it starts.");

        // Identity — a null leaves the field, so "set the colour" cannot wipe the notes.
        vehicle.Colour = patch.Colour ?? vehicle.Colour;
        vehicle.Vin = patch.Vin ?? vehicle.Vin;
        vehicle.BodyStyle = patch.BodyStyle ?? vehicle.BodyStyle;
        vehicle.Seller = patch.Seller ?? vehicle.Seller;
        vehicle.DefaultGarage = patch.DefaultGarage ?? vehicle.DefaultGarage;
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

        // Fluids is a single-field patch, and the field must be clearable — a null capacity is how the range is
        // switched off. So the presence of a fluids block sets the value authoritatively (value or null), rather
        // than the ?? merge the other blocks use. Omitting the block leaves it untouched.
        if (patch.Fluids is { } fluids)
        {
            vehicle.Fluids.FuelTankCapacityLitres = fluids.FuelTankCapacityLitres;
        }

        await context.SaveChangesAsync(cancellationToken);

        var summary = await metrics.GetVehicleSummaryAsync(vehicle.Id, cancellationToken);
        return summary is null
            ? WriteResult<VehicleSummary>.NotFound()
            : WriteResult<VehicleSummary>.Updated(summary);
    }
}
