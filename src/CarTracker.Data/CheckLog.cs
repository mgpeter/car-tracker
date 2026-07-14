using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// One performance of a check. Scoped through its definition rather than carrying its own vehicle id —
/// the definition is already vehicle-scoped and a second path would let the two disagree.
/// </summary>
public class CheckLog : IAuditable
{
    public int Id { get; set; }

    public int CheckDefinitionId { get; set; }

    public DateOnly PerformedOn { get; set; }

    public CheckResult? Result { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
