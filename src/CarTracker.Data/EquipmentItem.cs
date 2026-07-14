using CarTracker.Shared;

namespace CarTracker.Data;

public class EquipmentItem : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public required string Name { get; set; }

    public string? Category { get; set; }

    public DateOnly? PurchasedDate { get; set; }

    /// <summary>Named to avoid colliding with the audit <see cref="Source"/> column.</summary>
    public string? SourceVendor { get; set; }

    public decimal? Cost { get; set; }

    public string? StoredAt { get; set; }

    public EquipmentStatus Status { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
