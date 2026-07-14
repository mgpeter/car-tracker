namespace CarTracker.Shared;

/// <summary>
/// Issue watchlist severity. Ordering (Critical worst) is the domain's job, not the database's.
/// </summary>
public enum Severity
{
    Critical = 1,
    Medium = 2,
    Low = 3,
}
