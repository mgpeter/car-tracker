using CarTracker.Data;
using CarTracker.Shared;
// For IExecutionStrategy.ExecuteAsync's Func<Task> overload, which is an extension method.
using Microsoft.EntityFrameworkCore;

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
    /// <para>
    /// Two saves, because <see cref="MileageReading"/> carries a plain <c>VehicleId</c> with no navigation
    /// property, so the key must exist before the reading can point at it. The explicit transaction is what
    /// makes them one unit — and it makes the atomicity visible, which a navigation-property fixup would hide.
    /// </para>
    /// <para>
    /// The transaction runs <b>inside an execution strategy</b>. Aspire's <c>EnrichNpgsqlDbContext</c> installs
    /// a retrying strategy, and that strategy refuses a user-initiated transaction outright — it cannot retry
    /// a unit of work it does not own, so it declines rather than retry half of one. Wrapping is the
    /// documented answer, and it means a transient failure retries the whole create, not a fragment.
    /// </para>
    /// </remarks>
    /// <param name="checkSource">
    /// Where the vehicle's regular checks come from. Default <see cref="CheckSource.GenericStarterSet"/>,
    /// because the alternative is worse: <see cref="CheckDefinition"/> is vehicle-scoped and nothing else
    /// creates one, so a car created with no checks has a checks screen that is permanently empty and no
    /// obvious way to fill it. The set is a starting point and is owned by the vehicle the moment it lands —
    /// edit or delete freely, nothing re-applies it.
    /// </param>
    /// <param name="copyChecksFromVehicleId">
    /// Required when <paramref name="checkSource"/> is <see cref="CheckSource.CopyFromVehicle"/>. Copies every
    /// ACTIVE definition — an inactive one was switched off deliberately and carrying that decision to a
    /// different car would be guessing.
    /// </param>
    public async Task<Vehicle> CreateAsync(
        Vehicle vehicle,
        EntrySource source,
        CheckSource checkSource = CheckSource.GenericStarterSet,
        int? copyChecksFromVehicleId = null,
        CancellationToken cancellationToken = default)
    {
        if (checkSource == CheckSource.CopyFromVehicle && copyChecksFromVehicleId is null)
        {
            throw new ArgumentException(
                "CopyFromVehicle needs a vehicle to copy from.", nameof(copyChecksFromVehicleId));
        }

        var checks = await ResolveChecksAsync(checkSource, copyChecksFromVehicleId, cancellationToken);

        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
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

            foreach (var check in checks)
            {
                check.VehicleId = vehicle.Id;
                check.Source = source;
                context.CheckDefinitions.Add(check);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return vehicle;
    }

    /// <remarks>
    /// Read before the transaction opens. The copy source is a different vehicle's rows, and reading them
    /// inside the write transaction would hold it open across a query for no benefit.
    /// </remarks>
    private async Task<List<CheckDefinition>> ResolveChecksAsync(
        CheckSource checkSource,
        int? copyFromVehicleId,
        CancellationToken cancellationToken)
    {
        switch (checkSource)
        {
            case CheckSource.None:
                return [];

            case CheckSource.GenericStarterSet:
                return [.. CheckTemplate.For(vehicleId: 0)];

            case CheckSource.CopyFromVehicle:
                var source = await context.CheckDefinitions
                    .AsNoTracking()
                    .Where(d => d.VehicleId == copyFromVehicleId!.Value && d.IsActive)
                    .OrderBy(d => d.DisplayOrder)
                    .ToListAsync(cancellationToken);

                // New rows, not the loaded ones: an AsNoTracking entity carries the source vehicle's Id and
                // its audit stamps, and re-attaching it would try to move the original.
                return [.. source.Select(d => new CheckDefinition
                {
                    Name = d.Name,
                    CadenceLabel = d.CadenceLabel,
                    IntervalDays = d.IntervalDays,
                    Guidance = d.Guidance,
                    DisplayOrder = d.DisplayOrder,
                    IsActive = true,
                })];

            default:
                throw new ArgumentOutOfRangeException(nameof(checkSource), checkSource, "Unknown check source.");
        }
    }
}
