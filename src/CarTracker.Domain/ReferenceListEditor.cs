using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>The outcome of a reference-list edit, mapped to HTTP by the endpoint.</summary>
public enum ReferenceOpStatus
{
    Ok,

    /// <summary>The named row does not exist.</summary>
    NotFound,

    /// <summary>A create or rename would collide with an existing row (names are the key).</summary>
    NameCollision,

    /// <summary>A delete was refused because records reference the row and no re-home target was given.</summary>
    Referenced,

    /// <summary>A system category cannot be deleted ("seeded, undeletable").</summary>
    SystemLocked,

    /// <summary>The Fuel category cannot be renamed — the fuel-to-expense mirror resolves it by exact name.</summary>
    FuelRenameLocked,

    /// <summary>A re-home target is the row being deleted, or does not exist.</summary>
    BadRehomeTarget,
}

public sealed record ReferenceOpResult(ReferenceOpStatus Status, int ReferenceCount = 0)
{
    public static readonly ReferenceOpResult Ok = new(ReferenceOpStatus.Ok);
    public static readonly ReferenceOpResult NotFound = new(ReferenceOpStatus.NotFound);
    public static readonly ReferenceOpResult NameCollision = new(ReferenceOpStatus.NameCollision);
    public static readonly ReferenceOpResult SystemLocked = new(ReferenceOpStatus.SystemLocked);
    public static readonly ReferenceOpResult FuelRenameLocked = new(ReferenceOpStatus.FuelRenameLocked);
    public static readonly ReferenceOpResult BadRehomeTarget = new(ReferenceOpStatus.BadRehomeTarget);
    public static ReferenceOpResult Referenced(int count) => new(ReferenceOpStatus.Referenced, count);
}

public sealed record GarageRef(string Name, string? Contact, string? Address, string? Notes, int ReferenceCount);
public sealed record WashLocationRef(string Name, string? Notes, int ReferenceCount);
public sealed record ExpenseCategoryRef(string Name, bool IsMirrorOnly, bool IsSystem, int ReferenceCount);

/// <summary>
/// The edit/remove half of the reference lists — the side <see cref="ReferenceWriter"/> (create-on-first-use)
/// deliberately left off.
/// </summary>
/// <remarks>
/// <para>
/// Garages, wash locations and expense categories are keyed by <b>name</b>, and columns that look like free
/// text point at them as foreign keys (see <see cref="ReferenceWriter"/>). Two consequences drive everything
/// here. A <b>delete</b> of a referenced garage or wash location would not fail — those FKs are
/// <c>SetNull</c>, so the database would silently blank every referencing row — which is exactly why the delete
/// must count references and refuse, or re-home, rather than trust the constraint to stop it. And a
/// <b>rename</b> changes the primary key, so it cannot be an in-place update: the new-named row is created, the
/// referencing rows are re-pointed to it, and the old row is removed — one transaction, or the FK breaks
/// mid-flight.
/// </para>
/// <para>
/// Rename and re-home touch more than one table, so they run inside the retrying execution strategy's
/// transaction — <c>EnrichNpgsqlDbContext</c> refuses a user-initiated transaction outside it, the trap
/// <see cref="ServiceRecordFactory"/> documents.
/// </para>
/// </remarks>
public sealed class ReferenceListEditor(CarTrackerDbContext context)
{
    // ---- Garages ------------------------------------------------------------------------------------------

    private async Task<int> CountGarageReferencesAsync(string name, CancellationToken ct) =>
        await context.ServiceRecords.CountAsync(s => s.Garage == name, ct)
        + await context.Vehicles.CountAsync(v => v.DefaultGarage == name, ct)
        + await context.MaintenanceTasks.CountAsync(t => t.AssignedGarage == name, ct);

    public async Task<IReadOnlyList<GarageRef>> ListGaragesAsync(CancellationToken ct = default)
    {
        var garages = await context.Garages.OrderBy(g => g.Name).ToListAsync(ct);
        var result = new List<GarageRef>(garages.Count);
        foreach (var g in garages)
        {
            result.Add(new GarageRef(g.Name, g.Contact, g.Address, g.Notes, await CountGarageReferencesAsync(g.Name, ct)));
        }

        return result;
    }

