using CarTracker.Domain;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.ModelContextProtocol.Tools;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The write tools stamp the surface they were invoked on, not a constant.
/// </summary>
/// <remarks>
/// <para>
/// The tools used to hold <c>private const EntrySource Source = EntrySource.Mcp</c>, which was correct while
/// `/mcp` was the only caller. The in-app chat invokes the same methods, so the constant became a lie waiting to
/// be told: every chat-drafted row would have claimed to be an unattended MCP write. These tests are the ones
/// that would have failed.
/// </para>
/// <para>
/// Invoked directly rather than through a transport, because what is being asserted is the tool's contract with
/// the domain — that it passes the surface through to the service that stamps the row.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class McpWriteSurfaceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;
    private int _ownerId;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time, accessor);

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_write_surface");

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();
        _ownerId = await TestOwner.SeedAsync(seed, "test|surface-owner");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> EnsureVehicleAsync(CarTrackerDbContext context)
    {
        var existing = await context.Vehicles.FirstOrDefaultAsync(v => v.Registration == "SF01 SUR");
        if (existing is not null) return existing.Id;

        var vehicle = new Vehicle
        {
            Registration = "SF01 SUR", Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, _ownerId, EntrySource.Web);
        return vehicle.Id;
    }

    /// <summary>Runs `add_task` with the surface pinned, and returns the row it wrote.</summary>
    private async Task<MaintenanceTask> AddTaskThroughToolAsync(EntrySource surfaceSource, string title)
    {
        await using var context = NewContext(TestOwner.As(_ownerId));
        await EnsureVehicleAsync(context);

        var surface = new WriteSurface();
        if (surfaceSource is not EntrySource.Mcp) surface.Set(surfaceSource);

        var resolver = new VehicleResolver(context, new VehicleMetricsLoader(context));
        var tasks = new TaskService(context, new ReferenceWriter(context, TestOwner.As(_ownerId)), _time);

        var result = await WriteTools.AddTask(surface, resolver, tasks, title, vehicle: "SF01 SUR");

        return await context.MaintenanceTasks.SingleAsync(t => t.Id == result.Data.Id);
    }

    [Fact]
    public async Task A_tool_invoked_by_the_chat_stamps_chat()
    {
        var task = await AddTaskThroughToolAsync(EntrySource.Chat, "Replace front pads");

        Assert.Equal(EntrySource.Chat, task.Source);
    }

    [Fact]
    public async Task A_tool_invoked_with_the_default_surface_still_stamps_mcp()
    {
        // The default is what makes this refactor a no-op for `/mcp`: an unpinned surface behaves exactly as the
        // constant did, so every existing caller and test is unchanged.
        var task = await AddTaskThroughToolAsync(EntrySource.Mcp, "Check the coolant colour");

        Assert.Equal(EntrySource.Mcp, task.Source);
    }

    [Fact]
    public void A_surface_cannot_be_pinned_to_the_undefined_member()
    {
        // EntrySource has no zero member so that an unset value is detectable. A setter that accepted one would
        // defeat that from the outside.
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteSurface().Set(default));
    }
}
