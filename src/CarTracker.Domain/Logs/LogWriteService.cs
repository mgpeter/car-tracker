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

    /// <summary>A wash; the location is a keyed FK, created on first use (else an FK 500 the first time it is typed).</summary>
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
        context.WashEntries.Add(wash);
        await context.SaveChangesAsync(cancellationToken);

        var item = new WashItem(wash.Id, wash.WashDate, wash.Location, wash.WashType, wash.Cost, wash.Mileage, wash.Notes);
        return WriteResult<WashItem>.Created(item);
    }

    /// <summary>A plain inventory row — no shadows, no scan. A name is required.</summary>
    public async Task<WriteResult<EquipmentItemDto>> AddEquipmentAsync(
        int vehicleId, EquipmentInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return WriteResult<EquipmentItemDto>.Invalid("Name", "An equipment item needs a name.");

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
        context.EquipmentItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

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

    /// <summary>Corrects a wash; a new location name is a keyed FK, created on first use.</summary>
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

        await context.SaveChangesAsync(cancellationToken);
        var item = new WashItem(wash.Id, wash.WashDate, wash.Location, wash.WashType, wash.Cost, wash.Mileage, wash.Notes);
        return WriteResult<WashItem>.Updated(item);
    }

    /// <summary>Removes a wash entry.</summary>
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

    /// <summary>Corrects an inventory item — no shadows, no scan.</summary>
    public async Task<WriteResult<EquipmentItemDto>> UpdateEquipmentAsync(
        int vehicleId, int id, EquipmentPatch patch, EntrySource source, CancellationToken cancellationToken = default)
    {
        var item = await context.EquipmentItems
            .FirstOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId, cancellationToken);
        if (item is null) return WriteResult<EquipmentItemDto>.NotFound();

        item.Name = patch.Name ?? item.Name;
        item.Category = patch.Category ?? item.Category;
        item.PurchasedDate = patch.PurchasedDate ?? item.PurchasedDate;
        item.SourceVendor = patch.SourceVendor ?? item.SourceVendor;
        item.Cost = patch.Cost ?? item.Cost;
        item.StoredAt = patch.StoredAt ?? item.StoredAt;
        item.Status = patch.Status ?? item.Status;
        item.Notes = patch.Notes ?? item.Notes;

        await context.SaveChangesAsync(cancellationToken);
        var dto = new EquipmentItemDto(
            item.Id, item.Name, item.Category, item.PurchasedDate, item.SourceVendor,
            item.Cost, item.StoredAt, item.Status, item.Notes);
        return WriteResult<EquipmentItemDto>.Updated(dto);
    }

    /// <summary>Removes an inventory item.</summary>
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
