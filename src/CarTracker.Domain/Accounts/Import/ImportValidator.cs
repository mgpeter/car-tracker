using CarTracker.Shared;

namespace CarTracker.Domain.Accounts.Import;

/// <summary>
/// Everything the write paths would have enforced, asserted before a row is written.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the price of the central decision.</b> The import inserts rows rather than replaying them
/// through <c>FuelEntryFactory</c>, <c>ServiceRecordFactory</c> and the rest, because those write the mirrors
/// and the file already contains them - so an import built on the factories would double every money figure
/// on the dashboard. The invariants those factories carry therefore have to be asserted somewhere, and this is
/// where. A rule added to a write path and not to this file is a rule an import can walk past.
/// </para>
/// <para>
/// <b>It runs before the transaction opens</b>, so the common failures - a truncated file, an expense naming a
/// fill that is not in it, two purchase rows on one car - are reported without a write ever being attempted.
/// The alternative is a <c>DbUpdateException</c> naming a constraint, which tells the person who uploaded the
/// file nothing they can act on.
/// </para>
/// <para>
/// <b>Every key names the item, not the field.</b> <c>vehicles[0].expenses[7].fuelEntryId</c> is a path a
/// reader can follow into the file they are holding. <c>lib/formErrors.ts</c> matches no field of that shape,
/// so it folds them into the footer banner - which is the right place for them: they are statements about a
/// file, not about a form control.
/// </para>
/// <para>
/// <b>What it does not check.</b> Anything the account's own database decides: whether a registration
/// collides (that is a question about the importing garage, and the answer is a rename rather than a refusal),
/// and whether a garage the file names exists (reference lists merge by name and are created as used). Those
/// live in <see cref="AccountImportService"/> because they need the account, and this needs only the file.
/// </para>
/// </remarks>
public static class ImportValidator
{
    /// <summary>Every problem in the file, keyed by where it is. Empty means it can be imported.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(ImportPayload payload)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Fail(string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
            messages.Add(message);
        }

        for (var v = 0; v < payload.Vehicles.Count; v++)
        {
            ValidateVehicle(payload.Vehicles[v], $"vehicles[{v}]", Fail);
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void ValidateVehicle(ImportedVehicle vehicle, string at, Action<string, string> fail)
    {
        if (vehicle.Profile is null)
        {
            // Everything below reads through the profile or belongs to it, so there is nothing further to say
            // about this block. One clear sentence beats fifteen consequential ones.
            fail($"{at}.profile", "This vehicle has no profile, so there is no car to create the rows under.");
            return;
        }

        if (string.IsNullOrWhiteSpace(vehicle.Plate))
            fail($"{at}.registration", "This vehicle has no registration.");

        // The referenced tables, and only those. An id is read on the way in solely to be remapped, so a
        // mileage reading's is genuinely unused and demanding one would refuse files over a field nothing
        // consumes. These six are pointed at by something.
        var fuelIds = Ids(vehicle.FuelEntries.Select(f => f.Id), $"{at}.fuelEntries", "fill", fail);
        var serviceIds = Ids(vehicle.ServiceRecords.Select(s => s.Id), $"{at}.serviceRecords", "service record", fail);
        var equipmentIds = Ids(vehicle.Equipment.Select(e => e.Id), $"{at}.equipment", "equipment item", fail);
        var washIds = Ids(vehicle.WashEntries.Select(w => w.Id), $"{at}.washEntries", "wash", fail);
        var checkIds = Ids(vehicle.CheckDefinitions.Select(c => c.Id), $"{at}.checkDefinitions", "check", fail);
        var issueIds = Ids(vehicle.Issues.Select(i => i.Id), $"{at}.issues", "issue", fail);

        ValidateExpenses(vehicle, at, fuelIds, serviceIds, equipmentIds, washIds, fail);

        for (var i = 0; i < vehicle.CheckDefinitions.Count; i++)
        {
            if (vehicle.CheckDefinitions[i].IntervalDays <= 0)
            {
                fail($"{at}.checkDefinitions[{i}].intervalDays",
                    "A check's interval must be at least one day.");
            }
        }

        for (var i = 0; i < vehicle.CheckLogs.Count; i++)
        {
            var log = vehicle.CheckLogs[i];
            if (!checkIds.Contains(log.CheckDefinitionId))
            {
                fail($"{at}.checkLogs[{i}].checkDefinitionId",
                    $"This log is against check {log.CheckDefinitionId}, which this vehicle's file does not contain.");
            }
        }

        for (var i = 0; i < vehicle.Tasks.Count; i++)
        {
            var task = vehicle.Tasks[i];

            if (task.ServiceRecordId is { } linked && !serviceIds.Contains(linked))
            {
                fail($"{at}.tasks[{i}].serviceRecordId",
                    $"This task was promoted to service record {linked}, which this vehicle's file does not contain.");
            }

            // ck_tasks_completed_date_iff_done. Stated here so a hand-edited file is refused by name rather
            // than by a constraint, which is the whole reason validation runs before the transaction.
            if ((task.Status == MaintenanceTaskStatus.Done) != (task.CompletedDate is not null))
            {
                fail($"{at}.tasks[{i}].completedDate",
                    "A task is Done with a completion date or is neither. This one is one of the two.");
            }
        }

        for (var i = 0; i < vehicle.Issues.Count; i++)
        {
            var issue = vehicle.Issues[i];

            // ck_issues_resolved_date_iff_resolved, and the one the issue write path itself got wrong once.
            if ((issue.Status == IssueStatus.Resolved) != (issue.ResolvedDate is not null))
            {
                fail($"{at}.issues[{i}].resolvedDate",
                    "An issue is Resolved with a resolution date or is neither. This one is one of the two.");
            }
        }

        // The join reaches across two tables, so Postgres has no constraint for it and IssueService asserts it
        // on the write path. Both ends are resolved against *this vehicle's* lists, which is exactly what makes
        // a link that crosses vehicles unresolvable here rather than a separate rule.
        for (var i = 0; i < vehicle.IssueWatchChecks.Count; i++)
        {
            var link = vehicle.IssueWatchChecks[i];

            if (!issueIds.Contains(link.IssueId))
            {
                fail($"{at}.issueWatchChecks[{i}].issueId",
                    $"This watch links issue {link.IssueId}, which is not one of this vehicle's issues.");
            }

            if (!checkIds.Contains(link.CheckDefinitionId))
            {
                fail($"{at}.issueWatchChecks[{i}].checkDefinitionId",
                    $"This watch links check {link.CheckDefinitionId}, which is not one of this vehicle's checks. "
                    + "An issue can only watch checks on its own car.");
            }
        }
    }

    private static void ValidateExpenses(
        ImportedVehicle vehicle,
        string at,
        HashSet<int> fuelIds,
        HashSet<int> serviceIds,
        HashSet<int> equipmentIds,
        HashSet<int> washIds,
        Action<string, string> fail)
    {
        var purchases = 0;

        for (var i = 0; i < vehicle.Expenses.Count; i++)
        {
            var expense = vehicle.Expenses[i];
            var key = $"{at}.expenses[{i}]";

            if (string.IsNullOrWhiteSpace(expense.Category))
                fail($"{key}.category", "This expense names no category.");

            // The four mirror links. A mirror is a shadow of a row, so a shadow with nothing casting it is not
            // a row that can be inserted - and it is the failure a partially-copied file produces.
            Link(expense.FuelEntryId, fuelIds, $"{key}.fuelEntryId", "fill", fail);
            Link(expense.ServiceRecordId, serviceIds, $"{key}.serviceRecordId", "service record", fail);
            Link(expense.EquipmentItemId, equipmentIds, $"{key}.equipmentItemId", "equipment item", fail);
            Link(expense.WashEntryId, washIds, $"{key}.washEntryId", "wash", fail);

            if (expense.IsVehiclePurchase) purchases++;
        }

        if (purchases > 1)
        {
            // ix_expense_entries_vehicle_purchase is partial-unique on (vehicle_id) WHERE is_vehicle_purchase,
            // so the second one would be a DbUpdateException naming an index. Naming the rule instead is the
            // difference between "fix your file" and "something went wrong".
            fail($"{at}.expenses",
                $"{purchases} expenses are flagged as the vehicle purchase. A car is bought once, so at most "
                + "one row may carry that flag.");
        }
    }

    private static void Link(
        int? id, HashSet<int> known, string key, string what, Action<string, string> fail)
    {
        if (id is { } value && !known.Contains(value))
        {
            fail(key, $"This expense mirrors {what} {value}, which this vehicle's file does not contain.");
        }
    }

    /// <summary>The ids of one referenced table, complaining about any that is absent or repeated.</summary>
    private static HashSet<int> Ids(
        IEnumerable<int> ids, string at, string what, Action<string, string> fail)
    {
        var seen = new HashSet<int>();
        var index = 0;

        foreach (var id in ids)
        {
            if (id <= 0)
            {
                // System.Text.Json fills an absent member with default and says nothing, which is how a file
                // with a missing id becomes a set of rows that all mirror "0".
                fail($"{at}[{index}].id", $"This {what} has no id, so nothing in the file can refer to it.");
            }
            else if (!seen.Add(id))
            {
                fail($"{at}[{index}].id", $"Two {what}s share id {id}, so a row referring to it is ambiguous.");
            }

            index++;
        }

        return seen;
    }
}
