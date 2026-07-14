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

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
