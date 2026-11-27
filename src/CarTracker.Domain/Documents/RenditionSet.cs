namespace CarTracker.Domain.Documents;

/// <summary>One rendition part as it arrived: its declared content type and, when the caller knows it, its length.</summary>
public sealed record RenditionPart(string? ContentType, long? Length = null);

/// <summary>
/// The rendition parts of an upload or a backfill, described before any byte is streamed: a thumbnail, a display
/// copy and the decoded original's dimensions. All of them or none.
/// </summary>
/// <remarks>
/// The browser makes both renditions from one decode, so a thumbnail without a display copy, or dimensions
/// without renditions, is a client bug rather than a choice, and is refused rather than half stored. The
/// database says the same thing in <c>ck_documents_renditions</c> and <c>ck_documents_dimensions</c>.
/// </remarks>
public sealed record RenditionSet(RenditionPart? Thumbnail, RenditionPart? Display, int? Width, int? Height)
{
    public bool IsEmpty => Thumbnail is null && Display is null && Width is null && Height is null;

    /// <summary>The first thing wrong with the set, or null when it is complete and acceptable, or empty.</summary>
    public (string Field, string Message)? Validate()
    {
        if (IsEmpty) return null;

        if (Thumbnail is null)
            return ("Thumbnail", "Renditions come as a set: a thumbnail, a display copy, a width and a height.");
        if (Display is null)
            return ("Display", "Renditions come as a set: a thumbnail, a display copy, a width and a height.");

        if (Part("Thumbnail", "thumbnail", Thumbnail, DocumentStore.MaxThumbnailBytes) is { } thumbnailError)
            return thumbnailError;
        if (Part("Display", "display copy", Display, DocumentStore.MaxDisplayBytes) is { } displayError)
            return displayError;

        if (Width is null) return ("Width", "Renditions need the original's width in pixels.");
        if (Height is null) return ("Height", "Renditions need the original's height in pixels.");
        if (Width <= 0) return ("Width", "The width must be a positive number of pixels.");
        if (Height <= 0) return ("Height", "The height must be a positive number of pixels.");

        return null;
    }

    private static (string Field, string Message)? Part(string field, string name, RenditionPart part, long cap)
    {
        if (!DocumentStore.IsRendition(part.ContentType))
        {
            return (field,
                $"A {name} must be {string.Join(" or ", DocumentStore.RenditionContentTypes.Keys)}, "
                + $"not '{part.ContentType}'.");
        }

        if (part.Length > cap)
            return (field, $"A {name} may be at most {cap / 1024} KB.");

        return null;
    }
}

/// <summary>Both renditions' bytes and the dimensions, for a backfill or an upload, before they are stored.</summary>
public sealed record RenditionUpload(
    Stream Thumbnail,
    string ThumbnailContentType,
    Stream Display,
    string DisplayContentType,
    int Width,
    int Height)
{
    public RenditionSet Describe() =>
        new(new RenditionPart(ThumbnailContentType), new RenditionPart(DisplayContentType), Width, Height);
}

/// <summary>Both renditions on the volume, ready to be recorded against a row.</summary>
public sealed record StoredRenditions(StoredFile Thumbnail, StoredFile Display, int Width, int Height);
