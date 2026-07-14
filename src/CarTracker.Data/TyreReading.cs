using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Five pressures and four treads per README §2 — the spare has a pressure but no tracked tread. Flat
/// columns rather than a child table: four wheels is not a variable-cardinality relationship.
/// </summary>
public class TyreReading : IAuditable
{
    public int Id { get; set; }

    public int VehicleId { get; set; }

    public DateOnly ReadingDate { get; set; }

    public int? Mileage { get; set; }

    public decimal? PsiFrontLeft { get; set; }
    public decimal? PsiFrontRight { get; set; }
    public decimal? PsiRearLeft { get; set; }
    public decimal? PsiRearRight { get; set; }
    public decimal? PsiSpare { get; set; }

    public decimal? TreadFrontLeft { get; set; }
    public decimal? TreadFrontRight { get; set; }
    public decimal? TreadRearLeft { get; set; }
    public decimal? TreadRearRight { get; set; }

    public string? Location { get; set; }

    public string? Tool { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public EntrySource Source { get; set; }
}
