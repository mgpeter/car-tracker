using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// A flagged data problem, with a lifecycle.
/// </summary>
/// <remarks>
/// Rehomed from the deleted importer spec (DEC-008), because it never belonged to the importer: README §5.3
/// requires write paths to validate mileage monotonicity and flag anomalies rather than accept them silently,
/// and §3.2 wants the fuel quick-add to warn on outliers. Both are ordinary application behaviour.
/// </remarks>
public class DataAnomaly : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public AnomalyKind Kind { get; set; }

    public AnomalySeverity Severity { get; set; }

    /// <summary>The entity this concerns, e.g. <c>MileageReading</c>.</summary>
    public required string EntityType { get; set; }

    /// <summary>
    /// Nullable: a flag may outlive or precede the row it concerns.
    /// </summary>
    /// <remarks>
    /// A polymorphic pair here, where <see cref="Document"/> uses three real FKs — deliberately inconsistent.
    /// §3.9 names exactly three link targets for a document, so real FKs get real integrity. An anomaly can
    /// attach to any entity, and a real FK cannot point at a row that was never written.
    /// </remarks>
    public int? EntityId { get; set; }

    public required string Message { get; set; }

    /// <summary>Structured context — the two figures that disagree, say. Never queried relationally.</summary>
    public string? Detail { get; set; }

    public AnomalyStatus Status { get; set; } = AnomalyStatus.Open;

    /// <summary>Constrained: set iff <see cref="Status"/> is terminal.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Where the reasoning lives once someone decides.</summary>
    public string? ResolutionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
