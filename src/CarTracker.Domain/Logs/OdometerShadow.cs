using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Logs;

/// <summary>
/// Keeps a log's optional odometer shadow in step: creates it when a mileage first appears, moves it when the
/// date or mileage changes, and removes it when the mileage is cleared (or the row is deleted, by passing
/// <paramref name="newMileage"/> null). The shadow carries no foreign key back, so it is matched by its old
/// <paramref name="origin"/> key. Lifted out of <c>LogEndpoints</c> so the write services and the endpoints share
/// one copy, and stamped with the caller's <paramref name="source"/> so an MCP-written shadow is attributable.
/// </summary>
public static class OdometerShadow
{
    public static async Task SyncAsync(
        CarTrackerDbContext context,
        int vehicleId,
        MileageOrigin origin,
        DateOnly originalDate,
        int? originalMileage,
        DateOnly newDate,
        int? newMileage,
        EntrySource source,
        CancellationToken cancellationToken)
    {
        var existing = originalMileage is { } om
            ? await context.MileageReadings.FirstOrDefaultAsync(
                m => m.VehicleId == vehicleId && m.Origin == origin
                    && m.ReadingDate == originalDate && m.Mileage == om, cancellationToken)
            : null;

        if (newMileage is { } nm)
        {
            if (existing is not null)
            {
                existing.ReadingDate = newDate;
                existing.Mileage = nm;
            }
            else
            {
                context.MileageReadings.Add(new MileageReading
                {
                    VehicleId = vehicleId,
                    ReadingDate = newDate,
                    Mileage = nm,
                    Origin = origin,
                    Source = source,
                });
            }
        }
        else if (existing is not null)
        {
            context.MileageReadings.Remove(existing);
        }
    }
}
