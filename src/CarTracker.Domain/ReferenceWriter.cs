using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Creates a reference-list row the first time something names it.
/// </summary>
/// <remarks>
/// <para>
/// Garages and wash locations are <b>keyed reference tables</b>, and the columns that point at them look like
/// free text and are not: <c>ServiceRecord.Garage</c>, <c>MaintenanceTask.AssignedGarage</c>,
/// <c>Vehicle.DefaultGarage</c> and <c>WashEntry.Location</c> are all foreign keys to a name.
/// </para>
/// <para>
/// Both entities' comments say "upserted by the importer" — and DEC-008 deleted the importer, so nothing
/// upserted them any more. The result was a foreign-key violation, surfacing as a 500, the first time any
/// write named a garage that had not been seen before. That was found by typing "K & P Motors" into the
/// service screen; it would have been found three more times, once per write path, which is why this is one
/// class rather than a fix repeated wherever it bites.
/// </para>
/// <para>
/// CLAUDE.md is explicit that these lists are "created as used" — only the 13 expense categories are seeded.
/// So creating on first use is the design, not a workaround for it.
/// </para>
/// </remarks>
public sealed class ReferenceWriter(CarTrackerDbContext context)
{
    /// <summary>
    /// Ensures a garage exists. Call before saving anything whose garage column is set.
    /// </summary>
    /// <remarks>
    /// Keyed by name, so this is an existence check, not a merge. It deliberately does not normalise: "K & P
    /// Motors" and "K&P Motors" become two rows, which is honest. Deciding they are the same place is a
    /// judgement for the reference-list editor in settings — a write path that guesses would quietly merge two
    /// real garages that happen to have similar names.
    /// </remarks>
    public async Task EnsureGarageAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await context.Garages.AnyAsync(g => g.Name == name, cancellationToken)) return;

        context.Garages.Add(new Garage { Name = name });
    }

    /// <summary>Ensures a wash location exists. Same contract as <see cref="EnsureGarageAsync"/>.</summary>
    public async Task EnsureWashLocationAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await context.WashLocations.AnyAsync(w => w.Name == name, cancellationToken)) return;

        context.WashLocations.Add(new WashLocation { Name = name });
    }
}
