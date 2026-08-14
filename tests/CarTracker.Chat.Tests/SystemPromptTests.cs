using System.Reflection;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The system prompt is frozen and it is cached — and both halves are asserted, because the failure is silent.
/// </summary>
/// <remarks>
/// A date, a registration or a user id interpolated into the prompt does not break anything. It moves the whole
/// conversation from the 0.1× cache-read price to the 1.25× cache-write one, on every turn, for ever, with
/// nothing on screen looking different. The only symptom is the bill, which is why this is a test rather than a
/// comment.
/// </remarks>
public sealed class SystemPromptTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void The_prompt_is_a_compile_time_constant()
    {
        // Structural rather than textual, and deliberately so: a `const` cannot interpolate a date, a plate or a
        // version, so this forecloses the whole class of drift instead of listing the strings to look out for.
        // Turning it into a `static readonly` built at startup would still compile, and would still cache — for
        // exactly as long as nobody put today's date in it.
        var field = typeof(ChatSystemPrompt).GetField(
            nameof(ChatSystemPrompt.Text),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral, "the system prompt must stay a const — see this class's remarks");
    }

    [Fact]
    public void The_prompt_reaches_the_wire_as_a_cached_system_block()
    {
        // Not ChatOptions.Instructions: there is nowhere to attach a breakpoint to it, and top-level auto-caching
        // puts the breakpoint on the *last* cacheable block — the user's own turn — which measured as
        // `write 9609, read 0` twice running (spike 0.1).
        var settings = new ChatSettings { ApiKey = "test", Model = "test-model" };
        var raw = AnthropicChatExtras.RawOptions(ChatSystemPrompt.Text, settings)(null!);

        var system = Assert.IsType<MessageCreateParams>(raw).System;

        // A union of "a plain string" and "a list of blocks". The plain string is the shape with nowhere to hang
        // a cache breakpoint, so which arm this is decides whether the prefix caches at all.
        Assert.True(system!.TryPickTextBlockParams(out var blocks), "the system prompt went as a bare string, which cannot carry a cache breakpoint");
        var block = Assert.Single(blocks);

        Assert.Equal(ChatSystemPrompt.Text, block.Text);
        Assert.IsType<CacheControlEphemeral>(block.CacheControl);
    }

    /// <summary>
    /// The end-to-end version of the same claim: two turns, and the second one reads the prefix back.
    /// </summary>
    /// <remarks>
    /// Spike 0.1 proved the mechanism against a hand-built request. This proves the shipped path — the settings,
    /// the adapter, the catalogue and the conversation service as they are actually wired — which is the part
    /// that can rot.
    /// </remarks>
    [LiveFact]
    public async Task The_second_turn_reads_the_cached_prefix()
    {
        using var scope = LiveChat.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ChatConversationService>();

        List<ChatMessage> transcript = [new(ChatRole.User, "Reply with the single word: ok.")];
        var first = await service.ContinueAsync(transcript, scope.ServiceProvider);
        transcript.AddRange(first.Messages);

        transcript.Add(new(ChatRole.User, "Reply with the single word: ok."));
        var second = await service.ContinueAsync(transcript, scope.ServiceProvider);

        output.WriteLine($"first  → write {first.Usage.CacheWriteTokens}, read {first.Usage.CacheReadTokens}");
        output.WriteLine($"second → write {second.Usage.CacheWriteTokens}, read {second.Usage.CacheReadTokens}");

        // Not `write > 0` on the first turn: the entry outlives the test run, so a re-run inside the TTL reads
        // what an earlier run wrote and a cold-start assumption would fail for a reason that is not a defect.
        Assert.True(
            first.Usage.CacheWriteTokens + first.Usage.CacheReadTokens > 0,
            "the prefix did not participate in caching at all — the breakpoint never reached the wire");
        Assert.True(
            second.Usage.CacheReadTokens > 0,
            "the second turn did not read the cache — the prefix is not byte-identical between turns");
    }
}
