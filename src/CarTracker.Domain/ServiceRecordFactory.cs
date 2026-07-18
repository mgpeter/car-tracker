using CarTracker.Data;
using CarTracker.Shared;
// For IExecutionStrategy.ExecuteAsync's Func<Task> overload, which is an extension method.
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Records a service, MOT or repair, together with the rows it implies.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="FuelEntryFactory"/> and for the same reasons. A service is not one row: it is
/// the record, the odometer reading it was taken at, and — when it cost something — the money. Leaving any of
/// the three to be entered separately is what the workbook did, and its £163.16 fuel gap is the evidence for
/// what that costs.
/// </para>
/// <para>
/// <b>The mileage reading is the interesting one here.</b> Current mileage derives from readings only (§1), so
/// a service record that did not write one would never move the odometer — and, more to the point, the
/// workbook's 27 Jun 2026 row logging 83,000 mi against a current 80,712 would never reach the detector. That
/// row is the reason this class exists in the shape it does: the reading is written as given, the anomaly is
/// raised, and nothing is corrected on the way in.
/// </para>
/// </remarks>
public sealed class ServiceRecordFactory(CarTrackerDbContext context, ReferenceWriter references)
{
    /// <summary>
    /// The category a mirrored service cost is filed under.
    /// </summary>
    /// <remarks>
    /// Not the record's <see cref="ServiceRecord.Type"/>, which is free text and would scatter spend across
    /// "Service", "service", "Full service" and "MOT test" — the same fragmentation that makes the workbook's
    /// Expenses sheet hard to total. An MOT files under <see cref="MotCategory"/>; everything else here.
    /// </remarks>
    public const string ServiceCategory = "Service";

    /// <summary>MOT fees are statutory, not maintenance, and the dashboard totals them separately.</summary>
    public const string MotCategory = "MOT";

    /// <summary>The <see cref="ServiceRecord.Type"/> the MOT expiry derives from. Matched exactly.</summary>
    public const string MotType = "MOT";

    public static string CategoryFor(string type) =>
        string.Equals(type, MotType, StringComparison.OrdinalIgnoreCase) ? MotCategory : ServiceCategory;

    /// <summary>
    /// Creates the record, its mileage reading and — when it has a cost — its mirrored expense, in one
    /// transaction.
    /// </summary>
    /// <remarks>
    /// Two saves then a commit: the record's key must exist before the expense can point at it. The transaction
    /// runs inside an execution strategy because Aspire's <c>EnrichNpgsqlDbContext</c> installs a retrying one,
    /// which refuses a user-initiated transaction outright. The tests do not catch this — the test context has
    /// no retry strategy — so it is load-bearing and unprovable here.
    /// </remarks>
    public async Task<ServiceRecord> CreateAsync(
        ServiceRecord record,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // `ServiceRecord.Garage` is a foreign key to a keyed table, not the free text it looks like. See
            // ReferenceWriter — three other columns have the same trap.
            await references.EnsureGarageAsync(record.Garage, cancellationToken);

            record.Source = source;
            context.ServiceRecords.Add(record);
            await context.SaveChangesAsync(cancellationToken);

            context.MileageReadings.Add(new MileageReading
            {
                VehicleId = record.VehicleId,
                ReadingDate = record.ServiceDate,
                Mileage = record.Mileage,
                Origin = MileageOrigin.Service,
                Source = source,
            });

            // Only when there is money. A record with no cost is a real thing — a DIY oil change, an MOT
            // retest — and mirroring £0 into expenses would put a row in the log that never happened.
            if (record.Cost is { } cost)
            {
                context.ExpenseEntries.Add(new ExpenseEntry
                {
                    VehicleId = record.VehicleId,
                    EntryDate = record.ServiceDate,
                    Category = CategoryFor(record.Type),
                    SubCategory = record.Type,
                    Vendor = record.Garage,
                    Amount = cost,
                    Mileage = record.Mileage,
                    ServiceRecordId = record.Id,
                    Source = source,
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return record;
    }

    /// <summary>
    /// Persists an edit to an already-tracked record and keeps its shadows in step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mileage reading follows the record's date and mileage; it is matched by its old
    /// <see cref="MileageOrigin.Service"/> key (<paramref name="originalDate"/>, <paramref name="originalMileage"/>
    /// captured before the edit), because it carries no foreign key back.
    /// </para>
    /// <para>
    /// The expense mirror tracks the cost through all three of its transitions, not just "still costs money":
    /// a cost newly added creates the mirror, a cost removed deletes it, and a changed cost updates it. Leaving
    /// any of those out is how a record and its expense drift — the drift the mirror exists to prevent.
    /// </para>
    /// </remarks>
    public async Task UpdateAsync(
        ServiceRecord record,
        DateOnly originalDate,
        int originalMileage,
        CancellationToken cancellationToken = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // Garage is a foreign key to a keyed table, created on first use — the same trap CreateAsync guards.
            await references.EnsureGarageAsync(record.Garage, cancellationToken);

            var reading = await context.MileageReadings.FirstOrDefaultAsync(
                m => m.VehicleId == record.VehicleId
                    && m.Origin == MileageOrigin.Service
                    && m.ReadingDate == originalDate
                    && m.Mileage == originalMileage,
                cancellationToken);
            if (reading is not null)
            {
                reading.ReadingDate = record.ServiceDate;
                reading.Mileage = record.Mileage;
            }

            var expense = await context.ExpenseEntries.FirstOrDefaultAsync(
                e => e.ServiceRecordId == record.Id, cancellationToken);

            if (record.Cost is { } cost)
            {
                if (expense is null)
                {
                    // A cost added on edit: the mirror did not exist before and must now.
                    context.ExpenseEntries.Add(new ExpenseEntry
                    {
                        VehicleId = record.VehicleId,
                        EntryDate = record.ServiceDate,
                        Category = CategoryFor(record.Type),
                        SubCategory = record.Type,
                        Vendor = record.Garage,
                        Amount = cost,
                        Mileage = record.Mileage,
                        ServiceRecordId = record.Id,
                        Source = record.Source,
                    });
                }
                else
                {
                    expense.Amount = cost;
                    expense.EntryDate = record.ServiceDate;
                    expense.Mileage = record.Mileage;
                    expense.Category = CategoryFor(record.Type);
                    expense.SubCategory = record.Type;
                    expense.Vendor = record.Garage;
                }
            }
            else if (expense is not null)
            {
                // A cost removed on edit: mirroring £0 would leave a row in the log for money that was not spent.
                context.ExpenseEntries.Remove(expense);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>
    /// Removes a record and the rows it implied. The mirrored expense cascades on its foreign key; the mileage
    /// reading has none pointing at it, so it goes by hand — a shadow cannot outlive its source.
    /// </summary>
    public async Task DeleteAsync(ServiceRecord record, CancellationToken cancellationToken = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var reading = await context.MileageReadings.FirstOrDefaultAsync(
                m => m.VehicleId == record.VehicleId
                    && m.Origin == MileageOrigin.Service
                    && m.ReadingDate == record.ServiceDate
                    && m.Mileage == record.Mileage,
                cancellationToken);
            if (reading is not null) context.MileageReadings.Remove(reading);

            context.ServiceRecords.Remove(record);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
