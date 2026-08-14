namespace CarTracker.Chat.Tests;

/// <summary>
/// A budget that answers whatever the test needs, and remembers what it was told a turn cost.
/// </summary>
/// <remarks>
/// The real one reads a database; what these tests are about is the loop's relationship with it — that a refusal
/// happens before the model call, and that a completed turn reports itself. Both are properties of the caller,
/// not of the ledger, and the ledger is asserted against a real database in <c>ChatBudgetTests</c>.
/// </remarks>
internal sealed class FakeBudget(ChatBudgetRefusal? refusal = null) : IChatBudget
{
    public List<ChatTurnUsage> Recorded { get; } = [];

    public int Checks { get; private set; }

    public Task<ChatBudgetRefusal?> CheckAsync(CancellationToken cancellationToken = default)
    {
        Checks++;
        return Task.FromResult(refusal);
    }

    public Task RecordAsync(ChatTurnUsage usage, CancellationToken cancellationToken = default)
    {
        Recorded.Add(usage);
        return Task.CompletedTask;
    }

    /// <summary>A budget that has nothing left, with a reset time a test can assert on.</summary>
    public static FakeBudget Spent(string scope = "account") =>
        new(new ChatBudgetRefusal(scope, 1_000_000, 1_000_000, new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)));
}
