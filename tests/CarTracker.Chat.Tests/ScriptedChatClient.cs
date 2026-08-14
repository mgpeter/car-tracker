using Microsoft.Extensions.AI;

namespace CarTracker.Chat.Tests;

/// <summary>
/// An <see cref="IChatClient"/> that says what it is told to, in order.
/// </summary>
/// <remarks>
/// The suspend-on-write loop is deterministic and its behaviour is the safety property of the whole feature, so
/// it is tested against a scripted model rather than a live one: no key, no cost, no flake, and it runs in CI.
/// The live tests in this project answer a different question — whether the provider behaves as documented.
/// </remarks>
internal sealed class ScriptedChatClient(params ChatResponse[] script) : IChatClient
{
    private int _next;

    /// <summary>Every request the loop made, so a test can assert what the model was actually asked.</summary>
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add([.. messages]);

        if (_next >= script.Length)
        {
            throw new InvalidOperationException(
                $"The loop asked the model {_next + 1} times; the script has {script.Length} replies. Either the "
                + "loop is going round more than the test expects, or the script is short.");
        }

        return Task.FromResult(script[_next++]);
    }

    /// <summary>
    /// The same script, delivered as updates.
    /// </summary>
    /// <remarks>
    /// Split into updates through the SDK's own <c>ToChatResponseUpdates</c> rather than by hand, so what the
    /// loop sees here has the same shape a provider produces — including how a tool call is chunked, which is
    /// the part a hand-rolled fake would get wrong in exactly the way that matters.
    /// </remarks>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    /// <summary>A reply that calls one tool.</summary>
    public static ChatResponse Calls(string tool, string callId, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, arguments)]));

    /// <summary>
    /// A reply that calls several tools at once — sixteen fills off one pasted table, or a service record and
    /// a fill read from two photographs.
    /// </summary>
    /// <remarks>
    /// <b>The absence of this is why a batch shipped broken.</b> Every test scripted one call per response, so
    /// nothing exercised the case the provider produces freely: `AllowMultipleToolCalls = false` never reached
    /// the wire, and the loop dropped every suspension after the first.
    /// </remarks>
    public static ChatResponse CallsMany(params (string Tool, string CallId, Dictionary<string, object?> Arguments)[] calls) =>
        new(new ChatMessage(
            ChatRole.Assistant,
            [.. calls.Select(c => new FunctionCallContent(c.CallId, c.Tool, c.Arguments))]));

    /// <summary>A reply that just talks.</summary>
    public static ChatResponse Says(string text) => new(new ChatMessage(ChatRole.Assistant, text));
}