    public async Task<ReferenceOpResult> CreateGarageAsync(string name, string? contact, string? address, string? notes, CancellationToken ct = default)
    {
        if (await context.Garages.AnyAsync(g => g.Name == name, ct)) return ReferenceOpResult.NameCollision;
        context.Garages.Add(new Garage { Name = name, Contact = contact, Address = address, Notes = notes });
        await context.SaveChangesAsync(ct);
        return ReferenceOpResult.Ok;
    }

    public async Task<ReferenceOpResult> UpdateGarageAsync(string name, string? newName, string? contact, string? address, string? notes, CancellationToken ct = default)
    {
        var garage = await context.Garages.SingleOrDefaultAsync(g => g.Name == name, ct);
        if (garage is null) return ReferenceOpResult.NotFound;

        var rename = newName is not null && newName != name;
        if (rename && await context.Garages.AnyAsync(g => g.Name == newName, ct)) return ReferenceOpResult.NameCollision;

        // Field edits apply either way; a rename carries the (possibly edited) fields onto the new row.
        var newContact = contact ?? garage.Contact;
        var newAddress = address ?? garage.Address;
        var newNotes = notes ?? garage.Notes;

        if (!rename)
        {
            garage.Contact = newContact;
            garage.Address = newAddress;
            garage.Notes = newNotes;
            await context.SaveChangesAsync(ct);
            return ReferenceOpResult.Ok;
        }

        await InTransactionAsync(async () =>
        {
            context.Garages.Add(new Garage { Name = newName!, Contact = newContact, Address = newAddress, Notes = newNotes });
            await context.SaveChangesAsync(ct);
            await context.ServiceRecords.Where(s => s.Garage == name).ExecuteUpdateAsync(u => u.SetProperty(s => s.Garage, newName), ct);
            await context.Vehicles.Where(v => v.DefaultGarage == name).ExecuteUpdateAsync(u => u.SetProperty(v => v.DefaultGarage, newName), ct);
            await context.MaintenanceTasks.Where(t => t.AssignedGarage == name).ExecuteUpdateAsync(u => u.SetProperty(t => t.AssignedGarage, newName), ct);
            await context.Garages.Where(g => g.Name == name).ExecuteDeleteAsync(ct);
        }, ct);

        return ReferenceOpResult.Ok;
    }

    public async Task<ReferenceOpResult> DeleteGarageAsync(string name, string? rehomeTo, CancellationToken ct = default)
    {
        if (!await context.Garages.AnyAsync(g => g.Name == name, ct)) return ReferenceOpResult.NotFound;

        var references = await CountGarageReferencesAsync(name, ct);

        if (references == 0 && rehomeTo is null)
        {
            context.Garages.Remove(await context.Garages.SingleAsync(g => g.Name == name, ct));
            await context.SaveChangesAsync(ct);
            return ReferenceOpResult.Ok;
        }

        if (rehomeTo is null) return ReferenceOpResult.Referenced(references);

        if (rehomeTo == name || !await context.Garages.AnyAsync(g => g.Name == rehomeTo, ct)) return ReferenceOpResult.BadRehomeTarget;

        await InTransactionAsync(async () =>
        {
            await context.ServiceRecords.Where(s => s.Garage == name).ExecuteUpdateAsync(u => u.SetProperty(s => s.Garage, rehomeTo), ct);
            await context.Vehicles.Where(v => v.DefaultGarage == name).ExecuteUpdateAsync(u => u.SetProperty(v => v.DefaultGarage, rehomeTo), ct);
            await context.MaintenanceTasks.Where(t => t.AssignedGarage == name).ExecuteUpdateAsync(u => u.SetProperty(t => t.AssignedGarage, rehomeTo), ct);
            await context.Garages.Where(g => g.Name == name).ExecuteDeleteAsync(ct);
        }, ct);

        return ReferenceOpResult.Ok;
    }

