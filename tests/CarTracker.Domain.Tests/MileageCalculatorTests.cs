using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

public sealed class MileageCalculatorTests
{
    private const int PurchaseMileage = 76_632;

    private static MileageReading Reading(string date, int mileage, MileageOrigin origin = MileageOrigin.Manual, int createdOrder = 0) =>
        new()
        {
            Id = createdOrder,
            VehicleId = 1,
            ReadingDate = DateOnly.Parse(date),
            Mileage = mileage,
            Origin = origin,
            Source = EntrySource.Import,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(createdOrder),
        };

    /// <summary>
    /// The decision README §2 leaves open ("max/most-recent") and this data forces.
    /// </summary>
    /// <remarks>
    /// A service record dated 27 Jun 2026 logs 83,000 mi; the latest reading, 8 Jul, is 80,712. MAX returns
    /// 83,000 — wrong, and wrong forever, because a historical typo would inflate every downstream figure for
    /// the life of the vehicle. Most-recent-by-date returns the verified 80,712.
    /// </remarks>
    [Fact]
    public void Most_recent_by_date_wins_over_max()
    {
        var result = MileageCalculator.Calculate(
            [
                Reading("2026-06-20", 80_300, MileageOrigin.Fuel),
                Reading("2026-06-27", 83_000, MileageOrigin.Service), // the mistyped row
                Reading("2026-07-08", 80_712, MileageOrigin.Fuel),
            ],
            PurchaseMileage);

        Assert.Equal(80_712, result.CurrentMileage);
        Assert.Equal(new DateOnly(2026, 7, 8), result.AsOfDate);
    }

    [Fact]
    public void The_disagreement_is_reported_rather_than_hidden()
    {
        var result = MileageCalculator.Calculate(
            [
                Reading("2026-06-27", 83_000, MileageOrigin.Service),
                Reading("2026-07-08", 80_712, MileageOrigin.Fuel),
            ],
            PurchaseMileage);

        Assert.True(result.HasNonMonotonicHistory);
        Assert.Equal(83_000, result.HighestRecordedMileage);
    }

    [Fact]
    public void Clean_history_reports_no_anomaly_and_no_highest()
    {
        var result = MileageCalculator.Calculate(
            [
                Reading("2026-06-20", 80_300),
                Reading("2026-07-08", 80_712),
            ],
            PurchaseMileage);

        Assert.False(result.HasNonMonotonicHistory);
        // Only present when it disagrees — otherwise it is just the current mileage restated.
        Assert.Null(result.HighestRecordedMileage);
    }

    [Fact]
    public void Same_date_breaks_the_tie_on_higher_mileage()
    {
        var result = MileageCalculator.Calculate(
            [
                Reading("2026-07-08", 80_700, MileageOrigin.Wash),
                Reading("2026-07-08", 80_712, MileageOrigin.Fuel),
            ],
            PurchaseMileage);

        // Two readings on one day: the odometer advanced during it, so the higher is the later.
        Assert.Equal(80_712, result.CurrentMileage);
        Assert.False(result.HasNonMonotonicHistory);
    }

    [Fact]
    public void Same_date_and_mileage_breaks_the_tie_on_creation_order()
    {
        var result = MileageCalculator.Calculate(
            [
                Reading("2026-07-08", 80_712, MileageOrigin.Fuel, createdOrder: 1),
                Reading("2026-07-08", 80_712, MileageOrigin.Wash, createdOrder: 2),
            ],
            PurchaseMileage);

        Assert.Equal(80_712, result.CurrentMileage);
    }

    [Fact]
    public void Miles_since_purchase_matches_the_workbook()
    {
        var result = MileageCalculator.Calculate([Reading("2026-07-08", 80_712)], PurchaseMileage);

        // 80,712 - 76,632 = 4,080
        Assert.Equal(4_080, result.MilesSincePurchase);
    }

    [Fact]
    public void No_readings_yields_nulls_rather_than_zeroes()
    {
        var result = MileageCalculator.Calculate([], PurchaseMileage);

        // A vehicle with no readings has no current mileage. Zero would be a lie — it would read as an
        // odometer at 0 and make miles-since-purchase hugely negative.
        Assert.Null(result.CurrentMileage);
        Assert.Null(result.AsOfDate);
        Assert.Null(result.MilesSincePurchase);
        Assert.False(result.HasNonMonotonicHistory);
    }

    [Fact]
    public void A_single_reading_is_enough()
    {
        var result = MileageCalculator.Calculate([Reading("2026-03-14", 76_632)], PurchaseMileage);

        Assert.Equal(76_632, result.CurrentMileage);
        Assert.Equal(0, result.MilesSincePurchase);
        Assert.False(result.HasNonMonotonicHistory);
    }

    [Fact]
    public void A_reading_below_purchase_mileage_is_flagged_and_can_go_negative()
    {
        var result = MileageCalculator.Calculate([Reading("2026-04-01", 76_000)], PurchaseMileage);

        // Physically impossible, so it is bad data — but the calculator reports what the logs say and lets
        // the anomaly surface, rather than clamping to zero and hiding it.
        Assert.Equal(-632, result.MilesSincePurchase);
    }

    [Fact]
    public void Readings_are_not_assumed_sorted()
    {
        var result = MileageCalculator.Calculate(
            [
                Reading("2026-07-08", 80_712),
                Reading("2026-03-14", 76_632),
                Reading("2026-06-20", 80_300),
            ],
            PurchaseMileage);

        Assert.Equal(80_712, result.CurrentMileage);
    }
}
