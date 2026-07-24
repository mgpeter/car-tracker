using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarTracker.WebApi.Authentication;

/// <summary>
/// Hashes assistant tokens. Only the hash is stored, so the secret is unrecoverable from the database; the same
/// hash is computed on creation (to store) and on every request (to look up).
/// </summary>
public static class AssistantTokenHasher
{
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// The claims an authenticated assistant token carries. The MCP authorization policies check the scope claims,
/// never the scheme, which is the seam a future Auth0/JWT scheme drops into unchanged (DEC-014).
/// </summary>
public static class AssistantClaims
{
    public const string Scope = "scope";
    public const string ScopeRead = "mcp:read";
    public const string ScopeWrite = "mcp:write";
    public const string TokenId = "assistant_token_id";

    /// <summary>The local <see cref="User"/> id the token acts as — how the request resolves to an owner.</summary>
    public const string UserId = "user_id";
}

/// <summary>
/// Authenticates the assistant's <c>Authorization: Bearer &lt;token&gt;</c> against the hashed
/// <see cref="AssistantToken"/> table (README §5.1). A read-only token gets the <c>mcp:read</c> claim; a
/// read-write token also gets <c>mcp:write</c>. Separate from the web front-end's <c>X-Api-Key</c> (DEC-009).
/// </summary>
public sealed class AssistantTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "AssistantToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return AuthenticateResult.NoResult();

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var presented = value["Bearer ".Length..].Trim();
        if (presented.Length == 0)
            return AuthenticateResult.NoResult();

        var hash = AssistantTokenHasher.Hash(presented);

        var context = Context.RequestServices.GetRequiredService<CarTrackerDbContext>();
        var token = await context.AssistantTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);

        if (token is null)
            return AuthenticateResult.Fail("Unknown or revoked assistant token.");

        // Meaningful "last used" for the ASSISTANT ACCESS panel, and a coarse usage count. A single-user app can
        // afford the write; a leaked token's last-used jumping is itself a signal.
        token.LastUsedAt = TimeProvider.GetUtcNow();
        token.ReadCount++;
        await context.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, token.Name),
            new(AssistantClaims.TokenId, token.Id.ToString()),
            new(AssistantClaims.Scope, AssistantClaims.ScopeRead),
        };
        if (token.Scope == AssistantScope.ReadWrite)
            claims.Add(new Claim(AssistantClaims.Scope, AssistantClaims.ScopeWrite));
        // The owner the token acts as. A legacy (pre-multi-user) token has no owner and so carries no claim —
        // it authenticates but resolves no vehicles, the safe default.
        if (token.OwnerId is int ownerId)
            claims.Add(new Claim(AssistantClaims.UserId, ownerId.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = $"Bearer realm=\"{Scheme}\"";
        return Task.CompletedTask;
    }
}
