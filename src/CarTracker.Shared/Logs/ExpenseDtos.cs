using CarTracker.Shared.Metrics;

namespace CarTracker.Shared.Logs;

/// <summary>
/// One expense row, as every surface renders it. Lifted out of the WebApi endpoint so the MCP list tool and the
/// REST endpoint return the same shape from the same query.
/// </summary>
/// <param name="FuelEntryId">
/// Non-null on a fuel-mirrored row — read-only "from fuel"; the API refuses to edit or delete it here.
/// </param>
/// <param name="ServiceRecordId">
/// Non-null on a service-mirrored row. The record is the source and this its shadow, so edits go to the record.
/// </param>
/// <param name="EquipmentItemId">
/// Non-null on an equipment-mirrored row (a purchase with a cost and date). The item is the source; edit it there.
/// </param>
public sealed record ExpenseItem(
    int Id,
    DateOnly EntryDate,
    string Category,
    string? SubCategory,
    string? Vendor,
    decimal Amount,
    int? Mileage,
    string? PaymentMethod,
    int? FuelEntryId,
    int? ServiceRecordId,
    int? EquipmentItemId,
    string? Notes);

/// <param name="Rollups">
/// Computed, never a column — the same figure the dashboard shows (spec §4). The workbook's Expenses sheet
/// carried ~30 blank rows holding a running-total formula; a <c>SUM()</c> at render is the replacement.
/// </param>
public sealed record ExpenseLog(SpendSummary Rollups, IReadOnlyList<ExpenseItem> Entries);

/// <summary>
/// The fields needed to record an expense, independent of transport. The REST endpoint maps its request body to
/// this; an MCP <c>log_expense</c> maps its tool arguments to it — both then call the one write service, so the
/// Fuel-category refusal and the odometer-shadow rule cannot fork.
/// </summary>
public sealed record ExpenseInput(
    DateOnly EntryDate,
    string Category,
    decimal Amount,
    string? SubCategory = null,
    string? Vendor = null,
    int? Mileage = null,
    string? PaymentMethod = null,
    string? Notes = null);
