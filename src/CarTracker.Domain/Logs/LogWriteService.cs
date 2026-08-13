using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Logs;

/// <summary>
/// The add, edit and delete paths for the small logs — a manual mileage reading, a tyre reading, a wash, an
/// equipment item. The REST endpoints and the MCP write tools both call these, so the tyre odometer shadow, the
/// wash-location ensure and the "a mileage shadow is edited through its source" rule all live in one place.
/// </summary>
public sealed class LogWriteService(CarTrackerDbContext context, AnomalyScanner scanner, ReferenceWriter references)
{
    /// <summary>
    /// The refusal a mileage shadow (a fill/service/tyre/wash reading, or the founding purchase reading) gives when
    /// something tries to edit or delete it directly — it is corrected through its source, or the two drift.
    /// </summary>
    private static (string Title, string Detail) ShadowConflict(int id, MileageOrigin origin) =>
        ("Reading mirrors another log",
         $"Reading {id} was written by the {origin} log. Edit or remove that entry and this reading follows — "
         + "a shadow cannot be changed apart from its source.");

    /// <summary>A quick manual odometer reading. Below the current odometer is flagged, never rejected (§5.3).</summary>
    public async Task<WriteResult<MileageReadingItem>> AddMileageAsync(
        int vehicleId, MileageInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        if (input.Mileage <= 0)
            return WriteResult<MileageReadingItem>.Invalid(nameof(input.Mileage), "An odometer reading must be greater than zero.");

        var reading = new MileageReading
        {
            VehicleId = vehicleId,
            ReadingDate = input.ReadingDate,
            Mileage = input.Mileage,
            Origin = MileageOrigin.Manual,
            Notes = input.Notes,
            Source = source,
        };
        context.MileageReadings.Add(reading);
        await context.SaveChangesAsync(cancellationToken);

        var flags = await scanner.ScanAsync(vehicleId, source, cancellationToken);
        var item = new MileageReadingItem(reading.Id, reading.ReadingDate, reading.Mileage, reading.Origin, reading.Notes);
        return WriteResult<MileageReadingItem>.Created(item, flags.ToFlags());
    }

