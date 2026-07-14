using Microsoft.AspNetCore.Authentication;

namespace CarTracker.WebApi.Authentication;

/// <summary>
/// Single static API key, supplied by configuration (DEC-009).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a database table. One user, one key, rotated by changing the configuration value and
/// restarting. The MCP server's read-only / read-write scoped tokens (README §5.1) are a separate mechanism
/// that lands in Phase 4; this does not preclude them.
/// </para>
/// <para>
/// Named ...AuthenticationOptions rather than ApiKeyOptions because Scalar.AspNetCore exports a type by the
/// latter name, and both are in scope in Program.cs.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";

    /// <summary>The header carrying the key. Also the name advertised in the OpenAPI security scheme.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>The expected key. No default — an unset key fails closed, it does not allow everything.</summary>
    public string? Value { get; set; }
}
