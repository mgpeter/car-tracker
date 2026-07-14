using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Uploaded file metadata; the bytes live on a mounted volume (DEC-005), path relative to a configured root.
/// Three real nullable link FKs rather than a polymorphic pair — README §3.9 names exactly three targets,
/// and real FKs get real referential integrity. Links are severed (SET NULL), never cascaded: the MOT
/// certificate outlives the service row.
/// </summary>
public class Document : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DocumentType Type { get; set; }

    public required string Title { get; set; }

    public DateOnly? DocumentDate { get; set; }

    public required string FilePath { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Duplicate detection and backup verification; null until computed.</summary>
    public string? Sha256 { get; set; }

    public int? ServiceRecordId { get; set; }

    public int? ExpenseEntryId { get; set; }

    public int? IssueId { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
