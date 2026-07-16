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
public sealed class ServiceRecordFactory(CarTrackerDbContext context)
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

            // Garages are a keyed reference list, and `ServiceRecord.Garage` is a foreign key to it — not the
            // free text it looks like. CLAUDE.md says garages are "created as used" and the entity's own
            // comment says "upserted by the importer", but DEC-008 deleted the importer, so nothing upserts
            // them any more: every record naming a garage that had never been seen was a 500 until this.
            await EnsureGarageAsync(record.Garage, cancellationToken);

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
    /// Creates the garage if this is the first time it has been named.
    /// </summary>
    /// <remarks>
    /// Keyed by name, so this is an existence check rather than a merge. It deliberately does not tidy the
    /// name: "K & P Motors" and "K&P Motors" become two garages, which is honest — guessing they are the same
    /// place is a decision for the reference-list editor in settings, not for a write path.
    /// </remarks>
    private async Task EnsureGarageAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var exists = await context.Garages.AnyAsync(g => g.Name == name, cancellationToken);
        if (!exists) context.Garages.Add(new Garage { Name = name });
    }
}
