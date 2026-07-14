using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Stores only the annual target. YTD actual, remaining and % used all derive from expense entries —
/// a stored YTD is the same defect class as the workbook's doubled litres.
/// </summary>
public class BudgetCategory : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    /// <summary>Natural-key reference into <see cref="ExpenseCategory"/>.</summary>
    public required string Category { get; set; }

    public decimal AnnualBudget { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
