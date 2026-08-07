using CarTracker.Data;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Documents — the papers and photo sets, README §3.9 and the seventeenth and last workbook screen.
/// </summary>
/// <remarks>
/// <para>
/// The only group that takes a file. Upload is <c>multipart/form-data</c> where every other write here is JSON,
/// because the bytes go to a mounted volume (DEC-005) and the row keeps the path — <c>bytea</c> would bloat
/// <c>pg_dump</c> with photo sets, and MinIO is a third container to run and back up for one user.
/// </para>
/// <para>
/// It is also the only write group that never calls <c>AnomalyScanner</c>. A document is not a derived input:
/// it moves no figure and trips no detector. Filing the MOT certificate does not change the MOT countdown —
/// the logged pass already did.
/// </para>
/// </remarks>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/documents").WithTags("Documents");

        group.MapGet("/", GetDocumentsAsync)
            .WithName("GetDocuments")
            .WithSummary("Filed documents, split into papers and photos. Metadata only — the bytes are a separate GET.");

        group.MapPost("/", UploadDocumentAsync)
            .WithName("UploadDocument")
            .WithSummary("Files a PDF or photo, tagging it and optionally attaching it to one record.")
            .DisableAntiforgery();

        group.MapGet("/{id:int}/file", GetDocumentFileAsync)
            .WithName("GetDocumentFile")
            .WithSummary("The original bytes. ?download=true sends them as an attachment rather than inline.");

        group.MapPatch("/{id:int}", UpdateDocumentAsync)
            .WithName("UpdateDocument")
            .WithSummary("Re-tags a document — its type, title, date, notes and which record it attaches to.");

        group.MapDelete("/{id:int}", DeleteDocumentAsync)
            .WithName("DeleteDocument")
            .WithSummary("Removes the document and its bytes. Nothing else deletes a file from the volume.");

        return app;
    }

    private static async Task<Results<Ok<DocumentLog>, NotFound<ProblemDetails>>> GetDocumentsAsync(
        string registration,
        CarTrackerDbContext context,
        DocumentService documents,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        return TypedResults.Ok(await documents.GetLogAsync(vehicleId.Value, cancellationToken));
    }

    /// <remarks>
    /// The file is streamed to the volume <b>before</b> the row is written, because the content hash names the
    /// file and the row stores that name. An upload that then fails validation leaves bytes on disk that no row
    /// references — content-addressed, so the next identical upload reuses them rather than adding a second
    /// copy, and the Phase 5 backup story is a folder copy either way.
    /// </remarks>
    private static async Task<Results<Created<DocumentItem>, NotFound<ProblemDetails>, ValidationProblem, BadRequest<ProblemDetails>>>
        UploadDocumentAsync(
            string registration,
            HttpRequest request,
            CarTrackerDbContext context,
            DocumentService documents,
            DocumentStore store,
            CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (!request.HasFormContentType)
            return Problem("Upload must be multipart/form-data with a 'file' part.");

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
            return Problem("No file was uploaded. Send it as the 'file' part.");

        // From the received part, never a client-declared field: SizeBytes and ContentType describe what
        // actually arrived, and a form field saying otherwise is a claim, not a fact.
        var contentType = file.ContentType;
        if (!DocumentStore.IsAllowed(contentType))
        {
            return Problem(
                $"'{contentType}' cannot be filed. Documents are PDFs or photos: "
                + $"{string.Join(", ", DocumentStore.AllowedContentTypes.Keys)}.");
        }

        if (!Enum.TryParse<DocumentType>(form["type"], ignoreCase: true, out var type))
            return Problem($"'{form["type"]}' is not a document type.");

        DateOnly? documentDate = DateOnly.TryParse(form["documentDate"], out var parsedDate) ? parsedDate : null;

        await using var upload = file.OpenReadStream();
        var stored = await store.SaveAsync(vehicleId.Value, upload, contentType, cancellationToken);
        if (stored is null)
        {
            return Problem(
                $"That file is larger than the {DocumentStore.MaxSizeBytes / (1024 * 1024)} MB limit.");
        }

        var result = await documents.RecordAsync(
            vehicleId.Value,
            stored,
            contentType,
            type,
            form["title"].ToString(),
            documentDate,
            Id(form["serviceRecordId"]),
            Id(form["expenseEntryId"]),
            Id(form["issueId"]),
            Text(form["notes"]),
            EntrySource.Web,
            cancellationToken);

        if (result is { Status: WriteStatus.Validation, Errors: { } errors })
            return TypedResults.ValidationProblem(errors);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/documents/{result.Value!.Id}", result.Value);
    }

    /// <remarks>
    /// <c>Content-Disposition</c> is the whole of "view versus download" — inline for the viewer, attachment for
    /// the save. The design promises download keeps the original, so nothing re-encodes on the way out; the
    /// stored bytes are streamed back under the stored content type.
    /// </remarks>
    private static async Task<Results<FileStreamHttpResult, NotFound<ProblemDetails>>> GetDocumentFileAsync(
        string registration,
        int id,
        bool? download,
        CarTrackerDbContext context,
        DocumentService documents,
        DocumentStore store,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var document = await documents.FindAsync(vehicleId.Value, id, cancellationToken);
        if (document is null) return DocumentNotFound(id, registration);

        var stream = store.OpenRead(document.FilePath);
        if (stream is null)
        {
            // The row survived its bytes. File storage is not transactional with the database (DEC-005 names
            // this as its cost), so say which of the two is missing rather than returning a bare 500.
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "The file is missing from the volume",
                Detail =
                    $"Document {id} is filed, but its bytes are not on the volume at '{document.FilePath}'. "
                    + "The database and the document volume are backed up separately; this row has outlived its file.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        return TypedResults.File(
            stream,
            contentType: document.ContentType,
            // The download name is built from the title, not from whatever the file was called when it was
            // uploaded — the original name is not stored, because it is not load-bearing and is not a safe path
            // component. The title is what the screen shows and what the owner will recognise.
            fileDownloadName: download == true ? DownloadName(document) : null,
            enableRangeProcessing: true);
    }

    private static async Task<Results<Ok<DocumentItem>, NotFound<ProblemDetails>, ValidationProblem>>
        UpdateDocumentAsync(
            string registration,
            int id,
            DocumentPatch patch,
            CarTrackerDbContext context,
            DocumentService documents,
            CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await documents.UpdateAsync(vehicleId.Value, id, patch, cancellationToken);
        return result.Status switch
        {
            WriteStatus.NotFound => DocumentNotFound(id, registration),
            WriteStatus.Validation => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.Ok(result.Value!),
        };
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteDocumentAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        DocumentService documents,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await documents.DeleteAsync(vehicleId.Value, id, cancellationToken);
        return result.Status == WriteStatus.NotFound
            ? DocumentNotFound(id, registration)
            : TypedResults.NoContent();
    }

    /// <summary>A filename an owner would recognise, from the title rather than the uploaded name.</summary>
    private static string DownloadName(Document document)
    {
        var extension = DocumentStore.AllowedContentTypes.TryGetValue(document.ContentType, out var ext)
            ? ext
            : Path.GetExtension(document.FilePath);
        var safe = string.Join('-', document.Title.Split(Path.GetInvalidFileNameChars()));
        return $"{safe}{extension}";
    }

    private static int? Id(string? value) => int.TryParse(value, out var parsed) ? parsed : null;

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static BadRequest<ProblemDetails> Problem(string detail) =>
        TypedResults.BadRequest(new ProblemDetails
        {
            Title = "The upload was refused",
            Detail = detail,
            Status = StatusCodes.Status400BadRequest,
        });

    private static NotFound<ProblemDetails> DocumentNotFound(int id, string registration) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Document not found",
            Detail = $"No document {id} on '{registration}'.",
            Status = StatusCodes.Status404NotFound,
        });
}
