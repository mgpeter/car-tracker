using CarTracker.Chat;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CarTracker.Data.Tests;

/// <summary>
/// The daily ceiling: what it refuses, when it stops refusing, and that it survives a restart.
/// </summary>
/// <remarks>
/// Against a real database because the ledger is the whole mechanism. An in-memory counter would pass every
/// assertion here and still hand each account a fresh allowance every time Watchtower recreated the container —
/// which is minutes after every CI publish, and most often on the days work is being done.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ChatBudgetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Reference = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Reference);

    private string _connectionString = string.Empty;
    private int _firstOwner;
    private int _secondOwner;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.EnsureDatabaseAsync("cartracker_chat_budget");

        await using var seed = NewContext();
        await seed.Database.MigrateAsync();

        _firstOwner = await TestOwner.SeedAsync(seed, "test|budget-owner-one");
        _secondOwner = await TestOwner.SeedAsync(seed, "test|budget-owner-two");

        // Each test starts from an empty ledger; the database outlives the fixture.
        await seed.ChatUsage.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private CarTrackerDbContext NewContext(ICurrentUserAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<CarTrackerDbContext>().UseNpgsql(_connectionString).Options, _time, accessor);

    private ChatBudget BudgetFor(
        CarTrackerDbContext context,
        int? ownerId,
        long perOwner = 1_000,
        long global = 10_000,
        IAccountEntitlements? plan = null) =>
        new(
            context,
            new ChatSettings { ApiKey = "test", DailyTokensPerOwner = perOwner, DailyTokensGlobal = global },
            ownerId is { } id ? TestOwner.As(id) : new CurrentUserAccessor(),
            // The paid tier by default, which names no ceiling of its own - so these tests go on measuring
            // Chat:DailyTokensPerOwner, which is what they were written about.
            plan ?? TestEntitlements.Pro,
            new Clock(_time));

    private async Task SpendAsync(int ownerId, long tokens)
    {
        await using var context = NewContext(TestOwner.As(ownerId));

        await BudgetFor(context, ownerId).RecordAsync(new ChatTurnUsage(tokens, 0, 0, 0));
    }

    /// <summary>A ledger row for another day, written directly — <c>FakeTimeProvider</c> refuses to go back.</summary>
    private async Task SpendOnAsync(int ownerId, DateOnly day, long tokens)
    {
        await using var context = NewContext();

        context.ChatUsage.Add(new ChatUsage { OwnerId = ownerId, Day = day, InputTokens = tokens, Turns = 1 });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task An_owner_under_their_allowance_may_carry_on()
    {
        await SpendAsync(_firstOwner, 400);

        await using var context = NewContext(TestOwner.As(_firstOwner));

        Assert.Null(await BudgetFor(context, _firstOwner).CheckAsync());
    }

    [Fact]
    public async Task An_owner_over_their_allowance_is_refused_and_told_when_it_resets()
    {
        await SpendAsync(_firstOwner, 1_200);

        await using var context = NewContext(TestOwner.As(_firstOwner));
        var refusal = await BudgetFor(context, _firstOwner).CheckAsync();

        Assert.NotNull(refusal);
        Assert.Equal("account", refusal!.Scope);
        Assert.Equal(1_200, refusal.Spent);
        Assert.Equal(1_000, refusal.Limit);

        // Tomorrow's local midnight, which on 14 August is 23:00 UTC — the reset must land at the owner's
        // midnight, not at UTC's, or the message is an hour wrong for two thirds of the year.
        Assert.Equal(new DateOnly(2026, 8, 15), DateOnly.FromDateTime(refusal.ResetsAt.DateTime));
        Assert.Equal(TimeSpan.FromHours(1), refusal.ResetsAt.Offset);
    }

    [Fact]
    public async Task Yesterdays_spending_does_not_count_against_today()
    {
        await SpendOnAsync(_firstOwner, new DateOnly(2026, 8, 13), 5_000);

        await using var context = NewContext(TestOwner.As(_firstOwner));

        Assert.Null(await BudgetFor(context, _firstOwner).CheckAsync());
    }

    [Fact]
    public async Task The_deployment_ceiling_refuses_an_owner_who_is_within_their_own()
    {
        // Two fears, two ceilings. The per-owner limit cannot bound a deployment's bill without knowing how many
        // accounts it will have, so this one refuses on the total while the account itself has spent almost
        // nothing.
        await SpendAsync(_firstOwner, 900);
        await SpendAsync(_secondOwner, 200);

        await using var context = NewContext(TestOwner.As(_secondOwner));
        var refusal = await BudgetFor(context, _secondOwner, perOwner: 1_000, global: 1_000).CheckAsync();

        Assert.NotNull(refusal);
        Assert.Equal("deployment", refusal!.Scope);
        Assert.Equal(1_100, refusal.Spent);
    }

    [Fact]
    public async Task A_zero_allowance_means_the_chat_is_off_for_that_account()
    {
        // The fail-safe direction, and the opposite of the natural reading — which is why it is stated in
        // .env.example, the README and the setting's own doc comment as well as here.
        await using var context = NewContext(TestOwner.As(_firstOwner));
        var refusal = await BudgetFor(context, _firstOwner, perOwner: 0).CheckAsync();

        Assert.NotNull(refusal);
        Assert.Equal(0, refusal!.Limit);
        Assert.Equal(0, refusal.Spent);
    }

    [Fact]
    public async Task A_turn_nobody_can_be_charged_for_is_refused()
    {
        // No resolved owner means no request pipeline ran, or one ran and resolved nothing. Either way the turn
        // is unattributable, and an unattributable turn is one nobody is accountable for.
        await using var context = NewContext();

        Assert.NotNull(await BudgetFor(context, ownerId: null).CheckAsync());
    }

    [Fact]
    public async Task Spending_accumulates_into_one_row_a_day()
    {
        await SpendAsync(_firstOwner, 100);
        await SpendAsync(_firstOwner, 250);

        await using var context = NewContext();
        var row = await context.ChatUsage.SingleAsync(u => u.OwnerId == _firstOwner);

        Assert.Equal(350, row.InputTokens);
        Assert.Equal(2, row.Turns);
        Assert.Equal(new DateOnly(2026, 8, 14), row.Day);
    }

    [Fact]
    public async Task One_owners_spending_is_not_charged_to_another()
    {
        await SpendAsync(_firstOwner, 1_500);

        await using var context = NewContext(TestOwner.As(_secondOwner));

        Assert.Null(await BudgetFor(context, _secondOwner, perOwner: 1_000, global: 10_000).CheckAsync());
    }

    [Fact]
    public async Task A_plan_without_the_assistant_refuses_before_the_ledger_is_read()
    {
        // Not a budget refusal, and the difference is the whole point: a spent allowance has a figure and a
        // reset time and "come back tomorrow" is true. This one has neither, and collapsing the two would send
        // somebody back every morning for a feature they were never going to get.
        await using var context = NewContext(TestOwner.As(_firstOwner));

        var budget = BudgetFor(context, _firstOwner, plan: TestEntitlements.Free);

        await Assert.ThrowsAsync<ChatNotEntitledException>(() => budget.CheckAsync());
    }

    [Fact]
    public async Task A_free_account_is_refused_even_with_a_completely_unspent_ledger()
    {
        // The reading this rules out: "free means a ceiling of zero, so it will look like an exhausted budget".
        // It must not, because an exhausted budget is a 429 and this is a 403, and one of them tells the owner
        // to wait.
        await using var context = NewContext(TestOwner.As(_secondOwner));
        await context.ChatUsage.Where(u => u.OwnerId == _secondOwner).ExecuteDeleteAsync();

        await Assert.ThrowsAsync<ChatNotEntitledException>(
            () => BudgetFor(context, _secondOwner, plan: TestEntitlements.Free).CheckAsync());
    }

    [Fact]
    public async Task A_plan_naming_its_own_ceiling_overrides_the_deployment_s()
    {
        // The paid tier names none and defers to Chat:DailyTokensPerOwner, which is what every other test here
        // measures. A plan that does name one wins, so a future tier can be bounded without moving the
        // deployment's key out from under the accounts already on it.
        await SpendAsync(_firstOwner, 400);

        await using var context = NewContext(TestOwner.As(_firstOwner));

        // Deployment ceiling 1,000 and 400 spent: comfortably under, and refused anyway by the plan's 300.
        var refusal = await BudgetFor(
            context,
            _firstOwner,
            perOwner: 1_000,
            plan: TestEntitlements.With(dailyChatTokens: 300)).CheckAsync();

        Assert.NotNull(refusal);
        Assert.Equal(300, refusal.Limit);
    }
}
