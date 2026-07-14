using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class RenewalCalculatorTests
{
    private static readonly DateOnly Reference = new(2026, 7, 14);

    private static Vehicle NewVehicle(
        DateOnly? motSeed = null,
        DateOnly? insuranceEnd = null,
        DateOnly? vedExpiry = null) =>
        new()
        {
            Id = 1,
            Registration = "BT53 AKJ",
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            MotExpirySeed = motSeed,
            VedExpiry = vedExpiry,
            Insurance = new InsurancePolicy { Insurer = "Admiral", PeriodEnd = insuranceEnd },
            Source = EntrySource.Import,
        };

    private static ServiceRecord Service(string date, string type, int mileage, string? nextDue = null, int? nextDueMileage = null) =>
        new()
        {
            VehicleId = 1,
            ServiceDate = DateOnly.Parse(date),
            Mileage = mileage,
            Type = type,
            NextDueDate = nextDue is null ? null : DateOnly.Parse(nextDue),
            NextDueMileage = nextDueMileage,
            Source = EntrySource.Import,
        };

    /// <summary>
    /// The workbook's worst defect, corrected.
    /// </summary>
    /// <remarks>
    /// Its Dashboard stores MOT expiry and reads 6 Aug 2026 — 23 days, red — for a test that was already
    /// passed on 8 Jul 2026 at 80,705 mi, whose certificate runs to 8 Jul 2027. Deriving from the record
    /// gives 359 days and no alarm.
    /// </remarks>
    [Fact]
    public void Mot_expiry_derives_from_the_latest_mot_pass_not_the_stale_stored_value()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(motSeed: new DateOnly(2026, 8, 6)), // the stale figure the sheet would have used
            [Service("2026-07-08", "MOT", 80_705, nextDue: "2027-07-08")],
            Reference,
            currentMileage: 80_712);

        Assert.Equal(new DateOnly(2027, 7, 8), result.Mot.ExpiryDate);
        Assert.Equal(359, result.Mot.DaysRemaining);
        Assert.Equal(RenewalUrgency.Ok, result.Mot.Urgency);

        // Not the sheet's answer, which would have shown red for a renewal already done.
        Assert.NotEqual(new DateOnly(2026, 8, 6), result.Mot.ExpiryDate);
    }

    [Fact]
    public void The_seed_is_used_only_when_no_mot_record_exists()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(motSeed: new DateOnly(2026, 8, 6)),
            [Service("2026-05-01", "Service", 79_000, nextDue: "2027-05-01")], // not an MOT
            Reference,
            currentMileage: 80_712);

        Assert.Equal(new DateOnly(2026, 8, 6), result.Mot.ExpiryDate);
        Assert.Equal(23, result.Mot.DaysRemaining);
        Assert.Equal(RenewalUrgency.Red, result.Mot.Urgency);
        Assert.Contains("seeded", result.Mot.Source);
    }

    [Fact]
    public void The_latest_mot_record_wins_over_an_earlier_one()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(),
            [
                Service("2025-07-10", "MOT", 72_000, nextDue: "2026-07-10"),
                Service("2026-07-08", "MOT", 80_705, nextDue: "2027-07-08"),
            ],
            Reference,
            currentMileage: 80_712);

        Assert.Equal(new DateOnly(2027, 7, 8), result.Mot.ExpiryDate);
    }

    [Fact]
    public void Mot_type_matching_is_case_insensitive()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(),
            [Service("2026-07-08", "mot", 80_705, nextDue: "2027-07-08")],
            Reference,
            currentMileage: 80_712);

        Assert.Equal(new DateOnly(2027, 7, 8), result.Mot.ExpiryDate);
    }

    [Theory]
    [InlineData("2026-07-14", 0, RenewalUrgency.Red)]     // expires today
    [InlineData("2026-08-12", 29, RenewalUrgency.Red)]    // just inside 30
    [InlineData("2026-08-13", 30, RenewalUrgency.Amber)]  // exactly 30 is amber, not red
    [InlineData("2026-09-11", 59, RenewalUrgency.Amber)]  // just inside 60
    [InlineData("2026-09-12", 60, RenewalUrgency.Ok)]     // exactly 60 is ok
    public void Urgency_thresholds_match_the_spec(string expiry, int expectedDays, RenewalUrgency expected)
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(vedExpiry: DateOnly.Parse(expiry)), [], Reference, 80_712);

        Assert.Equal(expectedDays, result.RoadTax.DaysRemaining);
        Assert.Equal(expected, result.RoadTax.Urgency);
    }

    [Fact]
    public void An_expired_renewal_keeps_its_negative_day_count()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(vedExpiry: new DateOnly(2026, 7, 2)), [], Reference, 80_712);

        // "Expired 12 days ago" is actionable in a way a clamped zero is not.
        Assert.Equal(-12, result.RoadTax.DaysRemaining);
        Assert.Equal(RenewalUrgency.Red, result.RoadTax.Urgency);
    }

    [Fact]
    public void A_missing_expiry_is_null_rather_than_urgent()
    {
        var result = RenewalCalculator.Calculate(NewVehicle(), [], Reference, 80_712);

        // Unknown is not the same as due. Defaulting to Red would cry wolf on every unconfigured field.
        Assert.Null(result.Mot.ExpiryDate);
        Assert.Null(result.Mot.DaysRemaining);
        Assert.Null(result.Mot.Urgency);
    }

    [Fact]
    public void Insurance_reads_from_the_owned_block()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(insuranceEnd: new DateOnly(2027, 3, 15)), [], Reference, 80_712);

        Assert.Equal(new DateOnly(2027, 3, 15), result.Insurance.ExpiryDate);
        Assert.Equal(244, result.Insurance.DaysRemaining);
        Assert.Equal("Admiral", result.Insurance.Source);
    }

    [Fact]
    public void Next_service_miles_counts_down_from_current_mileage()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(),
            [Service("2026-05-01", "Service", 79_000, nextDue: "2027-05-01", nextDueMileage: 85_000)],
            Reference,
            currentMileage: 80_712);

        Assert.Equal(4_288, result.NextServiceMiles);
        Assert.Equal(new DateOnly(2027, 5, 1), result.NextServiceDate.ExpiryDate);
    }

    [Fact]
    public void Next_service_miles_is_null_when_mileage_is_unknown()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(),
            [Service("2026-05-01", "Service", 79_000, nextDueMileage: 85_000)],
            Reference,
            currentMileage: null);

        Assert.Null(result.NextServiceMiles);
    }

    [Fact]
    public void An_mot_record_does_not_leak_into_the_next_service_date()
    {
        var result = RenewalCalculator.Calculate(
            NewVehicle(),
            [Service("2026-07-08", "MOT", 80_705, nextDue: "2027-07-08")],
            Reference,
            currentMileage: 80_712);

        // An MOT is not a service; its next-due belongs to the MOT countdown only.
        Assert.Null(result.NextServiceDate.ExpiryDate);
    }
}
