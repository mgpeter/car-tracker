namespace CarTracker.Data;

/// <summary>
/// Global reference list. Upserted by the importer from side columns, editable in settings.
/// </summary>
public class Garage
{
    public required string Name { get; set; }

    public string? Contact { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }
}
