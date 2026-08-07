using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// One expense. No running-total column — the workbook's is a formula over ~30 trailing blank rows; the
/// replacement is <c>SUM()</c>.
/// </summary>
public class ExpenseEntry : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DateOnly EntryDate { get; set; }

    /// <summary>Natural-key reference into <see cref="ExpenseCategory"/>.</summary>
    public required string Category { get; set; }

    public string? SubCategory { get; set; }

    public string? Vendor { get; set; }

    public decimal Amount { get; set; }

    public int? Mileage { get; set; }

    public string? PaymentMethod { get; set; }

    /// <summary>
    /// The README §3.2 mirroring link. Unique per fill, cascade on fill delete — this is what closes the
    /// £163.16 gap: fuel spend from expenses and from fuel entries are the same rows, not two code paths.
    /// </summary>
    public int? FuelEntryId { get; set; }

    /// <summary>
    /// The same mirroring link, for a service record's cost. Unique per record, cascade on record delete.
    /// </summary>
    /// <remarks>
    /// <see cref="Calculators"/> aside, the reason this exists is that <c>SpendCalculator</c> reads expenses and
    /// nothing else: a <c>ServiceRecord.Cost</c> with no mirror moves no figure anywhere, so £603.99 of cambelt
    /// would be invisible to spend and cost-per-mile. The alternative is typing the cost into two screens and
    /// keeping them in step by hand, which is what the workbook did and why its fuel total was £163.16 out.
    ///
    /// At most one mirror marker is ever set — this, <see cref="FuelEntryId"/>, <see cref="EquipmentItemId"/>,
    /// <see cref="WashEntryId"/> or <see cref="IsVehiclePurchase"/>: a row is mirrored from one thing, or it was
    /// typed and is mirrored from nothing.
    /// </remarks>
    public int? ServiceRecordId { get; set; }

    /// <summary>
    /// The same mirroring link, for an equipment purchase's cost. Unique per item, cascade on item delete — so
    /// kit bought (a cost with a purchase date) counts toward spend, cost-per-mile and the Equipment &amp; Tools
    /// budget, instead of being invisible the way the workbook's separate Equipment sheet was.
    /// </summary>
    public int? EquipmentItemId { get; set; }

    /// <summary>
    /// The same mirroring link, for a wash's cost. Unique per wash, cascade on wash delete.
    /// </summary>
    /// <remarks>
    /// Without it a £12 hand wash was visible on the wash screen and invisible to spend, cost-per-mile and the
    /// budget — while the Budget page's own footer promises "money the app knows about is never hidden".
    /// </remarks>
    public int? WashEntryId { get; set; }

    /// <summary>
    /// Marks the row mirroring <see cref="Vehicle.PurchasePrice"/> — the car itself, the largest single line
    /// there will ever be. A marker rather than an FK because the source is the vehicle this row already points
    /// at; one such row per vehicle, enforced by a partial unique index.
    /// </summary>
    /// <remarks>
    /// <c>SpendCalculator</c> reads expenses and nothing else, so before this existed a stored
    /// <c>PurchasePrice</c> moved no figure anywhere: <c>TotalSincePurchase</c> and
    /// <c>TotalSincePurchaseExcludingPurchase</c> were silently the same number, as were the two cost-per-mile
    /// figures. The category name is deliberately *not* the marker — categories can be renamed, and a renamed
    /// "Purchase" would orphan the mirror the way a renamed "Fuel" would orphan the fills.
    /// </remarks>
    public bool IsVehiclePurchase { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