    /// <summary>A tyre reading; a supplied mileage writes an <c>Origin=Tyre</c> odometer shadow and scans.</summary>
    public async Task<WriteResult<TyreReadingItem>> AddTyreAsync(
        int vehicleId, TyreInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        var reading = new TyreReading
        {
            VehicleId = vehicleId,
            ReadingDate = input.ReadingDate,
            Mileage = input.Mileage,
            PsiFrontLeft = input.PsiFrontLeft,
            PsiFrontRight = input.PsiFrontRight,
            PsiRearLeft = input.PsiRearLeft,
            PsiRearRight = input.PsiRearRight,
            PsiSpare = input.PsiSpare,
            TreadFrontLeft = input.TreadFrontLeft,
            TreadFrontRight = input.TreadFrontRight,
            TreadRearLeft = input.TreadRearLeft,
            TreadRearRight = input.TreadRearRight,
            Location = input.Location,
            Tool = input.Tool,
            Notes = input.Notes,
            Source = source,
        };
        context.TyreReadings.Add(reading);

        if (input.Mileage is { } miles)
        {
            context.MileageReadings.Add(new MileageReading
            {
                VehicleId = vehicleId,
                ReadingDate = input.ReadingDate,
                Mileage = miles,
                Origin = MileageOrigin.Tyre,
                Source = source,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        IReadOnlyList<Shared.Logs.AnomalyFlag> flags = [];
        if (input.Mileage is not null)
            flags = (await scanner.ScanAsync(vehicleId, source, cancellationToken)).ToFlags();

        var item = new TyreReadingItem(
            reading.Id, reading.ReadingDate, reading.Mileage,
            reading.PsiFrontLeft, reading.PsiFrontRight, reading.PsiRearLeft, reading.PsiRearRight, reading.PsiSpare,
            reading.TreadFrontLeft, reading.TreadFrontRight, reading.TreadRearLeft, reading.TreadRearRight,
            reading.Location, reading.Tool, reading.Notes);
        return WriteResult<TyreReadingItem>.Created(item, flags);
    }

    /// <summary>The category a paid wash mirrors into — the seeded <c>Wash</c>.</summary>
    public const string WashCategory = "Wash";

    /// <summary>
    /// The mirrored expense for a wash, or <c>null</c> when there is nothing to mirror. Gated on a cost above
    /// zero: a free rinse at home is a wash that happened, not money spent, and a £0 expense row would be noise
    /// in the log. Unlike equipment there is no second gate on a date — a wash always has one.
    /// </summary>
    private static ExpenseEntry? MirrorFor(WashEntry wash, EntrySource source) =>
        wash.Cost is { } cost && cost > 0
            ? new ExpenseEntry
            {
                VehicleId = wash.VehicleId,
                EntryDate = wash.WashDate,
                Category = WashCategory,
                SubCategory = wash.WashType,
                Vendor = wash.Location,
                Amount = cost,
                // Deliberately not carrying wash.Mileage: an expense with a mileage writes its own odometer
                // reading on the hand-entry path, and the wash log already logs its own. Two readings for one
                // wash is the double-count in a different currency.
                WashEntryId = wash.Id,
                Source = source,
            }
            : null;

    /// <summary>
    /// A wash, plus its mirrored expense when it cost something; the location is a keyed FK, created on first use
    /// (else an FK 500 the first time it is typed). Two saves in one transaction — the wash's key must exist
    /// before the expense can point at it — inside the execution strategy the retrying provider requires.
    /// </summary>
    /// <remarks>
    /// The mirror is what makes a paid wash reach spend, cost-per-mile and the budget at all. Without it
    /// <see cref="WashEntry.Cost"/> was rendered on the wash screen and counted nowhere — while the Budget
    /// page's own footer promises that money the app knows about is never hidden.
    /// </remarks>
    public async Task<WriteResult<WashItem>> AddWashAsync(
        int vehicleId, WashInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        if (input.Location is { Length: > 0 })
            await references.EnsureWashLocationAsync(input.Location, cancellationToken);

        var wash = new WashEntry
        {
            VehicleId = vehicleId,
            WashDate = input.WashDate,
            Location = input.Location,
            WashType = input.WashType,
            Cost = input.Cost,
            Mileage = input.Mileage,
            Notes = input.Notes,
            Source = source,
        };

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            context.WashEntries.Add(wash);
            await context.SaveChangesAsync(cancellationToken);

            if (MirrorFor(wash, source) is { } mirror) context.ExpenseEntries.Add(mirror);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        var item = new WashItem(wash.Id, wash.WashDate, wash.Location, wash.WashType, wash.Cost, wash.Mileage, wash.Notes);
        return WriteResult<WashItem>.Created(item);
    }

    /// <summary>
    /// The category an equipment purchase mirrors into — the seeded <c>Tools/Equipment</c>, which the
    /// <c>Equipment &amp; Tools</c> default budget group also owns, so bought kit counts toward spend and that
    /// budget rather than being invisible the way the workbook's separate Equipment sheet was.
    /// </summary>
    public const string EquipmentCategory = "Tools/Equipment";

    /// <summary>
    /// A cost with no purchase date, which <see cref="MirrorFor(EquipmentItem, EntrySource)"/> cannot mirror —
    /// the date supplies the expense's <c>EntryDate</c>, and dating it "today" would misplace it in spend.
    /// Refused rather than accepted-and-dropped: money silently absent from cost-per-mile is the failure this
    /// whole area is being corrected for, and the fix is to ask for the date, not to guess one.
    /// </summary>
    /// <remarks>
    /// <b>Only when the cost is money.</b> The guard took no status and so demanded a purchase date for a
    /// <see cref="EquipmentStatus.ToOrder"/> item too — refusing "Tow rope, £40, to order" outright, which is
    /// the one thing a shopping list is for. See <see cref="EquipmentRules.CostIsSpend"/>.
    /// </remarks>
    private static (string Field, string Message)? CostNeedsDate(
        EquipmentStatus status, decimal? cost, DateOnly? purchasedDate) =>
        EquipmentRules.CostIsSpend(status) && cost is not null && purchasedDate is null
            ? ("PurchasedDate",
                "A purchase date is needed alongside a cost — it is the date the money lands in spend. Give the "
                + "date, leave the cost blank, or set the item to To order if you have not bought it yet.")
            : null;

    /// <summary>
    /// The mirrored expense for an equipment purchase, or <c>null</c> when there is nothing to mirror. Gated on
    /// a cost, a purchase date, and a status that means the money has actually gone: no amount is not an
    /// expense, the purchase date supplies the required <see cref="ExpenseEntry.EntryDate"/> (dating it "today"
    /// would misplace it in spend), and a <see cref="EquipmentStatus.ToOrder"/> price is an estimate.
    /// </summary>
    /// <remarks>
    /// The status check is the one that was missing, and it was the live defect: the add sheet pre-fills
    /// today's date, so a shopping-list item priced at £40 quietly became a real <c>Tools/Equipment</c> expense
    /// counted in spend, cost-per-mile and the Equipment &amp; Tools budget.
    /// </remarks>
    private static ExpenseEntry? MirrorFor(EquipmentItem item, EntrySource source) =>
        EquipmentRules.CostIsSpend(item.Status) && item.Cost is { } cost && item.PurchasedDate is { } date
            ? new ExpenseEntry
            {
                VehicleId = item.VehicleId,
                EntryDate = date,
                Category = EquipmentCategory,
                SubCategory = item.Category,
                Vendor = item.SourceVendor,
                Amount = cost,
                EquipmentItemId = item.Id,
                Source = source,
            }
            : null;

    /// <summary>
    /// An inventory row, plus its mirrored expense when it carries a cost and a purchase date. Two saves in one
    /// transaction (the item's key must exist before the expense can point at it), inside the execution strategy
    /// the retrying provider requires — the same shape as <see cref="ServiceRecordFactory"/>. A name is required.
    /// </summary>
    public async Task<WriteResult<EquipmentItemDto>> AddEquipmentAsync(
        int vehicleId, EquipmentInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return WriteResult<EquipmentItemDto>.Invalid("Name", "An equipment item needs a name.");

        if (CostNeedsDate(input.Status, input.Cost, input.PurchasedDate) is { } costError)
            return WriteResult<EquipmentItemDto>.Invalid(costError.Field, costError.Message);

        var item = new EquipmentItem
        {
            VehicleId = vehicleId,
            Name = input.Name.Trim(),
            Category = input.Category,
            PurchasedDate = input.PurchasedDate,
            SourceVendor = input.SourceVendor,
            Cost = input.Cost,
            StoredAt = input.StoredAt,
            Status = input.Status,
            Notes = input.Notes,
            Source = source,
        };

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            context.EquipmentItems.Add(item);
            await context.SaveChangesAsync(cancellationToken);

            if (MirrorFor(item, source) is { } mirror) context.ExpenseEntries.Add(mirror);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        var dto = new EquipmentItemDto(
            item.Id, item.Name, item.Category, item.PurchasedDate, item.SourceVendor,
            item.Cost, item.StoredAt, item.Status, item.Notes);
        return WriteResult<EquipmentItemDto>.Created(dto);
    }

    // ---- mileage edit/delete -----------------------------------------------------------------------------

    /// <summary>
    /// Corrects a manual reading and re-scans. Only a <see cref="MileageOrigin.Manual"/> reading is editable —
    /// the rest are shadows and are corrected through their source (a Conflict), and the founding Purchase reading
    /// is a shadow too, so it cannot be edited away. A single-table write, so no execution strategy.
    /// </summary>
    public async Task<WriteResult<MileageReadingItem>> UpdateMileageAsync(
        int vehicleId, int id, MileagePatch patch, EntrySource source, CancellationToken cancellationToken = default)
    {
        var reading = await context.MileageReadings
            .FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicleId, cancellationToken);
        if (reading is null) return WriteResult<MileageReadingItem>.NotFound();
        if (reading.Origin != MileageOrigin.Manual)
        {
            var (title, detail) = ShadowConflict(id, reading.Origin);
            return WriteResult<MileageReadingItem>.Conflict(title, detail);
        }
        if (patch.Mileage is <= 0)
            return WriteResult<MileageReadingItem>.Invalid(nameof(patch.Mileage), "An odometer reading must be greater than zero.");

        reading.ReadingDate = patch.ReadingDate ?? reading.ReadingDate;
        reading.Mileage = patch.Mileage ?? reading.Mileage;
        reading.Notes = patch.Notes ?? reading.Notes;
        await context.SaveChangesAsync(cancellationToken);

        // Editing a reading down can clear a non-monotonic flag; editing one up can raise one.
        var flags = await scanner.ScanAsync(vehicleId, source, cancellationToken);
        var item = new MileageReadingItem(reading.Id, reading.ReadingDate, reading.Mileage, reading.Origin, reading.Notes);
        return WriteResult<MileageReadingItem>.Updated(item, flags.ToFlags());
    }

    /// <summary>Removes a manual reading and re-scans. Shadows refuse (edit their source); Purchase cannot be removed.</summary>
    public async Task<WriteResult<bool>> DeleteMileageAsync(
        int vehicleId, int id, EntrySource source, CancellationToken cancellationToken = default)
    {
        var reading = await context.MileageReadings
            .FirstOrDefaultAsync(m => m.Id == id && m.VehicleId == vehicleId, cancellationToken);
        if (reading is null) return WriteResult<bool>.NotFound();
        if (reading.Origin != MileageOrigin.Manual)
        {
            var (title, detail) = ShadowConflict(id, reading.Origin);
            return WriteResult<bool>.Conflict(title, detail);
        }

        context.MileageReadings.Remove(reading);
        await context.SaveChangesAsync(cancellationToken);
        await scanner.ScanAsync(vehicleId, source, cancellationToken);
        return WriteResult<bool>.Updated(true);
    }

    // ---- tyre edit/delete --------------------------------------------------------------------------------

    /// <summary>Corrects a tyre reading; its odometer shadow follows via <see cref="OdometerShadow"/>, then re-scans.</summary>
    public async Task<WriteResult<TyreReadingItem>> UpdateTyreAsync(
        int vehicleId, int id, TyrePatch patch, EntrySource source, CancellationToken cancellationToken = default)
    {
        var reading = await context.TyreReadings
            .FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, cancellationToken);
        if (reading is null) return WriteResult<TyreReadingItem>.NotFound();

        var originalDate = reading.ReadingDate;
        var originalMileage = reading.Mileage;

        reading.ReadingDate = patch.ReadingDate ?? reading.ReadingDate;
        reading.Mileage = patch.Mileage ?? reading.Mileage;
        reading.PsiFrontLeft = patch.PsiFrontLeft ?? reading.PsiFrontLeft;
        reading.PsiFrontRight = patch.PsiFrontRight ?? reading.PsiFrontRight;
        reading.PsiRearLeft = patch.PsiRearLeft ?? reading.PsiRearLeft;
        reading.PsiRearRight = patch.PsiRearRight ?? reading.PsiRearRight;
        reading.PsiSpare = patch.PsiSpare ?? reading.PsiSpare;
        reading.TreadFrontLeft = patch.TreadFrontLeft ?? reading.TreadFrontLeft;
        reading.TreadFrontRight = patch.TreadFrontRight ?? reading.TreadFrontRight;
        reading.TreadRearLeft = patch.TreadRearLeft ?? reading.TreadRearLeft;
        reading.TreadRearRight = patch.TreadRearRight ?? reading.TreadRearRight;
        reading.Location = patch.Location ?? reading.Location;
        reading.Tool = patch.Tool ?? reading.Tool;
        reading.Notes = patch.Notes ?? reading.Notes;

        await OdometerShadow.SyncAsync(
            context, vehicleId, MileageOrigin.Tyre,
            originalDate, originalMileage, reading.ReadingDate, reading.Mileage, source, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await scanner.ScanAsync(vehicleId, source, cancellationToken);

        var item = new TyreReadingItem(
            reading.Id, reading.ReadingDate, reading.Mileage,
            reading.PsiFrontLeft, reading.PsiFrontRight, reading.PsiRearLeft, reading.PsiRearRight, reading.PsiSpare,
            reading.TreadFrontLeft, reading.TreadFrontRight, reading.TreadRearLeft, reading.TreadRearRight,
            reading.Location, reading.Tool, reading.Notes);
        return WriteResult<TyreReadingItem>.Updated(item);
    }

    /// <summary>Removes a tyre reading and its odometer shadow; re-scans only if it carried a mileage.</summary>
    public async Task<WriteResult<bool>> DeleteTyreAsync(
        int vehicleId, int id, EntrySource source, CancellationToken cancellationToken = default)
    {
        var reading = await context.TyreReadings
            .FirstOrDefaultAsync(t => t.Id == id && t.VehicleId == vehicleId, cancellationToken);
        if (reading is null) return WriteResult<bool>.NotFound();

        await OdometerShadow.SyncAsync(
            context, vehicleId, MileageOrigin.Tyre,
            reading.ReadingDate, reading.Mileage, reading.ReadingDate, newMileage: null, source, cancellationToken);

        var hadMileage = reading.Mileage is not null;
        context.TyreReadings.Remove(reading);
        await context.SaveChangesAsync(cancellationToken);
        if (hadMileage) await scanner.ScanAsync(vehicleId, source, cancellationToken);
        return WriteResult<bool>.Updated(true);
    }

    // ---- wash edit/delete --------------------------------------------------------------------------------

    /// <summary>
    /// Corrects a wash and reconciles its mirrored expense — created, updated or removed as the cost comes and
    /// goes, the same three transitions <see cref="UpdateEquipmentAsync"/> tracks. The wash already has a key, so
    /// a single save is atomic — no explicit transaction.
    /// </summary>
    public async Task<WriteResult<WashItem>> UpdateWashAsync(
        int vehicleId, int id, WashPatch patch, EntrySource source, CancellationToken cancellationToken = default)
    {
        var wash = await context.WashEntries
            .FirstOrDefaultAsync(w => w.Id == id && w.VehicleId == vehicleId, cancellationToken);
        if (wash is null) return WriteResult<WashItem>.NotFound();

        if (patch.Location is not null) await references.EnsureWashLocationAsync(patch.Location, cancellationToken);

        wash.WashDate = patch.WashDate ?? wash.WashDate;
        wash.Location = patch.Location ?? wash.Location;
        wash.WashType = patch.WashType ?? wash.WashType;
        wash.Cost = patch.Cost ?? wash.Cost;
        wash.Mileage = patch.Mileage ?? wash.Mileage;
        wash.Notes = patch.Notes ?? wash.Notes;

        var expense = await context.ExpenseEntries
            .FirstOrDefaultAsync(e => e.WashEntryId == wash.Id, cancellationToken);

        if (MirrorFor(wash, source) is { } fresh)
        {
            if (expense is null)
            {
                context.ExpenseEntries.Add(fresh);
            }
            else
            {
                expense.EntryDate = fresh.EntryDate;
                expense.Amount = fresh.Amount;
                expense.SubCategory = fresh.SubCategory;
                expense.Vendor = fresh.Vendor;
            }
        }
        else if (expense is not null)
        {
            // Cost cleared or zeroed: the wash still happened, but there is no longer any money behind the row.
            context.ExpenseEntries.Remove(expense);
        }

        await context.SaveChangesAsync(cancellationToken);
        var item = new WashItem(wash.Id, wash.WashDate, wash.Location, wash.WashType, wash.Cost, wash.Mileage, wash.Notes);
        return WriteResult<WashItem>.Updated(item);
    }

    /// <summary>Removes a wash entry; its mirrored expense cascades on the foreign key.</summary>
    public async Task<WriteResult<bool>> DeleteWashAsync(
        int vehicleId, int id, EntrySource source, CancellationToken cancellationToken = default)
    {
        var wash = await context.WashEntries
            .FirstOrDefaultAsync(w => w.Id == id && w.VehicleId == vehicleId, cancellationToken);
        if (wash is null) return WriteResult<bool>.NotFound();

        context.WashEntries.Remove(wash);
        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<bool>.Updated(true);
    }

    // ---- equipment edit/delete ---------------------------------------------------------------------------

    /// <summary>
    /// Corrects an inventory item and reconciles its mirrored expense — created, updated or removed as the cost
    /// and purchase date come and go, the same three transitions <see cref="ServiceRecordFactory.UpdateAsync"/>
    /// tracks. The item already has a key, so a single save is atomic — no explicit transaction.
    /// </summary>
    public async Task<WriteResult<EquipmentItemDto>> UpdateEquipmentAsync(
        int vehicleId, int id, EquipmentPatch patch, EntrySource source, CancellationToken cancellationToken = default)
    {
        var item = await context.EquipmentItems
            .FirstOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId, cancellationToken);
        if (item is null) return WriteResult<EquipmentItemDto>.NotFound();

        // Only guard when this edit actually touches the money, the date, or the status. A legacy item already
        // carrying a cost with no date must stay editable — otherwise renaming it would be blocked by a defect
        // it already has, and the data-integrity queue is where that gets surfaced and fixed.
        //
        // The status belongs in that list because moving a costed item OUT of To order is the moment its
        // estimate becomes money. Without it the flip would save silently, the mirror would find no date, and
        // the £40 would reach no total at all — the exact accept-and-drop this guard exists to refuse. So the
        // guard reads the RESULTING triple, not the patch.
        if (patch.Cost is not null || patch.PurchasedDate is not null || patch.Status is not null)
        {
            var status = patch.Status ?? item.Status;
            var cost = patch.Cost ?? item.Cost;
            var date = patch.PurchasedDate ?? item.PurchasedDate;
            if (CostNeedsDate(status, cost, date) is { } costError)
                return WriteResult<EquipmentItemDto>.Invalid(costError.Field, costError.Message);
        }

        item.Name = patch.Name ?? item.Name;
        item.Category = patch.Category ?? item.Category;
        item.PurchasedDate = patch.PurchasedDate ?? item.PurchasedDate;
        item.SourceVendor = patch.SourceVendor ?? item.SourceVendor;
        item.Cost = patch.Cost ?? item.Cost;
        item.StoredAt = patch.StoredAt ?? item.StoredAt;
        item.Status = patch.Status ?? item.Status;
        item.Notes = patch.Notes ?? item.Notes;

        var expense = await context.ExpenseEntries
            .FirstOrDefaultAsync(e => e.EquipmentItemId == item.Id, cancellationToken);
        // Same three conditions as `MirrorFor`, so an edit cannot mirror something a create would not — and
        // moving an owned item back to To order takes its expense off the budget on the way past.
        var shouldMirror =
            EquipmentRules.CostIsSpend(item.Status) && item.Cost is not null && item.PurchasedDate is not null;

        if (shouldMirror)
        {
            if (expense is null)
            {
                context.ExpenseEntries.Add(MirrorFor(item, source)!);
            }
            else
            {
                expense.EntryDate = item.PurchasedDate!.Value;
                expense.Amount = item.Cost!.Value;
                expense.SubCategory = item.Category;
                expense.Vendor = item.SourceVendor;
            }
        }
        else if (expense is not null)
        {
            // Cost or date cleared, or the item moved back to To order: mirroring it would leave an expense
            // the purchase no longer backs, still counted in spend and the budget.
            context.ExpenseEntries.Remove(expense);
        }

        await context.SaveChangesAsync(cancellationToken);
        var dto = new EquipmentItemDto(
            item.Id, item.Name, item.Category, item.PurchasedDate, item.SourceVendor,
            item.Cost, item.StoredAt, item.Status, item.Notes);
        return WriteResult<EquipmentItemDto>.Updated(dto);
    }

    /// <summary>Removes an inventory item; its mirrored expense cascades on the foreign key.</summary>
    public async Task<WriteResult<bool>> DeleteEquipmentAsync(
        int vehicleId, int id, EntrySource source, CancellationToken cancellationToken = default)
    {
        var item = await context.EquipmentItems
            .FirstOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId, cancellationToken);
        if (item is null) return WriteResult<bool>.NotFound();

        context.EquipmentItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<bool>.Updated(true);
    }
}
