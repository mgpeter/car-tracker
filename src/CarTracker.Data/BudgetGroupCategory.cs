namespace CarTracker.Data;

/// <summary>
/// One category's membership in a <see cref="BudgetGroup"/>. Structural (no audit block). <see cref="VehicleId"/>
/// is denormalised from the owning group purely to carry the unique index that enforces "each category is in at
/// most one group per vehicle" at the database.
/// </summary>
public class BudgetGroupCategory
{
    public int Id { get; set; }

    public int BudgetGroupId { get; set; }

    /// <summary>Denormalised from the group, so the one-group-per-category unique index can live on this table.</summary>
    public int VehicleId { get; set; }

    /// <summary>Natural-key reference into <see cref="ExpenseCategory"/>.</summary>
    public required string Category { get; set; }
}
