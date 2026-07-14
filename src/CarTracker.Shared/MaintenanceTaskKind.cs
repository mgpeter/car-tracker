namespace CarTracker.Shared;

/// <summary>
/// The DIY/Workshop discriminator on <c>MaintenanceTask</c> (README §2).
/// </summary>
/// <remarks>
/// Named <c>MaintenanceTaskKind</c> rather than the spec's <c>TaskKind</c> for symmetry with
/// <see cref="MaintenanceTaskStatus"/>, which had to avoid <c>System.Threading.Tasks.TaskStatus</c>.
/// </remarks>
public enum MaintenanceTaskKind
{
    DIY = 1,
    Workshop = 2,
}
