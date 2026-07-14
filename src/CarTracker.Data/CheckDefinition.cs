using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// A recurring inspection. Carries no status or next-due column — both derive from the latest
/// <see cref="CheckLog"/> plus <see cref="IntervalDays"/>, and a definition with zero logs is the real
/// fourth state (NeverLogged) the workbook's 17-of-18 count lost.
/// </summary>
public class CheckDefinition : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public required string Name { get; set; }

    public required string CadenceLabel { get; set; }

    public int IntervalDays { get; set; }

    public string? Guidance { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Retire a check without deleting its history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
