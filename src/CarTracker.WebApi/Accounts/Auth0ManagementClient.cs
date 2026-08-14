using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarTracker.Domain.Accounts;

namespace CarTracker.WebApi.Accounts;

/// <summary>
/// Reads a subject's real profile from Auth0's Management API.
/// </summary>
/// <remarks>
/// <para>
/// <b>The call is server-side and there is no other option.</b> A Management API credential can read and delete
/// every account in the tenant, so it must never reach a browser — and the app's strict CSP forbids a
/// browser→<c>auth0.com</c> management fetch outright regardless.
/// </para>
/// <para>
/// <b>It degrades to silence.</b> No credential, an unknown subject and an unreachable tenant all return null,
/// because all three mean the same thing to every caller: the address is unknown. What a caller must not do is
/// read "unknown" as "fine" — <see cref="SignupPolicy.Admits"/> refuses a null address, which is what makes an
/// unconfigured deployment closed rather than open.
/// </para>
/// <para>
/// <b>The client-credentials token is cached for its own lifetime.</b> It was originally fetched per call, on
/// the reasoning that provisioning happens once per account and deletion once per account ever — which is true
/// of the calls that <i>succeed</i>. A subject who is refused leaves no row by design, so nothing remembers the
/// refusal and the next request looks the subject up again; idling on the "not yet invited" panel with
/// <c>refetchOnWindowFocus</c> on turns that into a lookup per tab focus, each paying for a fresh token. Two
/// Management calls per request against a tenant with rate limits is how a throttle arrives, and a throttled
/// tenant answers nothing — which this class correctly reads as "address unknown" and the door correctly
/// refuses, so the uninvited visitor's traffic ends up shutting out an invited newcomer. The token cache halves
/// it; <see cref="SignupRefusalCache"/> is the other half and stops the loop at its source.
/// </para>
/// </remarks>
public sealed class Auth0ManagementClient(
    IHttpClientFactory httpClientFactory,
    Auth0ManagementOptions options,
    TimeProvider clock,
    ILogger<Auth0ManagementClient> logger) : IIdentityProviderClient
{
    public const string ManagementClient = "auth0-management";
    public const string TokenClient = "auth0-management-token";

    /// <summary>
    /// Taken off the token's own lifetime, so a token is never presented in the last moments of its validity —
    /// a clock a few seconds out at either end would otherwise produce an occasional inexplicable 401.
    /// </summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <remarks>
    /// One immutable object rather than a string and a timestamp side by side: the fast path reads it outside
    /// the gate, and two fields cannot be read consistently without one. <c>volatile</c> for the publication,
    /// <see cref="_tokenGate"/> so a burst of concurrent misses fetches once rather than once each.
    /// </remarks>
    private volatile CachedToken? _cachedToken;

    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    public bool IsConfigured => options.IsConfigured;

    public async Task<IdentityProfile?> GetProfileAsync(
        string externalId,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            // Information, not warning: on a deployment with a closed allowlist this is the designed state, and
            // a warning on every unseen subject would train someone to ignore the log.
            logger.LogInformation(
                "Auth0 Management is not configured (Auth0:Management:ClientId/ClientSecret), so no email "
                + "address can be resolved for {Subject} — it will not be admitted.", externalId);
            return null;
        }

        try
        {
            var token = await GetTokenAsync(cancellationToken);
            if (token is null) return null;

            var client = httpClientFactory.CreateClient(ManagementClient);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);

            // A subject contains a '|' ("auth0|68a…"), which is a path segment separator's worth of trouble if
            // it goes in raw — escaped, not interpolated.
            using var response = await client.GetAsync(
                $"users/{Uri.EscapeDataString(externalId)}", cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound) return null;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Auth0 Management returned {Status} for {Subject}; the address stays unknown.",
                    (int)response.StatusCode, externalId);
                return null;
            }

            var user = await response.Content.ReadFromJsonAsync<Auth0User>(Json, cancellationToken);
            return user is null ? null : new IdentityProfile(externalId, user.Email, user.Name, user.EmailVerified);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Auth0 Management lookup failed for {Subject}; the address stays unknown.", externalId);
            return null;
        }
    }

    /// <remarks>
    /// <para>
    /// <b>404 is success.</b> Deletion is idempotent at the provider and has to be here too: a retry that runs
    /// after the first attempt actually worked would otherwise never clear its pending row, and would go on
    /// asking forever about an identity nobody can sign in as.
    /// </para>
    /// <para>
    /// Everything else that goes wrong is <see cref="IdentityDeletionOutcome.Failed"/> rather than an exception,
    /// because the caller has already deleted the data by the time this runs. There is nothing to roll back and
    /// nothing to abort — the only useful thing to do with a failure is write it down and try again later, which
    /// is exactly what the pending row is.
    /// </para>
    /// </remarks>
    public async Task<IdentityDeletionResult> DeleteUserAsync(
        string externalId,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            return new IdentityDeletionResult(IdentityDeletionOutcome.NotConfigured,
                "Auth0:Management:ClientId/ClientSecret are not set, so the login behind this account cannot "
                + "be removed.");
        }

        try
        {
            var token = await GetTokenAsync(cancellationToken);
            if (token is null) return IdentityDeletionResult.Failed("Could not obtain a Management API token.");

            var client = httpClientFactory.CreateClient(ManagementClient);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);

            using var response = await client.DeleteAsync(
                $"users/{Uri.EscapeDataString(externalId)}", cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound)
            {
                logger.LogInformation("Auth0 identity {Subject} deleted (status {Status}).",
                    externalId, (int)response.StatusCode);
                return IdentityDeletionResult.Deleted;
            }

            // The status alone: the body of a Management API error can echo back the request, and this string is
            // stored on a row. The commonest cause is the M2M application lacking the delete:users grant.
            logger.LogWarning("Auth0 Management refused to delete {Subject} with {Status}; queued for retry.",
                externalId, (int)response.StatusCode);
            return IdentityDeletionResult.Failed(
                $"Auth0 Management returned {(int)response.StatusCode}. Check that the M2M application holds "
                + "the delete:users grant.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Auth0 Management was unreachable deleting {Subject}; queued for retry.", externalId);
            return IdentityDeletionResult.Failed($"Auth0 Management was unreachable: {ex.GetType().Name}.");
        }
    }

    /// <remarks>
    /// <para>
    /// Client credentials against the same tenant that issued the request's access token. The identity-deletion
    /// half calls this too — one credential, one token flow, one place to get it wrong.
    /// </para>
    /// <para>
    /// A live cached token short-circuits the whole thing. <b>A failure is never cached</b>: the null is
    /// returned and the next call tries again, because caching "we could not get a token" would turn a minute
    /// of tenant trouble into a fixed outage of exactly the length of the cache.
    /// </para>
    /// </remarks>
    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (LiveToken() is { } cached) return cached;

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            // Re-checked inside the gate: everyone queued behind the one request that went out uses its answer.
            if (LiveToken() is { } justFetched) return justFetched;

            var client = httpClientFactory.CreateClient(TokenClient);

            using var response = await client.PostAsync(options.TokenUrl, new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", options.ClientId!),
                new KeyValuePair<string, string>("client_secret", options.ClientSecret!),
                new KeyValuePair<string, string>("audience", options.ResolvedAudience),
            ]), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The common causes are all configuration: the M2M application is not authorised for the
                // Management API, or the audience is missing its trailing slash. Say the status; the body may
                // carry a secret.
                logger.LogWarning(
                    "Auth0 Management token request failed with {Status}. Check that the M2M application is "
                    + "authorised for {Audience} and holds the read:users and delete:users grants.",
                    (int)response.StatusCode, options.ResolvedAudience);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken);
            if (payload?.AccessToken is not { Length: > 0 } token) return null;

            // A tenant that sends no `expires_in` leaves ExpiresIn at 0, which expires the entry before it is
            // stored — so an unreadable lifetime degrades to the per-call behaviour this replaced rather than to
            // a token held past its death. Auth0's management tokens are good for 24 hours.
            _cachedToken = new CachedToken(
                token, clock.GetUtcNow() + TimeSpan.FromSeconds(payload.ExpiresIn) - ExpiryMargin);

            return token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private string? LiveToken() =>
        _cachedToken is { } t && t.ExpiresAt > clock.GetUtcNow() ? t.Value : null;

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    // The upstream shapes, only as far as this reads them.
    //
    // `email_verified` needs its name spelling out and `email`/`name` do not: JsonSerializerDefaults.Web matches
    // case-insensitively, which reconciles `Email` with `email` but never `EmailVerified` with `email_verified`
    // — an underscore is a character, not a case. Unnamed it would bind nothing, sit at false, and refuse every
    // newcomer on the deployment. Fail-safe, and still wrong.
    private sealed record Auth0User(
        string? Email,
        string? Name,
        [property: JsonPropertyName("email_verified")] bool EmailVerified);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
