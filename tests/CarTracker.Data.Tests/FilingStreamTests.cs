using CarTracker.Chat;
using CarTracker.Chat.Tests;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Documents;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// A chat turn that calls <c>offer_to_file</c> tells the panel what the tool proposed, and only then.
/// </summary>
/// <remarks>
/// <para>
/// The model is scripted and the tool is real: the offer is checked against PostgreSQL with the owner pinned on
/// the request's scope, exactly as the endpoint runs it. A <c>ChatFilingOfferEvent</c> is narration built from
/// the tool's own result, so these tests assert that a refusal, another tool or a write produces none.
/// </para>
/// <para>
/// The write-and-offer case is the one the system prompt steers away from and a model can still produce: both
/// calls in one response. The offer is then swept into the approval protocol with the write, and runs on the
/// confirm, where its call is in the transcript rather than in the stream.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class FilingStreamTests(PostgresFixture postgres) : IAsyncLifetime, IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

    private string _connectionString = string.Empty;
    private string _root = string.Empty;
    private int _owner;
    private int _stranger;
    private int _strangersIssue;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_filing_stream");
        _root = Path.Combine(Path.GetTempPath(), $"cartracker-filing-stream-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        await using var seed = Context(null);
        await seed.Database.MigrateAsync();
        _owner = await TestOwner.SeedAsync(seed, "test|filing-stream-owner");
        _stranger = await TestOwner.SeedAsync(seed, "test|filing-stream-stranger");

        await VehicleAsync(_owner, "FS01 AAA");
        var strangers = await VehicleAsync(_stranger, "FS02 ZZZ");

        await using var theirs = Context(_stranger);
        var issue = await theirs.Issues.FirstOrDefaultAsync(i => i.VehicleId == strangers);
        if (issue is null)
        {
            issue = new Issue
            {
                VehicleId = strangers, Title = "Not yours", Severity = Severity.Low, FirstNoted = new DateOnly(2026, 9, 1),
                Status = IssueStatus.Monitoring, Source = EntrySource.Web,
            };
            theirs.Issues.Add(issue);
            await theirs.SaveChangesAsync();
        }

        _strangersIssue = issue.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private CarTrackerDbContext Context(int? owner) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time,
            owner is { } id ? TestOwner.As(id) : null);

    private async Task<int> VehicleAsync(int owner, string registration)
    {
        await using var context = Context(owner);
        var existing = await context.Vehicles.FirstOrDefaultAsync(v => v.Registration == registration);
        if (existing is not null) return existing.Id;

        var vehicle = new Vehicle
        {
            Registration = registration, Make = "Land Rover", Model = "Freelander 1", Year = 2003,
            PurchaseDate = new DateOnly(2026, 3, 14), PurchaseMileage = 76_632, FuelType = FuelType.Petrol,
            Source = EntrySource.Web, IsDefault = true,
        };
        await new VehicleFactory(context).CreateAsync(vehicle, owner, EntrySource.Web, CheckSource.None);
        return vehicle.Id;
    }

    /// <summary>The request's container, with the owner pinned on the scope as the middleware pins it.</summary>
    private ServiceProvider ContainerFor(int owner)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddScoped<ICurrentUserAccessor>(_ => TestOwner.As(owner));
        services.AddDbContext<CarTrackerDbContext>(o => o.UseNpgsql(_connectionString));
        services.AddCarTrackerDomain();
        services.AddSingleton(new DocumentStorageOptions(_root));
        services.AddSingleton<DocumentStore>();
        services.AddScoped<IAccountEntitlements>(_ => TestEntitlements.Pro);
        services.AddSingleton(new AppOptions { PublicOrigin = "https://cambelt.app" });
        return services.BuildServiceProvider();
    }

    /// <summary>Built over the request's scope, as <c>AddCarTrackerChat</c> builds it: the tools bind their services from it.</summary>
    private static ChatConversationService NewService(ScriptedChatClient scripted, IServiceProvider services) =>
        new(
            scripted.AsBuilder()
                .UseFunctionInvocation(configure: f =>
                {
                    f.MaximumIterationsPerRequest = 4;
                    f.AllowConcurrentInvocation = false;
                })
                .Build(services),
            new ChatSettings { ApiKey = "test", Model = "test-model" },
            new FakeBudget());

    private static async Task<List<ChatStreamEvent>> DrainAsync(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        List<ChatStreamEvent> events = [];
        await foreach (var e in stream) events.Add(e);
        return events;
    }

    private static Dictionary<string, object?> Offer(params (string Key, object? Value)[] arguments) =>
        arguments.ToDictionary(a => a.Key, a => a.Value);

    [Fact]
    public async Task A_turn_calling_offer_to_file_yields_the_tools_offer_between_the_narration_and_done()
    {
        await using var provider = ContainerFor(_owner);
        using var scope = provider.CreateScope();

        var scripted = new ScriptedChatClient(
            ScriptedChatClient.Calls("offer_to_file", "call-1", Offer(
                ("type", "CarPhoto"), ("title", "After the wash"), ("documentDate", "2026-11-26"), ("attachment", 2))),
            ScriptedChatClient.Says("That one can go in Photos."));

        var events = await DrainAsync(NewService(scripted, scope.ServiceProvider).StreamAsync(
            [new(ChatRole.User, "Look at her after the wash")], scope.ServiceProvider));

        var offered = Assert.Single(events.OfType<ChatFilingOfferEvent>());
        var offer = offered.Offer;

        // Equal to what the tool itself returns for the same arguments.
        var direct = await scope.ServiceProvider.GetRequiredService<DocumentService>().ProposeFilingAsync(
            (await scope.ServiceProvider.GetRequiredService<VehicleResolver>().ResolveAsync(null))!,
            DocumentType.CarPhoto, "After the wash", new DateOnly(2026, 11, 26), null, null, null, 2,
            "https://cambelt.app");
        Assert.Equal(direct.Value, offer);

        var running = events.FindIndex(e => e is ChatToolEvent { Name: "offer_to_file", Status: "running" });
        var at = events.IndexOf(offered);
        var done = events.FindIndex(e => e is ChatDoneEvent);
        Assert.InRange(running, 0, at - 1);
        Assert.True(at < done, "the offer must arrive before done");
    }

    [Fact]
    public async Task A_refused_offer_yields_nothing_and_the_model_reads_the_refusal()
    {
        await using var provider = ContainerFor(_owner);
        using var scope = provider.CreateScope();

        var scripted = new ScriptedChatClient(
            ScriptedChatClient.Calls("offer_to_file", "call-1", Offer(
                ("type", "ConditionPhoto"), ("title", "Dent"), ("issueId", _strangersIssue))),
            ScriptedChatClient.Says("I could not link that to the issue."));

        var events = await DrainAsync(NewService(scripted, scope.ServiceProvider).StreamAsync(
            [new(ChatRole.User, "File this")], scope.ServiceProvider));

        Assert.Empty(events.OfType<ChatFilingOfferEvent>());

        var result = Assert.Single(scripted.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>());
        Assert.Contains($"Issue {_strangersIssue} is not this vehicle's", Assert.IsType<string>(result.Result));
    }

    [Fact]
    public async Task Another_tool_yields_no_offer()
    {
        await using var provider = ContainerFor(_owner);
        using var scope = provider.CreateScope();

        var scripted = new ScriptedChatClient(
            ScriptedChatClient.Calls("list_vehicles", "call-1", []),
            ScriptedChatClient.Says("One car."));

        var events = await DrainAsync(NewService(scripted, scope.ServiceProvider).StreamAsync(
            [new(ChatRole.User, "Which cars?")], scope.ServiceProvider));

        Assert.Contains(events, e => e is ChatToolEvent { Name: "list_vehicles" });
        Assert.Empty(events.OfType<ChatFilingOfferEvent>());
    }

    [Fact]
    public async Task An_offer_sharing_a_response_with_a_write_runs_on_the_confirm()
    {
        await using var provider = ContainerFor(_owner);
        using var scope = provider.CreateScope();

        var vendor = $"Euro Car Parts {Guid.NewGuid():N}";
        var scripted = new ScriptedChatClient(
            ScriptedChatClient.CallsMany(
                ("log_expense", "call-write", Offer(
                    ("category", "Repair"), ("amount", 63.20m), ("date", "2026-11-14"), ("vendor", vendor))),
                ("offer_to_file", "call-offer", Offer(
                    ("type", "PartsReceipt"), ("title", "Euro Car Parts receipt"), ("attachment", 1)))),
            ScriptedChatClient.Says("Logged, and the receipt is ready to file."));

        var service = NewService(scripted, scope.ServiceProvider);
        List<ChatMessage> transcript = [new(ChatRole.User, "Log this receipt")];

        var first = await DrainAsync(service.StreamAsync([.. transcript], scope.ServiceProvider));

        // Suspends on the write only: the offer is swept in with it and has not run.
        var pending = Assert.Single(first.OfType<ChatPendingWriteEvent>());
        Assert.Equal("log_expense", Assert.Single(pending.Writes).Tool);
        Assert.Empty(first.OfType<ChatFilingOfferEvent>());
        Assert.False(await ExpenseExistsAsync(vendor));

        transcript.AddRange(first.OfType<ChatDoneEvent>().Single().Turn.Messages);

        var resumed = await DrainAsync(service.StreamResumeAsync(
            transcript, [new WriteDecision("call-write", Approved: true)], reason: null, scope.ServiceProvider));

        var offered = Assert.Single(resumed.OfType<ChatFilingOfferEvent>());
        Assert.Equal(DocumentType.PartsReceipt, offered.Offer.Type);
        Assert.Equal(1, offered.Offer.Attachment);
        Assert.Contains(resumed, e => e is ChatToolEvent { Name: "offer_to_file", Status: "done" });
        // The write is never narrated: it was confirmed, not run on its own.
        Assert.DoesNotContain(resumed, e => e is ChatToolEvent { Name: "log_expense" });

        Assert.True(await ExpenseExistsAsync(vendor));
    }

    private async Task<bool> ExpenseExistsAsync(string vendor)
    {
        await using var context = Context(_owner);
        return await context.ExpenseEntries.AnyAsync(e => e.Vendor == vendor);
    }
}
