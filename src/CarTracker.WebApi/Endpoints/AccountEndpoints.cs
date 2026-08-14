using System.Security.Claims;
using CarTracker.Data;
using CarTracker.Domain.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The signed-in person's own account: what it holds, and how to destroy it (UK GDPR Art. 17).
/// </summary>
/// <remarks>
/// <para>
/// The first endpoint group that is <b>not</b> vehicle-scoped — it is about the person, not a car — and the
/// first deliberately reachable only through the web login. <b>None of it gets an MCP tool.</b> An assistant
/// holding a read-write token must not be able to delete an account or dump it; the blast radius of a leaked
/// token stays where DEC-014 put it. The Auth0 fallback policy is what enforces that, so an assistant token
/// presented here fails token validation and 401s at the door — the group is never widened to admit the
/// assistant scheme merely so it could be told 403 instead.
/// </para>
/// <para>
/// Both handlers are shells. The confirmation match, the not-configured refusal and the account-holder check all
/// live in <see cref="AccountDeletionService"/>, because there is no <c>CarTracker.WebApi.Tests</c> project and
/// the most destructive operation in the app must not be the only untested one. What is left here is the mapping
/// from an outcome to a status code, which is genuinely about HTTP.
/// </para>
/// </remarks>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").WithTags("Account");

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetAccountSummary")
            .WithSummary("What this account holds — the weight the deletion confirmation states before it arms.");

        group.MapDelete("", DeleteAsync)
            .WithName("DeleteAccount")
            .WithSummary("Destroys the account, everything it owns and the login behind it. Irreversible.");

        return app;
    }

    private static async Task<Results<Ok<AccountSummary>, UnauthorizedHttpResult>> GetSummaryAsync(
        AccountDeletionService accounts,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        var summary = await accounts.GetSummaryAsync(currentUser.OwnerId, cancellationToken);
        return summary is null ? TypedResults.Unauthorized() : TypedResults.Ok(summary);
    }

    /// <remarks>
    /// <para>
    /// The body is required even though the UI already asks for the address. The client is not the only possible
    /// caller, and an account-deleting <c>DELETE</c> that succeeds on an empty body is one mis-wired button away
    /// from being catastrophic.
    /// </para>
    /// <para>
    /// The 204 is not a promise that the Auth0 identity is already gone — it is a promise that the data is, and
    /// that the identity's removal is now guaranteed to be attempted until it succeeds. The client's next move
    /// is <c>logout()</c>; there is no account left to re-render the app against.
    /// </para>
    /// </remarks>
    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>>
        DeleteAsync(
            // Empty bodies allowed through to the service, so a DELETE with nothing in it is refused by the
            // confirmation rule with a field error rather than by the framework with a shape complaint. The
            // refusal is the same either way; only one of them tells the caller what was actually missing.
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
            DeleteAccountRequest? request,
            AccountDeletionService accounts,
            ICurrentUserAccessor currentUser,
            ClaimsPrincipal principal,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
    {
        var subject = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var result = await accounts.DeleteAsync(
            currentUser.OwnerId, subject, request?.ConfirmEmail, cancellationToken);

        var logger = loggerFactory.CreateLogger("AccountDeletion");

        switch (result.Outcome)
        {
            case AccountDeletionOutcome.Deleted:
                if (result.IdentityDeleted)
                    logger.LogInformation("Account deleted and its Auth0 identity removed.");
                else
                    logger.LogWarning("Account deleted; the Auth0 identity survives and is queued for retry: {Detail}", result.Detail);
                return TypedResults.NoContent();

            case AccountDeletionOutcome.ConfirmationMismatch:
                // A per-field RFC 9457 errors map, so the sheet marks the field the way every other form does
                // rather than showing a banner over a screen whose only control is a destructive button.
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [result.Field ?? "confirmEmail"] = [result.Detail ?? "The confirmation does not match."],
                });

            case AccountDeletionOutcome.NotAccountHolder:
                return TypedResults.Forbid();

            case AccountDeletionOutcome.IdentityDeletionNotConfigured:
                // 503, not 502, and the same reasoning as the DVLA lookup: a deployment with no credential is
                // not a broken gateway, it is a capability this instance does not have, and a retry cannot
                // succeed until someone configures it. Nothing was deleted.
                return TypedResults.Problem(
                    title: "Account deletion is not configured",
                    detail: result.Detail,
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            default:
                return TypedResults.Unauthorized();
        }
    }
}

/// <param name="ConfirmEmail">
/// The account's own email address, typed out. Matched ordinal and case-insensitively — a second gate behind the
/// UI's typed confirmation, because the UI is not the only thing that can call this.
/// </param>
public sealed record DeleteAccountRequest(string? ConfirmEmail);
