namespace CarTracker.Shared;

/// <summary>
/// Vehicle lifecycle (DEC-007). Sold and SORN keep their history; they just leave the attention surfaces.
/// </summary>
/// <remarks>
/// Member names match the stored strings exactly (including the SORN acronym) so
/// <c>HasConversion&lt;string&gt;()</c> round-trips without a custom mapper.
/// </remarks>
public enum VehicleStatus
{
    Active = 1,
    Sold = 2,
    SORN = 3,
}
