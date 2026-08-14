namespace CarTracker.Data;

/// <summary>
/// A garage on one account's reference list, created as it is used and editable in settings.
/// </summary>
/// <remarks>
/// Keyed <c>(OwnerId, Name)</c>: the list belongs to an account, so two users may each keep their own
/// "K &amp; P Motors" without one adopting the other's address, contact and notes. The columns pointing here
/// (<see cref="ServiceRecord.Garage"/>, <see cref="MaintenanceTask.AssignedGarage"/>,
/// <see cref="Vehicle.DefaultGarage"/>) carry the name alone and are deliberately no longer foreign keys —
/// see the DEC recorded with the per-owner reference lists.
/// </remarks>
public class Garage
{
    /// <summary>The account this list entry belongs to. Half the primary key.</summary>
    public int OwnerId { get; set; }

    public required string Name { get; set; }

    public string? Contact { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }
}
