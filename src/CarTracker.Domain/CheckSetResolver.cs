using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Resolves a <see cref="CheckSource"/> into the concrete <see cref="CheckDefinition"/> rows a vehicle should
/// receive — the generic starter set (optionally a chosen subset), a copy of another vehicle's active checks, or
/// none.
/// </summary>
/// <remarks>
/// <para>
/// Shared by <see cref="VehicleFactory"/> at create time and <see cref="CheckSetAdder"/> for adding a set to a
/// vehicle that already exists, so the two can never disagree about what "the generic set" or "copy from X"
/// means. The returned definitions carry no <c>VehicleId</c> or <c>Source</c> — the caller stamps those, because
/// the resolver does not know which vehicle they are for or who is adding them.
/// </para>
/// </remarks>
public sealed class CheckSetResolver(CarTrackerDbContext context)
{
    /// <param name="selectedNames">
    /// When non-null, restricts the result to checks whose <see cref="CheckDefinition.Name"/> is in the set —
    /// the add-sheet toggle selection. Applies to BOTH the generic set and a copy; <c>null</c> means the whole
    /// source (all fifteen / every active source check), so create-time callers that pass nothing are unchanged.
    /// An empty set means none.
    /// </param>
    public async Task<List<CheckDefinition>> ResolveAsync(
        CheckSource source,
        int? copyFromVehicleId,
        IReadOnlyList<string>? selectedNames,
        CancellationToken cancellationToken = default)
    {
        switch (source)
        {
            case CheckSource.None:
                return [];

            case CheckSource.GenericStarterSet:
                // null → the whole set; a subset → only those, in template order; [] → none.
                return [.. CheckTemplate.For(vehicleId: 0, selectedNames)];

            case CheckSource.CopyFromVehicle:
                if (copyFromVehicleId is null)
                {
                    throw new ArgumentException(
                        "CopyFromVehicle needs a vehicle to copy from.", nameof(copyFromVehicleId));
                }

                var sourceDefs = await context.CheckDefinitions
                    .AsNoTracking()
                    .Where(d => d.VehicleId == copyFromVehicleId.Value && d.IsActive)
                    .OrderBy(d => d.DisplayOrder)
                    .ToListAsync(cancellationToken);

                // Honour the selection when given: a copy is now filterable the same way the generic set is, so
                // the add sheet's toggle list works for either source. null copies every active check, as before.
                var selected = selectedNames is null
                    ? sourceDefs
                    : sourceDefs.Where(d => selectedNames.Contains(d.Name)).ToList();

                // New rows, not the loaded ones: an AsNoTracking entity carries the source vehicle's Id and its
                // audit stamps, and re-attaching it would try to move the original.
                return [.. selected.Select(d => new CheckDefinition
                {
                    Name = d.Name,
                    CadenceLabel = d.CadenceLabel,
                    IntervalDays = d.IntervalDays,
                    Guidance = d.Guidance,
                    DisplayOrder = d.DisplayOrder,
                    IsActive = true,
                })];

            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown check source.");
        }
    }
}
