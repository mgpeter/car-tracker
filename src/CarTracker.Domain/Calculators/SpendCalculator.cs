using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Calculators;

/// <summary>
/// Spend rollups, cost-per-mile and monthly average.
/// </summary>
public static class SpendCalculator
{
    /// <summary>Dashboard groups per README §3.1.</summary>
    public static readonly IReadOnlyDictionary<SpendGroup, string[]> Groups = new Dictionary<SpendGroup, string[]>
    {
        [SpendGroup.Fuel] = ["Fuel"],
        [SpendGroup.ServiceAndRepairs] = ["Service", "Repair", "Parts"],
        [SpendGroup.Statutory] = ["Insurance", "Tax", "MOT"],
    };

    private const string PurchaseCategory = "Purchase";

    /// <param name="expenses">
    /// The single source for spend. Fuel spend comes from here — from the mirrored expense rows — never from
    /// summing FuelEntry.TotalCost separately. Two code paths to one number is precisely how the workbook
    /// ended up reporting fuel YTD as both £725.70 and £888.86.
    /// </param>
    public static SpendSummary Calculate(
        IReadOnlyCollection<ExpenseEntry> expenses,
        DateOnly purchaseDate,
        DateOnly referenceDate,
        int? milesSincePurchase)
    {
        // Neither window is closed at today, and that is load-bearing rather than incidental.
        //
        // Both used to end at `referenceDate`. A tyre bill paid in advance and dated four days out was
        // therefore absent from every figure here while the expenses table and the cumulative chart both
        // showed it — £1,183 that the app held and refused to count, silently, with no flag and no note.
        //
        // The deciding argument is CostPerMile below: its denominator comes from MileageCalculator, which does
        // not even ACCEPT a reference date — the newest reading by date wins, full stop. So the odometer from
        // that same service counted while its money did not, and the ratio was a clamped numerator over an
        // unclamped denominator. Clamp one side alone and cost per mile stops meaning anything.
        //
        // "Has the date arrived" is not a test of whether money was spent. A wrong date is now visible (the
        // totals move) instead of invisible (the totals shrink), and AnomalyKind.FutureDatedEntry questions it.
        var ytd = expenses.Where(e => e.EntryDate.Year == referenceDate.Year).ToList();
        var sincePurchase = expenses.Where(e => e.EntryDate >= purchaseDate).ToList();

        var totalSincePurchase = sincePurchase.Sum(e => e.Amount);
        var purchaseCost = sincePurchase.Where(e => e.Category == PurchaseCategory).Sum(e => e.Amount);
        var totalExcludingPurchase = totalSincePurchase - purchaseCost;

        var daysSincePurchase = referenceDate.DayNumber - purchaseDate.DayNumber;
        // Fractional months, floored at one: a part-month would otherwise divide a month's spend by 0.3 and
        // report a wildly inflated monthly average in the first weeks of ownership.
        var months = Math.Max(1m, daysSincePurchase / 365.25m * 12m);

        // Null, not zero and not infinity: a vehicle that has covered no miles has no cost per mile.
        var hasMiles = milesSincePurchase is > 0;

        return new SpendSummary(
            FuelYtd: SumGroup(ytd, SpendGroup.Fuel),
            ServiceAndRepairsYtd: SumGroup(ytd, SpendGroup.ServiceAndRepairs),
            StatutoryYtd: SumGroup(ytd, SpendGroup.Statutory),
            TotalYtd: ytd.Sum(e => e.Amount),
            TotalSincePurchase: totalSincePurchase,
            TotalSincePurchaseExcludingPurchase: totalExcludingPurchase,
            MonthlyAverage: totalSincePurchase / months,
            CostPerMile: hasMiles ? totalSincePurchase / milesSincePurchase!.Value : null,
            CostPerMileExcludingPurchase: hasMiles ? totalExcludingPurchase / milesSincePurchase!.Value : null,
            YtdByCategory: ytd
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)),
            // The running-cost twin of MonthlyAverage. Same denominator — months of ownership, not months in
            // which something was spent — so the two differ only by the car.
            MonthlyAverageExcludingPurchase: totalExcludingPurchase / months);
    }

    private static decimal SumGroup(IEnumerable<ExpenseEntry> expenses, SpendGroup group) =>
        expenses.Where(e => Groups[group].Contains(e.Category)).Sum(e => e.Amount);
}
