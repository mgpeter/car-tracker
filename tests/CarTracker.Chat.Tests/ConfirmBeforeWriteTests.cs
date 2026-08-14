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

        var pending = Assert.Single(turn.PendingWrites);
        Assert.Equal("add_task", pending.Tool);
        Assert.Equal("call-1", pending.ToolCallId);
        Assert.Equal("Replace front pads", pending.Arguments["title"]);

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
            transcript, [new WriteDecision("call-1", Approved: false)], reason: "not now", TestCatalogue.Services);

        // The turn completes rather than hanging, and the model was told — an unanswered approval request is
        // rejected upstream and would break every later turn in the conversation.
        Assert.Empty(resumed.PendingWrites);
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

        await Assert.ThrowsAsync<ChatTranscriptException>(() => service.ResumeAsync(
            transcript, [new WriteDecision("call-does-not-exist", Approved: true)], reason: null, TestCatalogue.Services));
    }

    [Fact]
    public async Task Several_writes_in_one_response_all_suspend()
    {
        // The failure this test exists for: a pasted table of sixteen fills arrives as sixteen tool calls in
        // one response. The loop used to keep the first and drop the rest, and the rest — unanswered — had the
        // next request rejected outright.
        var scripted = new ScriptedChatClient(ScriptedChatClient.CallsMany(
            ("add_task", "call-1", new() { ["title"] = "Replace front pads" }),
            ("add_task", "call-2", new() { ["title"] = "Check the coolant" }),
            ("add_task", "call-3", new() { ["title"] = "Book the MOT" })));

        var turn = await NewService(scripted).ContinueAsync(
            [new(ChatRole.User, "Add these three")], TestCatalogue.Services);

        Assert.Equal(3, turn.PendingWrites.Count);
        Assert.Equal(["call-1", "call-2", "call-3"], turn.PendingWrites.Select(w => w.ToolCallId));
    }

    [Fact]
    public async Task A_read_swept_into_the_approval_protocol_is_not_shown_to_anyone()
    {
        // Documented behaviour: if any call in a response requires approval, every call in it does — including
        // the reads. Left alone, a turn that looks something up and drafts a fill would put a confirm button in
        // front of the lookup, which is the friction the design refuses on reads. The library marks those
        // RequiresConfirmation = false, and they are approved without being surfaced.
        var scripted = new ScriptedChatClient(ScriptedChatClient.CallsMany(
            ("list_vehicles", "call-read", []),
            ("add_task", "call-write", new() { ["title"] = "Replace front pads" })));

        var turn = await NewService(scripted).ContinueAsync(
            [new(ChatRole.User, "Which cars do I have, and add a task")], TestCatalogue.Services);

        var pending = Assert.Single(turn.PendingWrites);
        Assert.Equal("add_task", pending.Tool);
    }

    [Fact]
    public async Task Answering_some_of_a_batch_answers_all_of_it()
    {
        var scripted = new ScriptedChatClient(
            ScriptedChatClient.CallsMany(
                ("add_task", "call-1", new() { ["title"] = "One" }),
                ("add_task", "call-2", new() { ["title"] = "Two" }),
                ("add_task", "call-3", new() { ["title"] = "Three" })),
            ScriptedChatClient.Says("Two saved, one skipped."));

        var service = NewService(scripted);
        List<ChatMessage> transcript = [new(ChatRole.User, "Add these three")];

        var turn = await service.ContinueAsync(transcript, TestCatalogue.Services);
        transcript.AddRange(turn.Messages);

        // Only two decisions are supplied, and the third is not mentioned at all.
        await service.ResumeAsync(
            transcript,
            [new WriteDecision("call-1", Approved: false), new WriteDecision("call-3", Approved: false)],
            reason: "not now",
            TestCatalogue.Services);

        var answers = scripted.Requests[^1]
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Select(r => r.CallId)
            .ToList();

        // Every suspension answered, including the one nobody decided on. An unanswered approval request is
        // rejected upstream and breaks the transcript for every later turn.
        Assert.Equal(3, answers.Count);
        Assert.Contains("call-2", answers);
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

    [Fact]
    public async Task A_resumed_turn_hands_back_the_call_and_its_result()
    {
        // What the loop returns after a confirm is the write in its final shape: the function call and the
        // result of running it. The approval request/response pair is bookkeeping it consumed on the way, and
        // the client drops it — replaying both is the same write twice, which is rejected outright.
        var scripted = new ScriptedChatClient(
            ScriptedChatClient.Calls("add_task", "call-1", new() { ["title"] = "Replace front pads" }),
            ScriptedChatClient.Says("Saved."));

        var service = NewService(scripted);
        List<ChatMessage> transcript = [new(ChatRole.User, "Add a task")];

        var turn = await service.ContinueAsync(transcript, TestCatalogue.Services);
        transcript.AddRange(turn.Messages);

        var resumed = await service.ResumeAsync(
            transcript, [new WriteDecision("call-1", Approved: true)], reason: null, TestCatalogue.Services);

        var contents = resumed.Messages.SelectMany(m => m.Contents).ToList();

        Assert.Contains(contents, c => c is FunctionCallContent { Name: "add_task" });
        Assert.Contains(contents, c => c is FunctionResultContent);

        // And no approval content: the client would replay it beside the call above.
        Assert.DoesNotContain(contents, c => c is ToolApprovalRequestContent or ToolApprovalResponseContent);
    }
}
