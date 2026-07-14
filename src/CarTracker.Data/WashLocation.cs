namespace CarTracker.Data;

/// <summary>
/// Global reference list. Upserted by the importer from side columns, editable in settings.
/// </summary>
public class WashLocation
{
    public required string Name { get; set; }

    public string? Notes { get; set; }
}
