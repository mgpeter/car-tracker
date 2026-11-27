using CarTracker.Data;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

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

        group.MapPost("/{id:int}/renditions", AddRenditionsAsync)
            .WithName("AddDocumentRenditions")
            .WithSummary("Fills a photo's thumbnail and display copy when it has none. A no-op when it already has them.")
            .DisableAntiforgery();

        group.MapGet("/{id:int}/file", GetDocumentFileAsync)
            .WithName("GetDocumentFile")
            .WithSummary(
                "The bytes: the original by default, or ?variant=display|thumbnail for a rendition. "
                + "?download=true sends them as an attachment rather than inline.");

        group.MapPatch("/{id:int}", UpdateDocumentAsync)
            .WithName("UpdateDocument")
            .WithSummary("Re-tags a document — its type, title, date, notes and which record it attaches to.");

        group.MapDelete("/{id:int}", DeleteDocumentAsync)
            .WithName("DeleteDocument")
            .WithSummary("Removes the document and its bytes. Nothing else deletes a file from the volume.");

        app.MapGet("/api/document-types", () => TypedResults.Ok(DocumentCategories.All))
            .WithTags("Documents")
            .WithName("GetDocumentTypes")
            .WithSummary("The document categories in display order, each with its group, kind and label.");

        app.MapPut("/api/vehicles/{registration}/cover-photo", SetCoverPhotoAsync)
            .WithTags("Documents")
            .WithName("SetCoverPhoto")
            .WithSummary("Chooses one of the car's photos as its cover on the garage card, or clears it with null.");

        return app;
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, ValidationProblem>> SetCoverPhotoAsync(
        string registration,
        SetCoverPhotoRequest request,
        CarTrackerDbContext context,
        DocumentService documents,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var result = await documents.SetCoverAsync(vehicleId.Value, request.DocumentId, cancellationToken);
        return result.Status switch
        {
            WriteStatus.NotFound => VehicleLookup.NotFound(registration),
            WriteStatus.Validation => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NoContent(),
        };
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

        // Asked before a byte is streamed, which is the point of asking it here rather than letting RecordAsync
        // refuse. The bytes land on the volume first (see the remarks above), so a capacity refusal after the
        // stream would let somebody at their ceiling fill the volume with orphans by retrying - the exact abuse
        // the allowance exists to bound.
        if (await documents.CapacityAsync(cancellationToken) is { HasRoom: false } full)
            return Problem(DocumentService.FullMessage(full));

        DateOnly? documentDate = DateOnly.TryParse(form["documentDate"], out var parsedDate) ? parsedDate : null;

        // The rendition parts are checked as a set before any byte is streamed, so a partial or mistyped set
        // costs nothing on the volume.
        if (ReadRenditions(form) is { Error: { } renditionError })
            return Problem(renditionError);
        var renditions = ReadRenditions(form).Upload;

        await using var upload = file.OpenReadStream();
        var stored = await store.SaveAsync(vehicleId.Value, upload, contentType, cancellationToken);
        if (stored is null)
        {
            return Problem(
                $"That file is larger than the {DocumentStore.MaxSizeBytes / (1024 * 1024)} MB limit.");
        }

        StoredRenditions? savedRenditions = null;
        if (renditions is not null)
        {
            await using (renditions.Thumbnail)
            await using (renditions.Display)
            {
                var saved = await documents.SaveRenditionsAsync(vehicleId.Value, renditions, cancellationToken);
                if (saved is { Status: WriteStatus.Validation, Errors: { } savedErrors })
                    return Problem(savedErrors.Values.First()[0]);
                savedRenditions = saved.Value;
            }
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
            savedRenditions,
            cancellationToken);

        if (result is { Status: WriteStatus.Validation, Errors: { } errors })
            return TypedResults.ValidationProblem(errors);

        return TypedResults.Created(
            $"/api/vehicles/{registration}/documents/{result.Value!.Id}", result.Value);
    }

    /// <remarks>
    /// <para>
    /// The browser backfill, for photos filed without renditions (an HEIC uploaded from a desktop browser that
    /// could not decode it, or a photo filed before renditions existed). All four parts are required.
    /// </para>
    /// <para>
    /// Validated in the domain rather than here, because the no-op case (renditions already exist) is decided
    /// against the row, and is answered before a byte is streamed.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<DocumentItem>, NotFound<ProblemDetails>, ValidationProblem, BadRequest<ProblemDetails>>>
        AddRenditionsAsync(
            string registration,
            int id,
            HttpRequest request,
            CarTrackerDbContext context,
            DocumentService documents,
            CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (!request.HasFormContentType)
            return Problem("Renditions must be multipart/form-data with thumbnail, display, width and height parts.");

        var form = await request.ReadFormAsync(cancellationToken);
        var read = ReadRenditions(form);
        if (read.Error is { } error) return Problem(error);
        if (read.Upload is not { } renditions)
            return Problem("Send a thumbnail, a display copy, a width and a height.");

        await using (renditions.Thumbnail)
        await using (renditions.Display)
        {
            var result = await documents.AddRenditionsAsync(vehicleId.Value, id, renditions, cancellationToken);
            return result.Status switch
            {
                WriteStatus.NotFound => DocumentNotFound(id, registration),
                WriteStatus.Validation => TypedResults.ValidationProblem(result.Errors!),
                _ => TypedResults.Ok(result.Value!),
            };
        }
    }

    /// <summary>
    /// The rendition parts of a form as an upload, an error, or neither (no rendition parts at all).
    /// </summary>
    /// <remarks>
    /// The set is described from the received parts' own content types and lengths, never from a form field
    /// claiming either, and validated by <see cref="RenditionSet"/> before anything is opened.
    /// </remarks>
    private static (RenditionUpload? Upload, string? Error) ReadRenditions(IFormCollection form)
    {
        var thumbnail = form.Files["thumbnail"];
        var display = form.Files["display"];

        if (!Dimension(form["width"], out var width)) return (null, $"'{form["width"]}' is not a width in pixels.");
        if (!Dimension(form["height"], out var height)) return (null, $"'{form["height"]}' is not a height in pixels.");

        var set = new RenditionSet(
            thumbnail is null ? null : new RenditionPart(thumbnail.ContentType, thumbnail.Length),
            display is null ? null : new RenditionPart(display.ContentType, display.Length),
            width,
            height);

        if (set.IsEmpty) return (null, null);
        if (set.Validate() is { } invalid) return (null, invalid.Message);

        return (new RenditionUpload(
            thumbnail!.OpenReadStream(), thumbnail.ContentType,
            display!.OpenReadStream(), display.ContentType,
            width!.Value, height!.Value), null);
    }

    /// <summary>Absent is fine (null); present must be an integer, which <see cref="RenditionSet"/> then bounds.</summary>
    private static bool Dimension(string? value, out int? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!int.TryParse(value, out var number)) return false;
        parsed = number;
        return true;
    }

    /// <remarks>
    /// <para>
    /// <c>Content-Disposition</c> is the whole of "view versus download" — inline for the viewer, attachment for
    /// the save. The design promises download keeps the original, so nothing re-encodes on the way out; the
    /// stored bytes are streamed back under the stored content type.
    /// </para>
    /// <para>
    /// Renditions are content-addressed and never rewritten, so they are sent as immutable for a year with their
    /// hash as the <c>ETag</c>: the grid comes from the browser cache on a second visit. <c>private</c>, because
    /// they are one account's photos. The original keeps the headers it always had.
    /// </para>
    /// </remarks>
    private static async Task<Results<FileStreamHttpResult, NotFound<ProblemDetails>>> GetDocumentFileAsync(
        string registration,
        int id,
        bool? download,
        DocumentVariant? variant,
        HttpResponse response,
        CarTrackerDbContext context,
        DocumentService documents,
        DocumentStore store,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var wanted = variant ?? DocumentVariant.Original;
        var file = await documents.FindFileAsync(vehicleId.Value, id, wanted, cancellationToken);
        if (file is null)
        {
            return wanted == DocumentVariant.Original
                ? DocumentNotFound(id, registration)
                : TypedResults.NotFound(new ProblemDetails
                {
                    Title = "No such variant",
                    Detail = $"Document {id} on '{registration}' has no {wanted.ToString().ToLowerInvariant()} copy.",
                    Status = StatusCodes.Status404NotFound,
                });
        }

        var stream = store.OpenRead(file.RelativePath);
        if (stream is null)
        {
            // The row survived its bytes. File storage is not transactional with the database (DEC-005 names
            // this as its cost), so say which of the two is missing rather than returning a bare 500.
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "The file is missing from the volume",
                Detail =
                    $"Document {id} is filed, but its bytes are not on the volume at '{file.RelativePath}'. "
                    + "The database and the document volume are backed up separately; this row has outlived its file.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        EntityTagHeaderValue? etag = null;
        if (wanted != DocumentVariant.Original)
        {
            response.Headers.CacheControl = "private, max-age=31536000, immutable";
            etag = file.Hash is null ? null : new EntityTagHeaderValue($"\"{file.Hash}\"");
        }

        return TypedResults.File(
            stream,
            contentType: file.ContentType,
            // The download name is built from the title, not from whatever the file was called when it was
            // uploaded — the original name is not stored, because it is not load-bearing and is not a safe path
            // component. The title is what the screen shows and what the owner will recognise.
            fileDownloadName: download == true ? DownloadName(file) : null,
            entityTag: etag,
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
    private static string DownloadName(DocumentService.DocumentFile file)
    {
        var extension = DocumentStore.AllowedContentTypes.TryGetValue(file.ContentType, out var ext)
            ? ext
            : Path.GetExtension(file.RelativePath);
        var safe = string.Join('-', file.Title.Split(Path.GetInvalidFileNameChars()));
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
