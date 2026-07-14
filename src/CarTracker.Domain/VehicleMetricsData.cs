using CarTracker.Data;

namespace CarTracker.Domain;

/// <summary>
/// Everything the calculators need for one vehicle, already loaded.
/// </summary>
/// <remarks>
/// The seam that keeps the calculators pure. A query layer fills this from the database; the computation is a
/// function of it. That is what lets the workbook fixture be a C# constant rather than a seeded database.
/// </remarks>
public sealed record VehicleMetricsData(
    Vehicle Vehicle,
    IReadOnlyCollection<MileageReading> MileageReadings,
    IReadOnlyCollection<FuelEntry> FuelEntries,
    IReadOnlyCollection<ExpenseEntry> ExpenseEntries,
    IReadOnlyCollection<ServiceRecord> ServiceRecords,
    IReadOnlyCollection<CheckDefinition> CheckDefinitions,
    IReadOnlyCollection<CheckLog> CheckLogs,
    IReadOnlyCollection<BudgetCategory> BudgetCategories);
