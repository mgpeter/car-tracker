using System.Text.Json;
using CarTracker.Chat;
using CarTracker.Domain;
using CarTracker.ModelContextProtocol;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;

namespace CarTracker.Data.Tests;

/// <summary>
/// A chat tool sees exactly the signed-in owner's vehicles — because of which provider it was handed.
/// </summary>
/// <remarks>
/// <para>
/// The chat's tools are the MCP tools, and they take their <c>DbContext</c> from a container. The context's
/// global query filter reads the owner from <c>ICurrentUserAccessor</c>, which is pinned per request. So the
/// provider the invocation carries <b>is</b> the ownership boundary: hand the tools the root provider and every
/// tool runs with no owner pinned, which is not a leak but its mirror image — the filter matches nothing and the
/// assistant tells everyone their garage is empty.
/// </para>
/// <para>
/// Asserted against a real database with two real accounts, because "the filter applies" is a claim about
/// generated SQL. A mocked context would agree with whatever this test asserted.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ChatToolScopeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;
    private int _firstOwner;
    private int _secondOwner;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_chat_scope");

        await using var seed = new CarTrackerDbContext(
            new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options,
            _time,
            currentUser: null);

        await seed.Database.MigrateAsync();

        _firstOwner = await TestOwner.SeedAsync(seed, "test|chat-owner-one");
        _secondOwner = await TestOwner.SeedAsync(seed, "test|chat-owner-two");

        await SeedVehicleAsync(_firstOwner, "CH01 ONE");
        await SeedVehicleAsync(_secondOwner, "CH02 TWO");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedVehicleAsync(int ownerId, string registration)
    {
        await using var context = new CarTrackerDbContext(
            new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options,
            _time,
            TestOwner.As(ownerId));

        // Idempotent: the fixture is per-test and the database outlives it.
        if (await context.Vehicles.AnyAsync(v => v.Registration == registration)) return;

        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632,
            FuelType = FuelType.Petrol, Source = EntrySource.Web,
        };

        await new VehicleFactory(context).CreateAsync(vehicle, ownerId, EntrySource.Web);
    }

    /// <summary>The container a request builds: the owner pinned on the scope, exactly as the middleware does.</summary>
    private ServiceProvider ContainerFor(int ownerId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddScoped<ICurrentUserAccessor>(_ => TestOwner.As(ownerId));
        services.AddDbContext<CarTrackerDbContext>(o => o.UseNpgsql(_connectionString));
        services.AddCarTrackerDomain();
        return services.BuildServiceProvider();
    }

    private static async Task<string> CallAsync(IServiceProvider scoped, string tool, params (string Key, object? Value)[] arguments)
    {
        var function = CarTrackerToolCatalogue.AIFunctions(scoped).Single(f => f.Name == tool);

        // Services on the invocation, not just on the build: the factory binds every service parameter from
        // whatever provider the call carries. This is the line the property in the remarks rests on.
        var args = new AIFunctionArguments(arguments.ToDictionary(a => a.Key, a => a.Value)) { Services = scoped };

        return JsonSerializer.Serialize(await function.InvokeAsync(args));
    }

    [Fact]
    public async Task A_chat_tool_lists_only_the_signed_in_owners_vehicles()
    {
        await using var first = ContainerFor(_firstOwner);
        using var scope = first.CreateScope();

        var listed = await CallAsync(scope.ServiceProvider, "list_vehicles");

        Assert.Contains("CH01 ONE", listed);
        Assert.DoesNotContain("CH02 TWO", listed);
    }

    [Fact]
    public async Task Another_owners_registration_does_not_resolve_through_a_chat_tool()
    {
        // Naming the other car explicitly is the interesting case: the filter has to make it not exist, rather
        // than the listing simply not mentioning it. A model that guessed a plate must reach nothing.
        await using var first = ContainerFor(_firstOwner);
        using var scope = first.CreateScope();

        // It does not exist, so the tool refuses the way it refuses a typo — there is no third answer for "this
        // car belongs to someone else", which is the point: the filter makes the two cases identical.
        var refusal = await Assert.ThrowsAsync<McpException>(() =>
            CallAsync(scope.ServiceProvider, "get_vehicle_summary", ("vehicle", "CH02 TWO")));

        Assert.Contains("No vehicle matches", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_second_owner_sees_their_own_car_and_not_the_first()
    {
        // The mirror of the first test, and it is not redundant: a filter that matched nothing for anyone would
        // pass that one. This is what tells the two apart.
        await using var second = ContainerFor(_secondOwner);
        using var scope = second.CreateScope();

        var listed = await CallAsync(scope.ServiceProvider, "list_vehicles");

        Assert.Contains("CH02 TWO", listed);
        Assert.DoesNotContain("CH01 ONE", listed);
    }

    [Fact]
    public async Task A_write_through_the_catalogue_lands_on_the_owners_car_and_says_chat()
    {
        // The confirmed half of the loop, from the tool's side: the same AIFunction the model was shown, invoked
        // with the request's scope, writing a real row. What the endpoint adds on top is the pending-write id and
        // the SSE — neither of which can change what the tool does.
        await using var first = ContainerFor(_firstOwner);
        using var scope = first.CreateScope();

        scope.ServiceProvider.UseChatWriteSurface();

        await CallAsync(
            scope.ServiceProvider,
            "add_task",
            ("title", "Replace front pads"),
            ("vehicle", "CH01 ONE"),
            ("kind", "Workshop"));

        await using var context = new CarTrackerDbContext(
            new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options,
            _time,
            TestOwner.As(_firstOwner));

        var task = await context.MaintenanceTasks.SingleAsync(t => t.Title == "Replace front pads");

        // The attribution is the whole reason EntrySource.Chat exists: an assistant-drafted row must not claim
        // to be an unattended MCP write, and must not claim someone typed it either.
        Assert.Equal(EntrySource.Chat, task.Source);

        var vehicle = await context.Vehicles.SingleAsync(v => v.Id == task.VehicleId);
        Assert.Equal("CH01 ONE", vehicle.Registration);
    }
}
