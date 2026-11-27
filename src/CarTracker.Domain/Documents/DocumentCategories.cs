using CarTracker.Shared;

namespace CarTracker.Domain.Documents;

/// <summary>
/// The category table: which group each <see cref="DocumentType"/> sits in, whether it is a paper or a photo,
/// and what an owner calls it.
/// </summary>
/// <remarks>
/// Every named member has an explicit arm; the discard arm only catches unnamed values (a cast integer), as the
/// other enum switches in the domain do. A member added without an arm throws, and
/// <c>DocumentCategoriesTests</c> enumerates every member, so it fails a test rather than being silently dropped.
/// </remarks>
public static class DocumentCategories
{
    public static DocumentGroup Group(DocumentType type) => type switch
    {
        DocumentType.V5C or DocumentType.BillOfSale => DocumentGroup.Ownership,
        DocumentType.MOT or DocumentType.Insurance or DocumentType.RoadTax or DocumentType.Breakdown
            => DocumentGroup.LegalAndCover,
        DocumentType.ServiceInvoice or DocumentType.PartsReceipt or DocumentType.Warranty or DocumentType.Receipt
            => DocumentGroup.WorkAndParts,
        DocumentType.Manual or DocumentType.WorkshopManual or DocumentType.Diagram => DocumentGroup.Reference,
        DocumentType.CarPhoto or DocumentType.ConditionPhoto => DocumentGroup.Photos,
        DocumentType.Other => DocumentGroup.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a document type."),
    };

    /// <summary>Photo for the Photos group, paper for everything else.</summary>
    public static DocumentKind Kind(DocumentType type) =>
        Group(type) == DocumentGroup.Photos ? DocumentKind.Photo : DocumentKind.Paper;

    public static string Label(DocumentType type) => type switch
    {
        DocumentType.V5C => "V5C logbook",
        DocumentType.BillOfSale => "Bill of sale",
        DocumentType.MOT => "MOT certificate",
        DocumentType.Insurance => "Insurance certificate",
        DocumentType.RoadTax => "Road tax",
        DocumentType.Breakdown => "Breakdown cover",
        DocumentType.ServiceInvoice => "Service invoice",
        DocumentType.PartsReceipt => "Parts receipt",
        DocumentType.Warranty => "Warranty",
        DocumentType.Receipt => "Receipt",
        DocumentType.Manual => "Owner's handbook",
        DocumentType.WorkshopManual => "Workshop manual",
        DocumentType.Diagram => "Diagram",
        DocumentType.CarPhoto => "Car photo",
        DocumentType.ConditionPhoto => "Condition photo",
        DocumentType.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a document type."),
    };

    /// <summary>Every category in display order: group by group, members in the order the upload sheet lists them.</summary>
    public static IReadOnlyList<DocumentTypeInfo> All { get; } =
        new[]
        {
            DocumentType.V5C, DocumentType.BillOfSale,
            DocumentType.MOT, DocumentType.Insurance, DocumentType.RoadTax, DocumentType.Breakdown,
            DocumentType.ServiceInvoice, DocumentType.PartsReceipt, DocumentType.Warranty, DocumentType.Receipt,
            DocumentType.Manual, DocumentType.WorkshopManual, DocumentType.Diagram,
            DocumentType.CarPhoto, DocumentType.ConditionPhoto,
            DocumentType.Other,
        }
        .Select(t => new DocumentTypeInfo(t, Group(t), Kind(t), Label(t)))
        .ToArray();
}
