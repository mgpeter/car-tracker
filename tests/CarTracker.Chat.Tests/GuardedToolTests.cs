using Microsoft.Extensions.AI;
using ModelContextProtocol;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The chat runs the same tools `/mcp` does, so it has to fail the same way they do.
/// </summary>
/// <remarks>
/// The MCP fault filter and the audit filter are wired onto the <b>server</b> pipeline, which a chat invocation
/// never touches. <see cref="GuardedTool"/> is where that gap is closed; these are the behaviours it closes it
/// with, and the reason the tools are wrapped rather than called directly.
/// </remarks>
public sealed class GuardedToolTests
{
    private static AIFunction Guard(Func<string> body) =>
        new GuardedTool(AIFunctionFactory.Create(body, "probe", "A tool that does what the test needs."));

    [Fact]
    public async Task A_refusal_comes_back_as_something_the_model_can_read()
    {
        // McpException is how the tools say "no vehicle matches that plate" — a deliberate refusal, not a fault.
        // The MCP SDK turns it into a tool result the model answers; unwrapped here it would be an exception,
        // counted against MaximumConsecutiveErrorsPerRequest, so two honest "no such vehicle" replies in one turn
        // would end the conversation instead of correcting it.
        var tool = Guard(() => throw new McpException("No vehicle matches 'XY99 ZZZ'. Call list_vehicles."));

        var answer = await tool.InvokeAsync(new AIFunctionArguments());

        Assert.Contains("No vehicle matches", answer?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task A_tool_that_works_is_passed_straight_through()
    {
        var tool = Guard(() => "80,712 miles");

        var answer = await tool.InvokeAsync(new AIFunctionArguments());

        Assert.Contains("80,712", answer?.ToString() ?? string.Empty);
    }

    [Fact]
    public void The_wrapper_carries_the_tools_own_identity()
    {
        // Name and schema come from the inner function, because the catalogue is the definition. A wrapper that
        // renamed or re-shaped a tool would be a second definition of it — the thing this design exists to stop.
        var inner = AIFunctionFactory.Create(() => "x", "probe", "A tool that does what the test needs.");
        var guarded = new GuardedTool(inner);

        Assert.Equal(inner.Name, guarded.Name);
        Assert.Equal(inner.Description, guarded.Description);
        Assert.Equal(inner.JsonSchema.ToString(), guarded.JsonSchema.ToString());
    }
}
