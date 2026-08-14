using System.Reflection;
using CarTracker.Data;
using CarTracker.Domain.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// "Give me everything you hold about me, as a file" — UK GDPR Art. 15 and Art. 20 (portability).
/// </summary>
/// <remarks>
/// <para>
/// On the same <c>/api/account</c> group and behind the same web login as the deletion half, and for the same
/// reason: an assistant token must not be able to dump an account. It sits in its own file because it is the
/// only endpoint in the app that writes its own response body — see below.
/// </para>
/// <para>
/// <b>It has no declared response type, and that is honest.</b> The payload is written straight to the response
/// stream a vehicle at a time, so it has no static shape to generate a schema from; declaring one would be a
/// second definition of the format, maintained by hand, free to drift from the writer. The format is documented
/// where it is produced, in <see cref="AccountExportService"/>, and inside the file itself in its <c>notes</c>.
/// </para>
/// </remarks>
public static class AccountExportEndpoints
{
    public static IEndpointRouteBuilder MapAccountExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/account").WithTags("Account")
            .MapGet("/export", ExportAsync)
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .WithName("ExportAccount")
            .WithSummary("Everything this account owns, as raw rows. No calculated figures, no document files.");

        return app;
    }

    private static async Task<Results<EmptyHttpResult, UnauthorizedHttpResult>> ExportAsync(
        HttpContext http,
        AccountExportService export,
        ICurrentUserAccessor currentUser,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (currentUser.OwnerId is not int ownerId) return TypedResults.Unauthorized();

        // Headers first and only after the authorization check: once the first byte is written there is no
        // status code left to change.
        var date = clock.GetUtcNow().UtcDateTime;
        http.Response.ContentType = "application/json; charset=utf-8";
        http.Response.Headers.ContentDisposition =
            $"attachment; filename=\"cartracker-export-{date:yyyy-MM-dd}.json\"";

        await export.WriteAsync(ownerId, Version, http.Response.Body, cancellationToken);

        // The body is already written; this adds nothing to it.
        return TypedResults.Empty;
    }

    /// <remarks>The same source <c>GET /api/meta</c> reports, so a file and the app it came from agree.</remarks>
    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
}
