namespace CarTracker.Domain.Accounts.Import;

/// <summary>How an import call ended. Each maps to one status code and each has a distinct fix.</summary>
public enum ImportOutcome
{
    /// <summary>Parsed, validated, and held. Nothing was written.</summary>
    Previewed = 1,

    /// <summary>The rows are in.</summary>
    Committed = 2,

    /// <summary>Not JSON, truncated, or not an export of this app.</summary>
    Unreadable = 3,

    /// <summary>Readable and impossible. <c>Errors</c> names each item.</summary>
    Invalid = 4,

    /// <summary>Over 25 MB.</summary>
    TooLarge = 5,

    /// <summary>
    /// No such <c>importId</c> - <b>including one that belongs to somebody else</b>, which answers exactly as
    /// an expired one does. Telling them apart would confirm the id is real.
    /// </summary>
    NotFound = 6,

    /// <summary>A registration is taken, including one that became taken between the preview and the commit.</summary>
    Collision = 7,

    /// <summary>No account behind the request. Never reached through the web login, and asserted anyway.</summary>
    NoAccount = 8,
}

/// <param name="Detail">One sentence, naming what is wrong. Null when nothing is.</param>
/// <param name="Errors">The per-item map, keyed <c>vehicles[0].expenses[7].fuelEntryId</c>. Null when there is none.</param>
public sealed record ImportPreviewResult(
    ImportOutcome Outcome,
    ImportPreview? Preview = null,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

/// <inheritdoc cref="ImportPreviewResult"/>
public sealed record ImportCommitResult(
    ImportOutcome Outcome,
    ImportReport? Report = null,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>
/// What importing this file would do, and nothing it has done.
/// </summary>
/// <param name="ImportId">
/// Opaque, server-held and owner-keyed, valid for fifteen minutes. It is the whole authorisation for the
/// commit: the commit request carries the decisions and never the payload, so a crafted call cannot write
/// something the preview never described.
/// </param>
/// <param name="Warnings">
/// Prose, ordered, and the first one is load-bearing. Importing the same file twice silently succeeds - it
/// produces <c>-2</c> and then <c>-3</c> copies of everything - so "3 of 3 vehicles already exist and will be
/// imported as copies" is the sentence that stops an accidental second import, and it has to lead rather than
/// sit beside a row.
/// </param>
public sealed record ImportPreview(
    string ImportId,
    ImportSource Source,
    ImportReferencePreview Reference,
    IReadOnlyList<ImportVehiclePreview> Vehicles,
    IReadOnlyList<string> Warnings);

/// <param name="Email">Provenance only. Shown so the person can tell whose file they are holding; written nowhere.</param>
/// <param name="NewerThanThisApp">
/// True when the file was written by a later <c>VERSION</c>. Not a refusal - that would break every import on
/// every release - but worth saying, because fields a later version added are dropped in silence otherwise.
/// </param>
public sealed record ImportSource(
    DateTimeOffset ExportedAt,
    string? SchemaVersion,
    string? Email,
    string? DisplayName,
    bool NewerThanThisApp);

public sealed record ImportReferencePreview(
    ImportListPreview Garages,
    ImportListPreview WashLocations,
    ImportListPreview ExpenseCategories);

/// <param name="AlreadyYours">
/// Matched by name against your own list and left exactly as it is. A file's garage that names a different
/// address does not overwrite yours - letting an import rewrite the account's own reference data is the
/// cross-tenant write DEC-018 closed, arriving through the front door.
/// </param>
public sealed record ImportListPreview(int InFile, int WillCreate, int AlreadyYours);

/// <param name="Index">Position in this list. The commit's decisions refer to vehicles by it.</param>
/// <param name="Collides">True when the account already owns this registration.</param>
/// <param name="ProposedRegistration">
/// What it will be called. The same as <c>Registration</c> when nothing collides; otherwise the server's
/// proposal, which the commit accepts an override for - the person importing chooses the plate rather than
/// being handed one.
/// </param>
public sealed record ImportVehiclePreview(
    int Index,
    string Registration,
    string Description,
    bool Collides,
    string ProposedRegistration,
    ImportRowCounts Rows,
    ImportSkipped Skipped);

/// <summary>Rows that will be inserted, per table. Counted from the file's arrays, never from a claim in it.</summary>
public sealed record ImportRowCounts(
    int MileageReadings,
    int FuelEntries,
    int Expenses,
    int ServiceRecords,
    int TyreReadings,
    int WashEntries,
    int CheckDefinitions,
    int CheckLogs,
    int Tasks,
    int Issues,
    int IssueWatchChecks,
    int Equipment,
    int BudgetGroups);

/// <param name="Documents">
/// Rows naming files the export does not carry. Importing them would create rows pointing at bytes that do not
/// exist, which is the failure a dump restored without its documents volume produces.
/// </param>
/// <param name="Anomalies">
/// Flags. Not imported and not lost: they are worked out again from the rows once those land, so the integrity
/// queue describes this database rather than another one.
/// </param>
public sealed record ImportSkipped(int Documents, int Anomalies);

/// <summary>What was written.</summary>
public sealed record ImportReport(
    IReadOnlyList<ImportedVehicleReport> Vehicles,
    ImportReferenceReport Reference,
    ImportSkippedTotals Skipped,
    int TotalRows);

/// <param name="ImportedFrom">
/// The registration in the file. The same as <c>Registration</c> unless it was renamed, and the only place the
/// original plate survives besides the line the import adds to the vehicle's notes.
/// </param>
public sealed record ImportedVehicleReport(
    string Registration,
    string ImportedFrom,
    int Rows,
    int AnomaliesRaised);

public sealed record ImportReferenceReport(
    int GaragesCreated,
    int WashLocationsCreated,
    int ExpenseCategoriesCreated);

/// <param name="AssistantTokens">
/// Listed in the file without their secrets, so there is nothing to restore: a token row without its secret is
/// not a credential.
/// </param>
/// <param name="AuditEntries">
/// The assistant write-audit trail. It describes writes that happened on another deployment, and importing it
/// would be fabricating a record rather than restoring one.
/// </param>
public sealed record ImportSkippedTotals(
    int Documents,
    int Anomalies,
    int AssistantTokens,
    int AuditEntries);
