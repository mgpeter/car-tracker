using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Days-to-renewal for MOT, insurance, road tax and the next service.
/// </summary>
public static class RenewalCalculator
{
    /// <summary>The literal that makes a service record an MOT. Every writer must normalise to it.</summary>
    public const string MotType = "MOT";

    private const int RedThresholdDays = 30;
    private const int AmberThresholdDays = 60;

    public static RenewalSummary Calculate(
        Vehicle vehicle,
        IReadOnlyCollection<ServiceRecord> serviceRecords,
        DateOnly referenceDate,
        int? currentMileage)
    {
        var motRecords = serviceRecords
            .Where(r => string.Equals(r.Type, MotType, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.NextDueDate is not null)
            .ToList();

        // Derived, not stored. The workbook's Dashboard stores MOT expiry and reads 6 Aug 2026 / 23 days — a
        // red countdown for a test already passed on 8 Jul 2026, whose certificate runs to 8 Jul 2027. The
        // seed is a fallback for a vehicle with no MOT record yet, never an override.
        var motExpiry = motRecords.Count > 0
            ? motRecords.Max(r => r.NextDueDate)
            : vehicle.MotExpirySeed;

        var motSource = motRecords.Count > 0
            ? $"derived from the MOT pass on {motRecords.OrderByDescending(r => r.NextDueDate).First().ServiceDate:d MMM yyyy}"
            : vehicle.MotExpirySeed is not null ? "seeded — no MOT record yet" : null;

        var serviceRecordsWithDue = serviceRecords
            .Where(r => !string.Equals(r.Type, MotType, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.NextDueDate is not null)
            .ToList();

        var nextServiceDate = serviceRecordsWithDue.Count > 0
            ? serviceRecordsWithDue.Max(r => r.NextDueDate)
            : null;

        var latestWithDueMileage = serviceRecords
            .Where(r => r.NextDueMileage is not null)
            .OrderByDescending(r => r.ServiceDate)
            .FirstOrDefault();

        return new RenewalSummary(
            Mot: Build("MOT", motExpiry, referenceDate, motSource),
            Insurance: Build("Insurance", vehicle.Insurance.PeriodEnd, referenceDate, vehicle.Insurance.Insurer),
            RoadTax: Build("Road tax", vehicle.VedExpiry, referenceDate, null),
            NextServiceDate: Build("Next service", nextServiceDate, referenceDate, null),
            NextServiceMiles: latestWithDueMileage is not null && currentMileage is not null
                ? latestWithDueMileage.NextDueMileage!.Value - currentMileage.Value
                : null);
    }

    private static Renewal Build(string name, DateOnly? expiry, DateOnly referenceDate, string? source)
    {
        if (expiry is null)
        {
            return new Renewal(name, null, null, null, source);
        }

        var daysRemaining = expiry.Value.DayNumber - referenceDate.DayNumber;

        return new Renewal(name, expiry, daysRemaining, UrgencyOf(daysRemaining), source);
    }

    /// <summary>README §3.1: red under 30 days, amber under 60. Expired is red, and keeps its negative.</summary>
    private static RenewalUrgency UrgencyOf(int daysRemaining) => daysRemaining switch
    {
        < RedThresholdDays => RenewalUrgency.Red,
        < AmberThresholdDays => RenewalUrgency.Amber,
        _ => RenewalUrgency.Ok,
    };
}
