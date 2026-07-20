using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Logs;

/// <summary>
/// The add paths for the small logs — a manual mileage reading, a tyre reading, a wash, an equipment item. The
/// REST endpoints and the MCP write tools both call these, so the tyre odometer shadow and the wash-location
/// ensure live in one place. Edit and delete stay in the endpoints (the assistant does neither).
/// </summary>
public sealed class LogWriteService(CarTrackerDbContext context, AnomalyScanner scanner, ReferenceWriter references)
{
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
}
