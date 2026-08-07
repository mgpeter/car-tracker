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
    IReadOnlyCollection<BudgetGroup> BudgetGroups,
    IReadOnlyCollection<DataAnomaly>? OpenAnomalies = null,
    IReadOnlyCollection<EquipmentItem>? EquipmentItems = null)
{
    /// <summary>Open integrity flags, or none. Null-coalesced so a fixture without flags need not say so.</summary>
    public IReadOnlyCollection<DataAnomaly> OpenAnomalies { get; init; } = OpenAnomalies ?? [];

    /// <summary>
    /// Inventory, for the integrity scan only — no derived figure reads it. Present so the detector can see an
    /// item carrying a cost with no purchase date, whose money reaches no total.
    /// </summary>
    public IReadOnlyCollection<EquipmentItem> EquipmentItems { get; init; } = EquipmentItems ?? [];
}
