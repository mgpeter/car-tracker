using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace CarTracker.Chat.Tests;

/// <summary>
/// Spikes 0.1 and 0.2 — the two questions that decide where the provider seam sits.
/// </summary>
/// <remarks>
/// <para>
/// The design puts <see cref="IChatClient"/> between the app and the model so the provider is one registration
/// rather than a rewrite. That is only worth having if the two Anthropic-specific behaviours this feature
/// depends on survive the abstraction: <b>prompt caching</b> (the difference between a conversation costing
/// pennies and costing tens of pennies) and <b>thinking-block round-tripping</b> (the API rejects an edited or
/// dropped block, and on Opus 5 they arrive with their text omitted, which is easy to mistake for "empty, so
/// droppable").
/// </para>
/// <para>
/// If either fails, the seam moves up to <c>IChatConversationService</c> and the Anthropic SDK is used directly
/// beneath it. Both are acceptable outcomes; the point of the spike is that the answer is measured.
/// </para>
/// </remarks>
public sealed class AbstractionSpikeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static IChatClient NewChatClient() =>
        new AnthropicClient { ApiKey = LiveModel.ApiKey }.AsIChatClient(LiveModel.Model);

    /// <summary>
    /// A frozen system prompt comfortably over the minimum cacheable prefix (512 tokens on Opus 5, 1024 on
    /// Sonnet 5). Deterministic on purpose: a timestamp or a GUID in here is the classic silent cache killer.
    /// </summary>
    private static string FrozenSystemPrompt { get; } = string.Join("\n", Enumerable.Range(0, 120).Select(i =>
        $"Rule {i}: when the owner asks about their vehicle, answer from the logged figures and never from "
        + "memory; state the reading you are working from; and if a figure is missing, say so rather than "
        + "estimating it. Derived numbers are computed at render time and are never stored."));

    [LiveFact]
    public async Task Prompt_caching_survives_the_IChatClient_seam()
    {
        var client = NewChatClient();

        var options = new ChatOptions
        {
            MaxOutputTokens = 32,
            // Deliberately NOT ChatOptions.Instructions: the system prompt is set below, on the raw params,
            // because that is the only place a cache breakpoint can be attached to it.
            //
            // The provider-specific escape hatch. Everything Anthropic-shaped that the abstraction cannot carry
            // goes through here, and in the real code it lives in one adapter class.
            RawRepresentationFactory = _ => new MessageCreateParams
            {
                // The client overwrites these from ChatOptions; they are here because the type requires them.
                Model = LiveModel.Model,
                MaxTokens = 32,
                Messages = [],
                // The breakpoint is placed ON THE SYSTEM BLOCK, explicitly. Top-level auto-caching places it on
                // the *last* cacheable block, which in a chat request is the user's own turn — so it rewrites
                // the whole prefix every request and never reads it. See the spike's finding.
                System = new List<TextBlockParam>
                {
                    new() { Text = FrozenSystemPrompt, CacheControl = new CacheControlEphemeral() },
                },
            },
        };

        var first = await client.GetResponseAsync("Say ok.", options);
        var second = await client.GetResponseAsync("Say ok again.", options);

        var (write1, read1) = CacheCounts(first);
        var (write2, read2) = CacheCounts(second);

        output.WriteLine($"first  → write {write1}, read {read1}, input {first.Usage?.InputTokenCount}");
        output.WriteLine($"second → write {write2}, read {read2}, input {second.Usage?.InputTokenCount}");

        // The whole question: does a second identical-prefix request read from the cache?
        //
        // Note what is NOT asserted: that the first request *wrote* the cache. The entry outlives the test run
        // (5-minute TTL), so on a re-run inside that window the first request reads an entry an earlier run
        // wrote — and an assertion of `write1 > 0` fails for a reason that is not a defect. A cache test that
        // assumes a cold start is flaky by construction. What must hold either way is that the prefix
        // *participates* in caching, and that the second request reads it.
        Assert.True(write1 + read1 > 0, "the prefix did not participate in caching at all — the breakpoint never reached the wire");
        Assert.True(read2 > 0, "the second request did not read the cache — the prefix is not byte-identical, or caching does not survive the seam");
    }

    [LiveFact]
    public async Task Thinking_blocks_round_trip_through_the_seam()
    {
        var client = NewChatClient();

        var options = new ChatOptions
        {
            MaxOutputTokens = 2_048,
            // Adaptive thinking, expressed portably. On Opus 5 it is on by default; asking for it explicitly is
            // what the chat will do, because the same code has to work on a model where it is not.
            Reasoning = new ReasoningOptions(),
        };

        List<ChatMessage> transcript = [new(ChatRole.User, "A tank took 47.2 litres over 312 miles. What is the MPG? One line.")];

        var first = await client.GetResponseAsync(transcript, options);

        var reasoning = first.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .ToList();

        output.WriteLine($"messages back: {first.Messages.Count}, reasoning blocks: {reasoning.Count}");
        foreach (var r in reasoning)
        {
            output.WriteLine($"  reasoning: text={(r.Text?.Length ?? 0)} chars, protected={(r.ProtectedData is null ? "no" : "yes")}");
        }

        // Echo the assistant turn back verbatim — which is what the endpoint does every turn, because the client
        // holds the transcript. A block that cannot survive this trip breaks every conversation at turn two.
        transcript.AddRange(first.Messages);
        transcript.Add(new(ChatRole.User, "And in litres per 100 km? One line."));

        var second = await client.GetResponseAsync(transcript, options);

        output.WriteLine($"second turn ok, finish reason: {second.FinishReason}");
        Assert.NotEmpty(second.Text);
    }

    /// <summary>
    /// Cache counters, read from the provider's own response object. M.E.AI's <see cref="UsageDetails"/> models
    /// input and output; anything provider-specific rides in <c>AdditionalCounts</c> or the raw representation,
    /// and which of those carries it is itself part of what this spike is checking.
    /// </summary>
    private static (long Write, long Read) CacheCounts(ChatResponse response)
    {
        if (response.Usage?.AdditionalCounts is { } counts)
        {
            var write = counts.TryGetValue("cache_creation_input_tokens", out var w) ? w : 0;
            var read = counts.TryGetValue("cache_read_input_tokens", out var r) ? r : 0;
            if (write > 0 || read > 0) return (write, read);
        }

        if (response.RawRepresentation is Message message)
        {
            return (message.Usage.CacheCreationInputTokens ?? 0, message.Usage.CacheReadInputTokens ?? 0);
        }

        return (0, 0);
    }
}
