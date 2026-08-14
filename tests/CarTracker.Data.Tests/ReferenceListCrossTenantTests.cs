using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Per-account isolation of the reference lists — the defect task 1 of
/// <c>docs/specs/2026-08-11-pre-public-release-gates</c> proved, and the guarantee that closes it.
/// </summary>
/// <remarks>
/// <para>
/// Written first and watched red. Before the fix, <c>Garage</c>, <c>WashLocation</c> and
/// <c>ExpenseCategory</c> were keyed on <c>Name</c> alone, so two accounts could not each hold a
/// "K &amp; P Motors" — the second person to type it silently adopted the first one's row, address and contact
/// included — and every statement in <see cref="ReferenceListEditor"/> matched a bare name against an
/// unfiltered table. One account's rename rewrote every other account's service records, tasks, default garage,
/// wash entries, expenses and budget memberships, and one account's reference count aggregated everybody's rows.
/// </para>
/// <para>
/// Both halves are asserted here, because either alone is worthless: the rows are keyed
/// <c>(OwnerId, Name)</c> so B <i>has</i> a row of their own to leave alone, and the statements are scoped so A's
/// edit does not reach it. Every test therefore seeds <b>two</b> rows of the same name and checks B's survives
/// whole — the shape a single shared row could not express.
/// </para>
/// <para>
/// Contexts are built through <see cref="TestOwner.As"/>, and that is load-bearing: a context with no accessor
/// bypasses ownership, which makes both the query filters and the correlated <c>Vehicles.Any()</c> match every
/// row, and every assertion below would pass without isolating anything.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ReferenceListCrossTenantTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _clock, accessor);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_refcrosstenant");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>The editor as one account sees it — context and writer pinned to the same accessor, as DI does.</summary>
    private (CarTrackerDbContext Context, ReferenceListEditor Editor) EditorFor(int ownerId)
    {
        var accessor = TestOwner.As(ownerId);
        var context = NewContext(accessor);
        return (context, new ReferenceListEditor(context, accessor));
    }

    private static async Task<int> SeedVehicleAsync(CarTrackerDbContext context, string reg, int ownerId)
    {
        var vehicle = new Vehicle
        {
            Registration = reg,
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
            OwnerId = ownerId,
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle.Id;
    }

    private static ServiceRecord ServiceRecordAt(int vehicleId, string garage, int mileage) => new()
    {
        VehicleId = vehicleId,
        ServiceDate = new DateOnly(2026, 7, 8),
        Mileage = mileage,
        Type = "MOT",
        Garage = garage,
        Source = EntrySource.Web,
    };

    private static MaintenanceTask WorkshopTaskAt(int vehicleId, string garage, string title) => new()
    {
        VehicleId = vehicleId,
        Kind = MaintenanceTaskKind.Workshop,
        Priority = Priority.Medium,
        Title = title,
        Status = MaintenanceTaskStatus.Open,
        AssignedGarage = garage,
        Source = EntrySource.Web,
    };

    // ---- 1. Garage rename ------------------------------------------------------------------------------------

    [Fact]
    public async Task Owner_A_renaming_a_garage_leaves_owner_Bs_rows_alone()
    {
        int ownerA, ownerB, aVehicleId, bVehicleId;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-garage-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-garage-B");
            aVehicleId = await SeedVehicleAsync(seed, "XG11 AAA", ownerA);
            bVehicleId = await SeedVehicleAsync(seed, "XG22 BBB", ownerB);

            // Two rows, one name — the shape the composite key exists to allow, and each with its own contact so
            // an adopted row would be visible rather than merely suspected.
            seed.Garages.Add(new Garage { OwnerId = ownerA, Name = "K & P Motors", Contact = "01234 567890" });
            seed.Garages.Add(new Garage { OwnerId = ownerB, Name = "K & P Motors", Contact = "01999 111222" });
            seed.ServiceRecords.Add(ServiceRecordAt(aVehicleId, "K & P Motors", 80_000));
            seed.ServiceRecords.Add(ServiceRecordAt(bVehicleId, "K & P Motors", 42_000));
            seed.MaintenanceTasks.Add(WorkshopTaskAt(bVehicleId, "K & P Motors", "B's cambelt quote"));

            var bVehicle = await seed.Vehicles.SingleAsync(v => v.Id == bVehicleId);
            bVehicle.DefaultGarage = "K & P Motors";
            await seed.SaveChangesAsync();
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using (contextA)
        {
            var result = await editorA.UpdateGarageAsync("K & P Motors", newName: "K&P Motors", contact: null, address: null, notes: null);
            Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        }

        await using var reader = NewContext();

        // A's own record follows the rename, as it should.
        Assert.Equal("K&P Motors", (await reader.ServiceRecords.SingleAsync(s => s.VehicleId == aVehicleId)).Garage);

        // B never asked for anything. B's three references must be exactly as B left them. Asserted as one
        // value so a failure reports all three actuals — they do not fail the same way, and the difference
        // between them is the whole shape of the defect.
        var bRows = new[]
        {
            "service_records.garage=" + ((await reader.ServiceRecords.SingleAsync(s => s.VehicleId == bVehicleId)).Garage ?? "<null>"),
            "maintenance_tasks.assigned_garage=" + ((await reader.MaintenanceTasks.SingleAsync(t => t.VehicleId == bVehicleId)).AssignedGarage ?? "<null>"),
            "vehicles.default_garage=" + ((await reader.Vehicles.IgnoreQueryFilters().SingleAsync(v => v.Id == bVehicleId)).DefaultGarage ?? "<null>"),
        };

        Assert.Equal(
            [
                "service_records.garage=K & P Motors",
                "maintenance_tasks.assigned_garage=K & P Motors",
                "vehicles.default_garage=K & P Motors",
            ],
            bRows);

        // And B's list entry itself survives the rename that dropped A's, still holding B's contact.
        Assert.Equal("01999 111222", (await reader.Garages.SingleAsync(g => g.OwnerId == ownerB && g.Name == "K & P Motors")).Contact);
        Assert.False(await reader.Garages.AnyAsync(g => g.OwnerId == ownerA && g.Name == "K & P Motors"));
    }

    // ---- 2. Wash location rename -----------------------------------------------------------------------------

    [Fact]
    public async Task Owner_A_renaming_a_wash_location_leaves_owner_Bs_wash_entries_alone()
    {
        int ownerA, ownerB, aVehicleId, bVehicleId;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-wash-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-wash-B");
            aVehicleId = await SeedVehicleAsync(seed, "XW11 AAA", ownerA);
            bVehicleId = await SeedVehicleAsync(seed, "XW22 BBB", ownerB);

            seed.WashLocations.Add(new WashLocation { OwnerId = ownerA, Name = "Sparkle Hand Wash" });
            seed.WashLocations.Add(new WashLocation { OwnerId = ownerB, Name = "Sparkle Hand Wash" });
            seed.WashEntries.Add(new WashEntry { VehicleId = aVehicleId, WashDate = new DateOnly(2026, 7, 1), Location = "Sparkle Hand Wash", Source = EntrySource.Web });
            seed.WashEntries.Add(new WashEntry { VehicleId = bVehicleId, WashDate = new DateOnly(2026, 7, 2), Location = "Sparkle Hand Wash", Source = EntrySource.Web });
            await seed.SaveChangesAsync();
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using (contextA)
        {
            var result = await editorA.UpdateWashLocationAsync("Sparkle Hand Wash", newName: "Sparkle Handwash", notes: null);
            Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        }

        await using var reader = NewContext();

        Assert.Equal("Sparkle Handwash", (await reader.WashEntries.SingleAsync(w => w.VehicleId == aVehicleId)).Location);
        Assert.Equal("Sparkle Hand Wash", (await reader.WashEntries.SingleAsync(w => w.VehicleId == bVehicleId)).Location);
        Assert.True(await reader.WashLocations.AnyAsync(w => w.OwnerId == ownerB && w.Name == "Sparkle Hand Wash"));
    }

    // ---- 3. Expense category rename --------------------------------------------------------------------------

    [Fact]
    public async Task Owner_A_renaming_a_category_leaves_owner_Bs_expenses_and_budget_membership_alone()
    {
        int ownerA, ownerB, aVehicleId, bVehicleId;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-cat-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-cat-B");
            aVehicleId = await SeedVehicleAsync(seed, "XC11 AAA", ownerA);
            bVehicleId = await SeedVehicleAsync(seed, "XC22 BBB", ownerB);

            // A non-system, non-mirror-owned category, so the rename is permitted rather than locked.
            seed.ExpenseCategories.Add(new ExpenseCategory { OwnerId = ownerA, Name = "Detailing", DisplayOrder = 20, IsSystem = false });
            seed.ExpenseCategories.Add(new ExpenseCategory { OwnerId = ownerB, Name = "Detailing", DisplayOrder = 20, IsSystem = false });
            seed.ExpenseEntries.Add(new ExpenseEntry { VehicleId = aVehicleId, EntryDate = new DateOnly(2026, 7, 1), Category = "Detailing", Amount = 40m, Source = EntrySource.Web });
            seed.ExpenseEntries.Add(new ExpenseEntry { VehicleId = bVehicleId, EntryDate = new DateOnly(2026, 7, 2), Category = "Detailing", Amount = 55m, Source = EntrySource.Web });

            var aGroup = new BudgetGroup { VehicleId = aVehicleId, Name = "Cleaning", DisplayOrder = 1, Source = EntrySource.Web };
            var bGroup = new BudgetGroup { VehicleId = bVehicleId, Name = "Cleaning", DisplayOrder = 1, Source = EntrySource.Web };
            aGroup.Categories.Add(new BudgetGroupCategory { VehicleId = aVehicleId, Category = "Detailing" });
            bGroup.Categories.Add(new BudgetGroupCategory { VehicleId = bVehicleId, Category = "Detailing" });
            seed.BudgetGroups.AddRange(aGroup, bGroup);

            await seed.SaveChangesAsync();
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using (contextA)
        {
            var result = await editorA.UpdateCategoryAsync("Detailing", newName: "Valeting", displayOrder: null);
            Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        }

        await using var reader = NewContext();

        Assert.Equal("Valeting", (await reader.ExpenseEntries.SingleAsync(e => e.VehicleId == aVehicleId)).Category);
        Assert.Equal("Valeting", (await reader.BudgetGroupCategories.SingleAsync(b => b.VehicleId == aVehicleId)).Category);

        var bRows = new[]
        {
            "expense_entries.category=" + (await reader.ExpenseEntries.SingleAsync(e => e.VehicleId == bVehicleId)).Category,
            "budget_group_categories.category=" + (await reader.BudgetGroupCategories.SingleAsync(b => b.VehicleId == bVehicleId)).Category,
        };

        Assert.Equal(
            ["expense_entries.category=Detailing", "budget_group_categories.category=Detailing"],
            bRows);

        Assert.True(await reader.ExpenseCategories.AnyAsync(c => c.OwnerId == ownerB && c.Name == "Detailing"));
    }

    // ---- 4. Reference counts ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_garage_reference_count_covers_only_the_callers_own_rows()
    {
        int ownerA;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-count-A");
            var ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-count-B");
            var aVehicleId = await SeedVehicleAsync(seed, "XN11 AAA", ownerA);
            var bVehicleId = await SeedVehicleAsync(seed, "XN22 BBB", ownerB);

            seed.Garages.Add(new Garage { OwnerId = ownerA, Name = "Shared Bodyshop" });
            seed.Garages.Add(new Garage { OwnerId = ownerB, Name = "Shared Bodyshop" });

            // A: one service record. B: two service records and a workshop task.
            seed.ServiceRecords.Add(ServiceRecordAt(aVehicleId, "Shared Bodyshop", 80_100));
            seed.ServiceRecords.Add(ServiceRecordAt(bVehicleId, "Shared Bodyshop", 42_100));
            seed.ServiceRecords.Add(ServiceRecordAt(bVehicleId, "Shared Bodyshop", 42_200));
            seed.MaintenanceTasks.Add(WorkshopTaskAt(bVehicleId, "Shared Bodyshop", "B's respray"));
            await seed.SaveChangesAsync();
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using var _ = contextA;
        var garages = await editorA.ListGaragesAsync();

        // A has exactly one row pointing at it. B's three are none of A's business — and B's list entry of the
        // same name is not in A's list at all.
        Assert.Equal(1, garages.Single(g => g.Name == "Shared Bodyshop").ReferenceCount);
    }

    [Fact]
    public async Task A_category_reference_count_covers_only_the_callers_own_rows()
    {
        int ownerA;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-catcount-A");
            var ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-catcount-B");
            var aVehicleId = await SeedVehicleAsync(seed, "XK11 AAA", ownerA);
            var bVehicleId = await SeedVehicleAsync(seed, "XK22 BBB", ownerB);

            seed.ExpenseCategories.Add(new ExpenseCategory { OwnerId = ownerA, Name = "Track days", DisplayOrder = 21, IsSystem = false });
            seed.ExpenseCategories.Add(new ExpenseCategory { OwnerId = ownerB, Name = "Track days", DisplayOrder = 21, IsSystem = false });
            seed.ExpenseEntries.Add(new ExpenseEntry { VehicleId = aVehicleId, EntryDate = new DateOnly(2026, 7, 1), Category = "Track days", Amount = 120m, Source = EntrySource.Web });
            seed.ExpenseEntries.Add(new ExpenseEntry { VehicleId = bVehicleId, EntryDate = new DateOnly(2026, 7, 2), Category = "Track days", Amount = 130m, Source = EntrySource.Web });
            seed.ExpenseEntries.Add(new ExpenseEntry { VehicleId = bVehicleId, EntryDate = new DateOnly(2026, 7, 3), Category = "Track days", Amount = 140m, Source = EntrySource.Web });

            var bGroup = new BudgetGroup { VehicleId = bVehicleId, Name = "Fun", DisplayOrder = 2, Source = EntrySource.Web };
            bGroup.Categories.Add(new BudgetGroupCategory { VehicleId = bVehicleId, Category = "Track days" });
            seed.BudgetGroups.Add(bGroup);

            await seed.SaveChangesAsync();
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using var _ = contextA;
        var categories = await editorA.ListCategoriesAsync();

        Assert.Equal(1, categories.Single(c => c.Name == "Track days").ReferenceCount);
    }

    // ---- 5. The list each account is provisioned with ---------------------------------------------------------

    [Fact]
    public async Task Each_account_holds_its_own_thirteen_categories_with_Fuel_and_Purchase_locked()
    {
        int ownerA, ownerB;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-system-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-system-B");
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using (contextA)
        {
            // The 13 are per account now rather than migration seed data, so "the founding list" has to mean the
            // same thing to every account — thirteen rows, this account's, not the union of everyone's.
            var categories = await editorA.ListCategoriesAsync();
            Assert.Equal(13, categories.Count);
            Assert.All(categories, c => Assert.True(c.IsSystem));

            // Undeletable and rename-locked hold per account, for the reason they always did: the mirrors resolve
            // Fuel and Purchase by the exact constant, and the constant did not change when the row gained an owner.
            Assert.Equal(ReferenceOpStatus.SystemLocked, (await editorA.DeleteCategoryAsync("Fuel", rehomeTo: null)).Status);
            Assert.Equal(ReferenceOpStatus.SystemLocked, (await editorA.DeleteCategoryAsync("Purchase", rehomeTo: null)).Status);
            Assert.Equal(ReferenceOpStatus.MirrorRenameLocked, (await editorA.UpdateCategoryAsync("Fuel", newName: "Petrol", displayOrder: null)).Status);
            Assert.Equal(ReferenceOpStatus.MirrorRenameLocked, (await editorA.UpdateCategoryAsync("Purchase", newName: "Bought", displayOrder: null)).Status);
        }

        // B is untouched by A having tried: thirteen rows, Fuel among them.
        var (contextB, editorB) = EditorFor(ownerB);
        await using var _ = contextB;
        var bCategories = await editorB.ListCategoriesAsync();
        Assert.Equal(13, bCategories.Count);
        Assert.Contains(bCategories, c => c.Name == "Fuel");
    }

    // ---- 6. Creating a reference row with no account behind it -------------------------------------------------

    /// <remarks>
    /// <see cref="ReferenceWriter"/> probed for the name before resolving the owner, and under a bypass context
    /// — a background job, a design-time tool, a mis-wired caller — the probe reads through no filter at all.
    /// So <i>anybody's</i> row of that name answered it, the method returned having created nothing, and the
    /// exception <c>ReferenceOwner</c> exists to raise never fired on the one context that needs it. Silence
    /// where a diagnosis belongs, and only for names some other account happens to have used, which is the
    /// worst shape a bug of this kind can take.
    /// </remarks>
    [Fact]
    public async Task Creating_a_reference_row_with_no_account_behind_it_refuses_rather_than_doing_nothing()
    {
        await using (var seed = NewContext())
        {
            var ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-writer-A");
            seed.Garages.Add(new Garage { OwnerId = ownerA, Name = "Somebody Else's Garage" });
            seed.WashLocations.Add(new WashLocation { OwnerId = ownerA, Name = "Somebody Else's Jetwash" });
            await seed.SaveChangesAsync();
        }

        // No accessor at all — BypassOwnership, exactly what a background context has.
        await using var db = NewContext();
        var writer = new ReferenceWriter(db, new CurrentUserAccessor());

        var garage = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.EnsureGarageAsync("Somebody Else's Garage"));
        Assert.Contains("no signed-in account", garage.Message);

        var wash = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.EnsureWashLocationAsync("Somebody Else's Jetwash"));
        Assert.Contains("no signed-in account", wash.Message);

        // Nothing was written under the confusion either — the refusal is the whole outcome.
        await using var reader = NewContext();
        Assert.Equal(1, await reader.Garages.CountAsync(g => g.Name == "Somebody Else's Garage"));
        Assert.Equal(1, await reader.WashLocations.CountAsync(w => w.Name == "Somebody Else's Jetwash"));
    }

    [Fact]
    public async Task Owner_A_deleting_a_custom_category_leaves_owner_Bs_row_of_the_same_name()
    {
        int ownerA, ownerB;
        await using (var seed = NewContext())
        {
            ownerA = await TestOwner.SeedAsync(seed, "auth0|xt-del-A");
            ownerB = await TestOwner.SeedAsync(seed, "auth0|xt-del-B");
            seed.ExpenseCategories.Add(new ExpenseCategory { OwnerId = ownerA, Name = "Ferry", DisplayOrder = 22, IsSystem = false });
            seed.ExpenseCategories.Add(new ExpenseCategory { OwnerId = ownerB, Name = "Ferry", DisplayOrder = 22, IsSystem = false });
            await seed.SaveChangesAsync();
        }

        var (contextA, editorA) = EditorFor(ownerA);
        await using (contextA)
        {
            Assert.Equal(ReferenceOpStatus.Ok, (await editorA.DeleteCategoryAsync("Ferry", rehomeTo: null)).Status);
        }

        await using var reader = NewContext();
        Assert.False(await reader.ExpenseCategories.AnyAsync(c => c.OwnerId == ownerA && c.Name == "Ferry"));
        Assert.True(await reader.ExpenseCategories.AnyAsync(c => c.OwnerId == ownerB && c.Name == "Ferry"));
    }
}
