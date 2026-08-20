using System.Reflection;
using CarTracker.Domain.Accounts.Import;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// "Here is a file I exported; put it back" - the other half of UK GDPR Art. 20.
/// </summary>
/// <remarks>
/// <para>
/// On the same <c>/api/account</c> group and behind the same web login as the export and the deletion, and for
/// the same reason: an assistant token must not be able to bulk-write an account. It fails token validation
/// and <b>401s at the door</b> rather than being admitted in order to be told 403 - widening a scheme so that
/// it can be refused politely is a bad trade, which is the precedent <c>DELETE /api/account</c> set.
/// </para>
/// <para>
/// <b>Both handlers are shells.</b> Every refusal - unreadable, invalid, collision, expired, unknown vehicle -
/// is decided in <see cref="AccountImportService"/>, because there is no <c>CarTracker.WebApi.Tests</c> project
/// and an import is a bulk write into tables that had one write path. What is left here is the mapping from an
/// outcome to a status code, which is genuinely about HTTP.
/// </para>
/// <para>
/// <b>Two calls, and the second carries no payload.</b> The commit names an opaque server-held id and sends
/// only decisions about the file the server is already holding. Re-sending the file would validate the request
/// against itself and would let a commit write something the preview never described - the mistake an earlier
/// revision of the chat spec made and <c>PendingWriteStore</c> exists to prevent.
/// </para>
/// </remarks>
public static class AccountImportEndpoints
{
    /// <summary>The <c>type</c> on each refusal, so a client can tell them apart without parsing prose.</summary>
    private const string Problems = "https://cartracker.invalid/problems/";

    public static IEndpointRouteBuilder MapAccountImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account/import").WithTags("Account");

        group.MapPost("/preview", PreviewAsync)
            .WithName("PreviewAccountImport")
            .WithSummary("Reads an export file and reports exactly what importing it would do. Writes nothing.")
            .DisableAntiforgery();

        group.MapPost("/{importId}/commit", CommitAsync)
            .WithName("CommitAccountImport")
            .WithSummary("Writes a previewed import, under the decisions the caller made about its vehicles.");

        return app;
    }

    /// <remarks>
    /// <c>multipart/form-data</c>, the same shape as a document upload and for the same reason - it is a file.
    /// The 25 MB cap is enforced while the stream is read rather than from <c>Content-Length</c>, because a
    /// header is a claim by the client and the point of a cap is the case where the client is wrong.
    /// </remarks>
    private static async Task<Results<Ok<ImportPreview>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>>
        PreviewAsync(
            HttpRequest request,
            AccountImportService import,
            CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Problem("That upload could not be read",
                "Send the export as multipart/form-data with a 'file' part.",
                StatusCodes.Status400BadRequest, "import-unreadable");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Problem("No file was uploaded",
                "Send the export as the 'file' part of a multipart/form-data request.",
                StatusCodes.Status400BadRequest, "import-unreadable");
        }

        await using var upload = file.OpenReadStream();
        var result = await import.PreviewAsync(upload, Version, cancellationToken);

        return result.Outcome switch
        {
            ImportOutcome.Previewed => TypedResults.Ok(result.Preview!),

            // The per-item map, keyed vehicles[0].expenses[7].fuelEntryId. lib/formErrors.ts matches no field
            // of that shape, so it folds them into the footer banner - which is the right place for them:
            // they are statements about a file, not about a form control.
            ImportOutcome.Invalid => TypedResults.ValidationProblem(
                result.Errors!.ToDictionary(e => e.Key, e => e.Value),
                detail: result.Detail,
                title: "That export cannot be imported as it stands",
                type: Problems + "import-invalid"),

            ImportOutcome.TooLarge => Problem("That file is too large", result.Detail,
                StatusCodes.Status413PayloadTooLarge, "import-unreadable"),

            ImportOutcome.Unreadable => Problem("That file could not be read", result.Detail,
                StatusCodes.Status400BadRequest, "import-unreadable"),

            _ => TypedResults.Unauthorized(),
        };
    }

    private static async Task<Results<Ok<ImportReport>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>>
        CommitAsync(
            string importId,
            // Empty bodies allowed through, because an empty decisions list is a real request: it means
            // "import it exactly as you previewed it", and a framework shape complaint would refuse the
            // simplest correct call there is.
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
            CommitImportRequest? request,
            AccountImportService import,
            CancellationToken cancellationToken)
    {
        var result = await import.CommitAsync(importId, request?.Vehicles, cancellationToken);

        return result.Outcome switch
        {
            ImportOutcome.Committed => TypedResults.Ok(result.Report!),

            // A foreign id answers exactly as an expired one does. Telling them apart would confirm the id is
            // real, which is the same shape a cross-owner vehicle takes: not found, because for this account
            // it is not.
            ImportOutcome.NotFound => Problem("That upload is no longer held", result.Detail,
                StatusCodes.Status404NotFound, "import-not-found"),

            // Distinct from a 400 because the fix is different: pick another registration and commit again
            // against the same id, which survives this refusal precisely so that it can be.
            ImportOutcome.Collision => Problem("That registration is taken", result.Detail,
                StatusCodes.Status409Conflict, "import-collision"),

            ImportOutcome.Invalid => TypedResults.ValidationProblem(
                result.Errors!.ToDictionary(e => e.Key, e => e.Value),
                detail: result.Detail,
                title: "That import cannot be committed as asked",
                type: Problems + "import-invalid"),

            _ => TypedResults.Unauthorized(),
        };
    }

    private static ProblemHttpResult Problem(string title, string? detail, int status, string type) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: status, type: Problems + type);

    /// <remarks>The same source the export stamps into a file, so a preview can tell you the file is newer.</remarks>
    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
}

/// <summary>
/// What to do with the file the server is already holding.
/// </summary>
/// <param name="Vehicles">
/// Decisions, by the index the preview gave each vehicle. <b>Omitting a vehicle is not excluding it</b>: an
/// absent entry means include, so an empty array imports everything exactly as previewed.
/// </param>
public sealed record CommitImportRequest(IReadOnlyList<ImportVehicleDecision>? Vehicles);