    // ---- Wash locations -----------------------------------------------------------------------------------

    private Task<int> CountWashReferencesAsync(string name, CancellationToken ct) =>
        context.WashEntries.CountAsync(w => w.Location == name, ct);

    public async Task<IReadOnlyList<WashLocationRef>> ListWashLocationsAsync(CancellationToken ct = default)
    {
        var locations = await context.WashLocations.OrderBy(w => w.Name).ToListAsync(ct);
        var result = new List<WashLocationRef>(locations.Count);
        foreach (var w in locations)
        {
            result.Add(new WashLocationRef(w.Name, w.Notes, await CountWashReferencesAsync(w.Name, ct)));
        }

        return result;
    }

    public async Task<ReferenceOpResult> CreateWashLocationAsync(string name, string? notes, CancellationToken ct = default)
    {
        if (await context.WashLocations.AnyAsync(w => w.Name == name, ct)) return ReferenceOpResult.NameCollision;
        context.WashLocations.Add(new WashLocation { Name = name, Notes = notes });
        await context.SaveChangesAsync(ct);
        return ReferenceOpResult.Ok;
    }

    public async Task<ReferenceOpResult> UpdateWashLocationAsync(string name, string? newName, string? notes, CancellationToken ct = default)
    {
        var location = await context.WashLocations.SingleOrDefaultAsync(w => w.Name == name, ct);
        if (location is null) return ReferenceOpResult.NotFound;

        var rename = newName is not null && newName != name;
        if (rename && await context.WashLocations.AnyAsync(w => w.Name == newName, ct)) return ReferenceOpResult.NameCollision;

        var newNotes = notes ?? location.Notes;

        if (!rename)
        {
            location.Notes = newNotes;
            await context.SaveChangesAsync(ct);
            return ReferenceOpResult.Ok;
        }

        await InTransactionAsync(async () =>
        {
            context.WashLocations.Add(new WashLocation { Name = newName!, Notes = newNotes });
            await context.SaveChangesAsync(ct);
            await context.WashEntries.Where(w => w.Location == name).ExecuteUpdateAsync(u => u.SetProperty(w => w.Location, newName), ct);
            await context.WashLocations.Where(w => w.Name == name).ExecuteDeleteAsync(ct);
        }, ct);

        return ReferenceOpResult.Ok;
    }

    public async Task<ReferenceOpResult> DeleteWashLocationAsync(string name, string? rehomeTo, CancellationToken ct = default)
    {
        if (!await context.WashLocations.AnyAsync(w => w.Name == name, ct)) return ReferenceOpResult.NotFound;

        var references = await CountWashReferencesAsync(name, ct);

        if (references == 0 && rehomeTo is null)
        {
            context.WashLocations.Remove(await context.WashLocations.SingleAsync(w => w.Name == name, ct));
            await context.SaveChangesAsync(ct);
            return ReferenceOpResult.Ok;
        }

        if (rehomeTo is null) return ReferenceOpResult.Referenced(references);

        if (rehomeTo == name || !await context.WashLocations.AnyAsync(w => w.Name == rehomeTo, ct)) return ReferenceOpResult.BadRehomeTarget;

        await InTransactionAsync(async () =>
        {
            await context.WashEntries.Where(w => w.Location == name).ExecuteUpdateAsync(u => u.SetProperty(w => w.Location, rehomeTo), ct);
            await context.WashLocations.Where(w => w.Name == name).ExecuteDeleteAsync(ct);
        }, ct);

        return ReferenceOpResult.Ok;
    }

    // ---- Expense categories -------------------------------------------------------------------------------

    private async Task<int> CountCategoryReferencesAsync(string name, CancellationToken ct) =>
        await context.ExpenseEntries.CountAsync(e => e.Category == name, ct)
        + await context.BudgetCategories.CountAsync(b => b.Category == name, ct);

