namespace CarTracker.Domain.Accounts;

/// <summary>What the identity provider knows about a subject that the access token does not carry.</summary>
/// <param name="EmailVerified">
/// Whether the tenant has confirmed the person controls <paramref name="Email"/>. No default, deliberately:
/// either value would be a guess made silently at every construction site, and one of them opens the invitation
/// door to anyone who can type an address at a self-service sign-up form.
/// </param>
public sealed record IdentityProfile(string ExternalId, string? Email, string? DisplayName, bool EmailVerified);

/// <summary>
/// The seam onto the identity provider's management surface — the operations that are about an <i>account</i>
/// rather than about a request.
/// </summary>
/// <remarks>
/// <para>
/// It exists because <b>the access token carries no email address</b>. Auth0 puts <c>email</c> in an access
/// token only when the tenant is configured with an Action to add it, and this one is not, so the API sees
/// nothing but a <c>sub</c> of the form <c>auth0|68…</c>. Everything that needs to know who a person actually
/// is — the invitation allowlist, the deletion confirmation, the account panel — needs the real address, and
/// the only place holding it is the tenant.
/// </para>
/// <para>
/// The vocabulary is here, in the domain, and the HTTP is in <c>CarTracker.WebApi/Accounts/</c>, the same split
/// <see cref="Lookup.IVehicleLookupService"/> makes and for the same reason: the decision that reads the answer
/// is testable, the transport is not worth faking.
/// </para>
/// <para>
/// <b>Deletion lands on this interface too.</b> Erasing an account has to erase the login behind it, which is
/// another Management API call under the very same M2M credential — so the client is built to hold the
/// credential and hand out tokens rather than to answer one question, and the deletion half is a method here
/// rather than a second client with a second copy of the configuration.
/// </para>
/// </remarks>
public interface IIdentityProviderClient
{
    /// <summary>
    /// False when this deployment has no management credential, which is a fresh checkout's normal state.
    /// </summary>
    /// <remarks>
    /// Callers check it to avoid an HTTP call that cannot succeed, not to decide policy: an unconfigured
    /// provider means an address cannot be resolved, and an address that cannot be resolved is not on the
    /// invitation list. Unconfigured is therefore *closed*, exactly as an empty allowlist is.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>
    /// The profile behind an external id, or null when it cannot be read — unconfigured, unknown, or upstream
    /// unavailable. The three collapse deliberately: every one of them means "we do not know this address",
    /// and no caller may treat any of them as permission.
    /// </summary>
    Task<IdentityProfile?> GetProfileAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the login itself, so a deleted account cannot sign back in and re-provision under the same subject.
    /// </summary>
    /// <remarks>
    /// The three outcomes are kept apart because the callers do different things with them. <b>Unconfigured</b>
    /// is checked before anything is deleted and refuses the whole operation, so a deployment with no credential
    /// never destroys the data and leaves the login standing. <b>Failed</b> is recoverable and is what
    /// <see cref="Data.PendingIdentityDeletion"/> exists for. <b>Deleted</b> includes the provider answering
    /// "no such user": deletion is idempotent, and an identity that is already gone is the outcome we wanted.
    /// </remarks>
    Task<IdentityDeletionResult> DeleteUserAsync(string externalId, CancellationToken cancellationToken = default);
}

/// <summary>How an attempt to erase a login ended.</summary>
public enum IdentityDeletionOutcome
{
    /// <summary>The login is gone — either this call removed it, or it was already absent.</summary>
    Deleted = 1,

    /// <summary>No management credential on this deployment. Nothing was attempted and nothing can be.</summary>
    NotConfigured = 2,

    /// <summary>The provider refused or could not be reached. Worth retrying; the identity is still there.</summary>
    Failed = 3,
}

/// <param name="Detail">Why it failed, short enough to store on the pending row. Never carries a credential.</param>
public sealed record IdentityDeletionResult(IdentityDeletionOutcome Outcome, string? Detail = null)
{
    public static readonly IdentityDeletionResult Deleted = new(IdentityDeletionOutcome.Deleted);

    public static IdentityDeletionResult Failed(string detail) => new(IdentityDeletionOutcome.Failed, detail);
}

/// <summary>
/// The machine-to-machine credential for Auth0's Management API. Server-side only; never reaches the browser.
/// </summary>
/// <remarks>
/// Absent is the normal state of a fresh checkout and of CI, and the feature degrades rather than the app
/// refusing to start — the same posture <see cref="Lookup.VehicleLookupOptions"/> takes, with one difference
/// worth stating: an unconfigured lookup means a form is typed by hand, while an unconfigured management
/// credential means no address can be resolved and so nobody new is admitted. Both degrade safely; only one of
/// them degrades to "nothing happens".
/// </remarks>
public sealed class Auth0ManagementOptions
{
    /// <summary>The tenant, e.g. <c>https://usualexpat.uk.auth0.com/</c>. Defaults to <c>Auth0:Authority</c>.</summary>
    /// <remarks>
    /// The same tenant that issues the access tokens, so it is not configured separately in the ordinary case —
    /// the host seeds it from the authority it already resolved. Two settings for one tenant is how a deployment
    /// ends up validating tokens against one and managing users in another.
    /// </remarks>
    public string Authority { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>The Management API audience. Defaults to <c>{Authority}api/v2/</c>, which is what Auth0 issues.</summary>
    public string? Audience { get; set; }

    /// <summary>True when the M2M application's credentials are both present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>The API root, trailing slash included: <c>{Authority}api/v2/</c>.</summary>
    public string ManagementBaseUrl => $"{Authority.TrimEnd('/')}/api/v2/";

    /// <summary>The tenant's client-credentials token endpoint.</summary>
    public string TokenUrl => $"{Authority.TrimEnd('/')}/oauth/token";

    /// <summary>The audience a token is requested for — the configured override, or the API root.</summary>
    public string ResolvedAudience =>
        string.IsNullOrWhiteSpace(Audience) ? ManagementBaseUrl : Audience;
}
