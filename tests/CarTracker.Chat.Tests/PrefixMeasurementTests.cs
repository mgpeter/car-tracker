using Anthropic;
using Anthropic.Models.Messages;

namespace CarTracker.Chat.Tests;

/// <summary>
/// Spike 0.4 — how big is the prefix, really?
/// </summary>
/// <remarks>
/// Every cost decision in the spec rests on this number, and until this ran it was an estimate derived from
/// character counts (8–12k tokens). Measured with the API's own <c>count_tokens</c>, never a client-side
/// tokenizer: <c>tiktoken</c> and friends are OpenAI's and mis-count Claude tokens by 15–20% on prose and far
/// more on code and schemas.
/// </remarks>
public sealed class PrefixMeasurementTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static AnthropicClient NewClient() => new() { ApiKey = LiveModel.ApiKey };

    [LiveFact]
    public async Task The_key_works_and_the_model_answers()
    {
        // The smoke test. A key with no credit behind it authenticates and then fails here, which is a different
        // failure from a wrong key and worth being able to tell apart.
        var client = NewClient();

        var message = await client.Messages.Create(new MessageCreateParams
        {
            Model = LiveModel.Model,
            MaxTokens = 16,
            Messages = [new() { Role = Role.User, Content = "Reply with the single word: ok" }],
        });

        output.WriteLine($"model: {message.Model}, in: {message.Usage.InputTokens}, out: {message.Usage.OutputTokens}");
        Assert.NotEmpty(message.Content);
    }

    [LiveFact]
    public async Task The_tool_catalogue_is_measured_not_estimated()
    {
        var client = NewClient();

        var tools = ToolCatalogueSpike.All
            .Select(t => new MessageCountTokensTool(new Tool
            {
                Name = t.ProtocolTool.Name,
                Description = t.ProtocolTool.Description,
                InputSchema = System.Text.Json.JsonSerializer.Deserialize<InputSchema>(
                    t.ProtocolTool.InputSchema.GetRawText())!,
            }))
            .ToList();

        // The user turn is deliberately trivial: what is being measured is the prefix the chat pays for on every
        // request, not a conversation.
        var withTools = await client.Messages.CountTokens(new MessageCountTokensParams
        {
            Model = LiveModel.Model,
            Messages = [new() { Role = Role.User, Content = "hello" }],
            Tools = tools,
        });

        var withoutTools = await client.Messages.CountTokens(new MessageCountTokensParams
        {
            Model = LiveModel.Model,
            Messages = [new() { Role = Role.User, Content = "hello" }],
        });

        var catalogue = withTools.InputTokens - withoutTools.InputTokens;

        output.WriteLine($"tools: {ToolCatalogueSpike.All.Count}");
        output.WriteLine($"catalogue tokens: {catalogue}");
        output.WriteLine($"baseline (no tools): {withoutTools.InputTokens}");
        output.WriteLine($"cache write @ Opus 5 (1.25x, $5/Mtok): ${catalogue * 1.25m * 5 / 1_000_000m:0.0000}");
        output.WriteLine($"cache read  @ Opus 5 (0.10x, $5/Mtok): ${catalogue * 0.10m * 5 / 1_000_000m:0.0000}");
        output.WriteLine($"cache write @ Sonnet 5 (1.25x, $2/Mtok): ${catalogue * 1.25m * 2 / 1_000_000m:0.0000}");
        output.WriteLine($"cache read  @ Sonnet 5 (0.10x, $2/Mtok): ${catalogue * 0.10m * 2 / 1_000_000m:0.0000}");

        // Not an assertion about the exact figure — that moves whenever a tool description is edited — but about
        // the order of magnitude the design assumes. If the catalogue ever crosses ~20k, tool search
        // (`defer_loading`) stops being a wash and starts being the answer; see the technical spec.
        Assert.InRange(catalogue, 1_000, 20_000);
    }
}
