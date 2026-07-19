using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Adds a resolved set of checks to a vehicle that already exists — the settings "add checks" path, and the
/// post-hoc counterpart to <see cref="VehicleFactory"/>'s create-time set.
/// </summary>
/// <remarks>
/// <para>
/// Adds only the checks the vehicle does not already have, matched by <see cref="CheckDefinition.Name"/>. The
/// unique <c>(VehicleId, Name)</c> index ignores <c>IsActive</c>, so a <b>retired</b> check with the same name
/// still blocks a re-add — the diff therefore considers active and retired definitions alike, and a name already
/// present is reported as skipped rather than inserted (which would violate the index).
/// </para>
/// <para>
/// New definitions are <b>appended</b>: <c>DisplayOrder</c> continues from the vehicle's current maximum, the
/// same rule <c>ChecksEndpoints.AddDefinitionAsync</c> uses for a single add — so a bulk add slots after what is
/// already there rather than renumbering from one. Resolution goes through <see cref="CheckSetResolver"/>, so
/// "the generic set" and "copy from X" mean exactly what they mean at create time.
/// </para>
/// </remarks>
public sealed class CheckSetAdder(CarTrackerDbContext context)
{
    /// <param name="Added">The definitions actually inserted, in the order they were added.</param>
    /// <param name="Skipped">Names the vehicle already had (active or retired), left untouched.</param>
    public sealed record Result(IReadOnlyList<CheckDefinition> Added, IReadOnlyList<string> Skipped);

    public async Task<Result> AddSetAsync(
        int vehicleId,
        CheckSource source,
        int? copyFromVehicleId,
        IReadOnlyList<string>? selectedNames,
        EntrySource entrySource,
        CancellationToken cancellationToken = default)
    {
        var resolved = await new CheckSetResolver(context)
            .ResolveAsync(source, copyFromVehicleId, selectedNames, cancellationToken);

        // Active AND retired: the unique index ignores IsActive, so either would block a re-add.
        var existing = await context.CheckDefinitions
            .Where(d => d.VehicleId == vehicleId)
            .Select(d => new { d.Name, d.DisplayOrder })
            .ToListAsync(cancellationToken);

        var existingNames = existing.Select(e => e.Name).ToHashSet();
        var nextOrder = existing.Count == 0 ? 1 : existing.Max(e => e.DisplayOrder) + 1;

        var added = new List<CheckDefinition>();
        var skipped = new List<string>();

        foreach (var def in resolved)
        {
            if (!existingNames.Add(def.Name))
            {
                skipped.Add(def.Name);
                continue;
            }

            def.VehicleId = vehicleId;
            def.Source = entrySource;
            def.DisplayOrder = nextOrder++;
            def.IsActive = true;
            added.Add(def);
        }

        if (added.Count > 0)
        {
            // A single SaveChanges is atomic on its own; the retrying execution strategy handles it without an
            // explicit user transaction (which it would refuse).
            context.CheckDefinitions.AddRange(added);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new Result(added, skipped);
    }
}
