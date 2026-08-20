using CarTracker.Data;
using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Accounts.Import;

/// <summary>
/// An account export, read back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every member is one of the export's own shapes.</b> Not a parallel set declared here: the row DTOs come
/// from <c>CarTracker.Shared/Logs/</c>, the four reference and account shapes from <c>ExportedRows.cs</c>
/// beside the writer, and the vehicle profile is <see cref="Data.Vehicle"/> itself, because the export writes
/// the entity rather than a projection of it. So a column added to <c>Vehicle</c>, or a field added to a
/// garage row, travels out and back with no code change here - which is the whole reason the export was
/// written that way, and would be given up by declaring a reader's own copy of the format.
/// </para>
/// <para>
/// <b>Every list is nullable on the way in and empty on the way out.</b> An absent array and an empty one mean
/// the same thing to an import, and the alternative is a null check at each of sixteen use sites. What an
/// absent array must never do is pass for a <i>populated</i> one, which is why the counts the preview reports
/// are counts of these lists rather than anything the file asserts about itself.
/// </para>
/// <para>
/// <b>What is deliberately absent from this type:</b> nothing. <c>documents</c>, <c>anomalies</c>,
/// <c>assistantTokens</c> and <c>assistantWriteAudit</c> are all read even though none of them is imported,
/// because the preview counts what it is skipping and a count of something you did not parse is a guess. The
/// <c>notes</c> array is the one omission: it is prose the writer puts in for a human reader.
/// </para>
/// </remarks>
/// <param name="ExportedAt">When the file was written. Absent or default means this is not one of ours.</param>
/// <param name="SchemaVersion">
/// The app <c>VERSION</c> that wrote it. <b>Reported, never enforced.</b> Refusing a mismatch would break every
/// import on every release; the preview states it and warns when the file is newer than the running app, since
/// fields a later version added are dropped in silence otherwise.
/// </param>
/// <param name="Account">Provenance. Shown in the preview and written nowhere - an import cannot change who you are.</param>
public sealed record ImportPayload(
    DateTimeOffset ExportedAt,
    string? SchemaVersion,
    ExportedAccount? Account,
    ImportedReference? Reference,
    IReadOnlyList<ImportedVehicle>? Vehicles,
    IReadOnlyList<ExportedAssistantToken>? AssistantTokens = null,
    IReadOnlyList<ExportedAssistantWrite>? AssistantWriteAudit = null)
{
    public IReadOnlyList<ImportedVehicle> Vehicles { get; init; } = Vehicles ?? [];
    public IReadOnlyList<ExportedAssistantToken> AssistantTokens { get; init; } = AssistantTokens ?? [];
    public IReadOnlyList<ExportedAssistantWrite> AssistantWriteAudit { get; init; } = AssistantWriteAudit ?? [];

    /// <summary>The three lists, never null, so callers need not ask whether the file carried the block.</summary>
    public ImportedReference Reference { get; init; } = Reference ?? new ImportedReference(null, null, null);
}

/// <summary>The account's three reference lists, keyed <c>(OwnerId, Name)</c> since DEC-018.</summary>
public sealed record ImportedReference(
    IReadOnlyList<ExportedGarage>? Garages,
    IReadOnlyList<ExportedWashLocation>? WashLocations,
    IReadOnlyList<ExportedExpenseCategory>? ExpenseCategories)
{
    public IReadOnlyList<ExportedGarage> Garages { get; init; } = Garages ?? [];
    public IReadOnlyList<ExportedWashLocation> WashLocations { get; init; } = WashLocations ?? [];
    public IReadOnlyList<ExportedExpenseCategory> ExpenseCategories { get; init; } = ExpenseCategories ?? [];
}

