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
/// <param name="PendingWrites">
/// The writes awaiting confirmation, in the order the model proposed them. Empty when the turn simply
/// finished.
/// </param>
/// <param name="Usage">Token counts for the spending guard.</param>
public sealed record ChatTurn(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<PendingWrite> PendingWrites,
    ChatTurnUsage Usage);

/// <summary>A write the model proposed and the owner has not yet confirmed.</summary>
/// <param name="ToolCallId">The approval request's id — what a confirmation must answer.</param>
/// <param name="Tool">The tool name, for display. Never trusted back from the client.</param>
/// <param name="Arguments">What the model proposed, for the draft card to render and the owner to correct.</param>
public sealed record PendingWrite(string ToolCallId, string Tool, IDictionary<string, object?> Arguments);

/// <summary>What the owner decided about one proposed write.</summary>
/// <param name="Arguments">
/// The values they confirmed, which may differ from what was proposed. Null approves the call exactly as the
/// model made it.
/// </param>
public sealed record WriteDecision(string ToolCallId, bool Approved, IDictionary<string, object?>? Arguments = null);

/// <summary>
/// The transcript cannot answer the question being asked of it — a stale tab, or a client that rebuilt its
/// history.
/// </summary>
/// <remarks>
/// Typed so the endpoint can say something an owner can act on. The library's own exception for this is
/// phrased for whoever wrote the loop ("ToolApprovalRequestContent found with FunctionCall.CallId(s) …"), and
/// it reached a car app's chat panel verbatim once already.
/// </remarks>
public sealed class ChatTranscriptException(string message) : Exception(message);

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

        if (turn.PendingWrites.Count > 0) yield return new ChatPendingWriteEvent(turn.PendingWrites);

        yield return new ChatDoneEvent(turn);
    }

    /// <summary>Reads a completed response into the turn the endpoints and the tests both work from.</summary>
    /// <remarks>
    /// <b>Every suspension is kept, not the first.</b> One response can legitimately propose sixteen fills, or
    /// a service record and a fill read off two photographs, and the earlier code took `.FirstOrDefault()` on
    /// the strength of a comment claiming `AllowMultipleToolCalls = false` made "the first one" also "the only
    /// one". It does not: the Anthropic seam only emits a `tool_choice` when `ChatOptions.ToolMode` is
    /// non-null, that property defaults to null, and so `disable_parallel_tool_use` has never once been sent.
    /// The abstraction says as much anyway — "the underlying provider is not guaranteed to support or honor
    /// this flag". Dropping the rest left them unanswered, and the next request was rejected outright.
    /// </remarks>
    private static ChatTurn ToTurn(ChatResponse response)
    {
        var pending = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalRequestContent>()
            // Only what actually needs a human. If any call in a response requires approval then every call in
            // it does — including reads — so a turn that looks something up and drafts a fill would otherwise
            // put a confirm button in front of the lookup. The library flags those with RequiresConfirmation
            // false; ResumeAsync answers them without asking.
            .Where(NeedsConfirmation)
            .Select(r => r.ToolCall as FunctionCallContent)
            .OfType<FunctionCallContent>()
            .Select(call => new PendingWrite(
                call.CallId,
                call.Name,
                call.Arguments ?? new Dictionary<string, object?>()))
            .ToList();

        var (cacheWrite, cacheRead) = AnthropicChatExtras.CacheCounts(response);

        return new ChatTurn(
            // What the model said back — which, after a resume, already contains the answered call and its
            // result. The approval request/response pair is bookkeeping the loop consumes; the client drops it
            // once answered rather than replaying it beside the call it turned into. See `withoutApprovals` in
            // useChat.ts, and the two-shapes-of-the-same-write failure it exists to prevent.
            [.. response.Messages],
            pending,
            new ChatTurnUsage(
                response.Usage?.InputTokenCount ?? 0,
                response.Usage?.OutputTokenCount ?? 0,
                cacheWrite,
                cacheRead));
    }

    /// <summary>
    /// Answers the suspended writes — each approved with the owner's final arguments, or declined — and
    /// carries the conversation on.
    /// </summary>
    /// <param name="decisions">
    /// One per write the owner was shown. A call id absent from this list is declined, so a client that
    /// forgets one cannot wedge the conversation.
    /// </param>
    /// <remarks>
    /// <b>Every suspension must be answered, not just the one being confirmed.</b> An unanswered approval
    /// request is rejected upstream and breaks the transcript for every later turn — which is why declining is
    /// a request rather than a silence, and why this takes a list rather than an id.
    /// </remarks>
    public async Task<ChatTurn> ResumeAsync(
        IList<ChatMessage> messages,
        IReadOnlyList<WriteDecision> decisions,
        string? reason,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        AnswerAll(messages, decisions, reason);

        return await ContinueAsync(messages, services, cancellationToken);
    }

    /// <summary>The same answer, with the resumed turn streamed.</summary>
    public IAsyncEnumerable<ChatStreamEvent> StreamResumeAsync(
        IList<ChatMessage> messages,
        IReadOnlyList<WriteDecision> decisions,
        string? reason,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Outside the iterator on purpose: an unanswerable transcript must throw when the endpoint calls this,
        // not on the first MoveNext, so it can still become a status code rather than an error event in a 200.
        AnswerAll(messages, decisions, reason);

        return StreamAsync(messages, services, cancellationToken);
    }

    /// <summary>
    /// Appends one response for every outstanding approval request in the transcript.
    /// </summary>
    /// <remarks>
    /// The list is walked, not the decisions: a request nobody decided on is declined rather than skipped, so
    /// "answer them all" holds however short the caller's list is. Reads that were swept into the approval
    /// protocol — <c>RequiresConfirmation</c> false, because they shared a response with a write — are
    /// approved here without being shown to anyone, which is what keeps "reads never ask" true.
    /// </remarks>
    private static void AnswerAll(
        IList<ChatMessage> messages,
        IReadOnlyList<WriteDecision> decisions,
        string? reason)
    {
        var outstanding = messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalRequestContent>()
            .Where(r => !Answered(messages, r))
            .ToList();

        if (outstanding.Count == 0)
        {
            throw new ChatTranscriptException(
                "That draft is no longer part of this conversation. It has already been answered, or the "
                + "conversation has moved on since it was proposed.");
        }

        List<AIContent> answers = [];

        foreach (var request in outstanding)
        {
            var callId = (request.ToolCall as FunctionCallContent)?.CallId;
            var decision = decisions.FirstOrDefault(d => d.ToolCallId == callId);

            var approved = !NeedsConfirmation(request) || (decision?.Approved ?? false);

            if (approved && decision?.Arguments is { } values && request.ToolCall is FunctionCallContent proposed)
            {
                // The owner's values replace the model's, in place, so the call the loop executes is the call
                // they looked at. Replacing rather than merging: a field they cleared must clear.
                proposed.Arguments = values;
            }

            answers.Add(request.CreateResponse(approved, approved ? null : reason));
        }

        messages.Add(new ChatMessage(ChatRole.User, answers));
    }

    /// <summary>Whether an earlier turn already answered this request — its response rides in the transcript.</summary>
    /// <remarks>
    /// Correlated by the tool call, which both halves carry. The id the two are constructed with is not exposed
    /// as a property, and the call id is what everything else here keys on anyway.
    /// </remarks>
    private static bool Answered(IList<ChatMessage> messages, ToolApprovalRequestContent request) =>
        CallIdOf(request.ToolCall) is { } callId
        && messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalResponseContent>()
            .Any(r => CallIdOf(r.ToolCall) == callId);

    private static string? CallIdOf(ToolCallContent? call) => (call as FunctionCallContent)?.CallId;

    /// <summary>
    /// Whether this request is one a person has to answer, or one the loop swept in alongside it.
    /// </summary>
    /// <remarks>
    /// <c>RequiresConfirmation</c> is marked experimental (MEAI001), and the suppression is here, once, rather
    /// than at each use. The alternative is re-deriving the answer from
    /// <c>McpToolClassification.IsWrite</c> — a second opinion about which tools change a row, which is exactly
    /// the drift that list exists to prevent. If the API is withdrawn, this method is the one place to change;
    /// <c>Every_write_tool_is_marked_approval_required_and_no_read_tool_is</c> holds the two lists together, and
    /// <c>A_read_swept_into_the_approval_protocol_is_not_shown_to_anyone</c> pins this behaviour.
    /// </remarks>
#pragma warning disable MEAI001
    private static bool NeedsConfirmation(ToolApprovalRequestContent request) => request.RequiresConfirmation;
#pragma warning restore MEAI001

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

        // Several tool calls per response are allowed, and this pair says so explicitly rather than by default.
        //
        // It was `AllowMultipleToolCalls = false` until a batch of sixteen fills arrived in one response and
        // proved otherwise. The Anthropic seam only sends a `tool_choice` when ToolMode is non-null, and it
        // defaults to null — so the flag never reached the wire and parallel tool use has always been on. The
        // abstraction warns as much: "the underlying provider is not guaranteed to support or honor this flag".
        //
        // Setting both changes nothing on the wire (`disable_parallel_tool_use: false` is the default already
        // in force). What it changes is that the request now states what the loop actually relies on — and the
        // loop relies on answering every suspension, not on there being one.
        ToolMode = ChatToolMode.Auto,
        AllowMultipleToolCalls = true,

        // Adaptive thinking, expressed portably. Leave it on: with thinking off the model occasionally writes a
        // tool call into its visible text instead of emitting one, which here means a draft card that silently
        // never appears.
        Reasoning = new ReasoningOptions(),
    };
}
