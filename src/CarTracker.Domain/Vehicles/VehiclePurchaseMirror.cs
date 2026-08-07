using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Vehicles;

/// <summary>
/// Keeps the expense row mirroring <see cref="Vehicle.PurchasePrice"/> in step with the vehicle — the fourth
/// mirror, beside fuel, service and equipment.
/// </summary>
/// <remarks>
/// <para>
/// <c>SpendCalculator</c> reads expenses and nothing else. Before this existed, a stored purchase price moved no
/// figure anywhere: <c>TotalSincePurchase</c> and <c>TotalSincePurchaseExcludingPurchase</c> were silently the
/// same number, as were the two cost-per-mile figures, and the dashboard's "including the £1,700 car itself"
/// clause — conditional on those two totals differing — simply never rendered. The failure looked like nothing.
/// </para>
/// <para>
/// One method, both callers: <see cref="VehicleFactory"/> at create and <see cref="VehicleUpdateService"/> at
/// edit. A separate create-time path and edit-time path is how two numbers for one fact start, which is the
/// whole reason the spend figures are derived rather than stored.
/// </para>
/// </remarks>
public sealed class VehiclePurchaseMirror(CarTrackerDbContext context)
{
    /// <summary>The category a mirrored purchase is filed under. Seeded, <c>IsSystem</c>, and rename-locked.</summary>
    public const string PurchaseCategory = "Purchase";

    /// <summary>
    /// Creates, updates or removes the vehicle's purchase expense so it matches the vehicle. Does not save —
    /// the caller owns the transaction, so the mirror lands with the write that caused it or not at all.
    /// </summary>
    /// <param name="source">
    /// Stamped on a newly created row. An existing row keeps the source it was created with: the mirror records
    /// where the purchase was first entered, not who last touched an unrelated field on the vehicle.
    /// </param>
    public async Task SyncAsync(Vehicle vehicle, EntrySource source, CancellationToken cancellationToken = default)
    {
        // The flag, not the category name, identifies the row. Categories can be renamed (only Fuel is
        // rename-locked today), and resolving the mirror by a renameable string is how the fills would have
        // silently stopped filing — the same trap, one table over.
        var existing = await context.ExpenseEntries
            .SingleOrDefaultAsync(e => e.VehicleId == vehicle.Id && e.IsVehiclePurchase, cancellationToken);

        if (vehicle.PurchasePrice is not { } price)
        {
            // No price on record means no purchase row. A mirror is a shadow: it cannot outlive its source.
            if (existing is not null) context.ExpenseEntries.Remove(existing);
            return;
        }

        if (existing is null)
        {
            context.ExpenseEntries.Add(new ExpenseEntry
            {
                VehicleId = vehicle.Id,
                EntryDate = vehicle.PurchaseDate,
                Category = PurchaseCategory,
                Vendor = vehicle.Seller,
                Amount = price,
                // The odometer at purchase. It is the same reading VehicleFactory writes as the founding
                // MileageReading, so the expense row and the mileage log agree about the day the car arrived.
                Mileage = vehicle.PurchaseMileage,
                IsVehiclePurchase = true,
                Source = source,
            });
            return;
        }

        existing.EntryDate = vehicle.PurchaseDate;
        existing.Vendor = vehicle.Seller;
        existing.Amount = price;
        existing.Mileage = vehicle.PurchaseMileage;
        // Category is deliberately reasserted: a rename that slipped past the lock would otherwise leave the row
        // filed under a name SpendCalculator no longer splits out of the running-cost figures.
        existing.Category = PurchaseCategory;
    }
}
