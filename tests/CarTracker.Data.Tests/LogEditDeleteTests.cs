using CarTracker.Domain;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The edit/delete paths lifted into <see cref="LogWriteService"/> and <see cref="CheckService"/> — the one path
/// behind both the REST PATCH/DELETE endpoints and the MCP <c>update_*</c>/<c>delete_*</c> tools. Against a real
/// database, because these are claims about which rows moved (the mileage shadow) and which refused (a shadow
/// reading edited apart from its source).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LogEditDeleteTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private int _ownerId;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, Clock);

    private static LogWriteService NewWrites(CarTrackerDbContext context) =>
        new(context, new AnomalyScanner(context, new VehicleMetricsLoader(context), Clock, new Clock(Clock)), new ReferenceWriter(context));

    private static CheckService NewChecks(CarTrackerDbContext context) =>
        new(context, new DerivedMetricsService(new VehicleMetricsLoader(context), new Clock(Clock)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_editdelete");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> NewVehicleAsync(CarTrackerDbContext context, string registration)
    {
        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web);
        return vehicle.Id;
    }

    // ---- mileage: only Manual is editable ----------------------------------------------------------------

    [Fact]
    public async Task A_manual_reading_edits_and_deletes()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 111");
        var writes = NewWrites(context);

        var added = await writes.AddMileageAsync(vehicleId, new MileageInput(new DateOnly(2026, 7, 1), 80_000, null), EntrySource.Web);
        var id = added.Value!.Id;

        var edit = await writes.UpdateMileageAsync(vehicleId, id, new MileagePatch(Mileage: 80_050), EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, edit.Status);
        Assert.Equal(80_050, edit.Value!.Mileage);

        var del = await writes.DeleteMileageAsync(vehicleId, id, EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, del.Status);
        Assert.False(await context.MileageReadings.AnyAsync(m => m.Id == id));
    }

    [Fact]
    public async Task A_shadow_reading_refuses_to_be_edited_apart_from_its_source()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 222");
        var writes = NewWrites(context);

        // The founding Purchase reading is a shadow — it must refuse both edit and delete.
        var shadow = await context.MileageReadings
            .SingleAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Purchase);

        var edit = await writes.UpdateMileageAsync(vehicleId, shadow.Id, new MileagePatch(Mileage: 1), EntrySource.Web);
        Assert.Equal(WriteStatus.Conflict, edit.Status);

        var del = await writes.DeleteMileageAsync(vehicleId, shadow.Id, EntrySource.Web);
        Assert.Equal(WriteStatus.Conflict, del.Status);
        Assert.True(await context.MileageReadings.AnyAsync(m => m.Id == shadow.Id));
    }

    [Fact]
    public async Task Editing_a_manual_reading_below_the_odometer_is_flagged_never_rejected()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 333");
        var writes = NewWrites(context);

        var added = await writes.AddMileageAsync(vehicleId, new MileageInput(new DateOnly(2026, 7, 10), 80_712, null), EntrySource.Web);
        var id = added.Value!.Id;

        // A later reading dated further back but lower — non-monotonic. Recorded and flagged, not refused (§5.3).
        await writes.AddMileageAsync(vehicleId, new MileageInput(new DateOnly(2026, 7, 12), 80_800, null), EntrySource.Web);
        var edit = await writes.UpdateMileageAsync(vehicleId, id, new MileagePatch(Mileage: 90_000), EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, edit.Status);
    }

    // ---- tyre: the odometer shadow follows ---------------------------------------------------------------

    [Fact]
    public async Task Editing_a_tyre_reading_moves_its_odometer_shadow()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 444");
        var writes = NewWrites(context);

        var added = await writes.AddTyreAsync(vehicleId,
            new TyreInput(new DateOnly(2026, 7, 1), 80_100, 30m, null, null, null, null, null, null, null, null, null, null, null),
            EntrySource.Web);
        var id = added.Value!.Id;

        await writes.UpdateTyreAsync(vehicleId, id, new TyrePatch(Mileage: 80_150), EntrySource.Web);

        // The Tyre-origin shadow moved with the reading — one, at the new figure.
        var shadow = await context.MileageReadings
            .SingleAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Tyre);
        Assert.Equal(80_150, shadow.Mileage);

        await writes.DeleteTyreAsync(vehicleId, id, EntrySource.Web);
        Assert.False(await context.MileageReadings.AnyAsync(m => m.VehicleId == vehicleId && m.Origin == MileageOrigin.Tyre));
    }

    // ---- wash: a new location is created on first use ----------------------------------------------------

    [Fact]
    public async Task Editing_a_wash_to_a_new_location_creates_the_keyed_row()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 555");
        var writes = NewWrites(context);

        var added = await writes.AddWashAsync(vehicleId, new WashInput(new DateOnly(2026, 7, 1), "Home", null, null, null, null), EntrySource.Web);
        var id = added.Value!.Id;

        var edit = await writes.UpdateWashAsync(vehicleId, id, new WashPatch(Location: "Waterless Valet, Kingston"), EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, edit.Status);
        Assert.True(await context.WashLocations.AnyAsync(w => w.Name == "Waterless Valet, Kingston"));

        var del = await writes.DeleteWashAsync(vehicleId, id, EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, del.Status);
        Assert.False(await context.WashEntries.AnyAsync(w => w.Id == id));
    }

    [Fact]
    public async Task A_paid_wash_mirrors_into_expenses_and_follows_its_cost()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 55W");
        var writes = NewWrites(context);

        // A free rinse at home is a wash that happened, not money spent — nothing to mirror.
        var free = await writes.AddWashAsync(
            vehicleId, new WashInput(new DateOnly(2026, 7, 1), "Home", null, null, null, null), EntrySource.Web);
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.WashEntryId == free.Value!.Id));

        // A paid one reaches spend. Before this mirror, WashEntry.Cost was rendered on the wash screen and
        // counted nowhere — invisible to spend, cost-per-mile and the budget.
        var paid = await writes.AddWashAsync(
            vehicleId,
            new WashInput(new DateOnly(2026, 7, 8), "Waterless Valet, Kingston", "Full valet", 25m, null, null),
            EntrySource.Web);
        var paidId = paid.Value!.Id;

        var expense = await context.ExpenseEntries.SingleAsync(e => e.WashEntryId == paidId);
        Assert.Equal("Wash", expense.Category);
        Assert.Equal(25m, expense.Amount);
        Assert.Equal(new DateOnly(2026, 7, 8), expense.EntryDate);
        Assert.Equal("Waterless Valet, Kingston", expense.Vendor);

        // Correcting the cost moves the mirror rather than leaving a second, stale row.
        Assert.Equal(WriteStatus.Updated,
            (await writes.UpdateWashAsync(vehicleId, paidId, new WashPatch(Cost: 30m), EntrySource.Web)).Status);
        await using (var reader = NewContext())
        {
            Assert.Equal(30m, (await reader.ExpenseEntries.SingleAsync(e => e.WashEntryId == paidId)).Amount);
        }

        // And deleting the wash takes its shadow with it, on the FK cascade.
        Assert.Equal(WriteStatus.Updated, (await writes.DeleteWashAsync(vehicleId, paidId, EntrySource.Web)).Status);
        await using (var reader = NewContext())
        {
            Assert.False(await reader.ExpenseEntries.AnyAsync(e => e.WashEntryId == paidId));
        }
    }

    // ---- equipment: a purchase (cost + date) mirrors into expenses ---------------------------------------

    [Fact]
    public async Task Buying_equipment_with_a_cost_and_date_mirrors_into_expenses()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 66A");
        var writes = NewWrites(context);

        var added = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Recovery straps", EquipmentStatus.Owned, "Recovery", new DateOnly(2026, 4, 2), "Halfords", 24.99m, "Boot", null),
            EntrySource.Web);
        var id = added.Value!.Id;

        // One mirrored expense under Tools/Equipment, linked back — so it flows into the Equipment & Tools budget
        // group and running costs rather than being invisible like the workbook's separate Equipment sheet.
        var expense = await context.ExpenseEntries.SingleAsync(e => e.EquipmentItemId == id);
        Assert.Equal("Tools/Equipment", expense.Category);
        Assert.Equal(24.99m, expense.Amount);
        Assert.Equal(new DateOnly(2026, 4, 2), expense.EntryDate);
        Assert.Equal("Halfords", expense.Vendor);
    }

    [Fact]
    public async Task Equipment_with_a_cost_needs_a_date_and_without_a_cost_does_not_mirror()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 66B");
        var writes = NewWrites(context);

        // Cost but no date is now REFUSED, not accepted-and-dropped. The mirror needs the date for its entry
        // date, so such an item's money reached no total at all — spend, cost-per-mile and the Equipment & Tools
        // budget were all silently short. Asking for the date is the fix; guessing "today" would misplace it.
        var noDate = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Tow rope", EquipmentStatus.Owned, null, null, null, 30m, null, null), EntrySource.Web);
        Assert.Equal(WriteStatus.Validation, noDate.Status);
        Assert.Contains("PurchasedDate", noDate.Errors!.Keys);
        // Scoped to this vehicle. Unscoped, it asserted that no row anywhere in the shared test database was
        // named "Tow rope" — which was true only for as long as no other test used the name, and the To-order
        // case below uses exactly that name because it is the example the rule exists for.
        Assert.False(await context.EquipmentItems.AnyAsync(e => e.VehicleId == vehicleId && e.Name == "Tow rope"));

        // On order is money too — it is paid for and on its way, so it wants the date the same way.
        var onOrder = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Snorkel", EquipmentStatus.OnOrder, null, null, null, 180m, null, null), EntrySource.Web);
        Assert.Equal(WriteStatus.Validation, onOrder.Status);
        Assert.Contains("PurchasedDate", onOrder.Errors!.Keys);

        // Date but no cost is fine and mirrors nothing — an item that cost nothing is not an expense.
        var noCost = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Gifted jack", EquipmentStatus.Owned, null, new DateOnly(2026, 4, 2), null, null, null, null), EntrySource.Web);
        Assert.Equal(WriteStatus.Created, noCost.Status);
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == noCost.Value!.Id));
    }

    [Fact]
    public async Task A_shopping_list_item_can_be_priced_without_a_date_and_reaches_no_expense()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 66H");
        var writes = NewWrites(context);

        // The refusal above was status-blind, so it also refused THIS — and pricing something before you buy
        // it is the entire purpose of a To-order row. A cost here is an estimate, not a payment.
        var planned = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Tow rope", EquipmentStatus.ToOrder, null, null, null, 40m, null, null), EntrySource.Web);
        Assert.Equal(WriteStatus.Created, planned.Status);
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == planned.Value!.Id));

        // And a date on an unbought item still mirrors NOTHING. This is the half that was actually leaking:
        // the add sheet pre-filled today, so an estimate became a real Tools/Equipment expense counted in
        // spend, cost-per-mile and the Equipment & Tools budget.
        var dated = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Roof bars", EquipmentStatus.ToOrder, null, new DateOnly(2026, 6, 1), null, 120m, null, null),
            EntrySource.Web);
        Assert.Equal(WriteStatus.Created, dated.Status);
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == dated.Value!.Id));
    }

    [Fact]
    public async Task Buying_a_planned_item_asks_when_and_moving_it_back_takes_the_expense_with_it()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 66J");
        var writes = NewWrites(context);

        var planned = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Tow rope", EquipmentStatus.ToOrder, null, null, null, 40m, null, null), EntrySource.Web);
        var id = planned.Value!.Id;

        // Flipping it to Owned is the moment the estimate becomes money, so it is the moment to ask when. The
        // guard fires on the resulting triple, not on the patch — a status-only edit would otherwise save
        // silently and the £40 would reach no total at all.
        var bought = await writes.UpdateEquipmentAsync(vehicleId, id,
            new EquipmentPatch(Status: EquipmentStatus.Owned), EntrySource.Web);
        Assert.Equal(WriteStatus.Validation, bought.Status);
        Assert.Contains("PurchasedDate", bought.Errors!.Keys);

        // With the date, it lands — one mirrored expense under Tools/Equipment.
        var withDate = await writes.UpdateEquipmentAsync(vehicleId, id,
            new EquipmentPatch(Status: EquipmentStatus.Owned, PurchasedDate: new DateOnly(2026, 6, 4)), EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, withDate.Status);
        var mirror = await context.ExpenseEntries.SingleAsync(e => e.EquipmentItemId == id);
        Assert.Equal(40m, mirror.Amount);

        // And back the other way: returned to the shopping list, its expense goes with it rather than sitting
        // in the budget backed by nothing.
        await writes.UpdateEquipmentAsync(vehicleId, id,
            new EquipmentPatch(Status: EquipmentStatus.ToOrder), EntrySource.Web);
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == id));
    }

    [Fact]
    public async Task Editing_equipment_creates_then_updates_the_mirror()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 66C");
        var writes = NewWrites(context);

        // Start with no cost/date → no mirror.
        var added = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Compressor", EquipmentStatus.Owned, null, null, null, null, null, null), EntrySource.Web);
        var id = added.Value!.Id;
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == id));

        // A cost + date added on edit → the mirror appears.
        await writes.UpdateEquipmentAsync(vehicleId, id,
            new EquipmentPatch(Cost: 89.99m, PurchasedDate: new DateOnly(2026, 5, 1)), EntrySource.Web);
        var mirror = await context.ExpenseEntries.SingleAsync(e => e.EquipmentItemId == id);
        Assert.Equal(89.99m, mirror.Amount);
        Assert.Equal(new DateOnly(2026, 5, 1), mirror.EntryDate);

        // The cost changed → the single mirror follows, not a duplicate left at the old figure.
        await writes.UpdateEquipmentAsync(vehicleId, id, new EquipmentPatch(Cost: 79.99m), EntrySource.Web);
        var after = await context.ExpenseEntries.SingleAsync(e => e.EquipmentItemId == id);
        Assert.Equal(79.99m, after.Amount);
    }

    [Fact]
    public async Task Deleting_a_purchased_equipment_item_cascades_its_mirror()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 66D");
        var writes = NewWrites(context);

        var added = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Winch", EquipmentStatus.Owned, null, new DateOnly(2026, 4, 2), null, 240m, null, null), EntrySource.Web);
        var id = added.Value!.Id;
        Assert.True(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == id));

        await writes.DeleteEquipmentAsync(vehicleId, id, EntrySource.Web);

        Assert.False(await context.EquipmentItems.AnyAsync(e => e.Id == id));
        Assert.False(await context.ExpenseEntries.AnyAsync(e => e.EquipmentItemId == id)); // cascaded
    }

    // ---- equipment: plain row (no cost/date, so no mirror) ------------------------------------------------

    [Fact]
    public async Task An_equipment_item_edits_and_deletes()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 666");
        var writes = NewWrites(context);

        var added = await writes.AddEquipmentAsync(vehicleId,
            new EquipmentInput("Recovery straps", EquipmentStatus.ToOrder, null, null, null, null, null, null), EntrySource.Web);
        var id = added.Value!.Id;

        var edit = await writes.UpdateEquipmentAsync(vehicleId, id, new EquipmentPatch(Status: EquipmentStatus.Owned), EntrySource.Web);
        Assert.Equal(EquipmentStatus.Owned, edit.Value!.Status);
        Assert.Equal("Recovery straps", edit.Value.Name); // an omitted field is left untouched

        var del = await writes.DeleteEquipmentAsync(vehicleId, id, EntrySource.Web);
        Assert.Equal(WriteStatus.Updated, del.Status);
        Assert.False(await context.EquipmentItems.AnyAsync(e => e.Id == id));
    }

    [Fact]
    public async Task Deleting_a_missing_row_is_a_NotFound_not_a_throw()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 777");
        var writes = NewWrites(context);

        Assert.Equal(WriteStatus.NotFound, (await writes.DeleteEquipmentAsync(vehicleId, 999_999, EntrySource.Web)).Status);
        Assert.Equal(WriteStatus.NotFound, (await writes.DeleteWashAsync(vehicleId, 999_999, EntrySource.Web)).Status);
        Assert.Equal(WriteStatus.NotFound, (await writes.DeleteTyreAsync(vehicleId, 999_999, EntrySource.Web)).Status);
    }

    // ---- mark a single check done: id path + slim reply --------------------------------------------------

    [Fact]
    public async Task Marking_one_check_done_by_id_returns_just_that_check_and_the_counts()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 888"); // gets the generic 15, all never-logged
        var checks = NewChecks(context);

        var definitionId = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId).OrderBy(d => d.DisplayOrder).Select(d => d.Id).FirstAsync();

        var result = await checks.MarkSingleDoneAsync(
            vehicleId, definitionId, null, new DateOnly(2026, 7, 14), CheckResult.OK, null, EntrySource.Mcp);

        Assert.Equal(WriteStatus.Updated, result.Status);
        // The reply is just the affected check plus the counts — not all 15 definitions.
        Assert.Equal(definitionId, result.Value!.Check.CheckDefinitionId);
        Assert.Equal(CheckStatus.Ok, result.Value.Check.Status);
        Assert.Equal(1, result.Value.OkCount);
        Assert.Equal(14, result.Value.NeverLoggedCount);
        Assert.Equal(15, result.Value.TotalCount);
    }

    [Fact]
    public async Task Marking_a_check_done_by_an_unknown_id_is_a_clear_validation_failure()
    {
        await using var context = NewContext();
        var vehicleId = await NewVehicleAsync(context, "EDL 999");
        var checks = NewChecks(context);

        var result = await checks.MarkSingleDoneAsync(
            vehicleId, 999_999, null, new DateOnly(2026, 7, 14), null, null, EntrySource.Mcp);

        Assert.Equal(WriteStatus.Validation, result.Status);
        Assert.True(result.Errors!.ContainsKey("checkDefinitionId"));
    }
}
