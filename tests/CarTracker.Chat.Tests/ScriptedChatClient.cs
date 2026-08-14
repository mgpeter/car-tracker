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

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The loop tests use the non-streaming path.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    /// <summary>A reply that calls one tool.</summary>
    public static ChatResponse Calls(string tool, string callId, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, arguments)]));

    /// <summary>A reply that just talks.</summary>
    public static ChatResponse Says(string text) => new(new ChatMessage(ChatRole.Assistant, text));
}
