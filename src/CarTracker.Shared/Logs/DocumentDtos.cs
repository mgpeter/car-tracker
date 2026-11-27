namespace CarTracker.Shared.Logs;

/// <summary>
/// One filed document, as the screen and any future assistant tool render it. Metadata only — the bytes are a
/// separate GET, so listing a vehicle's papers never reads the volume.
/// </summary>
/// <param name="Sha256">
/// The content hash. Exposed because it is what makes "you already have this file" explainable rather than
/// magical, and what the Phase 5 folder-copy backup will be verified against.
/// </param>
/// <param name="LinkedTo">
/// Which record this is attached to, if any — the design's <c>→ service record</c> chip. Derived from whichever
/// of the three FKs is set, so the client renders one chip rather than testing three fields.
/// </param>
/// <param name="Kind">Paper or photo, derived from <paramref name="Type"/> on read; which screen shows it.</param>
/// <param name="Group">The category group <paramref name="Type"/> sits in, derived on read.</param>
/// <param name="HasThumbnail">
/// Whether <c>?variant=thumbnail</c> exists. The client asks only for variants that do, so a 404 on a variant
/// means a row whose file has gone, not a guess that missed.
/// </param>
/// <param name="HasDisplay">Whether <c>?variant=display</c> exists. Always equal to <paramref name="HasThumbnail"/> today.</param>
/// <param name="Width">The decoded original's width in pixels, when the browser that filed it could decode it.</param>
/// <param name="Height">The decoded original's height in pixels, with <paramref name="Width"/>.</param>
/// <param name="CreatedAt">When it was filed: what the Photos screen dates a photo by when it has no date of its own.</param>
public sealed record DocumentItem(
    int Id,
    DocumentType Type,
    string Title,
    DateOnly? DocumentDate,
    string ContentType,
    long SizeBytes,
    string? Sha256,
    int? ServiceRecordId,
    int? ExpenseEntryId,
    int? IssueId,
    string? Notes,
    DocumentLink? LinkedTo,
    DocumentKind Kind,
    DocumentGroup Group,
    bool HasThumbnail,
    bool HasDisplay,
    int? Width,
    int? Height,
    DateTimeOffset CreatedAt);

/// <summary>The one record a document is attached to, named for display and identified for navigation.</summary>
public sealed record DocumentLink(DocumentLinkKind Kind, int Id, string Label);

public enum DocumentLinkKind
{
    ServiceRecord = 1,
    Expense = 2,
    Issue = 3,
}

/// <param name="Papers">Every paper-kind document: the Documents screen's table.</param>
/// <param name="Photos">
/// Every photo-kind document (<c>CarPhoto</c> and <c>ConditionPhoto</c>): the Photos screen's grid. Condition
/// photos are the baseline and the issue evidence that "worsening" is measured against; car photos are the rest.
/// </param>
public sealed record DocumentLog(
    IReadOnlyList<DocumentItem> Papers,
    IReadOnlyList<DocumentItem> Photos,
    int TotalCount,
    long TotalSizeBytes);

/// <summary>A partial edit to a document's metadata and its link. An omitted field is untouched.</summary>
/// <param name="ClearLink">
/// True detaches the document from whatever it was attached to. A separate flag because null on the three id
/// fields already means "leave it alone" — without this there would be no way to express "attached to nothing",
/// the same reason the vehicle patch clears fields through a separate <c>Clear</c> list.
/// </param>
public sealed record DocumentPatch(
    DocumentType? Type = null,
    string? Title = null,
    DateOnly? DocumentDate = null,
    int? ServiceRecordId = null,
    int? ExpenseEntryId = null,
    int? IssueId = null,
    string? Notes = null,
    bool ClearLink = false);

/// <summary>The body of <c>PUT /api/vehicles/{registration}/cover-photo</c>. Null clears the cover.</summary>
public sealed record SetCoverPhotoRequest(int? DocumentId);
