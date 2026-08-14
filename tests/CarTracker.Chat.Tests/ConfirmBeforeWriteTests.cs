using Microsoft.Extensions.AI;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The safety property: a chat turn can read, and can spend tokens, and cannot change a row.
/// </summary>
/// <remarks>
/// Everything else in this feature is convenience. This is the part that has to be true, so it is asserted
/// against a scripted model — no key, no cost, runs in CI — rather than observed once by hand.
/// </remarks>
public sealed class ConfirmBeforeWriteTests
{
    private static ChatSettings Settings => new() { ApiKey = "test", Model = "test-model", MaxToolIterations = 4 };

    private static ChatConversationService NewService(ScriptedChatClient scripted, IChatBudget? budget = null) =>
        new(
            scripted.AsBuilder()
                .UseFunctionInvocation(configure: f =>
                {
                    f.MaximumIterationsPerRequest = 4;
                    f.AllowConcurrentInvocation = false;
                })
                .Build(),
            Settings,
            budget ?? new FakeBudget());

    [Fact]
    public async Task A_write_tool_suspends_the_turn_instead_of_running()
    {
        // add_task is a write, so the loop must not invoke it. If it did, this test would need a database —
        // which is itself the assertion: the tool never gets that far.
        var scripted = new ScriptedChatClient(
            ScriptedChatClient.Calls("add_task", "call-1", new() { ["title"] = "Replace front pads" }));

        var service = NewService(scripted);
        List<ChatMessage> transcript = [new(ChatRole.User, "Add a task to replace the front pads")];

        var turn = await service.ContinueAsync(transcript, TestCatalogue.Services);

        Assert.NotNull(turn.PendingWrite);
        Assert.Equal("add_task", turn.PendingWrite!.Tool);
        Assert.Equal("call-1", turn.PendingWrite.ToolCallId);
        Assert.Equal("Replace front pads", turn.PendingWrite.Arguments["title"]);

        // One round trip: the loop stopped rather than carrying on without an answer.
        Assert.Single(scripted.Requests);
    }

    [Fact]
    public async Task Declining_answers_the_suspension_rather_than_dropping_it()
    {
        var scripted = new ScriptedChatClient(
            ScriptedChatClient.Calls("add_task", "call-1", new() { ["title"] = "Something" }),
            ScriptedChatClient.Says("Fine — nothing saved."));

        var service = NewService(scripted);
        List<ChatMessage> transcript = [new(ChatRole.User, "Add a task")];

        var turn = await service.ContinueAsync(transcript, TestCatalogue.Services);
        transcript.AddRange(turn.Messages);

        var resumed = await service.ResumeAsync(
            transcript, "call-1", approved: false, arguments: null, reason: "not now", TestCatalogue.Services);

        // The turn completes rather than hanging, and the model was told — an unanswered approval request is
        // rejected upstream and would break every later turn in the conversation.
        Assert.Null(resumed.PendingWrite);
        Assert.Contains(
            "nothing saved",
            string.Concat(resumed.Messages.Select(m => m.Text)),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, scripted.Requests.Count);

        // The model is told through the ordinary tool-result channel, not the approval protocol: the loop turns a
        // refusal into a result for that call id. Which is what makes the transcript valid to send back — a call
        // with no result is rejected upstream and would break every later turn in the conversation.
        var answer = scripted.Requests[^1]
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Single();

        Assert.Equal("call-1", answer.CallId);
        Assert.Contains("not now", answer.Result?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Answering_a_write_that_was_never_proposed_is_refused()
    {
        // The confirm endpoint's server-held id is the real guard, but the service must not be the weak link:
        // a transcript that does not contain the suspension cannot resume one.
        var service = NewService(new ScriptedChatClient(ScriptedChatClient.Says("hello")));
        List<ChatMessage> transcript = [new(ChatRole.User, "hello")];

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(
            transcript, "call-does-not-exist", approved: true, arguments: null, reason: null, TestCatalogue.Services));
    }

    [Fact]
    public void One_tool_call_per_response_so_a_read_is_never_gated_behind_a_confirm()
    {
        // Documented FunctionInvokingChatClient behaviour: if ANY call in a response requires approval, EVERY
        // call in that response does — the reads included. Left on, a turn that reads the odometer and drafts a
        // fill would put a confirm button in front of the *read*, which is exactly the friction the design
        // refuses on reads. The cost is a round trip per tool; what it buys is the distinction the feature rests
        // on. Asserted rather than reviewed, because the wrong value is invisible until a turn happens to make
        // two calls at once.
        var options = NewService(new ScriptedChatClient()).Options(TestCatalogue.Services);

        Assert.False(options.AllowMultipleToolCalls);
        Assert.NotNull(options.Reasoning);
    }

    [Fact]
    public void Every_write_tool_is_marked_approval_required_and_no_read_tool_is()
    {
        // The marking comes from McpToolClassification, so this is really asserting that the chat's gate and the
        // audit filter cannot disagree about which tools change a row.
        var tools = ChatToolset.For(TestCatalogue.Services).OfType<AIFunction>().ToList();

        var wrongly = tools
            .Where(t => (t is ApprovalRequiredAIFunction) != CarTracker.ModelContextProtocol.McpToolClassification.IsWrite(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(wrongly);
        Assert.Equal(49, tools.Count);
        Assert.Equal(30, tools.Count(t => t is ApprovalRequiredAIFunction));
    }

    [Fact]
    public async Task A_turn_over_the_daily_allowance_never_reaches_the_model()
    {
        // The difference between a budget and a report: the refusal happens before the request is made, so a
        // turn that would exceed the ceiling costs nothing at all rather than costing one more turn.
        var scripted = new ScriptedChatClient(ScriptedChatClient.Says("this must never be reached"));
        var budget = FakeBudget.Spent();

        var refused = await Assert.ThrowsAsync<ChatBudgetExceededException>(() =>
            NewService(scripted, budget).ContinueAsync(
                [new(ChatRole.User, "What is my MPG?")], TestCatalogue.Services));

        Assert.Empty(scripted.Requests);
        Assert.Equal("account", refused.Refusal.Scope);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero), refused.Refusal.ResetsAt);
    }

    [Fact]
    public async Task A_completed_turn_reports_what_it_cost()
    {
        // Recorded after the fact, because what a turn costs is not knowable before it runs. The scripted client
        // reports no usage, so this asserts that the report happens at all — the figures themselves come from
        // the provider and are asserted live in SystemPromptTests.
        var budget = new FakeBudget();

        await NewService(new ScriptedChatClient(ScriptedChatClient.Says("42 MPG.")), budget)
            .ContinueAsync([new(ChatRole.User, "What is my MPG?")], TestCatalogue.Services);

        Assert.Single(budget.Recorded);
        Assert.Equal(1, budget.Checks);
    }
}
