using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Unified DIY/Workshop task per README §2 — a single CLR type with a <see cref="Kind"/> discriminator,
/// not EF inheritance. Named MaintenanceTask because System.Threading.Tasks.Task collides.
/// </summary>
public class MaintenanceTask : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public MaintenanceTaskKind Kind { get; set; }

    public Priority Priority { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public decimal? EstimatedCost { get; set; }

    public MaintenanceTaskStatus Status { get; set; }

    public DateOnly? TargetDate { get; set; }

    public string? TargetService { get; set; }

    /// <summary>Constrained: present iff <see cref="Status"/> is Done.</summary>
    public DateOnly? CompletedDate { get; set; }

    /// <summary>Constrained: only a Workshop task may carry a garage.</summary>
    public string? AssignedGarage { get; set; }

    /// <summary>README §3.3's one-click promotion link; preserved after promotion.</summary>
    public int? ServiceRecordId { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
