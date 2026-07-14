using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests.Workbook;

/// <summary>
/// The four defects, resolved against the real history.
/// </summary>
/// <remarks>
/// This is the project's central claim under test: that computing on read from the logs produces different —
/// and correct — answers where the spreadsheet's stored values drifted. Every figure here was verified against
/// the workbook itself, not taken from the spec.
/// </remarks>
public sealed class WorkbookValidationTests
{
    private static VehicleSummary Summary() =>
        DerivedMetrics.Compute(WorkbookFixture.Data(), WorkbookFixture.ReferenceDate);

    /// <summary>Defect 1: a stale stored MOT expiry, showing red for a test already passed.</summary>
    [Fact]
    public void Mot_expiry_resolves_to_8_July_2027_not_the_stored_6_August_2026()
    {
        var mot = Summary().Renewals.Mot;

        Assert.Equal(new DateOnly(2027, 7, 8), mot.ExpiryDate);
        Assert.Equal(359, mot.DaysRemaining);
        Assert.Equal(RenewalUrgency.Ok, mot.Urgency);

        // What the sheet stores, and would have shown: 23 days and red, for an MOT passed on 8 Jul 2026.
        Assert.NotEqual(new DateOnly(2026, 8, 6), mot.ExpiryDate);
    }

    /// <summary>Defect 2: total litres double-counted — exactly 2.0000x.</summary>
    [Fact]
    public void Total_litres_resolves_to_556_47_not_1112_94()
    {
        var fuel = Summary().Fuel;

        Assert.Equal(556.47m, fuel.TotalLitres);
        Assert.Equal(13, fuel.FillCount);

        // The ratio is the tell: not a rounding drift or a missing row, but every fill counted twice.
        Assert.Equal(1112.94m, fuel.TotalLitres * 2m);
    }

    /// <summary>Defect 3: fuel spend disagrees between two sheets by £163.16.</summary>
    [Fact]
    public void The_fuel_gap_is_163_16_and_mirroring_is_what_closes_it()
    {
        var summary = Summary();

        // The Expenses sheet carries one lumped "fuel to date" row (£453.17 on 15 May) plus four per-fill
        // rows. Together they make the Dashboard's £725.70.
        Assert.Equal(725.70m, summary.Spend.FuelYtd);

        // The Fuel Log's own total is £888.86 — the true figure.
        Assert.Equal(888.86m, Math.Round(summary.Fuel.TotalCost, 2));

        // The gap README §3.2's auto-mirroring exists to remove.
        Assert.Equal(163.16m, Math.Round(summary.Fuel.TotalCost - summary.Spend.FuelYtd, 2));
    }

    /// <summary>Defect 4: current mileage taken from a manual field that the logs had overtaken.</summary>
    [Fact]
    public void Current_mileage_resolves_to_80712_not_the_manual_80705()
    {
        var mileage = Summary().Mileage;

        Assert.Equal(80_712, mileage.CurrentMileage);
        Assert.Equal(4_080, mileage.MilesSincePurchase);

        // The sheet's manual figure, and the 4,073 it derives from it.
        Assert.NotEqual(80_705, mileage.CurrentMileage);
        Assert.NotEqual(4_073, mileage.MilesSincePurchase);
    }

    /// <summary>The mistyped service row: reported, not silently accepted, and not allowed to win.</summary>
    [Fact]
    public void The_83000_mile_row_is_flagged_without_becoming_current_mileage()
    {
        var mileage = Summary().Mileage;

        Assert.True(mileage.HasNonMonotonicHistory);
        Assert.Equal(83_000, mileage.HighestRecordedMileage);

        // MAX would have taken it and inflated every downstream figure for the life of the vehicle.
        Assert.Equal(80_712, mileage.CurrentMileage);
    }

    [Fact]
    public void All_four_defects_resolve_together_at_the_reference_date()
    {
        var summary = Summary();

        Assert.Equal(WorkbookFixture.ReferenceDate, summary.AsOfDate);
        Assert.Equal(new DateOnly(2027, 7, 8), summary.Renewals.Mot.ExpiryDate);
        Assert.Equal(556.47m, summary.Fuel.TotalLitres);
        Assert.Equal(888.86m, Math.Round(summary.Fuel.TotalCost, 2));
        Assert.Equal(80_712, summary.Mileage.CurrentMileage);
    }
}
