namespace CarTracker.Data;

/// <summary>
/// An application user — the owner of vehicles and of the assistant tokens scoped to them. Identity is
/// federated to Auth0: <see cref="ExternalId"/> is the access token's stable <c>sub</c> claim, and a row is
/// created just in time the first time a validated token for a new subject reaches the API. No password or
/// secret lives here — authentication is Auth0's, ownership is ours.
/// </summary>
/// <remarks>
/// Not <see cref="IAuditable"/>: like the reference tables, this is an identity row, not one of README §6's
/// mutable domain entities. It carries a single <see cref="CreatedAt"/>, stamped at provisioning.
/// </remarks>
public sealed class User
{
    public int Id { get; set; }

    /// <summary>The Auth0 subject (<c>sub</c>) — stable per identity and unique. The join between a JWT and a row.</summary>
    public required string ExternalId { get; set; }

    public required string Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
