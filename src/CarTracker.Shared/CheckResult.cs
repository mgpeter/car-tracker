namespace CarTracker.Shared;

/// <summary>
/// Outcome of a performed check. Nullable on the log — a bare "done" with no observation is the common case.
/// </summary>
public enum CheckResult
{
    OK = 1,
    Attention = 2,
    Failed = 3,
}