/// <summary>
/// One car and everything filed under it.
/// </summary>
/// <param name="Registration">
/// Written beside the profile by the export so a file is readable without unpacking the entity. The profile's
/// own registration is the one that is imported; this is the label the preview shows and the plate the
/// vehicle's notes record it was cloned from.
/// </param>
/// <param name="Profile">
/// The <see cref="Data.Vehicle"/> row itself, with its four owned blocks. Deserialising into the entity is what
/// makes a new column travel both ways for free; <c>Id</c>, <c>OwnerId</c> and the computed normalised
/// registration are overwritten on the way in, and every other column is the file's.
/// </param>
public sealed record ImportedVehicle(
    string? Registration,
    Vehicle? Profile,
    IReadOnlyList<MileageReadingItem>? MileageReadings = null,
    IReadOnlyList<FuelEntryItem>? FuelEntries = null,
    IReadOnlyList<ExpenseItem>? Expenses = null,
    IReadOnlyList<ServiceRecordItem>? ServiceRecords = null,
    IReadOnlyList<TyreReadingItem>? TyreReadings = null,
    IReadOnlyList<WashItem>? WashEntries = null,
    IReadOnlyList<CheckDefinitionResponse>? CheckDefinitions = null,
    IReadOnlyList<CheckLogItem>? CheckLogs = null,
    IReadOnlyList<TaskItem>? Tasks = null,
    IReadOnlyList<IssueRowItem>? Issues = null,
    IReadOnlyList<IssueWatchLinkItem>? IssueWatchChecks = null,
    IReadOnlyList<EquipmentItemDto>? Equipment = null,
    IReadOnlyList<BudgetGroupItem>? BudgetGroups = null,
    IReadOnlyList<DocumentRowItem>? Documents = null,
    IReadOnlyList<AnomalyItem>? Anomalies = null)
{
    public IReadOnlyList<MileageReadingItem> MileageReadings { get; init; } = MileageReadings ?? [];
    public IReadOnlyList<FuelEntryItem> FuelEntries { get; init; } = FuelEntries ?? [];
    public IReadOnlyList<ExpenseItem> Expenses { get; init; } = Expenses ?? [];
    public IReadOnlyList<ServiceRecordItem> ServiceRecords { get; init; } = ServiceRecords ?? [];
    public IReadOnlyList<TyreReadingItem> TyreReadings { get; init; } = TyreReadings ?? [];
    public IReadOnlyList<WashItem> WashEntries { get; init; } = WashEntries ?? [];
    public IReadOnlyList<CheckDefinitionResponse> CheckDefinitions { get; init; } = CheckDefinitions ?? [];
    public IReadOnlyList<CheckLogItem> CheckLogs { get; init; } = CheckLogs ?? [];
    public IReadOnlyList<TaskItem> Tasks { get; init; } = Tasks ?? [];
    public IReadOnlyList<IssueRowItem> Issues { get; init; } = Issues ?? [];
    public IReadOnlyList<IssueWatchLinkItem> IssueWatchChecks { get; init; } = IssueWatchChecks ?? [];
    public IReadOnlyList<EquipmentItemDto> Equipment { get; init; } = Equipment ?? [];
    public IReadOnlyList<BudgetGroupItem> BudgetGroups { get; init; } = BudgetGroups ?? [];
    public IReadOnlyList<DocumentRowItem> Documents { get; init; } = Documents ?? [];
    public IReadOnlyList<AnomalyItem> Anomalies { get; init; } = Anomalies ?? [];

    /// <summary>The plate to show for this block, whichever half of the file carries it.</summary>
    public string Plate =>
        !string.IsNullOrWhiteSpace(Registration) ? Registration!
        : Profile?.Registration ?? string.Empty;

    /// <summary>Every row that will be inserted, counted once. Documents and anomalies are not among them.</summary>
    public int RowCount =>
        1
        + MileageReadings.Count + FuelEntries.Count + Expenses.Count + ServiceRecords.Count
        + TyreReadings.Count + WashEntries.Count + CheckDefinitions.Count + CheckLogs.Count
        + Tasks.Count + Issues.Count + IssueWatchChecks.Count + Equipment.Count
        + BudgetGroups.Count + BudgetGroups.Sum(g => g.Categories?.Count ?? 0);
}
