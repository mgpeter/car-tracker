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
}
