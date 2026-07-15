using CarTracker.Data;
using CarTracker.Shared;

namespace CarTracker.Domain;

/// <summary>
/// Brings a vehicle into existence, with its opening odometer reading.
/// </summary>
/// <remarks>
/// <para>
/// A vehicle without a founding <see cref="MileageReading"/> has a <see cref="Vehicle.PurchaseMileage"/> in the
/// database while every derived figure reports null — current mileage derives from readings, and there are
/// none. This is the only supported way to create a vehicle, so that state cannot occur.
/// </para>
/// <para>
/// A domain service rather than a SaveChanges hook or an EF interceptor: this is a rule about how a vehicle
/// comes into being, not a cross-cutting concern like audit stamping. An interceptor would also fire on bulk
/// paths where it may not be wanted.
/// </para>
/// </remarks>
public sealed class VehicleFactory(CarTrackerDbContext context)
{
    /// <summary>
    /// Creates the vehicle and its opening reading in one transaction.
    /// </summary>
    /// <remarks>
    /// Two saves, because <see cref="MileageReading"/> carries a plain <c>VehicleId</c> with no navigation
    /// property, so the key must exist before the reading can point at it. The explicit transaction is what
    /// makes them one unit — and it makes the atomicity visible, which a navigation-property fixup would hide.
    /// </remarks>
    public async Task<Vehicle> CreateAsync(
        Vehicle vehicle,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        vehicle.Source = source;
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync(cancellationToken);

        context.MileageReadings.Add(new MileageReading
        {
            VehicleId = vehicle.Id,
            ReadingDate = vehicle.PurchaseDate,
            Mileage = vehicle.PurchaseMileage,
            Origin = MileageOrigin.Purchase,
            Source = source,
        });
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return vehicle;
    }
}
