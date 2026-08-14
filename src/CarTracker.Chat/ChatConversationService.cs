using System.Runtime.CompilerServices;
using CarTracker.ModelContextProtocol;
using Microsoft.Extensions.AI;

namespace CarTracker.Chat;

/// <summary>What one turn produced: what to say, and whether it is waiting on the owner.</summary>
/// <param name="Messages">
/// The assistant turn(s), to be appended to the transcript and echoed back verbatim next time — reasoning
/// blocks included. They arrive with their text omitted and their signature in <c>ProtectedData</c>; dropping
/// one because it looks empty breaks the next turn.
/// </param>
/// <param name="PendingWrite">The write awaiting confirmation, or null when the turn simply finished.</param>
/// <param name="Usage">Token counts for the spending guard.</param>
public sealed record ChatTurn(
    IReadOnlyList<ChatMessage> Messages,
    PendingWrite? PendingWrite,
    ChatTurnUsage Usage);

/// <summary>A write the model proposed and the owner has not yet confirmed.</summary>
/// <param name="ToolCallId">The approval request's id — what a confirmation must answer.</param>
/// <param name="Tool">The tool name, for display. Never trusted back from the client.</param>
/// <param name="Arguments">What the model proposed, for the draft card to render and the owner to correct.</param>
public sealed record PendingWrite(string ToolCallId, string Tool, IDictionary<string, object?> Arguments);

/// <param name="CacheWriteTokens">Prefix written to cache — the 1.25× turn, once per conversation if all is well.</param>
/// <param name="CacheReadTokens">Prefix read from cache at 0.1×. Zero on every turn means caching is broken.</param>
public sealed record ChatTurnUsage(long InputTokens, long OutputTokens, long CacheWriteTokens, long CacheReadTokens);

/// <summary>
/// The conversation: reads run, writes suspend, and the owner decides.
/// </summary>
/// <remarks>
/// <para>
/// The loop is <see cref="FunctionInvokingChatClient"/>'s, not one written here. Its
/// <c>ApprovalRequiredAIFunction</c> → <c>ToolApprovalRequestContent</c> → <c>ToolApprovalResponseContent</c>
/// round trip is exactly this feature's confirm-before-write gate: a gate that suspends, returns, and resumes
/// from a later HTTP request. An earlier draft of the spec called for hand-rolling it, on the grounds that the
/// Anthropic SDK's runner gates synchronously — true of that runner, and not of this one.
/// </para>
/// <para>
/// Stateless, like the API beneath it: the transcript arrives with the request and leaves with the response.
/// The only server-held state in this feature is the pending write, and it lives in the endpoint layer where the
/// authorisation question is.
/// </para>
/// </remarks>
public sealed class ChatConversationService(IChatClient client, ChatSettings settings, IChatBudget budget)
{
    /// <summary>
    /// Runs the conversation until the assistant finishes its turn or asks to write something.
    /// </summary>
    /// <param name="messages">The transcript, including the new user message.</param>
    /// <param name="services">The request's scoped provider — see <see cref="ChatToolset.For"/>.</param>
    public async Task<ChatTurn> ContinueAsync(
        IList<ChatMessage> messages,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Before the model, not after: a turn that would exceed the ceiling costs nothing at all, which is the
        // difference between a budget and a report. Every path into the loop passes through here, including a
        // resumption, so an endpoint cannot forget to ask.
        if (await budget.CheckAsync(cancellationToken) is { } refusal) throw new ChatBudgetExceededException(refusal);

        var response = await client.GetResponseAsync(messages, Options(services), cancellationToken);
        var turn = ToTurn(response);

        await budget.RecordAsync(turn.Usage, cancellationToken);

        return turn;
    }

    /// <summary>
    /// The same turn, delivered as it happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The budget check runs before the first event is yielded</b>, so an endpoint that pulls one item before
    /// writing its response headers still gets a real 429 rather than an <c>error</c> event inside a 200. That
    /// ordering is the reason this is an iterator and not a callback.
    /// </para>
    /// <para>
    /// A tool call is narrated only when it is a read. A write never runs here — it suspends — so narrating one
    /// would say a thing had happened that had not.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        IList<ChatMessage> messages,
        IServiceProvider services,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (await budget.CheckAsync(cancellationToken) is { } refusal) throw new ChatBudgetExceededException(refusal);

        List<ChatResponseUpdate> updates = [];
        Dictionary<string, string> callNames = [];

        await foreach (var update in client.GetStreamingResponseAsync(messages, Options(services), cancellationToken))
        {
            updates.Add(update);

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } text:
                        yield return new ChatTextEvent(text.Text);
                        break;

                    case FunctionCallContent call:
                        callNames[call.CallId] = call.Name;
                        if (!McpToolClassification.IsWrite(call.Name)) yield return new ChatToolEvent(call.Name, "running");
                        break;

