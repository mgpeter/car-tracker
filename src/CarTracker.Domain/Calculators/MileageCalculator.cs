using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Current mileage, derived from the readings — never from a stored field.
/// </summary>
/// <remarks>
/// Pure: collections in, result out. No DbContext, no IQueryable, no async. That is what makes the fixture
/// tests possible without a database.
/// </remarks>
public static class MileageCalculator
{
    public static MileageResult Calculate(IReadOnlyCollection<MileageReading> readings, int purchaseMileage)
    {
        if (readings.Count == 0)
        {
            return new MileageResult(null, null, null, HasNonMonotonicHistory: false, null);
        }

        // Most recent by date — not MAX(mileage). README §2 says "max/most-recent" and in this data they
        // disagree: the 27 Jun service record logs 83,000 against a latest reading of 80,712 on 8 Jul. An
        // odometer only advances, so a disagreement means one row is wrong; taking the latest reading keeps a
        // historical typo from inflating current mileage forever, which is what MAX would do.
        //
        // Ties: same date -> the higher mileage is the later one, because the odometer advanced during the
        // day. Same date and mileage -> whichever was recorded first; they are the same reading logged twice.
        var current = readings
            .OrderByDescending(r => r.ReadingDate)
            .ThenByDescending(r => r.Mileage)
            .ThenBy(r => r.CreatedAt)
            .First();

        var highest = readings.Max(r => r.Mileage);
        var hasNonMonotonicHistory = highest > current.Mileage;

        return new MileageResult(
            CurrentMileage: current.Mileage,
            AsOfDate: current.ReadingDate,
            // Can be negative if a reading predates the purchase mileage. That is impossible in reality, so it
            // is bad data — reported rather than clamped, so the anomaly stays visible.
            MilesSincePurchase: current.Mileage - purchaseMileage,
            HasNonMonotonicHistory: hasNonMonotonicHistory,
            // Only meaningful when it disagrees; otherwise it merely restates CurrentMileage.
            HighestRecordedMileage: hasNonMonotonicHistory ? highest : null);
    }
}
