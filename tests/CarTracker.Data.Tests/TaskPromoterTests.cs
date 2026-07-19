using CarTracker.Domain;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// Promoting a Workshop task to a service record, against a real database — the promotion goes through
/// <see cref="ServiceRecordFactory"/>, whose one-transaction, three-row write only behaves under real Postgres.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TaskPromoterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    private static TaskPromoter Promoter(CarTrackerDbContext context) =>
        new(context, new ServiceRecordFactory(context, new ReferenceWriter(context)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_promote");
        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedVehicleAsync(CarTrackerDbContext context, string reg)
    {
        var vehicle = new Vehicle
        {
            Registration = reg, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle.Id;
    }

    private static MaintenanceTask DoneWorkshop(int vehicleId) => new()
    {
        VehicleId = vehicleId,
        Kind = MaintenanceTaskKind.Workshop,
        Priority = Priority.Medium,
        Title = "Cambelt and water pump",
        Description = "interference engine — overdue",
        EstimatedCost = 603.99m,
        Status = MaintenanceTaskStatus.Done,
        CompletedDate = new DateOnly(2026, 6, 27),
        AssignedGarage = "K & P Motors",
        Source = EntrySource.Web,
    };

    [Fact]
    public async Task Promoting_a_done_workshop_task_creates_the_record_reading_and_mirrored_expense()
    {
        int vid, taskId;
        await using (var setup = NewContext())
        {
            vid = await SeedVehicleAsync(setup, "PRO 1");
            await new ReferenceWriter(setup).EnsureGarageAsync("K & P Motors");
            var task = DoneWorkshop(vid);
            setup.MaintenanceTasks.Add(task);
            await setup.SaveChangesAsync();
            taskId = task.Id;
        }

        PromoteResult result;
        await using (var context = NewContext())
        {
            result = await Promoter(context).PromoteAsync(vid, taskId, mileage: 80_100, type: "Service", cost: null, notes: null, source: EntrySource.Web);
        }

        Assert.Equal(PromoteStatus.Ok, result.Status);

        await using var reader = NewContext();
        var record = await reader.ServiceRecords.SingleAsync(r => r.Id == result.ServiceRecordId);
        Assert.Equal(new DateOnly(2026, 6, 27), record.ServiceDate); // the task's completion date
        Assert.Equal(80_100, record.Mileage);
        Assert.Equal("K & P Motors", record.Garage);
        Assert.Equal(603.99m, record.Cost); // the estimate carried over as the default
        Assert.Contains("Cambelt and water pump", record.WorkDone);
        Assert.Contains("Converted from workshop task", record.Notes);

        // The factory's three rows: the record, its Service-origin reading, and its mirrored expense.
        Assert.True(await reader.MileageReadings.AnyAsync(m => m.VehicleId == vid && m.Mileage == 80_100 && m.Origin == MileageOrigin.Service));
        Assert.True(await reader.ExpenseEntries.AnyAsync(e => e.ServiceRecordId == record.Id && e.Amount == 603.99m));

        // The task now links to the record and stays where it is.
        Assert.Equal(result.ServiceRecordId, (await reader.MaintenanceTasks.SingleAsync(t => t.Id == taskId)).ServiceRecordId);
    }

    [Fact]
    public async Task An_edited_cost_wins_over_the_estimate()
    {
        int vid, taskId;
        await using (var setup = NewContext())
        {
            vid = await SeedVehicleAsync(setup, "PRO 2");
            await new ReferenceWriter(setup).EnsureGarageAsync("K & P Motors");
            var task = DoneWorkshop(vid);
            setup.MaintenanceTasks.Add(task);
            await setup.SaveChangesAsync();
            taskId = task.Id;
        }

        await using var context = NewContext();
        var result = await Promoter(context).PromoteAsync(vid, taskId, mileage: 80_100, type: "Service", cost: 640.00m, notes: null, source: EntrySource.Web);

        await using var reader = NewContext();
        Assert.Equal(640.00m, (await reader.ServiceRecords.SingleAsync(r => r.Id == result.ServiceRecordId)).Cost);
    }

    [Fact]
    public async Task A_non_done_task_is_refused_and_writes_nothing()
    {
        int vid, taskId;
        await using (var setup = NewContext())
        {
            vid = await SeedVehicleAsync(setup, "PRO 3");
            await new ReferenceWriter(setup).EnsureGarageAsync("K & P Motors");
            var task = DoneWorkshop(vid);
            task.Status = MaintenanceTaskStatus.Open;
            task.CompletedDate = null;
            setup.MaintenanceTasks.Add(task);
            await setup.SaveChangesAsync();
            taskId = task.Id;
        }

        await using var context = NewContext();
        var result = await Promoter(context).PromoteAsync(vid, taskId, mileage: 80_100, type: "Service", cost: null, notes: null, source: EntrySource.Web);

        Assert.Equal(PromoteStatus.NotDone, result.Status);
        await using var reader = NewContext();
        Assert.Empty(await reader.ServiceRecords.Where(r => r.VehicleId == vid).ToListAsync());
        Assert.Null((await reader.MaintenanceTasks.SingleAsync(t => t.Id == taskId)).ServiceRecordId);
    }

    [Fact]
    public async Task A_diy_task_is_refused()
    {
        int vid, taskId;
        await using (var setup = NewContext())
        {
            vid = await SeedVehicleAsync(setup, "PRO 4");
            await new ReferenceWriter(setup).EnsureGarageAsync("K & P Motors");
            var task = DoneWorkshop(vid);
            task.Kind = MaintenanceTaskKind.DIY;
            task.AssignedGarage = null; // a DIY task may not carry a garage
            setup.MaintenanceTasks.Add(task);
            await setup.SaveChangesAsync();
            taskId = task.Id;
        }

        await using var context = NewContext();
        var result = await Promoter(context).PromoteAsync(vid, taskId, mileage: 80_100, type: "Service", cost: null, notes: null, source: EntrySource.Web);

        Assert.Equal(PromoteStatus.NotWorkshop, result.Status);
    }

    [Fact]
    public async Task Promoting_twice_is_refused_the_second_time()
    {
        int vid, taskId;
        await using (var setup = NewContext())
        {
            vid = await SeedVehicleAsync(setup, "PRO 5");
            await new ReferenceWriter(setup).EnsureGarageAsync("K & P Motors");
            var task = DoneWorkshop(vid);
            setup.MaintenanceTasks.Add(task);
            await setup.SaveChangesAsync();
            taskId = task.Id;
        }

        await using (var first = NewContext())
        {
            var ok = await Promoter(first).PromoteAsync(vid, taskId, mileage: 80_100, type: "Service", cost: null, notes: null, source: EntrySource.Web);
            Assert.Equal(PromoteStatus.Ok, ok.Status);
        }

        await using var second = NewContext();
        var again = await Promoter(second).PromoteAsync(vid, taskId, mileage: 80_100, type: "Service", cost: null, notes: null, source: EntrySource.Web);

        Assert.Equal(PromoteStatus.AlreadyPromoted, again.Status);
        // Still exactly one record.
        await using var reader = NewContext();
        Assert.Single(await reader.ServiceRecords.Where(r => r.VehicleId == vid).ToListAsync());
    }
}
