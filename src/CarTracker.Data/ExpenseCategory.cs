namespace CarTracker.Data;

/// <summary>
/// An expense category on one account's reference list, editable in settings (README §2).
/// </summary>
/// <remarks>
/// Not <see cref="IAuditable"/> — reference tables carry no audit block per the schema spec. The 13 system
/// categories are no longer migration seed data: they are created per account at provisioning from
/// <see cref="Configuration.ExpenseCategoryConfiguration.SystemCategories"/>, because a seeded row has no owner
/// and there is no sensible owner to invent for one.
/// </remarks>
public class ExpenseCategory
{
    /// <summary>The account this list entry belongs to. Half the primary key.</summary>
    public int OwnerId { get; set; }

    /// <summary>Natural key within the account: rows reference categories by name so a dump stays readable.</summary>
    public required string Name { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>
    /// System categories are ones the domain reasons about by name (notably Fuel, which auto-mirroring
    /// depends on). They may be renamed for display but never deleted.
    /// </summary>
    public bool IsSystem { get; set; }
}
