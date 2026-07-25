using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// A named budget covering one or more expense categories (e.g. "Insurance, Tax &amp; MOT"). Per-vehicle. Only the
/// annual target is stored — YTD actual, remaining and % used all derive from the expense entries of its member
/// categories, exactly as the old per-category budget did. Replaces <c>BudgetCategory</c>: a single-category
/// budget is just a group with one member.
/// </summary>
public class BudgetGroup : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    /// <summary>The group's display name, unique per vehicle. Free text — "Fuel", "Insurance, Tax &amp; MOT".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The annual target, or <c>null</c> for a <b>tracked</b> group — one whose spend is shown but has no target
    /// yet (the state a newly seeded default group is in until the owner sets a number). Null is not zero: zero
    /// means "spend nothing here and tell me when you do".
    /// </summary>
    public decimal? AnnualBudget { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>The member categories. Each category belongs to at most one group per vehicle (a DB unique index).</summary>
    public ICollection<BudgetGroupCategory> Categories { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
