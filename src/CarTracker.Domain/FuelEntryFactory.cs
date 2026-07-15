using CarTracker.Data;
using CarTracker.Shared;
// For IExecutionStrategy.ExecuteAsync's Func<Task> overload, which is an extension method.
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Records a fill, together with the two rows it implies.
/// </summary>
/// <remarks>
/// <para>
/// A fill is never one row. It is a fuel entry, the odometer reading it was taken at, and the money it cost —
/// and the workbook's fourth defect is what happens when those drift apart. Its Expenses sheet carried a
/// single lumped "fuel to date" row of £725.70 while the Fuel Log totalled £888.86: a £163.16 gap, invisible
/// because the two were maintained by hand and nothing tied them together. Spec §3.2's auto-mirroring is what
/// closes it, and this is where that happens.
/// </para>
/// <para>
/// The mirror is not a convenience. It is the reason the gap cannot reopen: the expense is created *from* the
/// fill, carries <see cref="ExpenseEntry.FuelEntryId"/> back to it, and is never typed by a human. The fuel
/// log and the fuel-category expense total are the same numbers by construction rather than by discipline.
/// </para>
/// </remarks>
public sealed class FuelEntryFactory(CarTrackerDbContext context)
{
    /// <summary>The category a mirrored fill is filed under. Seeded and <c>IsSystem</c> — it cannot be deleted.</summary>
    public const string FuelCategory = "Fuel";

    /// <summary>
    /// Creates the fill, its mileage reading and its mirrored expense in one transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two saves, then a commit: the fill's key must exist before the expense can point at it. The transaction
    /// runs <b>inside an execution strategy</b> — Aspire's <c>EnrichNpgsqlDbContext</c> installs a retrying
    /// strategy that refuses a user-initiated transaction outright, because it cannot retry a unit of work it
    /// does not own. Wrapping is the documented answer, and it means a transient failure retries the whole
    /// fill rather than a fragment of it.
    /// </para>
    /// <para>
    /// The odometer reading is written with <see cref="MileageOrigin.Fuel"/> rather than left implicit. Current
    /// mileage derives from readings only (§1) — the fill's own <c>Mileage</c> column is not consulted — so a
    /// fill that did not write one would be a fill that never moved the odometer.
    /// </para>
    /// </remarks>
    public async Task<FuelEntry> CreateAsync(
        FuelEntry entry,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            entry.Source = source;
            context.FuelEntries.Add(entry);
            await context.SaveChangesAsync(cancellationToken);

            context.MileageReadings.Add(new MileageReading
            {
                VehicleId = entry.VehicleId,
                ReadingDate = entry.EntryDate,
                Mileage = entry.Mileage,
                Origin = MileageOrigin.Fuel,
                Source = source,
            });

            context.ExpenseEntries.Add(new ExpenseEntry
            {
                VehicleId = entry.VehicleId,
                EntryDate = entry.EntryDate,
                Category = FuelCategory,
                Vendor = entry.Station,
                Amount = entry.TotalCost,
                Mileage = entry.Mileage,
                // The link back. It is what lets the expenses screen say "edit the fill, not the expense" and
                // mean it, and what makes the mirror auditable rather than a coincidence of equal numbers.
                FuelEntryId = entry.Id,
                Source = source,
            });

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return entry;
    }
}
