using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace CarTracker.Chat.Tests;

/// <summary>
/// Spike 0.3 — is there one tool object, or two derivations of one tool?
/// </summary>
/// <remarks>
/// <para>
/// DEC-019 says the chat consumes the MCP catalogue rather than reimplementing it. Whether that is a *type* or
/// a *discipline* turns on one fact: if the SDK's <see cref="McpServerTool"/> were itself an
/// <see cref="AIFunction"/>, `/mcp` and the chat could hold literally the same object and there would be
/// nothing to keep in step.
/// </para>
/// <para>
/// <b>It is not — and the two are not even related.</b> <see cref="McpServerTool"/> descends straight from
/// <see cref="object"/>, while <see cref="AIFunction"/> sits under <c>AIFunctionDeclaration</c> under
/// <see cref="AITool"/>. The compiler says so outright (<c>CS8121: an expression of type 'McpServerTool' cannot
/// be handled by a pattern of type 'AIFunction'</c>), which is why the obvious `is AIFunction` check is not in
/// this file. The consequence is the whole reason `CarTrackerToolCatalogue` exists: the single definition has
/// to be the *method*, each surface builds its own wrapper from it, and the drift test is load-bearing rather
/// than belt-and-braces.
/// </para>
/// </remarks>
public sealed class CatalogueSeamTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void McpServerTool_and_AIFunction_are_siblings_so_neither_can_wrap_the_other()
    {
        var mcpChain = Chain(typeof(McpServerTool)).ToList();
        var fnChain = Chain(typeof(AIFunction)).ToList();

        output.WriteLine($"McpServerTool : {string.Join(" -> ", mcpChain)}");
        output.WriteLine($"AIFunction    : {string.Join(" -> ", fnChain)}");

        // Neither is assignable to the other. If a future SDK version changes that, this test fails and the
        // catalogue collapses to one object — which would be a welcome simplification, and is why the assertion
        // is stated this way round rather than as a comment.
        Assert.False(typeof(AIFunction).IsAssignableFrom(typeof(McpServerTool)));
        Assert.False(typeof(McpServerTool).IsAssignableFrom(typeof(AIFunction)));

        // Not even a shared root: McpServerTool descends straight from System.Object, while AIFunction sits
        // under AIFunctionDeclaration under AITool. The two SDKs meet at the *method* and nowhere else, which
        // is a stronger statement than "siblings" and makes the drift test the only thing holding them
        // together.
        Assert.Equal(["ModelContextProtocol.Server.McpServerTool", "System.Object"], mcpChain);
        Assert.Contains("Microsoft.Extensions.AI.AITool", fnChain);

        static IEnumerable<string> Chain(Type? t)
        {
            for (; t is not null; t = t.BaseType) yield return t.FullName!;
        }
    }
}
