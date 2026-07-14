using CarTracker.Shared;

namespace CarTracker.Data;

public class WashEntry : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DateOnly WashDate { get; set; }

    /// <summary>Natural-key reference into <see cref="WashLocation"/>; kept on location delete.</summary>
    public string? Location { get; set; }

    public string? WashType { get; set; }

    public decimal? Cost { get; set; }

    public int? Mileage { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
