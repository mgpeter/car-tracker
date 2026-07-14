using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Garage and DIY service history. <see cref="Type"/> is free text so the workbook's varied descriptions
/// import losslessly — but the literal <c>MOT</c> is load-bearing: derived MOT expiry is the max
/// <see cref="NextDueDate"/> over a vehicle's records with <c>Type = "MOT"</c>.
/// </summary>
public class ServiceRecord : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DateOnly ServiceDate { get; set; }

    /// <summary>
    /// Not constrained monotonic: the workbook's 27 Jun 2026 row logs 83,000 mi against a current 80,712,
    /// and it must import as written, flagged — silently correcting it would destroy the evidence.
    /// </summary>
    public int Mileage { get; set; }

    public required string Type { get; set; }

    public string? Garage { get; set; }

    public string? WorkDone { get; set; }

    public string? PartsReplaced { get; set; }

    public decimal? Cost { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public int? NextDueMileage { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
