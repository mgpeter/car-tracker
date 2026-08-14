using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The edit/remove half of the reference lists, against a real database — the composite keys, the owner
/// cascade and the multi-statement rename transaction are all schema behaviour the in-memory provider ignores.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReferenceListEditorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    // The one account these tests act as. Reference rows and the 13 categories belong to it now, so every seed
    // here stamps it — there is no ownerless row to create any more.
    private int _ownerId;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    // The editor acts as an account: a create stamps it, and the isolation these tests do not exercise is
    // ReferenceListCrossTenantTests' subject. One owner here, so the reads are the same either way.
    private ReferenceListEditor Editor(CarTrackerDbContext context) => new(context, TestOwner.As(_ownerId));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_refedit");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedVehicleAsync(CarTrackerDbContext context, string reg)
    {
        var vehicle = new Vehicle
        {
            Registration = reg, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, FuelType = FuelType.Petrol,
            Source = EntrySource.Web, OwnerId = _ownerId,
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle.Id;
    }

    // ---- Garages ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_a_referenced_garage_is_refused_with_the_count_and_leaves_the_records()
    {
        await using (var setup = NewContext())
        {
            var vid = await SeedVehicleAsync(setup, "REF 1G");
            setup.Garages.Add(new Garage { OwnerId = _ownerId, Name = "K & P Motors" });
            setup.ServiceRecords.Add(new ServiceRecord { VehicleId = vid, ServiceDate = new DateOnly(2026, 7, 8), Mileage = 80_000, Type = "MOT", Garage = "K & P Motors", Source = EntrySource.Web });
            setup.ServiceRecords.Add(new ServiceRecord { VehicleId = vid, ServiceDate = new DateOnly(2026, 6, 1), Mileage = 79_000, Type = "Service", Garage = "K & P Motors", Source = EntrySource.Web });
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var result = await Editor(context).DeleteGarageAsync("K & P Motors", rehomeTo: null);

        Assert.Equal(ReferenceOpStatus.Referenced, result.Status);
        Assert.Equal(2, result.ReferenceCount);

        // Refused, not partly applied: the garage and both records survive with the reference intact. The count
        // is now the only thing standing between a delete and two records naming a garage that is gone — the
        // SetNull FK that used to blank them instead was dropped with the per-owner reference lists.
        await using var reader = NewContext();
        Assert.True(await reader.Garages.AnyAsync(g => g.Name == "K & P Motors"));
        Assert.Equal(2, await reader.ServiceRecords.CountAsync(s => s.Garage == "K & P Motors"));
    }

    [Fact]
    public async Task Rehoming_repoints_the_records_and_removes_the_garage()
    {
        await using (var setup = NewContext())
        {
            var vid = await SeedVehicleAsync(setup, "REF 2G");
            setup.Garages.Add(new Garage { OwnerId = _ownerId, Name = "Old Garage" });
            setup.Garages.Add(new Garage { OwnerId = _ownerId, Name = "New Garage" });
            setup.ServiceRecords.Add(new ServiceRecord { VehicleId = vid, ServiceDate = new DateOnly(2026, 7, 8), Mileage = 80_000, Type = "MOT", Garage = "Old Garage", Source = EntrySource.Web });
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var result = await Editor(context).DeleteGarageAsync("Old Garage", rehomeTo: "New Garage");

        Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        await using var reader = NewContext();
        Assert.False(await reader.Garages.AnyAsync(g => g.Name == "Old Garage"));
        Assert.Equal(1, await reader.ServiceRecords.CountAsync(s => s.Garage == "New Garage"));
        Assert.Equal(0, await reader.ServiceRecords.CountAsync(s => s.Garage == "Old Garage"));
    }

    [Fact]
    public async Task Renaming_a_garage_cascades_to_the_referencing_rows()
    {
        int vid;
        await using (var setup = NewContext())
        {
            vid = await SeedVehicleAsync(setup, "REF 3G");
            setup.Garages.Add(new Garage { OwnerId = _ownerId, Name = "K & P Motors", Contact = "0123" });
            setup.ServiceRecords.Add(new ServiceRecord { VehicleId = vid, ServiceDate = new DateOnly(2026, 7, 8), Mileage = 80_000, Type = "MOT", Garage = "K & P Motors", Source = EntrySource.Web });
            var v = await setup.Vehicles.SingleAsync(x => x.Id == vid);
            v.DefaultGarage = "K & P Motors";
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var result = await Editor(context).UpdateGarageAsync("K & P Motors", newName: "K&P Motors", contact: null, address: null, notes: null);

        Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        await using var reader = NewContext();
        Assert.False(await reader.Garages.AnyAsync(g => g.Name == "K & P Motors"));
        // The new row carries the old fields, and both referencing columns followed the rename.
        Assert.Equal("0123", (await reader.Garages.SingleAsync(g => g.Name == "K&P Motors")).Contact);
        Assert.Equal(1, await reader.ServiceRecords.CountAsync(s => s.Garage == "K&P Motors"));
        Assert.Equal("K&P Motors", (await reader.Vehicles.SingleAsync(x => x.Id == vid)).DefaultGarage);
    }

    [Fact]
    public async Task An_unreferenced_garage_deletes_cleanly()
    {
        await using (var setup = NewContext())
        {
            setup.Garages.Add(new Garage { OwnerId = _ownerId, Name = "Never Used" });
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var result = await Editor(context).DeleteGarageAsync("Never Used", rehomeTo: null);

        Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        await using var reader = NewContext();
        Assert.False(await reader.Garages.AnyAsync(g => g.Name == "Never Used"));
    }

    // ---- Expense categories -------------------------------------------------------------------------------

    [Fact]
    public async Task The_fuel_category_cannot_be_deleted_or_renamed()
    {
        await using var context = NewContext();
        var editor = Editor(context);

        Assert.Equal(ReferenceOpStatus.SystemLocked, (await editor.DeleteCategoryAsync("Fuel", rehomeTo: null)).Status);
        Assert.Equal(ReferenceOpStatus.MirrorRenameLocked, (await editor.UpdateCategoryAsync("Fuel", newName: "Petrol", displayOrder: null)).Status);

        // Purchase is locked for the same reason: VehiclePurchaseMirror resolves it by the exact constant, and a
        // rename would drop the car out of the purchase/running-cost split.
        Assert.Equal(ReferenceOpStatus.MirrorRenameLocked, (await editor.UpdateCategoryAsync("Purchase", newName: "Bought", displayOrder: null)).Status);

        await using var reader = NewContext();
        Assert.True(await reader.ExpenseCategories.AnyAsync(c => c.Name == "Fuel"));
    }

    [Fact]
    public async Task A_referenced_custom_category_refuses_delete_then_re_homes()
    {
        await using (var setup = NewContext())
        {
            var vid = await SeedVehicleAsync(setup, "REF 1C");
            setup.ExpenseCategories.Add(new ExpenseCategory { OwnerId = _ownerId, Name = "Detailing", DisplayOrder = 20, IsSystem = false });
            setup.ExpenseEntries.Add(new ExpenseEntry { VehicleId = vid, EntryDate = new DateOnly(2026, 7, 1), Category = "Detailing", Amount = 40m, Source = EntrySource.Web });
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var editor = Editor(context);

        var blocked = await editor.DeleteCategoryAsync("Detailing", rehomeTo: null);
        Assert.Equal(ReferenceOpStatus.Referenced, blocked.Status);
        Assert.Equal(1, blocked.ReferenceCount);

        var rehomed = await editor.DeleteCategoryAsync("Detailing", rehomeTo: "Wash");
        Assert.Equal(ReferenceOpStatus.Ok, rehomed.Status);

        await using var reader = NewContext();
        Assert.False(await reader.ExpenseCategories.AnyAsync(c => c.Name == "Detailing"));
        Assert.Equal(1, await reader.ExpenseEntries.CountAsync(e => e.Category == "Wash"));
    }

    [Fact]
    public async Task A_non_system_category_with_no_entries_deletes()
    {
        await using (var setup = NewContext())
        {
            setup.ExpenseCategories.Add(new ExpenseCategory { OwnerId = _ownerId, Name = "Mistake", DisplayOrder = 30, IsSystem = false });
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var result = await Editor(context).DeleteCategoryAsync("Mistake", rehomeTo: null);

        Assert.Equal(ReferenceOpStatus.Ok, result.Status);
        await using var reader = NewContext();
        Assert.False(await reader.ExpenseCategories.AnyAsync(c => c.Name == "Mistake"));
    }

    [Fact]
    public async Task Listing_categories_reports_system_and_reference_counts()
    {
        await using (var setup = NewContext())
        {
            var vid = await SeedVehicleAsync(setup, "REF 2C");
            setup.ExpenseEntries.Add(new ExpenseEntry { VehicleId = vid, EntryDate = new DateOnly(2026, 7, 1), Category = "Repair", Amount = 100m, Source = EntrySource.Web });
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var categories = await Editor(context).ListCategoriesAsync();

        var fuel = categories.Single(c => c.Name == "Fuel");
        Assert.True(fuel.IsSystem);
        Assert.True(fuel.IsMirrorOnly);
        Assert.Equal(1, categories.Single(c => c.Name == "Repair").ReferenceCount);
    }
}
