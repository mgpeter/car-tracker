using CarTracker.Domain.Documents;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The server-owned category table. The client renders groups and labels from it, so a member missing here is a
/// paper the upload sheet cannot offer and a photo the gallery cannot find.
/// </summary>
public sealed class DocumentCategoriesTests
{
    public static TheoryData<DocumentType> EveryType()
    {
        var data = new TheoryData<DocumentType>();
        foreach (var type in Enum.GetValues<DocumentType>()) data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryType))]
    public void Every_type_has_a_group_a_kind_and_a_label(DocumentType type)
    {
        Assert.True(Enum.IsDefined(DocumentCategories.Group(type)));
        Assert.True(Enum.IsDefined(DocumentCategories.Kind(type)));
        Assert.False(string.IsNullOrWhiteSpace(DocumentCategories.Label(type)));
    }

    [Theory]
    [MemberData(nameof(EveryType))]
    public void Kind_is_photo_exactly_for_the_photos_group(DocumentType type)
    {
        var isPhotoGroup = DocumentCategories.Group(type) == DocumentGroup.Photos;
        Assert.Equal(isPhotoGroup ? DocumentKind.Photo : DocumentKind.Paper, DocumentCategories.Kind(type));
    }

    [Fact]
    public void The_photo_categories_are_car_and_condition_photos()
    {
        Assert.Equal(
            [DocumentType.ConditionPhoto, DocumentType.CarPhoto],
            Enum.GetValues<DocumentType>().Where(t => DocumentCategories.Kind(t) == DocumentKind.Photo).Order());
    }

    [Fact]
    public void All_lists_every_member_once_in_display_order()
    {
        DocumentType[] expected =
        [
            DocumentType.V5C, DocumentType.BillOfSale,
            DocumentType.MOT, DocumentType.Insurance, DocumentType.RoadTax, DocumentType.Breakdown,
            DocumentType.ServiceInvoice, DocumentType.PartsReceipt, DocumentType.Warranty, DocumentType.Receipt,
            DocumentType.Manual, DocumentType.WorkshopManual, DocumentType.Diagram,
            DocumentType.CarPhoto, DocumentType.ConditionPhoto,
            DocumentType.Other,
        ];

        Assert.Equal(expected, DocumentCategories.All.Select(c => c.Type));
        Assert.Equal(Enum.GetValues<DocumentType>().Length, DocumentCategories.All.Count);

        // Groups appear as contiguous runs, in the order the upload sheet shows them.
        Assert.Equal(
            [
                DocumentGroup.Ownership, DocumentGroup.LegalAndCover, DocumentGroup.WorkAndParts,
                DocumentGroup.Reference, DocumentGroup.Photos, DocumentGroup.Other,
            ],
            DocumentCategories.All.Select(c => c.Group).Distinct());
    }

    [Fact]
    public void All_carries_the_same_group_kind_and_label_as_the_lookups()
    {
        foreach (var info in DocumentCategories.All)
        {
            Assert.Equal(DocumentCategories.Group(info.Type), info.Group);
            Assert.Equal(DocumentCategories.Kind(info.Type), info.Kind);
            Assert.Equal(DocumentCategories.Label(info.Type), info.Label);
        }
    }

    [Fact]
    public void The_spec_labels_are_what_an_owner_calls_the_paper()
    {
        Assert.Equal("V5C logbook", DocumentCategories.Label(DocumentType.V5C));
        Assert.Equal("Car photo", DocumentCategories.Label(DocumentType.CarPhoto));
        Assert.Equal("Service invoice", DocumentCategories.Label(DocumentType.ServiceInvoice));
        Assert.Equal("Workshop manual", DocumentCategories.Label(DocumentType.WorkshopManual));
    }

    [Fact]
    public void Stored_numbers_do_not_move_and_the_photo_rename_keeps_five()
    {
        Assert.Equal(1, (int)DocumentType.V5C);
        Assert.Equal(2, (int)DocumentType.Insurance);
        Assert.Equal(3, (int)DocumentType.MOT);
        Assert.Equal(4, (int)DocumentType.Receipt);
        Assert.Equal(5, (int)DocumentType.ConditionPhoto);
        Assert.Equal(6, (int)DocumentType.Manual);
        Assert.Equal(7, (int)DocumentType.Other);
        Assert.Equal(8, (int)DocumentType.BillOfSale);
        Assert.Equal(16, (int)DocumentType.CarPhoto);
        Assert.False(Enum.TryParse<DocumentType>("Photo", out _));
    }
}
