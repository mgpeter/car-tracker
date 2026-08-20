using CarTracker.Data;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Accounts.Import;

/// <summary>
/// The half that writes: an id map, an insert order forced by the foreign keys, and one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here goes through a factory, and the reason is the mirrors.</b> <c>FuelEntryFactory</c> writes
/// three rows for one fill and the file contains all three; <c>ServiceRecordFactory</c> writes three for one
/// service; <c>VehiclePurchaseMirror</c>, the wash mirror and the equipment mirror each write another. Replaying
/// the file through them would produce a second copy of every mirror on top of the ones the file already
/// carries, and every money figure on the dashboard would be inflated by roughly its own mirrors, silently.
/// <c>VehicleFactory.CreateAsync</c> is ruled out for the same reason once over: it creates the opening
/// <c>MileageReading</c>, applies a <c>CheckTemplate</c> and calls the purchase mirror, and all three are in the
/// file. What the factories would have enforced is asserted by <see cref="ImportValidator"/> instead.
/// </para>
/// <para>
/// <b>The order is the foreign keys, not a preference.</b> Every id in the file belongs to another database, so
/// a table is inserted only once everything it points at has real ids to point at - and a <c>SaveChangesAsync</c>
/// between layers is what turns store-generated keys into entries in <see cref="ImportIdMap"/>. Expenses come
/// after all four mirror sources; issue watch links after both issues and check definitions.
/// </para>
/// <para>
/// <b>One transaction for the whole import.</b> A half-imported garage that looks complete is worse than a
/// refusal: the vehicle would be there, its fuel log would be there, its expenses would not, and every money
/// figure would be wrong with nothing flagging it.
/// </para>
/// </remarks>
public sealed class ImportWriter(
    CarTrackerDbContext db,
    ReferenceWriter reference,
    AnomalyScanner anomalies)
{
    /// <summary>
    /// Writes the whole plan, or none of it.
    /// </summary>
    /// <param name="payload">The file, for the reference lists and for the counts the report states.</param>
    /// <param name="plan">One entry per vehicle to import, with its registration already settled.</param>
    public async Task<ImportReport> WriteAsync(
        int ownerId,
        ImportPayload payload,
        IReadOnlyList<ImportVehiclePlan> plan,
        CancellationToken cancellationToken = default)
    {
        var created = new ReferenceTally();
        var vehicles = new List<ImportedVehicleReport>(plan.Count);

        // Mandatory, not decorative: Aspire's EnrichNpgsqlDbContext installs a retrying execution strategy that
        // refuses a user-initiated transaction outside it. This is the trap CLAUDE.md records as passing 41
        // tests and throwing on the first real request, and the test context has no retry strategy, so it is
        // one of the failures the suite cannot catch.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry re-runs this whole body on the *same* context, which still holds everything the failed
            // attempt staged - so without this the second attempt re-sends rows the first already added and the
            // transient failure the strategy exists to absorb becomes a permanent one instead.
            db.ChangeTracker.Clear();
            created = new ReferenceTally();
            vehicles.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await MergeReferenceListsAsync(payload, created, cancellationToken);

            foreach (var item in plan)
            {
                vehicles.Add(await WriteVehicleAsync(ownerId, item, payload.ExportedAt, cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        });

        return new ImportReport(
            vehicles,
            new ImportReferenceReport(created.Garages, created.WashLocations, created.ExpenseCategories),
            new ImportSkippedTotals(
                payload.Vehicles.Sum(v => v.Documents.Count),
                payload.Vehicles.Sum(v => v.Anomalies.Count),
                payload.AssistantTokens.Count,
                payload.AssistantWriteAudit.Count),
            vehicles.Sum(v => v.Rows));
    }

    /// <summary>
    /// The account's three lists, gaining only what they do not already hold.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ReferenceWriter"/>, so <c>ReferenceOwner.Require</c> still guards the inserts and the
    /// existence probe still reads through the owner query filter. <b>Never an update:</b> a file's garage that
    /// names an address different from yours leaves yours alone, because letting an import rewrite the
    /// account's own reference data is the cross-tenant write DEC-018 closed, arriving through the front door.
    /// </remarks>
    private async Task MergeReferenceListsAsync(
        ImportPayload payload, ReferenceTally created, CancellationToken ct)
    {
        foreach (var garage in payload.Reference.Garages)
        {
            if (await reference.EnsureGarageAsync(garage.Name, garage.Contact, garage.Address, garage.Notes, ct))
                created.Garages++;
        }

        foreach (var location in payload.Reference.WashLocations)
        {
            if (await reference.EnsureWashLocationAsync(location.Name, location.Notes, ct))
                created.WashLocations++;
        }

        foreach (var category in payload.Reference.ExpenseCategories)
        {
            if (await reference.EnsureExpenseCategoryAsync(
                    category.Name, category.DisplayOrder, category.IsSystem, ct))
                created.ExpenseCategories++;
        }

        // Then every name the rows themselves mention. A file whose reference block predates a row - or was
        // hand-assembled - would otherwise leave a service record pointing at a garage that is not in the list,
        // which is precisely the 500 ReferenceWriter was written to stop. "Created as used" is the house rule
        // and this is the same door.
        var nextOrder = payload.Reference.ExpenseCategories.Count == 0
            ? 1
            : payload.Reference.ExpenseCategories.Max(c => c.DisplayOrder) + 1;

        foreach (var vehicle in payload.Vehicles)
        {
            if (await reference.EnsureGarageAsync(vehicle.Profile?.DefaultGarage, ct)) created.Garages++;

            foreach (var record in vehicle.ServiceRecords)
                if (await reference.EnsureGarageAsync(record.Garage, ct)) created.Garages++;

            foreach (var task in vehicle.Tasks)
                if (await reference.EnsureGarageAsync(task.AssignedGarage, ct)) created.Garages++;

            foreach (var wash in vehicle.WashEntries)
                if (await reference.EnsureWashLocationAsync(wash.Location, ct)) created.WashLocations++;

            foreach (var expense in vehicle.Expenses)
                if (await reference.EnsureExpenseCategoryAsync(expense.Category, nextOrder, false, ct))
                    created.ExpenseCategories++;

            foreach (var group in vehicle.BudgetGroups)
                foreach (var name in group.Categories ?? [])
                    if (await reference.EnsureExpenseCategoryAsync(name, nextOrder, false, ct))
                        created.ExpenseCategories++;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<ImportedVehicleReport> WriteVehicleAsync(
        int ownerId, ImportVehiclePlan plan, DateTimeOffset exportedAt, CancellationToken ct)
    {
        var source = plan.Vehicle;
        var vehicleId = await WriteProfileAsync(ownerId, plan, exportedAt, ct);
        var map = new ImportIdMap();

        await WriteChecksAsync(vehicleId, source, map, ct);
        await WriteServiceAndTasksAsync(vehicleId, source, map, ct);
        await WriteMirrorSourcesAsync(vehicleId, source, map, ct);
        await WriteExpensesAsync(vehicleId, source, map, ct);
        await WriteReadingsAsync(vehicleId, source, ct);
        await WriteIssuesAsync(vehicleId, source, map, ct);
        await WriteBudgetAsync(vehicleId, source, ct);

        // After the rows land, and inside the same transaction, so a flag can never describe a row that was
        // rolled back. Flags themselves are never imported: an anomaly is a statement about the data in *this*
        // database, its Detail embeds ids and values from another one, and an imported Corrected flag would be
        // an assertion nothing here can check. The deliberate loss is that a flag the exporting owner had
        // Accepted or Dismissed comes back Open.
        var raised = await anomalies.ScanAsync(vehicleId, EntrySource.Import, ct);

        return new ImportedVehicleReport(plan.Registration, plan.ImportedFrom, source.RowCount, raised.Count);
    }

    private async Task<int> WriteProfileAsync(
        int ownerId, ImportVehiclePlan plan, DateTimeOffset exportedAt, CancellationToken ct)
    {
        // The entity out of the file, not a projection of it - which is what makes a column added to Vehicle
        // travel both ways with no code change here. Only the four fields below are the importing account's to
        // decide; every other column is the file's.
        var vehicle = plan.Vehicle.Profile!;

        vehicle.Id = 0;
        vehicle.OwnerId = ownerId;
        vehicle.Registration = plan.Registration;
        vehicle.Source = EntrySource.Import;
        vehicle.Notes = Provenance(vehicle.Notes, plan.ImportedFrom, exportedAt);

        // ix_vehicles_default is unique on (OwnerId) WHERE is_default, so an imported default landing in a
        // garage that already has one is not a second default - it is a failed insert. The account's existing
        // choice wins: an import adds cars, it does not reorganise the garage around them.
        if (vehicle.IsDefault && await db.Vehicles.AnyAsync(v => v.OwnerId == ownerId && v.IsDefault, ct))
        {
            vehicle.IsDefault = false;
        }

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        return vehicle.Id;
    }

    /// <summary>
    /// The line the vehicle's notes gain: where it came from, and when that file was written.
    /// </summary>
    /// <remarks>
    /// It is the only place the original plate survives when the registration has been rewritten, which is the
    /// mitigation the rename rule leans on - a fictional plate whose real one is recorded is a different thing
    /// from a fictional plate. Appended rather than replacing, because the note the owner wrote about their own
    /// car is theirs.
    /// </remarks>
    private static string Provenance(string? existing, string importedFrom, DateTimeOffset exportedAt)
    {
        var line = $"Imported from an account export written on {exportedAt:yyyy-MM-dd}, "
            + $"where this car was registered {importedFrom}.";

        return string.IsNullOrWhiteSpace(existing) ? line : $"{existing.TrimEnd()}\n\n{line}";
    }

    private async Task WriteChecksAsync(int vehicleId, ImportedVehicle source, ImportIdMap map, CancellationToken ct)
    {
        foreach (var definition in source.CheckDefinitions)
        {
            var row = new CheckDefinition
            {
                VehicleId = vehicleId,
                Name = definition.Name,
                CadenceLabel = definition.CadenceLabel,
                IntervalDays = definition.IntervalDays,
                Guidance = Blank(definition.Guidance),
                DisplayOrder = definition.DisplayOrder,
                IsActive = definition.IsActive,
                Source = EntrySource.Import,
            };

            db.CheckDefinitions.Add(row);
            map.Track(ImportTable.CheckDefinition, definition.Id, () => row.Id);
        }

        await db.SaveChangesAsync(ct);

        foreach (var log in source.CheckLogs)
        {
            db.CheckLogs.Add(new CheckLog
            {
                // A check log carries no vehicle column: it reaches its vehicle only through its definition,
                // which is why the definitions had to be saved before this line could run.
                CheckDefinitionId = map.Require(ImportTable.CheckDefinition, log.CheckDefinitionId),
                PerformedOn = log.PerformedOn,
                Result = log.Result,
                Notes = Blank(log.Notes),
                Source = EntrySource.Import,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task WriteServiceAndTasksAsync(
        int vehicleId, ImportedVehicle source, ImportIdMap map, CancellationToken ct)
    {
        foreach (var record in source.ServiceRecords)
        {
            var row = new ServiceRecord
            {
                VehicleId = vehicleId,
                ServiceDate = record.ServiceDate,
                Mileage = record.Mileage,
                Type = record.Type,
                Garage = Blank(record.Garage),
                WorkDone = Blank(record.WorkDone),
                PartsReplaced = Blank(record.PartsReplaced),
                Cost = record.Cost,
                NextDueDate = record.NextDueDate,
                NextDueMileage = record.NextDueMileage,
                Notes = Blank(record.Notes),
                Source = EntrySource.Import,
            };

            db.ServiceRecords.Add(row);
            map.Track(ImportTable.ServiceRecord, record.Id, () => row.Id);
        }

        await db.SaveChangesAsync(ct);

        foreach (var task in source.Tasks)
        {
            db.MaintenanceTasks.Add(new MaintenanceTask
            {
                VehicleId = vehicleId,
                Kind = task.Kind,
                Priority = task.Priority,
                Title = task.Title,
                Description = Blank(task.Description),
                EstimatedCost = task.EstimatedCost,
                Status = task.Status,
                TargetDate = task.TargetDate,
                TargetService = Blank(task.TargetService),
                CompletedDate = task.CompletedDate,
                AssignedGarage = Blank(task.AssignedGarage),
                // A promoted task points at the service record it became. Nullable, and remapped when set.
                ServiceRecordId = map.Translate(ImportTable.ServiceRecord, task.ServiceRecordId),
                Notes = Blank(task.Notes),
                Source = EntrySource.Import,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Fills, equipment and washes - the three tables an expense can be the shadow of.</summary>
    private async Task WriteMirrorSourcesAsync(
        int vehicleId, ImportedVehicle source, ImportIdMap map, CancellationToken ct)
    {
        foreach (var fill in source.FuelEntries)
        {
            var row = new FuelEntry
            {
                VehicleId = vehicleId,
                EntryDate = fill.EntryDate,
                Mileage = fill.Mileage,
                Litres = fill.Litres,
                PricePerLitre = fill.PricePerLitre,
                TotalCost = fill.TotalCost,
                Station = Blank(fill.Station),
                // Load-bearing rather than decorative: Full or unrecorded closes the tank and a partial defers
                // MPG to the next full fill, so a dropped fill level silently changes the economy figures.
                FillLevel = fill.FillLevel,
                Notes = Blank(fill.Notes),
                Source = EntrySource.Import,
            };

            db.FuelEntries.Add(row);
            map.Track(ImportTable.FuelEntry, fill.Id, () => row.Id);
        }

        foreach (var item in source.Equipment)
        {
            var row = new EquipmentItem
            {
                VehicleId = vehicleId,
                Name = item.Name,
                Category = Blank(item.Category),
                PurchasedDate = item.PurchasedDate,
                SourceVendor = Blank(item.SourceVendor),
                Cost = item.Cost,
                StoredAt = Blank(item.StoredAt),
                Status = item.Status,
                Notes = Blank(item.Notes),
                Source = EntrySource.Import,
            };

            db.EquipmentItems.Add(row);
            map.Track(ImportTable.EquipmentItem, item.Id, () => row.Id);
        }

        foreach (var wash in source.WashEntries)
        {
            var row = new WashEntry
            {
                VehicleId = vehicleId,
                WashDate = wash.WashDate,
                Location = Blank(wash.Location),
                WashType = Blank(wash.WashType),
                Cost = wash.Cost,
                Mileage = wash.Mileage,
                Notes = Blank(wash.Notes),
                Source = EntrySource.Import,
            };

            db.WashEntries.Add(row);
            map.Track(ImportTable.WashEntry, wash.Id, () => row.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <remarks>
    /// After every mirror source exists, because all four of an expense's source links remap. The
    /// <c>IsVehiclePurchase</c> flag rides across as it stands: <c>ix_expense_entries_vehicle_purchase</c> is
    /// partial-unique per vehicle and the validator has already refused a file carrying two.
    /// </remarks>
    private async Task WriteExpensesAsync(
        int vehicleId, ImportedVehicle source, ImportIdMap map, CancellationToken ct)
    {
        foreach (var expense in source.Expenses)
        {
            db.ExpenseEntries.Add(new ExpenseEntry
            {
                VehicleId = vehicleId,
                EntryDate = expense.EntryDate,
                Category = expense.Category,
                SubCategory = Blank(expense.SubCategory),
                Vendor = Blank(expense.Vendor),
                Amount = expense.Amount,
                Mileage = expense.Mileage,
                PaymentMethod = Blank(expense.PaymentMethod),
                FuelEntryId = map.Translate(ImportTable.FuelEntry, expense.FuelEntryId),
                ServiceRecordId = map.Translate(ImportTable.ServiceRecord, expense.ServiceRecordId),
                EquipmentItemId = map.Translate(ImportTable.EquipmentItem, expense.EquipmentItemId),
                WashEntryId = map.Translate(ImportTable.WashEntry, expense.WashEntryId),
                IsVehiclePurchase = expense.IsVehiclePurchase,
                Notes = Blank(expense.Notes),
                Source = EntrySource.Import,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <remarks>
    /// Mileage readings are inserted verbatim, <c>Origin</c> preserved. A reading whose origin is <c>Fuel</c>
    /// or <c>Service</c> is emphatically <b>not</b> re-derived from the fill or the record that produced it on
    /// the other deployment: it is a row, and the file has it. Re-deriving would be the doubling this whole
    /// design exists to avoid, one table further down.
    /// </remarks>
    private async Task WriteReadingsAsync(int vehicleId, ImportedVehicle source, CancellationToken ct)
    {
        foreach (var reading in source.MileageReadings)
        {
            db.MileageReadings.Add(new MileageReading
            {
                VehicleId = vehicleId,
                ReadingDate = reading.ReadingDate,
                Mileage = reading.Mileage,
                Origin = reading.Origin,
                Notes = Blank(reading.Notes),
                Source = EntrySource.Import,
            });
        }

        foreach (var tyre in source.TyreReadings)
        {
            db.TyreReadings.Add(new TyreReading
            {
                VehicleId = vehicleId,
                ReadingDate = tyre.ReadingDate,
                Mileage = tyre.Mileage,
                PsiFrontLeft = tyre.PsiFrontLeft,
                PsiFrontRight = tyre.PsiFrontRight,
                PsiRearLeft = tyre.PsiRearLeft,
                PsiRearRight = tyre.PsiRearRight,
                PsiSpare = tyre.PsiSpare,
                TreadFrontLeft = tyre.TreadFrontLeft,
                TreadFrontRight = tyre.TreadFrontRight,
                TreadRearLeft = tyre.TreadRearLeft,
                TreadRearRight = tyre.TreadRearRight,
                Location = Blank(tyre.Location),
                Tool = Blank(tyre.Tool),
                Notes = Blank(tyre.Notes),
                Source = EntrySource.Import,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task WriteIssuesAsync(
        int vehicleId, ImportedVehicle source, ImportIdMap map, CancellationToken ct)
    {
        foreach (var issue in source.Issues)
        {
            var row = new Issue
            {
                VehicleId = vehicleId,
                Title = issue.Title,
                Severity = issue.Severity,
                FirstNoted = issue.FirstNoted,
                LastChecked = issue.LastChecked,
                CurrentObservation = Blank(issue.CurrentObservation),
                ActionIfWorsens = Blank(issue.ActionIfWorsens),
                EstimatedFixCost = issue.EstimatedFixCost,
                Status = issue.Status,
                ResolvedDate = issue.ResolvedDate,
                Notes = Blank(issue.Notes),
                Source = EntrySource.Import,
            };

            db.Issues.Add(row);
            map.Track(ImportTable.Issue, issue.Id, () => row.Id);
        }

        await db.SaveChangesAsync(ct);

        foreach (var link in source.IssueWatchChecks)
        {
            // Both ends resolve through this vehicle's own map, which is what enforces the same-vehicle
            // invariant here: a link naming another car's check has nothing to translate against, and the
            // validator has already refused the file for it. Postgres has no constraint for this - the join
            // reaches across two tables - so IssueService asserts it on the write path and this does here.
            db.IssueWatchChecks.Add(new IssueWatchCheck
            {
                IssueId = map.Require(ImportTable.Issue, link.IssueId),
                CheckDefinitionId = map.Require(ImportTable.CheckDefinition, link.CheckDefinitionId),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task WriteBudgetAsync(int vehicleId, ImportedVehicle source, CancellationToken ct)
    {
        foreach (var group in source.BudgetGroups)
        {
            // The memberships hang off the navigation rather than being saved separately: BudgetGroup is the
            // one imported entity with a collection, so EF fills the foreign key in and one save does both.
            db.BudgetGroups.Add(new BudgetGroup
            {
                VehicleId = vehicleId,
                Name = group.Name,
                // Null is a *tracked* group - spend shown, no target set - and is not zero. Flattening the two
                // would turn "no target yet" into "spend nothing here".
                AnnualBudget = group.AnnualBudget,
                DisplayOrder = group.DisplayOrder,
                Source = EntrySource.Import,
                Categories = [.. (group.Categories ?? []).Select(name => new BudgetGroupCategory
                {
                    VehicleId = vehicleId,
                    Category = name,
                })],
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Null for an empty string.
    /// </summary>
    /// <remarks>
    /// Every log table carries a <c>notes &lt;&gt; ''</c> check constraint, so an empty string is not a shorter
    /// note - it is a failed insert, and it is the one difference between a row a form produced and a row a
    /// file replayed.
    /// </remarks>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class ReferenceTally
    {
        public int Garages;
        public int WashLocations;
        public int ExpenseCategories;
    }
}

/// <summary>The tables whose ids anything in the file refers to.</summary>
public enum ImportTable
{
    CheckDefinition = 1,
    ServiceRecord = 2,
    FuelEntry = 3,
    EquipmentItem = 4,
    WashEntry = 5,
    Issue = 6,
}

/// <summary>
/// A file's ids to this database's, per table, for the length of one vehicle.
/// </summary>
/// <remarks>
/// <para>
/// Every id in an export belongs to another database, and several of them are pointed at: an expense names the
/// fill it mirrors, a check log names its definition, a promoted task names its service record, a watch link
/// names both an issue and a check. So each row is inserted, its store-generated id read back, and the pair
/// recorded here for whatever refers to it later.
/// </para>
/// <para>
/// <b>Ids are read lazily.</b> <c>Track</c> takes a function rather than a value because the new id does not
/// exist until <c>SaveChangesAsync</c> runs, and the whole layer is staged before that happens. Reading eagerly
/// would record a map of zeroes, which is the kind of bug that produces rows that all mirror the same thing.
/// </para>
/// <para>
/// <b>Per vehicle, not per import.</b> Two cars in one file can each have a fill numbered 9, because the ids
/// were unique per table on the other deployment and not per account. A map shared across vehicles would let
/// one car's expense mirror another car's fill.
/// </para>
/// </remarks>
public sealed class ImportIdMap
{
    private readonly Dictionary<(ImportTable Table, int OldId), Func<int>> _rows = [];

    public void Track(ImportTable table, int oldId, Func<int> newId) => _rows[(table, oldId)] = newId;

    /// <summary>The new id for one the file named, or null when it named none.</summary>
    public int? Translate(ImportTable table, int? oldId) =>
        oldId is { } id ? Require(table, id) : null;

    /// <summary>
    /// The new id for one the file named, refusing loudly when there is not one.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When the file referred to a row it does not contain. <see cref="ImportValidator"/> has already refused
    /// every file that does, so reaching this is a bug in the validator rather than a bad upload - and a named
    /// failure inside the transaction beats a foreign-key violation, or worse, a silent zero.
    /// </exception>
    public int Require(ImportTable table, int oldId) =>
        _rows.TryGetValue((table, oldId), out var newId)
            ? newId()
            : throw new InvalidOperationException(
                $"The import refers to {table} {oldId}, which is not in this vehicle's file. Validation should "
                + "have refused this before the transaction opened.");
}
