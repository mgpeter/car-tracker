using Microsoft.Extensions.AI;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The turn as the panel receives it: prose as it arrives, reads narrated, a write stopping the stream.
/// </summary>
/// <remarks>
/// Streaming is a second path through the loop, not a rendering of the first, so the safety property has to
/// hold on it independently — a write that suspends under <c>GetResponseAsync</c> and runs under
/// <c>GetStreamingResponseAsync</c> would be the worst possible defect and the easiest one to not look for.
/// </remarks>
public sealed class StreamingTurnTests
{
    private static ChatConversationService NewService(ScriptedChatClient scripted, IChatBudget? budget = null) =>
        new(
            scripted.AsBuilder()
                .UseFunctionInvocation(configure: f =>
                {
                    f.MaximumIterationsPerRequest = 4;
                    f.AllowConcurrentInvocation = false;
                })
                .Build(),
            new ChatSettings { ApiKey = "test", Model = "test-model" },
            budget ?? new FakeBudget());

    private static async Task<List<ChatStreamEvent>> RunAsync(ChatConversationService service, string say)
    {
        List<ChatStreamEvent> events = [];

        await foreach (var e in service.StreamAsync([new(ChatRole.User, say)], TestCatalogue.Services))
        {
            events.Add(e);
        }

        return events;
    }

    [Fact]
    public async Task Prose_arrives_as_text_events_and_the_turn_ends_with_done()
    {
        var events = await RunAsync(
            NewService(new ScriptedChatClient(ScriptedChatClient.Says("Your MOT is due in 359 days."))),
            "When is the MOT due?");

        Assert.Contains(
            "359",
            string.Concat(events.OfType<ChatTextEvent>().Select(t => t.Delta)));

        Assert.IsType<ChatDoneEvent>(events[^1]);
    }

    [Fact]
    public async Task A_write_suspends_on_the_streaming_path_too()
    {
        var events = await RunAsync(
            NewService(new ScriptedChatClient(
                ScriptedChatClient.Calls("add_task", "call-1", new() { ["title"] = "Replace front pads" }))),
            "Add a task to replace the front pads");

        var pending = Assert.Single(events.OfType<ChatPendingWriteEvent>());

        var only = Assert.Single(pending.Writes);
        Assert.Equal("add_task", only.Tool);
        Assert.Equal("call-1", only.ToolCallId);

        // And it is the last thing before done: the draft card is what the turn ends on, not something the
        // assistant carries on talking past.
        Assert.IsType<ChatDoneEvent>(events[^1]);

        var done = (ChatDoneEvent)events[^1];
        Assert.NotEmpty(done.Turn.PendingWrites);
    }

    [Fact]
    public async Task A_refusal_is_raised_before_the_first_event()
    {
        // What lets the endpoint answer 429 rather than opening a 200 and putting the refusal inside it. The
        // exception must surface on the first MoveNextAsync, before a byte of the response is written.
        var scripted = new ScriptedChatClient(ScriptedChatClient.Says("never reached"));
        var service = NewService(scripted, FakeBudget.Spent());

        await Assert.ThrowsAsync<ChatBudgetExceededException>(() => RunAsync(service, "hello"));

        Assert.Empty(scripted.Requests);
    }

}
