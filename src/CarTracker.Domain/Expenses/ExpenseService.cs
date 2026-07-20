using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Expenses;

/// <summary>
/// The shared expense read + add path (README §3.2). The REST endpoint and the MCP tools both call this, so the
/// Fuel-category refusal and the mileage-shadow rule live in one place — the "no second write path" guarantee
/// the single-domain design rests on.
/// </summary>
/// <remarks>
/// Edit and delete stay in the REST endpoint: the assistant does neither (add/log + safe updates only), so there
/// is one caller and no divergence risk. What the assistant <i>can</i> do — list and add — is what lives here.
/// </remarks>
public sealed class ExpenseService(CarTrackerDbContext context, AnomalyScanner scanner)
{
    /// <summary>Every entry for a vehicle, newest first, projected to the shared row shape.</summary>
    public Task<List<ExpenseItem>> ListAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.ExpenseEntries
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicleId)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.Id)
            .Select(e => new ExpenseItem(
                e.Id, e.EntryDate, e.Category, e.SubCategory, e.Vendor, e.Amount,
                e.Mileage, e.PaymentMethod, e.FuelEntryId, e.ServiceRecordId, e.Notes))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Records an expense and, when it carries a mileage, its odometer reading — then scans for anomalies. A
    /// Fuel-category expense is refused: the fuel log and the fuel-category total are equal by construction
    /// (<see cref="FuelEntryFactory"/> is the only thing that writes one), and letting this add another gives the
    /// workbook's £163.16 gap somewhere to return from.
    /// </summary>
    public async Task<WriteResult<ExpenseItem>> AddAsync(
        int vehicleId,
        ExpenseInput input,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        if (input.Amount <= 0)
            return WriteResult<ExpenseItem>.Invalid(nameof(input.Amount), "An amount must be greater than zero.");

        if (input.Category == FuelEntryFactory.FuelCategory)
        {
            return WriteResult<ExpenseItem>.Invalid("Category",
                "Fuel expenses are created from the fuel log, not entered here — add a fill and its expense "
                + "mirrors automatically. That is what keeps the two totals equal.");
        }

        if (!await context.ExpenseCategories.AnyAsync(c => c.Name == input.Category, cancellationToken))
        {
            return WriteResult<ExpenseItem>.Invalid("Category",
                $"'{input.Category}' is not an expense category. Add it in Settings first.");
        }

        if (input.Mileage is < 0)
            return WriteResult<ExpenseItem>.Invalid(nameof(input.Mileage), "A mileage cannot be negative.");

        var entry = new ExpenseEntry
        {
            VehicleId = vehicleId,
            EntryDate = input.EntryDate,
            Category = input.Category,
            SubCategory = input.SubCategory,
            Vendor = input.Vendor,
            Amount = input.Amount,
            Mileage = input.Mileage,
            PaymentMethod = input.PaymentMethod,
            Notes = input.Notes,
            Source = source,
        };

        context.ExpenseEntries.Add(entry);

        // An expense that carries a mileage is an odometer reading too — the same rule a fill follows. Without
        // it, "MOT at 80,705 mi" would be a number on a receipt the odometer never learned about.
        if (input.Mileage is { } mileage)
        {
            context.MileageReadings.Add(new MileageReading
            {
                VehicleId = vehicleId,
                ReadingDate = input.EntryDate,
                Mileage = mileage,
                Origin = MileageOrigin.Manual,
                Notes = $"{input.Category}: {input.Vendor}".Trim(' ', ':'),
                Source = source,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await scanner.ScanAsync(vehicleId, source, cancellationToken);

        var item = new ExpenseItem(
            entry.Id, entry.EntryDate, entry.Category, entry.SubCategory, entry.Vendor, entry.Amount,
            entry.Mileage, entry.PaymentMethod, entry.FuelEntryId, entry.ServiceRecordId, entry.Notes);

        return WriteResult<ExpenseItem>.Created(item);
    }
}
