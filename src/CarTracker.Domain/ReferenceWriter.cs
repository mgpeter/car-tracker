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
/// <para>
/// The lists are per-account, so the existence check reads through the owner query filter — another account's
/// "K &amp; P Motors" is invisible here and this one creates its own — and the insert stamps the requesting
/// account. Nothing threads an ownerId in: the accessor is populated for both surfaces by
/// <c>CurrentUserMiddleware</c>, so an MCP write is covered by the same line as a web write.
/// </para>
/// </remarks>
public sealed class ReferenceWriter(CarTrackerDbContext context, ICurrentUserAccessor currentUser)
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
    public Task<bool> EnsureGarageAsync(string? name, CancellationToken cancellationToken = default) =>
        EnsureGarageAsync(name, null, null, null, cancellationToken);

    /// <summary>
    /// Ensures a garage exists, carrying its details when the caller has them.
    /// </summary>
    /// <returns>True when a row was added, so an import can report what it created.</returns>
    /// <remarks>
    /// The account import is the only caller with more than a name: it is cloning a garage row out of a file
    /// that carries the contact, the address and the notes, and dropping them on the way in would make the
    /// clone quietly lossy. <b>It still merges rather than updates</b> - a name the account already holds is
    /// left exactly as it is, address and all, because letting an imported file rewrite the account's own
    /// reference data is the cross-tenant write DEC-018 closed, self-inflicted.
    /// </remarks>
    public async Task<bool> EnsureGarageAsync(
        string? name, string? contact, string? address, string? notes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // The owner is resolved *before* the existence probe, and the order is the whole guard. That probe reads
        // through the query filter, and a context with no account behind it bypasses the filter — so it would
        // see any account's row of that name, return early, and this method would silently do nothing at all.
        // The loud exception ReferenceOwner exists to raise would never fire on the one context that needs it.
        var ownerId = ReferenceOwner.Require(currentUser, "garage");
        if (await context.Garages.AnyAsync(g => g.Name == name, cancellationToken)) return false;

        // Locally too: a caller ensuring the same name twice before saving would otherwise stage two rows and
        // fail on the composite key. The web write paths ensure one name per request; an import ensures every
        // name in a file and then every name its rows mention.
        if (context.Garages.Local.Any(g => g.OwnerId == ownerId && g.Name == name)) return false;

        context.Garages.Add(new Garage
        {
            OwnerId = ownerId, Name = name, Contact = Blank(contact), Address = Blank(address), Notes = Blank(notes),
        });

        return true;
    }

    /// <summary>Ensures a wash location exists. Same contract as <see cref="EnsureGarageAsync(string?, CancellationToken)"/>.</summary>
    public Task<bool> EnsureWashLocationAsync(string? name, CancellationToken cancellationToken = default) =>
        EnsureWashLocationAsync(name, null, cancellationToken);

    /// <inheritdoc cref="EnsureWashLocationAsync(string?, CancellationToken)"/>
    public async Task<bool> EnsureWashLocationAsync(
        string? name, string? notes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // Owner first, for the reason spelled out in EnsureGarageAsync.
        var ownerId = ReferenceOwner.Require(currentUser, "wash location");
        if (await context.WashLocations.AnyAsync(w => w.Name == name, cancellationToken)) return false;
        if (context.WashLocations.Local.Any(w => w.OwnerId == ownerId && w.Name == name)) return false;

        context.WashLocations.Add(new WashLocation { OwnerId = ownerId, Name = name, Notes = Blank(notes) });

        return true;
    }

    /// <summary>
    /// Ensures an expense category exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third keyed reference list, and the only one no web write path creates: the thirteen system
    /// categories are provisioned with the account, and <c>ExpenseService</c> refuses a category outside them
    /// rather than inventing one. An import is the exception, because a file carries the categories its own
    /// owner added and an expense row pointing at a category that does not exist is a row nothing on the
    /// budget screen can group.
    /// </para>
    /// <para>
    /// <c>IsSystem</c> travels from the file. A system category the account somehow lacks should come back as
    /// one; everything else arrives as the ordinary category it was.
    /// </para>
    /// </remarks>
    public async Task<bool> EnsureExpenseCategoryAsync(
        string? name, int displayOrder, bool isSystem, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var ownerId = ReferenceOwner.Require(currentUser, "expense category");
        if (await context.ExpenseCategories.AnyAsync(c => c.Name == name, cancellationToken)) return false;
        if (context.ExpenseCategories.Local.Any(c => c.OwnerId == ownerId && c.Name == name)) return false;

        context.ExpenseCategories.Add(new ExpenseCategory
        {
            OwnerId = ownerId, Name = name, DisplayOrder = displayOrder, IsSystem = isSystem,
        });

        return true;
    }

    /// <summary>
    /// Null for an empty string.
    /// </summary>
    /// <remarks>
    /// Every one of these tables carries a <c>notes &lt;&gt; ''</c> check constraint, so an empty string is not
    /// a shorter note - it is a failed insert. The web forms send null; a file being replayed is the one caller
    /// whose input nobody typed.
    /// </remarks>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