    public async Task<IReadOnlyList<ExpenseCategoryRef>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var categories = await context.ExpenseCategories.OrderBy(c => c.DisplayOrder).ToListAsync(ct);
        var result = new List<ExpenseCategoryRef>(categories.Count);
        foreach (var c in categories)
        {
            result.Add(new ExpenseCategoryRef(
                c.Name,
                IsMirrorOnly: c.Name == FuelEntryFactory.FuelCategory,
                IsSystem: c.IsSystem,
                ReferenceCount: await CountCategoryReferencesAsync(c.Name, ct)));
        }

        return result;
    }

    public async Task<ReferenceOpResult> UpdateCategoryAsync(string name, string? newName, int? displayOrder, CancellationToken ct = default)
    {
        var category = await context.ExpenseCategories.SingleOrDefaultAsync(c => c.Name == name, ct);
        if (category is null) return ReferenceOpResult.NotFound;

        var rename = newName is not null && newName != name;

        // Fuel is rename-locked: the mirror resolves it by the exact constant, so a renamed Fuel would silently
        // stop filing fills — the £163.16 gap re-opened from the reference side.
        if (rename && name == FuelEntryFactory.FuelCategory) return ReferenceOpResult.FuelRenameLocked;
        if (rename && await context.ExpenseCategories.AnyAsync(c => c.Name == newName, ct)) return ReferenceOpResult.NameCollision;

        var newOrder = displayOrder ?? category.DisplayOrder;

        if (!rename)
        {
            category.DisplayOrder = newOrder;
            await context.SaveChangesAsync(ct);
            return ReferenceOpResult.Ok;
        }

        await InTransactionAsync(async () =>
        {
            context.ExpenseCategories.Add(new ExpenseCategory { Name = newName!, DisplayOrder = newOrder, IsSystem = category.IsSystem });
            await context.SaveChangesAsync(ct);
            await context.ExpenseEntries.Where(e => e.Category == name).ExecuteUpdateAsync(u => u.SetProperty(e => e.Category, newName), ct);
            await context.BudgetCategories.Where(b => b.Category == name).ExecuteUpdateAsync(u => u.SetProperty(b => b.Category, newName), ct);
            await context.ExpenseCategories.Where(c => c.Name == name).ExecuteDeleteAsync(ct);
        }, ct);

        return ReferenceOpResult.Ok;
    }

    public async Task<ReferenceOpResult> DeleteCategoryAsync(string name, string? rehomeTo, CancellationToken ct = default)
    {
        var category = await context.ExpenseCategories.SingleOrDefaultAsync(c => c.Name == name, ct);
        if (category is null) return ReferenceOpResult.NotFound;

        // Seeded categories are undeletable (Fuel most of all — never even offered by the UI).
        if (category.IsSystem) return ReferenceOpResult.SystemLocked;

        var references = await CountCategoryReferencesAsync(name, ct);

        if (references == 0 && rehomeTo is null)
        {
            context.ExpenseCategories.Remove(category);
            await context.SaveChangesAsync(ct);
            return ReferenceOpResult.Ok;
        }

        if (rehomeTo is null) return ReferenceOpResult.Referenced(references);

        if (rehomeTo == name || !await context.ExpenseCategories.AnyAsync(c => c.Name == rehomeTo, ct)) return ReferenceOpResult.BadRehomeTarget;

        await InTransactionAsync(async () =>
        {
            await context.ExpenseEntries.Where(e => e.Category == name).ExecuteUpdateAsync(u => u.SetProperty(e => e.Category, rehomeTo), ct);
            await context.BudgetCategories.Where(b => b.Category == name).ExecuteUpdateAsync(u => u.SetProperty(b => b.Category, rehomeTo), ct);
            await context.ExpenseCategories.Where(c => c.Name == name).ExecuteDeleteAsync(ct);
        }, ct);

        return ReferenceOpResult.Ok;
    }

    // ---- Shared -------------------------------------------------------------------------------------------

    /// <summary>
    /// Run a multi-row edit inside the retrying execution strategy's transaction — required because Aspire's
    /// enrichment installs a retrying strategy that refuses a user-initiated transaction outside it.
    /// </summary>
    private async Task InTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            await work();
            await transaction.CommitAsync(ct);
        });
    }
}
