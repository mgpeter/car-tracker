using CarTracker.Data;
using CarTracker.Domain.Accounts.Import;
using CarTracker.Shared;
using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The invariants the factories would have enforced, asserted against a file instead.
/// </summary>
/// <remarks>
/// <para>
/// These exist because of the central decision in the spec: the import inserts rows rather than replaying them
/// through <c>FuelEntryFactory</c> and its siblings, since the file already contains the mirrors those write.
/// So the rules move here, and a rule that is only in a write path is a rule an import walks past.
/// </para>
/// <para>
/// <b>Every assertion is on a key as well as a message.</b> The key is a path into the file the person is
/// holding, and a validator that reported "something is wrong with your expenses" would have been cheaper to
/// write and worth nothing to the person reading it.
/// </para>
/// </remarks>
public class ImportValidatorTests
{
    private static Vehicle Profile(string registration = "BT53 AKJ") => new()
    {
        Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
        PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, FuelType = FuelType.Petrol,
    };

    private static ImportPayload With(ImportedVehicle vehicle) =>
        new(new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero), "0.18.0", null, null, [vehicle]);

    private static ImportedVehicle Car(
        IReadOnlyList<FuelEntryItem>? fuel = null,
        IReadOnlyList<ExpenseItem>? expenses = null,
        IReadOnlyList<ServiceRecordItem>? services = null,
        IReadOnlyList<CheckDefinitionResponse>? checks = null,
        IReadOnlyList<CheckLogItem>? checkLogs = null,
        IReadOnlyList<TaskItem>? tasks = null,
        IReadOnlyList<IssueRowItem>? issues = null,
        IReadOnlyList<IssueWatchLinkItem>? watch = null,
        Vehicle? profile = null) =>
        new("BT53 AKJ", profile ?? Profile(),
            FuelEntries: fuel, Expenses: expenses, ServiceRecords: services,
            CheckDefinitions: checks, CheckLogs: checkLogs, Tasks: tasks,
            Issues: issues, IssueWatchChecks: watch);

    private static FuelEntryItem Fill(int id) =>
        new(id, new DateOnly(2026, 4, 2), 77_881, 44.02m, 1.599m, 70.39m, "Applegreen", FillLevel.Full, null);

    private static ExpenseItem Expense(
        int id, int? fuelId = null, int? serviceId = null, int? equipmentId = null, int? washId = null,
        bool purchase = false) =>
        new(id, new DateOnly(2026, 4, 2), "Fuel", null, null, 70.39m, null, null,
            fuelId, serviceId, equipmentId, null, washId, purchase);

    private static CheckDefinitionResponse Check(int id, int intervalDays = 7) =>
        new(id, $"Check {id}", "Weekly", intervalDays, null, id, true);

    [Fact]
    public void A_coherent_file_has_nothing_to_say_about_it()
    {
        var errors = ImportValidator.Validate(With(Car(
            fuel: [Fill(9)],
            expenses: [Expense(21, fuelId: 9), Expense(22, purchase: true)],
            checks: [Check(3)],
            checkLogs: [new CheckLogItem(1, 3, new DateOnly(2026, 7, 1), CheckResult.OK, null)],
            issues: [Issue(5, IssueStatus.Monitoring)],
            watch: [new IssueWatchLinkItem(5, 3)])));

        Assert.Empty(errors);
    }

    [Fact]
    public void A_vehicle_with_no_profile_is_refused_once_rather_than_fifteen_times()
    {
        var errors = ImportValidator.Validate(With(
            new ImportedVehicle("BT53 AKJ", null, Expenses: [Expense(21, fuelId: 999)])));

        var key = Assert.Single(errors).Key;
        Assert.Equal("vehicles[0].profile", key);
    }

    /// <summary>
    /// The case the spec names: an expense mirroring a fill that is not in the file. A mirror is a shadow, and
    /// a shadow with nothing casting it cannot be inserted.
    /// </summary>
    [Fact]
    public void An_expense_mirroring_a_fill_the_file_does_not_contain_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(
            fuel: [Fill(9)],
            expenses: [Expense(21, fuelId: 9), Expense(22, fuelId: 404)])));

        var message = Assert.Single(Assert.Contains("vehicles[0].expenses[1].fuelEntryId", errors));
        Assert.Contains("404", message);
    }

    [Theory]
    [InlineData("serviceRecordId")]
    [InlineData("equipmentItemId")]
    [InlineData("washEntryId")]
    public void The_other_three_mirror_links_are_checked_the_same_way(string field)
    {
        var expense = field switch
        {
            "serviceRecordId" => Expense(21, serviceId: 77),
            "equipmentItemId" => Expense(21, equipmentId: 77),
            _ => Expense(21, washId: 77),
        };

        var errors = ImportValidator.Validate(With(Car(expenses: [expense])));

        Assert.Contains($"vehicles[0].expenses[0].{field}", errors);
    }

    /// <summary>
    /// <c>ix_expense_entries_vehicle_purchase</c> is partial-unique on the flag, so the second row would be a
    /// <c>DbUpdateException</c> naming an index. This is the same refusal with the rule in it.
    /// </summary>
    [Fact]
    public void Two_rows_flagged_as_the_vehicle_purchase_are_refused_by_the_rule_not_the_index()
    {
        var errors = ImportValidator.Validate(With(Car(
            expenses: [Expense(21, purchase: true), Expense(22, purchase: true)])));

        var message = Assert.Single(Assert.Contains("vehicles[0].expenses", errors));
        Assert.Contains("A car is bought once", message);
    }

    [Fact]
    public void One_row_flagged_as_the_vehicle_purchase_is_the_normal_case()
    {
        Assert.Empty(ImportValidator.Validate(With(Car(expenses: [Expense(21, purchase: true)]))));
    }

    /// <summary>
    /// The same-vehicle invariant <c>IssueService</c> enforces on the write path. The join reaches across two
    /// tables, so Postgres has no constraint for it - resolving both ends against this vehicle's own lists is
    /// what makes a crossing link unresolvable rather than a separate rule to remember.
    /// </summary>
    [Fact]
    public void A_watch_link_naming_another_vehicles_check_is_refused()
    {
        var payload = new ImportPayload(
            new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero), "0.18.0", null, null,
            [
                Car(checks: [Check(3)], issues: [Issue(5, IssueStatus.Monitoring)],
                    watch: [new IssueWatchLinkItem(5, 3)]),
                // The second car's issue reaching for the first car's check, which is the shape the guard is for.
                new ImportedVehicle("KV02 XYZ", Profile("KV02 XYZ"),
                    CheckDefinitions: [Check(4)],
                    Issues: [Issue(6, IssueStatus.Monitoring)],
                    IssueWatchChecks: [new IssueWatchLinkItem(6, 3)]),
            ]);

        var errors = ImportValidator.Validate(payload);

        var message = Assert.Single(Assert.Contains("vehicles[1].issueWatchChecks[0].checkDefinitionId", errors));
        Assert.Contains("its own car", message);
    }

    [Fact]
    public void A_check_log_against_a_check_the_file_does_not_contain_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(
            checks: [Check(3)],
            checkLogs: [new CheckLogItem(1, 99, new DateOnly(2026, 7, 1), CheckResult.OK, null)])));

        Assert.Contains("vehicles[0].checkLogs[0].checkDefinitionId", errors);
    }

    [Fact]
    public void A_task_promoted_to_a_service_record_the_file_does_not_contain_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(
            tasks: [Task(1, MaintenanceTaskStatus.Done, new DateOnly(2026, 7, 8), serviceRecordId: 42)])));

        Assert.Contains("vehicles[0].tasks[0].serviceRecordId", errors);
    }

    /// <summary>
    /// The silence <see cref="System.Text.Json"/> leaves behind: an absent id is <c>0</c>, and every row that
    /// mirrors it would mirror nothing. Only the referenced tables are checked - a mileage reading's id is
    /// read solely to be discarded, and demanding one would refuse files over a field nothing consumes.
    /// </summary>
    [Fact]
    public void A_referenced_row_with_no_id_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(fuel: [Fill(0)])));

        var message = Assert.Single(Assert.Contains("vehicles[0].fuelEntries[0].id", errors));
        Assert.Contains("nothing in the file can refer to it", message);
    }

    [Fact]
    public void A_mileage_reading_needs_no_id_because_nothing_points_at_one()
    {
        var payload = With(new ImportedVehicle("BT53 AKJ", Profile(),
            MileageReadings: [new MileageReadingItem(0, new DateOnly(2026, 7, 8), 80_705, MileageOrigin.Manual, null)]));

        Assert.Empty(ImportValidator.Validate(payload));
    }

    [Fact]
    public void Two_referenced_rows_sharing_an_id_are_refused_because_a_mirror_would_be_ambiguous()
    {
        var errors = ImportValidator.Validate(With(Car(fuel: [Fill(9), Fill(9)])));

        var message = Assert.Single(Assert.Contains("vehicles[0].fuelEntries[1].id", errors));
        Assert.Contains("ambiguous", message);
    }

    [Fact]
    public void A_check_with_no_interval_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(checks: [Check(3, intervalDays: 0)])));

        Assert.Contains("vehicles[0].checkDefinitions[0].intervalDays", errors);
    }

    /// <summary>
    /// <c>ck_issues_resolved_date_iff_resolved</c>. Worth stating by name here: the write path missed exactly
    /// this pairing once and the failure was a bare <c>DbUpdateException</c> on the first issue ever posted
    /// already resolved.
    /// </summary>
    [Fact]
    public void A_resolved_issue_with_no_resolution_date_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(issues: [Issue(5, IssueStatus.Resolved)])));

        Assert.Contains("vehicles[0].issues[0].resolvedDate", errors);
    }

    [Fact]
    public void A_done_task_with_no_completion_date_is_refused()
    {
        var errors = ImportValidator.Validate(With(Car(
            tasks: [Task(1, MaintenanceTaskStatus.Done, completed: null)])));

        Assert.Contains("vehicles[0].tasks[0].completedDate", errors);
    }

    /// <summary>Two problems in one file are two entries, not the first one found.</summary>
    [Fact]
    public void Every_problem_is_reported_rather_than_the_first()
    {
        var errors = ImportValidator.Validate(With(Car(
            expenses: [Expense(21, fuelId: 404), Expense(22, serviceId: 505)])));

        Assert.Equal(2, errors.Count);
    }

    /// <summary>Deliberately never carries a resolution date, so passing Resolved is the refusal case.</summary>
    private static IssueRowItem Issue(int id, IssueStatus status) =>
        new(id, $"Issue {id}", Severity.Low, new DateOnly(2026, 3, 14), null, null, null, null, status,
            null, null);

    private static TaskItem Task(
        int id, MaintenanceTaskStatus status, DateOnly? completed = null, int? serviceRecordId = null) =>
        new(id, MaintenanceTaskKind.DIY, Priority.Low, $"Task {id}", null, null, status, null, null,
            completed, null, serviceRecordId, null);
}
