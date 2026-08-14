using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace CarTracker.Chat;

/// <summary>
/// The one class that knows which provider we are on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IChatClient"/> carries messages, tools, reasoning and streaming portably. It does not carry
/// Anthropic's own request shapes — prompt-cache breakpoints above all — and the documented way to reach them
/// is <see cref="Microsoft.Extensions.AI.ChatOptions.RawRepresentationFactory"/>. Everything provider-specific
/// goes through here so the count of files that would change when the provider changes stays at one.
/// </para>
/// <para>
/// <b>The system prompt travels here, not in <c>ChatOptions.Instructions</c>, and that is not a style choice.</b>
/// Measured 2026-08-14: top-level auto-caching places the breakpoint on the <i>last</i> cacheable block, which in
/// a chat request is the user's own turn — so every request rewrote the whole prefix and read nothing
/// (<c>write 9609, read 0</c> → <c>write 9610, read 0</c>), silently, at the 1.25× write premium forever.
/// Placed explicitly on the system block: <c>write 9602, read 0</c> → <c>write 0, read 9602</c>. There is
/// nowhere to attach a breakpoint to <c>Instructions</c>, so the prompt cannot live there.
/// </para>
/// </remarks>
internal static class AnthropicChatExtras
{
    /// <summary>
    /// Builds the raw-options factory: the frozen system prompt, cached, plus the effort setting.
    /// </summary>
    /// <param name="systemPrompt">
    /// Must be byte-identical across requests. A timestamp, a registration or a user id interpolated in here is
    /// a 10× cost regression whose only symptom is the bill.
    /// </param>
    public static Func<IChatClient, object?> RawOptions(string systemPrompt, ChatSettings settings) =>
        _ => new MessageCreateParams
        {
            // Overwritten by the client from ChatOptions; required members of the type.
            Model = settings.Model,
            MaxTokens = settings.MaxOutputTokens,
            Messages = [],

            // The cache breakpoint, placed by hand on the system block. See the class remarks.
            System = new List<TextBlockParam>
            {
                new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
            },
        };

    /// <summary>
    /// Cache counters for logging and the spending guard, read from wherever the provider put them.
    /// </summary>
    /// <remarks>
    /// M.E.AI's <see cref="UsageDetails"/> models input and output; anything provider-specific rides in
    /// <c>AdditionalCounts</c> or the raw response. Both are checked, so this keeps working if the mapping
    /// improves — and returns zeroes rather than throwing on a provider that has no such concept.
    /// </remarks>
    public static (long CacheWrite, long CacheRead) CacheCounts(ChatResponse response)
    {
        if (response.Usage?.AdditionalCounts is { } counts)
        {
            var write = counts.TryGetValue("cache_creation_input_tokens", out var w) ? w : 0;
            var read = counts.TryGetValue("cache_read_input_tokens", out var r) ? r : 0;
            if (write > 0 || read > 0) return (write, read);
        }

        return response.RawRepresentation is Message message
            ? (message.Usage.CacheCreationInputTokens ?? 0, message.Usage.CacheReadInputTokens ?? 0)
            : (0, 0);
    }
}
