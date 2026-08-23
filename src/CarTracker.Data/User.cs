using CarTracker.Shared;

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

    /// <summary>
    /// The tier an administrator has pinned this account to, or null when none has decided anything about it.
    /// </summary>
    /// <remarks>
    /// <b>A stored input, not a stored derived value</b> (DEC-023). The resolved plan is still computed on
    /// every request by <c>PlanResolver</c> from this, the comp list and a verified
    /// address, and is stored nowhere. This column is the same <i>kind</i> of thing <c>Plans:CompEmails</c>
    /// already is; it differs only in living in a table rather than in a container's environment, which is
    /// exactly what makes it editable without a restart. DEC-002 is untouched.
    /// <para>
    /// <b>Null is not <see cref="Shared.AccountPlan.Free"/>.</b> An account with no override falls
    /// through to the comp list and can be promoted by a configuration change; one overridden to Free is
    /// pinned below whatever the list says. That second state is why this is a nullable plan rather than a
    /// boolean, and it is the only way to answer a domain comp entry that has caught somebody it should not.
    /// </para>
    /// </remarks>
    public Shared.AccountPlan? PlanOverride { get; set; }

    /// <summary>
    /// Which version of the published legal documents was in force when this account was provisioned, or null.
    /// </summary>
    /// <remarks>
    /// <b>A stored observation, not a stored derived value</b> - the same sentence <see cref="LastSeenAt"/>
    /// carries, in a file otherwise entirely about deriving on read. Nothing recomputes it and there is no
    /// underlying record it could disagree with, because the provisioning <i>is</i> the record.
    /// <para>
    /// <b>Null is load-bearing and means one of two true things</b>: the account was created before these
    /// documents existed, or it was created on a deployment that publishes none (DEC-024). <b>It is never
    /// backfilled.</b> Writing the current version into those rows would assert that somebody accepted a text
    /// that did not exist when they signed up, and being able to tell those rows apart is the entire reason
    /// for the column.
    /// </para>
    /// <para>
    /// Not a boolean, which could not say <i>which</i> terms and would go stale silently the first time the
    /// wording changed - the case this exists for. Not a second timestamp either: <see cref="CreatedAt"/> is
    /// stamped in the same place at the same moment, and two columns for one event is two things to keep true.
    /// </para>
    /// </remarks>
    public string? TermsVersion { get; set; }

    /// <summary>When this account was last seen making an authenticated request. Null means never, since 0.26.0.</summary>
    /// <remarks>
    /// <b>An observation, not a derived value</b>, which is worth saying in a file otherwise entirely about the
    /// derive-on-read premise. Nothing recomputes when somebody signs in and there is no underlying record this
    /// could disagree with, because the sign-in is the record.
    /// <para>
    /// Deriving it instead from <c>chat_usage</c>, <c>assistant_tokens.last_used_at</c> and the
    /// <c>IAuditable</c> timestamps on rows the account created needs no column and was rejected: it is blind
    /// to somebody who signs in, reads their dashboard and writes nothing, which is most of a first session and
    /// precisely the visit worth knowing about.
    /// </para>
    /// <para>
    /// Written on the authenticated request path but <b>coalesced to fifteen minutes</b> against the stored
    /// value - see <c>AccountProvisioner.TouchLastSeenAsync</c>. Compared against the column rather than a
    /// cache so the coalescing survives a container recreate, which is the reasoning <c>chat_usage</c> already
    /// records about being a table rather than a counter.
    /// </para>
    /// </remarks>
    public DateTimeOffset? LastSeenAt { get; set; }
}
