using System.Security.Claims;
using CarTracker.Data;
using CarTracker.Domain.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarTracker.WebApi.Authentication;

/// <summary>
/// Resolves the authenticated principal to a local <see cref="User"/> and pins it on the request-scoped
/// <see cref="CurrentUserAccessor"/> that <see cref="CarTrackerDbContext"/>'s query filters read.
/// </summary>
/// <remarks>
/// <para>
/// Runs <b>after</b> authorization, so both the web principal (the Auth0 JWT, established by the fallback
/// policy) and the MCP principal (the assistant token, established by the <c>McpRead</c> policy) are populated
/// by the time it runs — the two paths carry the owner differently and this is the one place that reconciles
/// them.
/// </para>
/// <list type="bullet">
/// <item>Auth0 principal → <see cref="AccountProvisioner"/> finds the account, provisions one for an invited
/// newcomer, or refuses an uninvited one.</item>
/// <item>Assistant token → read the owner the token already carries (<see cref="AssistantClaims.UserId"/>).</item>
/// <item>Anything else (API key, anonymous) → no resolved user, which the filters read as "nothing".</item>
/// </list>
/// <para>
/// The account logic itself is <see cref="AccountProvisioner"/> in the domain rather than inline here. What
/// this file keeps is the part that is genuinely about HTTP: which claim carries the subject, and how a refusal
/// is reported.
/// </para>
/// </remarks>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        CurrentUserAccessor accessor,
        AccountProvisioner accounts,
        ILogger<CurrentUserMiddleware> logger)
    {
        var principal = context.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            // Anonymous (e.g. /api/meta). Not a system context — resolves to no vehicles, though anonymous
            // endpoints do not query them.
            accessor.SetOwner(null);
        }
        else if (int.TryParse(principal.FindFirst(AssistantClaims.UserId)?.Value, out var tokenOwnerId))
        {
            accessor.SetOwner(tokenOwnerId);
        }
        else if ((principal.FindFirst("sub") ?? principal.FindFirst(ClaimTypes.NameIdentifier)) is { Value: var sub })
        {
            var resolution = await accounts.ResolveAsync(
                sub,
                principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value,
                // A JWT boolean arrives here as the string "true" — ClaimsIdentity has no other type. Anything
                // else, absent included, is not a confirmation, and the door treats it as none.
                string.Equals(principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value,
                context.RequestAborted);

            if (resolution.Outcome is AccountOutcome.NotInvited)
            {
                // No account, so no vehicles — set before the short-circuit, because an anonymous endpoint
                // downstream still gets a context and must not inherit a bypass.
                accessor.SetOwner(null);
                logger.LogInformation("Refused an uninvited sign-in for {Subject}: {Detail}", sub, resolution.Detail);

                if (!AllowsAnonymous(context))
                {
                    await WriteNotInvitedAsync(context, resolution.Detail);
                    return;
                }
            }
            else
            {
                accessor.SetOwner(resolution.UserId);
            }
        }
        else
        {
            // Authenticated but neither an Auth0 subject nor an owned token — an API-key principal. No vehicles.
            accessor.SetOwner(null);
        }

        await next(context);
    }

    /// <summary>
    /// Whether the endpoint this request routed to is open to everyone anyway.
    /// </summary>
    /// <remarks>
    /// A refused sign-in still reaches <c>/api/meta</c>, and that is deliberate: the browser attaches the bearer
    /// to every call it makes, so refusing the anonymous endpoints too would take away the build metadata the
    /// client needs to render the panel explaining the refusal. Routing has already run — this middleware sits
    /// after <c>UseAuthorization</c> — so the endpoint and its metadata are available here.
    /// </remarks>
    private static bool AllowsAnonymous(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    /// <remarks>
    /// <para>
    /// 403 rather than 401: the token is perfectly valid and signing in again will produce the same one, so an
    /// invitation to re-authenticate would be a loop. ProblemDetails rather than a bare status, carrying
    /// <see cref="SignupPolicy.NotInvitedProblemType"/>, so the client can tell this from every other 403 and
    /// say what is actually wrong instead of "forbidden".
    /// </para>
    /// <para>
    /// Written straight to the response rather than raised as an exception: this is a middleware, there is no
    /// endpoint result to return, and the refusal must stop the pipeline before anything reads the database
    /// under an accessor with no owner.
    /// </para>
    /// </remarks>
    private static Task WriteNotInvitedAsync(HttpContext context, string? detail) =>
        TypedResults.Problem(new ProblemDetails
        {
            Type = SignupPolicy.NotInvitedProblemType,
            Title = "Not yet invited",
            Detail = detail,
            Status = StatusCodes.Status403Forbidden,
        }).ExecuteAsync(context);
}
