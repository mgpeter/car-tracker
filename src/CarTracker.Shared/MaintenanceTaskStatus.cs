namespace CarTracker.Shared;

/// <remarks>
/// The spec calls this <c>TaskStatus</c>, but that name is ambiguous with
/// <c>System.Threading.Tasks.TaskStatus</c> under implicit usings — the same collision that renamed the
/// entity <c>Task</c> → <c>MaintenanceTask</c>.
/// </remarks>
public enum MaintenanceTaskStatus
{
    Open = 1,
    InProgress = 2,
    Scheduled = 3,
    Done = 4,
}
