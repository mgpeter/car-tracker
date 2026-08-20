namespace CarTracker.Domain.Accounts;

// The shapes the export writes that have no DTO of their own in CarTracker.Shared, because no screen reads
// them: an account's own identity row, its three reference lists without their derived counts, and its tokens
// without their secrets.
//
// They were private to AccountExportService until the import needed to read four of them. Promoting them
// rather than declaring a second set beside the reader is the same property CatalogueDriftTests protects for
// the tool catalogue: one definition of the format, read from both ends, so a field added to a garage row
// travels out and back with no second place to remember. The two assistant shapes are written and never read
// - an import deliberately restores neither (a token without its secret is not a credential, and an audit
// trail describes writes that happened on another deployment) - and they sit here with their siblings so the
// file is the format rather than the subset that happens to round-trip.

/// <summary>
/// The account the file came from. Provenance on the way back in: an import shows it and writes it nowhere.
/// </summary>
/// <param name="ExternalId">
/// The identity-provider subject. Present in an export because it is the identifier the whole account hangs
/// off and a subject access response that withheld it would be withholding the one field that explains every
/// other. It is emphatically not read on import - an import cannot change who you are.
/// </param>
public sealed record ExportedAccount(
    string ExternalId, string Email, string? DisplayName, DateTimeOffset CreatedAt);

public sealed record ExportedGarage(string Name, string? Contact, string? Address, string? Notes);

public sealed record ExportedWashLocation(string Name, string? Notes);

public sealed record ExportedExpenseCategory(string Name, int DisplayOrder, bool IsSystem);

public sealed record ExportedAssistantToken(
    int Id,
    string Name,
    Shared.AssistantScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    int ReadCount,
    int WriteCount);

public sealed record ExportedAssistantWrite(
    int TokenId, string Tool, int? VehicleId, string Summary, DateTimeOffset TimestampUtc);
