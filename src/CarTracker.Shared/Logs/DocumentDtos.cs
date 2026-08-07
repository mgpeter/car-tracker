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
    DocumentLink? LinkedTo);

/// <summary>The one record a document is attached to, named for display and identified for navigation.</summary>
public sealed record DocumentLink(DocumentLinkKind Kind, int Id, string Label);

public enum DocumentLinkKind
{
    ServiceRecord = 1,
    Expense = 2,
    Issue = 3,
}

/// <param name="Papers">Everything that is not a photo — the design's list, which is a table of aligned facts.</param>
/// <param name="Photos">
/// <c>Type = Photo</c>, which the design grids rather than lists: a set of images is not columns of figures.
/// The unlinked ones are the March 2026 condition baseline that "worsening" is later measured against.
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
/// the same reason the vehicle patch cannot clear a purchase price.
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
