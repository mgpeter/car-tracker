namespace CarTracker.Shared;

/// <summary>
/// What a filed document is. A fixed, server-owned list in six groups (<see cref="DocumentGroup"/>); the group,
/// kind and label of each member live in <c>DocumentCategories</c> in the domain and are served by
/// <c>GET /api/document-types</c>, so the client never hand-types them.
/// </summary>
/// <remarks>
/// <para>
/// Member names match the stored strings exactly (V5C, MOT) so <c>HasConversion&lt;string&gt;()</c>
/// round-trips without a custom mapper.
/// </para>
/// <para>
/// The numbers are stable: <c>Photo</c> was renamed <see cref="ConditionPhoto"/> and kept 5, and members added
/// with the photo gallery take 8 onwards. Declaration order is not display order; that is
/// <c>DocumentCategories.All</c>.
/// </para>
/// </remarks>
public enum DocumentType
{
    V5C = 1,
    Insurance = 2,
    MOT = 3,
    Receipt = 4,
    ConditionPhoto = 5,
    Manual = 6,
    Other = 7,
    BillOfSale = 8,
    RoadTax = 9,
    Breakdown = 10,
    ServiceInvoice = 11,
    PartsReceipt = 12,
    Warranty = 13,
    WorkshopManual = 14,
    Diagram = 15,
    CarPhoto = 16,
}

/// <summary>The six headings the category list is shown under, in display order.</summary>
public enum DocumentGroup
{
    Ownership = 1,
    LegalAndCover = 2,
    WorkAndParts = 3,
    Reference = 4,
    Photos = 5,
    Other = 6,
}

/// <summary>
/// Whether a document belongs on the Documents screen or the Photos screen. Derived from its type on read,
/// never stored, so it cannot disagree with the type.
/// </summary>
public enum DocumentKind
{
    Paper = 1,
    Photo = 2,
}

/// <summary>One row of the category table, as <c>GET /api/document-types</c> serves it.</summary>
public sealed record DocumentTypeInfo(DocumentType Type, DocumentGroup Group, DocumentKind Kind, string Label);
