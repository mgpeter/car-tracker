using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ChecksAndTasksTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    private CarTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>()
                .UseNpgsql(_connectionString)
                .Options,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero)));

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_schema");
        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedVehicleAsync(string registration)
    {
        await using var context = NewContext();
        var vehicle = new Vehicle
        {
            Registration = registration,
            Make = "Land Rover",
            Model = "Freelander 1",
            Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14),
            PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol,
            Source = EntrySource.Web,
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle.Id;
    }

    private static MaintenanceTask NewTask(int vehicleId, MaintenanceTaskKind kind) => new()
    {
        VehicleId = vehicleId,
        Kind = kind,
        Priority = Priority.Medium,
        Title = "Test task",
        Status = MaintenanceTaskStatus.Open,
        Source = EntrySource.Web,
    };

    [Fact]
    public async Task A_DIY_task_cannot_carry_a_garage()
    {
        var vehicleId = await SeedVehicleAsync("CT1 AAA");

        await using var context = NewContext();
        context.Garages.Add(new Garage { Name = "CT Garage One" });
        await context.SaveChangesAsync();

        var task = NewTask(vehicleId, MaintenanceTaskKind.DIY);
        task.AssignedGarage = "CT Garage One";
        context.MaintenanceTasks.Add(task);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_Done_task_requires_a_completed_date_and_vice_versa()
    {
        var vehicleId = await SeedVehicleAsync("CT2 BBB");

        await using (var context = NewContext())
        {
            var doneWithoutDate = NewTask(vehicleId, MaintenanceTaskKind.DIY);
            doneWithoutDate.Status = MaintenanceTaskStatus.Done;
            context.MaintenanceTasks.Add(doneWithoutDate);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = NewContext())
        {
            var openWithDate = NewTask(vehicleId, MaintenanceTaskKind.DIY);
            openWithDate.CompletedDate = new DateOnly(2026, 7, 1);
            context.MaintenanceTasks.Add(openWithDate);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = NewContext())
        {
            var coherent = NewTask(vehicleId, MaintenanceTaskKind.Workshop);
            coherent.Status = MaintenanceTaskStatus.Done;
            coherent.CompletedDate = new DateOnly(2026, 7, 1);
            context.MaintenanceTasks.Add(coherent);
            Assert.Equal(1, await context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_never_logged_check_is_queryable_and_distinct_from_a_logged_one()
    {
        var vehicleId = await SeedVehicleAsync("CT3 CCC");

        await using (var context = NewContext())
        {
            var oilCap = new CheckDefinition
            {
                VehicleId = vehicleId,
                Name = "Oil filler cap (mayo residue)",
                CadenceLabel = "Weekly",
                IntervalDays = 7,
                DisplayOrder = 1,
                Source = EntrySource.Import,
            };
            var spare = new CheckDefinition
            {
                VehicleId = vehicleId,
                Name = "Spare tyre pressure",
                CadenceLabel = "Monthly",
                IntervalDays = 30,
                DisplayOrder = 2,
                Source = EntrySource.Import,
            };
            context.CheckDefinitions.AddRange(oilCap, spare);
            await context.SaveChangesAsync();

            context.CheckLogs.Add(new CheckLog
            {
                CheckDefinitionId = oilCap.Id,
                PerformedOn = new DateOnly(2026, 6, 18),
                Result = CheckResult.OK,
                Source = EntrySource.Import,
            });
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            var neverLogged = await reader.CheckDefinitions
                .Where(d => d.VehicleId == vehicleId)
                .Where(d => !reader.CheckLogs.Any(l => l.CheckDefinitionId == d.Id))
                .Select(d => d.Name)
                .ToListAsync();

            Assert.Equal(["Spare tyre pressure"], neverLogged);
        }
    }

    [Fact]
    public async Task Check_definitions_carry_no_status_or_next_due_column()
    {
        await using var context = NewContext();

        var columns = await context.Database
            .SqlQuery<string>($"SELECT column_name FROM information_schema.columns WHERE table_name = 'check_definitions'")
            .ToListAsync();

        Assert.DoesNotContain("status", columns);
        Assert.DoesNotContain("next_due", columns);
        Assert.DoesNotContain("next_due_date", columns);
    }

    [Fact]
    public async Task Deleting_a_linked_issue_severs_the_document_link_but_keeps_the_document()
    {
        var vehicleId = await SeedVehicleAsync("CT4 DDD");

        int documentId;
        await using (var context = NewContext())
        {
            var issue = new Issue
            {
                VehicleId = vehicleId,
                Title = "Headlamp lens hazing",
                Severity = Severity.Low,
                FirstNoted = new DateOnly(2026, 7, 8),
                Status = IssueStatus.Monitoring,
                Source = EntrySource.Web,
            };
            context.Issues.Add(issue);
            await context.SaveChangesAsync();

            var document = new Document
            {
                VehicleId = vehicleId,
                Type = DocumentType.Photo,
                Title = "Headlamp condition photos",
                FilePath = "photos/headlamp-2026-07-08.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 1024,
                IssueId = issue.Id,
                Source = EntrySource.Web,
            };
            context.Documents.Add(document);
            await context.SaveChangesAsync();
            documentId = document.Id;

            context.Issues.Remove(issue);
            await context.SaveChangesAsync();
        }

        await using (var reader = NewContext())
        {
            var survivor = await reader.Documents.SingleAsync(d => d.Id == documentId);
            Assert.Null(survivor.IssueId);
        }
    }
}
