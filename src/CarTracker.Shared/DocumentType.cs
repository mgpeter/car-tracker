namespace CarTracker.Shared;

/// <remarks>
/// Member names match the stored strings exactly (V5C, MOT) so <c>HasConversion&lt;string&gt;()</c>
/// round-trips without a custom mapper.
/// </remarks>
public enum DocumentType
{
    V5C = 1,
    Insurance = 2,
    MOT = 3,
    Receipt = 4,
    Photo = 5,
    Manual = 6,
    Other = 7,
}
