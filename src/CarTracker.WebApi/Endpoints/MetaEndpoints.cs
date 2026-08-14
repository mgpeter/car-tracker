using System.Reflection;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Build and environment metadata. Per `docs/specs/2026-07-14-react-app-foundation/sub-specs/api-spec.md`,
/// this exists so the OpenAPI → codegen → typed fetch → render loop can be proven before the Dashboard does.
/// </summary>
public static class MetaEndpoints
{
    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Meta");

        group.MapGet("/meta", (TimeProvider timeProvider, CarTracker.Domain.Accounts.IIdentityProviderClient identity) =>
                new MetaResponse(
                    ApplicationName: "CarTracker",
                    Version: Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? "0.0.0",
                    Environment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                    // Through TimeProvider for the same reason the domain does: keeping "no direct clock access"
                    // true with no exceptions means nobody finds a precedent for reading the clock directly.
                    ServerTimeUtc: timeProvider.GetUtcNow(),
                    IdentityDeletionConfigured: identity.IsConfigured))
            // The one open endpoint (DEC-009). The front-end needs something to call before a key is entered,
            // so it can tell "no key yet" from "the API is down" — two different problems, two different fixes.
            .AllowAnonymous()
            .WithName("GetMeta")
            .WithSummary("Build and environment metadata. Requires no API key.");

        // Exists solely so the front-end can verify a key is valid, and so the 401 path is exercised end to
        // end. Carries no data of its own.
        group.MapGet("/meta/authenticated", () => new AuthenticatedResponse(true))
            .WithName("GetAuthenticatedMeta")
            .WithSummary("Returns 200 only with a valid API key. Used to verify the configured key.");

        return app;
    }
}

/// <param name="IdentityDeletionConfigured">
/// Whether this deployment can erase the login behind an account. False means <c>DELETE /api/account</c> would
/// answer 503 and delete nothing, so the settings panel shows the export and explains that deletion is
/// unavailable here — offering a button that cannot work is worse than not offering one. Anonymous, like the
/// rest of this response, and safe to be: it says what a capability is, not what a credential is.
/// </param>
public sealed record MetaResponse(
    string ApplicationName,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc,
    bool IdentityDeletionConfigured = false);

public sealed record AuthenticatedResponse(bool Authenticated);
