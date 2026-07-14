using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Watchlist item — a symptom tracked over months before it becomes a repair.
/// </summary>
public class Issue : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public required string Title { get; set; }

    public Severity Severity { get; set; }

    public DateOnly FirstNoted { get; set; }

    public DateOnly? LastChecked { get; set; }

    public string? CurrentObservation { get; set; }

    public string? ActionIfWorsens { get; set; }

    public decimal? EstimatedFixCost { get; set; }

    public IssueStatus Status { get; set; }

    /// <summary>Constrained: present iff <see cref="Status"/> is Resolved.</summary>
    public DateOnly? ResolvedDate { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
