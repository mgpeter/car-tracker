namespace CarTracker.Shared;

/// <summary>
/// How full the tank was after a fill. MPG is only reliable between two Full fills.
/// </summary>
public enum FillLevel
{
    Full = 1,
    Half = 2,
    Quarter = 3,
}
