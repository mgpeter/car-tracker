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

    /// <summary>
    /// The person's address, or - when none could be resolved - a copy of <see cref="ExternalId"/>.
    /// </summary>
    /// <remarks>
    /// That fallback is a deliberate sentinel rather than a shrug: an access token on this tenant carries no
    /// <c>email</c> claim, so the address comes from the Management API, and a deployment with no Management
    /// credential must still be able to create an account under open sign-up. <c>Email == ExternalId</c> is an
    /// equality no real address can satisfy, which is what lets <c>AccountProvisioner.BackfillEmailAsync</c>
    /// recognise such a row with certainty and repair it on a later request.
    /// </remarks>
    public required string Email { get; set; }

    /// <summary>
    /// Whether the identity provider has confirmed the person controls <see cref="Email"/>.
    /// </summary>
    /// <remarks>
    /// <b>Stored rather than asked per request, and it is what makes an address mean anything.</b> A comp or
    /// invitation list written as a domain would otherwise hand entitlement to anyone willing to register as
    /// <c>anything@that-domain</c> - an allowlist satisfiable by typing is not an allowlist. Asking the tenant
    /// on every request would put a rate-limited network call on the read path of a plan check; a column costs
    /// one bool and is refreshed opportunistically by the same backfill that repairs the address.
    /// <para>
    /// Defaults to false, which is the fail-safe direction: an account whose verification cannot be established
    /// is on the free tier rather than the paid one.
    /// </para>
    /// </remarks>
    public bool EmailVerified { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