                    // The result carries a call id and no name — the pairing is why the names are kept above.
                    case FunctionResultContent result when callNames.TryGetValue(result.CallId, out var name):
                        if (!McpToolClassification.IsWrite(name)) yield return new ChatToolEvent(name, "done");
                        break;
                }
            }
        }

        var turn = ToTurn(updates.ToChatResponse());

        await budget.RecordAsync(turn.Usage, cancellationToken);

        if (turn.PendingWrite is { } pending) yield return new ChatPendingWriteEvent(pending);

        yield return new ChatDoneEvent(turn);
    }

    /// <summary>Reads a completed response into the turn the endpoints and the tests both work from.</summary>
    private static ChatTurn ToTurn(ChatResponse response)
    {
        // A write tool never ran: the loop replaced the call with an approval request and returned. `AllowMulti-
        // pleToolCalls = false` is what makes "the first one" also "the only one".
        var pending = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalRequestContent>()
            .Select(r => r.ToolCall as FunctionCallContent)
            .OfType<FunctionCallContent>()
            .Select(call => new PendingWrite(
                call.CallId,
                call.Name,
                call.Arguments ?? new Dictionary<string, object?>()))
            .FirstOrDefault();

        var (cacheWrite, cacheRead) = AnthropicChatExtras.CacheCounts(response);

        return new ChatTurn(
            [.. response.Messages],
            pending,
            new ChatTurnUsage(
                response.Usage?.InputTokenCount ?? 0,
                response.Usage?.OutputTokenCount ?? 0,
                cacheWrite,
                cacheRead));
    }

    /// <summary>
    /// Answers a suspended write — approved with the owner's final arguments, or declined — and carries the
    /// conversation on.
    /// </summary>
    /// <param name="arguments">
    /// What the owner actually confirmed, which may differ from what the model proposed. That difference is the
    /// entire point of the draft card, so it is applied to the call itself rather than checked against it.
    /// </param>
    /// <remarks>
    /// <b>A suspension must always be answered</b>, including a refusal. An unanswered approval request is
    /// rejected upstream and would break the transcript for every later turn — which is why declining is a
    /// request rather than a silence.
    /// </remarks>
    public async Task<ChatTurn> ResumeAsync(
        IList<ChatMessage> messages,
        string toolCallId,
        bool approved,
        IDictionary<string, object?>? arguments,
        string? reason,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        Answer(messages, toolCallId, approved, arguments, reason);

        return await ContinueAsync(messages, services, cancellationToken);
    }

    /// <summary>The same answer, with the resumed turn streamed.</summary>
    public IAsyncEnumerable<ChatStreamEvent> StreamResumeAsync(
        IList<ChatMessage> messages,
        string toolCallId,
        bool approved,
        IDictionary<string, object?>? arguments,
        string? reason,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Outside the iterator on purpose: a bad tool call id must throw when the endpoint calls this, not on
        // the first MoveNext, so it can still become a 404 rather than an error event inside a 200.
        Answer(messages, toolCallId, approved, arguments, reason);

        return StreamAsync(messages, services, cancellationToken);
    }

    private static void Answer(
        IList<ChatMessage> messages,
        string toolCallId,
        bool approved,
        IDictionary<string, object?>? arguments,
        string? reason)
    {
        var request = messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalRequestContent>()
            .FirstOrDefault(r => r.ToolCall is FunctionCallContent call && call.CallId == toolCallId)
            ?? throw new InvalidOperationException(
                $"No pending write with id '{toolCallId}' in this transcript. It has already been answered, or "
                + "the transcript is not the one the write was proposed in.");

        if (approved && arguments is not null && request.ToolCall is FunctionCallContent proposed)
        {
            // The owner's values replace the model's, in place, so the call the loop executes is the call they
            // looked at. Replacing rather than merging: a field they cleared must clear.
            proposed.Arguments = arguments;
        }

        messages.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(approved, reason)]));
    }

    /// <summary>
    /// The per-request options. Internal rather than private because three of the four settings below are
    /// footguns whose wrong value is invisible at runtime, so they are asserted rather than reviewed.
    /// </summary>
    internal ChatOptions Options(IServiceProvider services) => new()
    {
        ModelId = settings.Model,
        MaxOutputTokens = settings.MaxOutputTokens,
        Tools = ChatToolset.For(services),

        // Not Instructions: the system prompt travels on the raw options so a cache breakpoint can be attached
        // to it. See AnthropicChatExtras.
        RawRepresentationFactory = AnthropicChatExtras.RawOptions(ChatSystemPrompt.Text, settings),

        // One tool call per response, and this is not a preference.
        //
        // Documented behaviour: if ANY call in a response requires approval, EVERY call in that response does —
        // including the reads. Left on, a turn that reads the odometer and drafts a fill would put a confirm
        // button in front of the read, which is exactly the friction the design refuses on reads. The cost is a
        // round trip per tool; what it buys is the read-now/confirm-to-write distinction the feature rests on.
        AllowMultipleToolCalls = false,

        // Adaptive thinking, expressed portably. Leave it on: with thinking off the model occasionally writes a
        // tool call into its visible text instead of emitting one, which here means a draft card that silently
        // never appears.
        Reasoning = new ReasoningOptions(),
    };
}
