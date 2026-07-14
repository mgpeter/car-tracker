namespace CarTracker.Shared;

/// <remarks>
/// Member names match the stored strings exactly (including LPG) so <c>HasConversion&lt;string&gt;()</c>
/// round-trips without a custom mapper.
/// </remarks>
public enum FuelType
{
    Petrol = 1,
    Diesel = 2,
    Hybrid = 3,
    Electric = 4,
    LPG = 5,
}
