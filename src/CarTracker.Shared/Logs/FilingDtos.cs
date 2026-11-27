namespace CarTracker.Shared.Logs;

/// <summary>
/// Where a shared photo or document should be filed: what <c>offer_to_file</c> returns and the chat's
/// <c>filing_offer</c> frame carries. A proposal only. Filing is the owner's own upload.
/// </summary>
/// <param name="Registration">The vehicle as written in the garage. The web app builds its own slug from it.</param>
/// <param name="Kind">Paper or photo: which screen the link opens.</param>
/// <param name="Label">What an owner calls <paramref name="Type"/>, from the category table.</param>
/// <param name="Title">The proposed title, trimmed.</param>
/// <param name="LinkedTo">The record it would be attached to, labelled as a filed document's chip is.</param>
/// <param name="Attachment">
/// Chat only: the 1-based position of the file in the owner's latest message with attachments. Echoed as sent.
/// </param>
/// <param name="Allowance">How much of the account's document allowance is used now.</param>
/// <param name="Path">The <c>/file?...</c> link, always present.</param>
/// <param name="Url">The same link made absolute with <c>App:PublicOrigin</c>, or null when that is blank.</param>
public sealed record FilingOffer(
    string Registration,
    DocumentType Type,
    DocumentKind Kind,
    DocumentGroup Group,
    string Label,
    string Title,
    DateOnly? DocumentDate,
    DocumentLink? LinkedTo,
    int? Attachment,
    FilingAllowance Allowance,
    string Path,
    string? Url);

/// <summary>The account's documents held against what its plan allows.</summary>
public sealed record FilingAllowance(int Used, int Limit);
