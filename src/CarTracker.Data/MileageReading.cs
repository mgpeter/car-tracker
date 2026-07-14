using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Dedicated odometer log (README §2). Current mileage is derived from here — never from a manual field on
/// the vehicle. Deliberately no monotonicity constraint: README §5.3 requires flagging anomalies, not
/// refusing them, and the workbook's 83,000 mi row must import.
/// </summary>
public class MileageReading : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DateOnly ReadingDate { get; set; }

    public int Mileage { get; set; }

    public MileageOrigin Origin { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
