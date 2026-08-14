namespace CarTracker.Data;

/// <summary>
/// A wash location on one account's reference list, created as it is used and editable in settings.
/// </summary>
/// <remarks>
/// Keyed <c>(OwnerId, Name)</c> for the reason <see cref="Garage"/> records. <see cref="WashEntry.Location"/>
/// carries the name alone and is no longer a foreign key.
/// </remarks>
public class WashLocation
{
    /// <summary>The account this list entry belongs to. Half the primary key.</summary>
    public int OwnerId { get; set; }

    public required string Name { get; set; }

    public string? Notes { get; set; }
}
