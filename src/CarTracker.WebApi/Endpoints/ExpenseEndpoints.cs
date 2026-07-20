using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Expenses — every pound the car has cost, including the fills it mirrors.
/// </summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/expenses").WithTags("Expenses");

        group.MapGet("/", GetExpensesAsync)
            .WithName("GetExpenses")
            .WithSummary("Every entry with the derived rollups. The running total is computed, never a column.");

        group.MapPost("/", AddExpenseAsync)
            .WithName("AddExpense")
            .WithSummary("Records an expense. Fuel-category entries are refused — they come from the fuel log.");

        group.MapPatch("/{id:int}", UpdateExpenseAsync)
            .WithName("UpdateExpense")
            .WithSummary("Edits an expense. A mirrored fuel entry is not editable here — edit the fill.");

        group.MapDelete("/{id:int}", DeleteExpenseAsync)
            .WithName("DeleteExpense")
            .WithSummary("Deletes an expense. A mirrored fuel entry is not deletable here — delete the fill.");

        return app;
    }

    private static async Task<Results<Ok<ExpenseLog>, NotFound<ProblemDetails>>> GetExpensesAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        ExpenseService expenses,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        if (summary is null) return VehicleLookup.NotFound(registration);

        // The rows come from the shared service (so the MCP list tool projects them identically); the rollups
        // come from the summary, not a SUM here, so they are the same figure the dashboard shows.
        var entries = await expenses.ListAsync(vehicleId.Value, cancellationToken);
        return TypedResults.Ok(new ExpenseLog(summary.Spend, entries));
    }

    /// <remarks>
    /// <para>
    /// A Fuel-category expense cannot be created here, and that refusal is load-bearing. The fuel log and the
    /// fuel-category expense total are equal <b>by construction</b> — <see cref="FuelEntryFactory"/> is the
    /// only thing that writes one. Let this endpoint add another and the invariant becomes a hope, and the
    /// workbook's £163.16 gap has somewhere to come back from.
    /// </para>
    /// <para>
    /// Protecting it at the write path rather than detecting it afterwards is the stronger move: a detector
    /// tells you the totals diverged last Tuesday, whereas this makes diverging impossible.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<ExpenseItem>, NotFound<ProblemDetails>, ValidationProblem>> AddExpenseAsync(
        string registration,
        AddExpenseRequest request,
        CarTrackerDbContext context,
        ExpenseService expenses,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var input = new ExpenseInput(
            request.EntryDate, request.Category, request.Amount, request.SubCategory,
            request.Vendor, request.Mileage, request.PaymentMethod, request.Notes);

        var result = await expenses.AddAsync(vehicleId.Value, input, EntrySource.Web, cancellationToken);
        if (result is { Status: WriteStatus.Validation, Errors: { } errors })
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Created($"/api/vehicles/{registration}/expenses", result.Value);
    }

    private static async Task<Results<Ok<ExpenseItem>, NotFound<ProblemDetails>, ValidationProblem, Conflict<ProblemDetails>>> UpdateExpenseAsync(
        string registration,
        int id,
        UpdateExpenseRequest request,
        CarTrackerDbContext context,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var entry = await context.ExpenseEntries
            .SingleOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId.Value, cancellationToken);

        if (entry is null) return ExpenseNotFound(id, registration);
        // Both mirror directions are read-only here: a fuel-mirror (FuelEntryId) and a service-mirror
        // (ServiceRecordId). Editing a service-mirror directly would silently desync it from its record — the
        // reverse of the drift the mirror closes — so the source is where the edit belongs.
        if (entry.FuelEntryId is not null) return MirroredRow(id, fuel: true);
        if (entry.ServiceRecordId is not null) return MirroredRow(id, fuel: false);

        if (request.Category is { } category && !await CategoryExistsAsync(context, category, cancellationToken))
        {
            return TypedResults.ValidationProblem(UnknownCategory(category));
        }

        if (request.Category is FuelEntryFactory.FuelCategory)
        {
            return TypedResults.ValidationProblem(FuelIsMirrored());
        }

        if (request.Amount is <= 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Amount)] = ["An amount must be greater than zero."],
            });
        }

        entry.EntryDate = request.EntryDate ?? entry.EntryDate;
        entry.Category = request.Category ?? entry.Category;
        entry.SubCategory = request.SubCategory ?? entry.SubCategory;
        entry.Vendor = request.Vendor ?? entry.Vendor;
        entry.Amount = request.Amount ?? entry.Amount;
        entry.Mileage = request.Mileage ?? entry.Mileage;
        entry.PaymentMethod = request.PaymentMethod ?? entry.PaymentMethod;
        entry.Notes = request.Notes ?? entry.Notes;

        await context.SaveChangesAsync(cancellationToken);

        // An expense can carry a mileage, so editing one can change the anomaly picture. POST scanned; PATCH
        // must too, or an edit that introduces (or clears) a non-monotonic reading goes unnoticed.
        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.Ok(ToItem(entry));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> DeleteExpenseAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        AnomalyScanner scanner,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var entry = await context.ExpenseEntries
            .SingleOrDefaultAsync(e => e.Id == id && e.VehicleId == vehicleId.Value, cancellationToken);

        if (entry is null) return ExpenseNotFound(id, registration);

        // Deleting a mirrored row would leave its source — a fill or a service record — with no money against
        // it, and the two totals would drift: the exact gap the mirror closes. Delete the source instead.
        if (entry.FuelEntryId is not null) return MirroredRow(id, fuel: true);
        if (entry.ServiceRecordId is not null) return MirroredRow(id, fuel: false);

        // An expense that carried a mileage wrote its own odometer reading (Origin=Manual) on POST. That shadow
        // cannot outlive its source either — matched by the same (origin, date, mileage) key the other mirrors
        // use, since the reading carries no foreign key back.
        if (entry.Mileage is { } mileage)
        {
            var reading = await context.MileageReadings.FirstOrDefaultAsync(
                m => m.VehicleId == vehicleId.Value
                    && m.Origin == MileageOrigin.Manual
                    && m.ReadingDate == entry.EntryDate
                    && m.Mileage == mileage,
                cancellationToken);
            if (reading is not null) context.MileageReadings.Remove(reading);
        }

        context.ExpenseEntries.Remove(entry);
        await context.SaveChangesAsync(cancellationToken);

        // Removing the reading can clear a non-monotonic flag it caused; auto-reconcile closes it on this scan.
        await scanner.ScanAsync(vehicleId.Value, EntrySource.Web, cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<bool> CategoryExistsAsync(CarTrackerDbContext context, string name, CancellationToken ct) =>
        context.ExpenseCategories.AnyAsync(c => c.Name == name, ct);

    private static Dictionary<string, string[]> FuelIsMirrored() => new()
    {
        ["Category"] =
        [
            "Fuel expenses are created from the fuel log, not entered here — add a fill and its expense "
            + "mirrors automatically. That is what keeps the two totals equal.",
        ],
    };

    private static Dictionary<string, string[]> UnknownCategory(string category) => new()
    {
        ["Category"] = [$"'{category}' is not an expense category. Add it in Settings first."],
    };

    private static NotFound<ProblemDetails> ExpenseNotFound(int id, string registration) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Expense not found",
            Detail = $"No expense {id} on '{registration}'.",
            Status = StatusCodes.Status404NotFound,
        });

    private static Conflict<ProblemDetails> MirroredRow(int id, bool fuel) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = fuel ? "Mirrored from a fuel entry" : "Mirrored from a service record",
            Detail = fuel
                ? $"Expense {id} mirrors a fill. Edit the fill and this follows — that is what keeps the "
                  + "fuel log and the fuel-category total equal to the penny."
                : $"Expense {id} mirrors a service record. Edit the service record and this follows — the "
                  + "record is the source and the expense its shadow.",
            Status = StatusCodes.Status409Conflict,
        });

    private static ExpenseItem ToItem(ExpenseEntry e) =>
        new(e.Id, e.EntryDate, e.Category, e.SubCategory, e.Vendor, e.Amount,
            e.Mileage, e.PaymentMethod, e.FuelEntryId, e.ServiceRecordId, e.Notes);
}

public sealed record AddExpenseRequest(
    DateOnly EntryDate,
    string Category,
    decimal Amount,
    string? SubCategory = null,
    string? Vendor = null,
    /// <summary>Optional — but when present it writes an odometer reading, like every other log that carries one.</summary>
    int? Mileage = null,
    string? PaymentMethod = null,
    string? Notes = null);

public sealed record UpdateExpenseRequest(
    DateOnly? EntryDate = null,
    string? Category = null,
    decimal? Amount = null,
    string? SubCategory = null,
    string? Vendor = null,
    int? Mileage = null,
    string? PaymentMethod = null,
    string? Notes = null);
