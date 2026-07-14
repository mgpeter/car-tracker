using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CarTracker.WebApi.Authentication;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configured = Options.Value;

        if (string.IsNullOrEmpty(configured))
        {
            // Fail closed. An unconfigured key must never mean "let everyone in" — that is the failure mode
            // where a misconfigured deployment silently has no auth at all.
            Logger.LogError(
                "No {Key} is configured, so every authenticated request will be rejected. Set it via user-secrets in development or an environment variable in production.",
                $"{ApiKeyAuthenticationOptions.Scheme}:{nameof(ApiKeyAuthenticationOptions.Value)}");

            return Task.FromResult(AuthenticateResult.Fail("No API key is configured on the server."));
        }

        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var presented))
        {
            // No credentials offered — distinct from wrong credentials. NoResult lets [AllowAnonymous]
            // endpoints through and produces a challenge (401) on the rest.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!FixedTimeEquals(presented.ToString(), configured))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "owner")],
            ApiKeyAuthenticationOptions.Scheme);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationOptions.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = $"{ApiKeyAuthenticationOptions.Scheme} header=\"{ApiKeyAuthenticationOptions.HeaderName}\"";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Compares without leaking length or content through timing.
    /// </summary>
    /// <remarks>
    /// Overkill for a self-hosted single-user app, but it costs one call and removes a whole question.
    /// <c>FixedTimeEquals</c> requires equal lengths, so the length check short-circuits — that leaks key
    /// length, which is not a secret worth protecting.
    /// </remarks>
    private static bool FixedTimeEquals(string presented, string configured)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);

        return presentedBytes.Length == configuredBytes.Length
               && CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
    }
}
