namespace CarTracker.Data;

/// <summary>
/// Global reference list, seeded closed and editable in settings (README §2).
/// </summary>
/// <remarks>
/// Not <see cref="IAuditable"/> — reference tables carry no audit block per the schema spec.
/// </remarks>
public class ExpenseCategory
{
    /// <summary>Natural key: rows reference categories by name so a dump stays readable.</summary>
    public required string Name { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>
    /// System categories are ones the domain reasons about by name (notably Fuel, which auto-mirroring
    /// depends on). They may be renamed for display but never deleted.
    /// </summary>
    public bool IsSystem { get; set; }
}
