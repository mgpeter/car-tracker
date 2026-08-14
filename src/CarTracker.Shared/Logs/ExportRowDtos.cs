namespace CarTracker.Shared.Logs;

// The six tables no screen lists as rows: fuel entries (the fuel screen shows derived fills), check logs (the
// checks screen shows a status per definition), budget groups with their memberships (the budget screen shows
// targets against derived spend), the issue-watch links (the issues screen shows each watch's live check
// status), issues themselves (IssueItem carries that watch) and documents (DocumentItem carries a rendered link
// label, and its wrapper a size total). Each was therefore the one kind of row that had no shape here — which is
// exactly how an export comes to omit a table and nobody notices, because no screen would have looked thinner.
//
// They sit beside the screen row DTOs because they are the same kind of thing: a stored row, every field a
// column, not one derived value among them. Nothing stops a future screen reading one.

/// <summary>
/// One fill exactly as stored. No MPG, no L/100 km, no miles-since-last — those are computed per fill from this
/// row and its predecessor, and storing them is the defect that doubled the workbook's litres total.
/// </summary>
public sealed record FuelEntryItem(
    int Id,
    DateOnly EntryDate,
    int Mileage,
    decimal Litres,
    decimal PricePerLitre,
    decimal TotalCost,
    string? Station,
    FillLevel? FillLevel,
    string? Notes);

/// <summary>
/// One performance of a check, carrying the definition it belongs to rather than a vehicle id — the same scoping
/// the row itself has, because a check log reaches its vehicle only through its definition.
/// </summary>
/// <param name="Result">
/// Null on a log recorded before the verdict was surfaced. Null is not OK: the status calculator treats a
/// verdict-less log as "performed, nothing reported", and a reader of an export must be able to tell the two apart.
/// </param>
public sealed record CheckLogItem(
    int Id,
    int CheckDefinitionId,
    DateOnly PerformedOn,
    CheckResult? Result,
    string? Notes);

/// <summary>
/// One budget group and the categories it covers. The target is the only stored figure — YTD actual, remaining
/// and % used all derive from the member categories' expense rows.
/// </summary>
/// <param name="AnnualBudget">
/// Null for a <b>tracked</b> group — spend shown, no target set. Null is not zero, and an export that flattened
/// the two would turn "no target yet" into "spend nothing here".
/// </param>
/// <param name="Categories">The member category names, which are the natural keys of the expense-category table.</param>
public sealed record BudgetGroupItem(
    int Id,
    string Name,
    decimal? AnnualBudget,
    int DisplayOrder,
    IReadOnlyList<string> Categories);

/// <summary>
/// One link in an issue's early-warning watch. The pair is the whole row — the watch's status is the live status
/// of the linked checks and is never stored (DEC-002), so there is nothing else here to carry.
/// </summary>
public sealed record IssueWatchLinkItem(int IssueId, int CheckDefinitionId);

/// <summary>
/// One watchlist issue exactly as stored, without its watch.
/// </summary>
/// <remarks>
/// <see cref="IssueItem"/> is the screen's shape and carries <c>Watch</c> — the linked checks with their
/// <i>live</i> status, recomputed on every read. That is right for a screen and wrong for an export: it is a
/// status as at the moment of the download, and reading it back a year later would present a stale verdict as a
/// stored fact. The links themselves are exported separately as <see cref="IssueWatchLinkItem"/>, which is what
/// the database actually holds.
/// </remarks>
public sealed record IssueRowItem(
    int Id,
    string Title,
    Severity Severity,
    DateOnly FirstNoted,
    DateOnly? LastChecked,
    string? CurrentObservation,
    string? ActionIfWorsens,
    decimal? EstimatedFixCost,
    IssueStatus Status,
    DateOnly? ResolvedDate,
    string? Notes);

/// <summary>
/// One filed document's row, without the link label the screen renders.
/// </summary>
/// <param name="FilePath">
/// Where the bytes sit under the documents volume, <c>{vehicleId}/{sha256}.{ext}</c>. Included because the bytes
/// are <b>not</b>: an export that names the file it is not carrying can be reconciled against a folder-copy
/// backup, and one that hides the name cannot.
/// </param>
public sealed record DocumentRowItem(
    int Id,
    DocumentType Type,
    string Title,
    DateOnly? DocumentDate,
    string FilePath,
    string ContentType,
    long SizeBytes,
    string? Sha256,
    int? ServiceRecordId,
    int? ExpenseEntryId,
    int? IssueId,
    string? Notes);
