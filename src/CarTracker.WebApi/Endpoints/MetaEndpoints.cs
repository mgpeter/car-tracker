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

        group.MapGet("/meta", (
                TimeProvider timeProvider,
                CarTracker.Domain.Accounts.IIdentityProviderClient identity,
                CarTracker.Domain.Lookup.VehicleLookupOptions lookup,
                CarTracker.Chat.ChatSettings chat) =>
                new MetaResponse(
                    ApplicationName: "CarTracker",
                    Version: Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? "0.0.0",
                    Environment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                    // Through TimeProvider for the same reason the domain does: keeping "no direct clock access"
                    // true with no exceptions means nobody finds a precedent for reading the clock directly.
                    ServerTimeUtc: timeProvider.GetUtcNow(),
                    IdentityDeletionConfigured: identity.IsConfigured,
                    // VES alone is enough to offer the button — the MOT half is independently optional and its
                    // absence costs only the expiry seed, so `IsConfigured` is the right question and
                    // `IsMotConfigured` is not.
                    VehicleLookupConfigured: lookup.IsConfigured,
                    // A capability, and only that: whether this deployment holds a model credential at all. The
                    // budget is not part of the answer — an account over its daily allowance still has a chat,
                    // and hiding the icon would tell it the feature had been removed.
                    ChatConfigured: chat.IsConfigured))
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
/// <param name="VehicleLookupConfigured">
/// Whether this deployment holds a DVLA credential. False means <c>GET /api/vehicles/lookup/{reg}</c> would
/// answer 503 <c>NotConfigured</c> whatever the plate, so the add-car sheet omits its "Look up" button
/// entirely — the same rule as deletion above, and the same reason: a control offered on the first screen of a
/// new account must be one that can work. Anonymous like the rest, and safe to be for the same reason.
/// </param>
/// <param name="ChatConfigured">
/// Whether this deployment can run the in-app assistant. False means <c>/api/chat</c> would answer 503, so the
/// shell renders no chat entry point at all — the third capability flag on this response and the third for the
/// same reason. The client tests it as <c>=== true</c>, so an in-flight <c>meta</c> hides the icon rather than
/// offering one that fails.
/// </param>
public sealed record MetaResponse(
    string ApplicationName,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc,
    bool IdentityDeletionConfigured = false,
    bool VehicleLookupConfigured = false,
    bool ChatConfigured = false);

public sealed record AuthenticatedResponse(bool Authenticated);
