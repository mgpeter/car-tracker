using CarTracker.Data;
using CarTracker.Shared;

namespace CarTracker.Domain;

/// <summary>
/// The default budget groups a new vehicle starts with — the four that mirror the dashboard's spend bars, seeded
/// with <b>no target</b> (tracked, until the owner sets a number against a car with actual history).
/// </summary>
/// <remarks>
/// A code template rather than <c>HasData</c> seeding, for the same reason as <see cref="CheckTemplate"/>:
/// <see cref="BudgetGroup"/> is vehicle-scoped and DEC-007 forbids seeding anything scoped to a vehicle. Applied
/// at create time then owned by the vehicle — rename, retarget, regroup or delete freely, and nothing re-applies
/// this list. Category names reference the seeded <see cref="ExpenseCategory"/> list; <c>Tools/Equipment</c>
/// exactly (it is what an equipment purchase mirrors into).
/// </remarks>
public static class BudgetGroupTemplate
{
    public sealed record Item(string Name, string[] Categories);

    public static readonly IReadOnlyList<Item> Defaults =
    [
        new("Fuel", ["Fuel"]),
        new("Service & Repairs", ["Service", "Repair", "Parts"]),
        new("Insurance, Tax & MOT", ["Insurance", "Tax", "MOT"]),
        new("Equipment & Tools", ["Tools/Equipment"]),
    ];

    internal static IEnumerable<BudgetGroup> For(int vehicleId, EntrySource source) =>
        Defaults.Select((item, index) => new BudgetGroup
        {
            VehicleId = vehicleId,
            Name = item.Name,
            AnnualBudget = null,
            DisplayOrder = index + 1,
            Source = source,
            Categories = [.. item.Categories.Select(c => new BudgetGroupCategory { VehicleId = vehicleId, Category = c })],
        });
}
