namespace CarTracker.Shared;

/// <summary>
/// Which log produced a mileage reading. Distinct from <see cref="EntrySource"/>: a fill-up logged via MCP
/// is <c>origin = fuel</c>, <c>source = mcp</c>. Both matter and neither substitutes for the other.
/// </summary>
public enum MileageOrigin
{
    Manual = 1,
    Fuel = 2,
    Tyre = 3,
    Wash = 4,
    Service = 5,

    /// <summary>
    /// The founding reading, created with the vehicle from its purchase mileage.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Manual"/> on purpose. "The odometer read 76,632 when I bought it" is a
    /// purchase record; "I typed 80,705 in" is an observation someone made later. Collapsing them loses the
    /// ability to answer where the car started — which is what miles-since-purchase rests on.
    /// </remarks>
    Purchase = 6,
}
