using System.Text.Json;
using CarTracker.ModelContextProtocol;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The two wrappings of one catalogue must describe the same tools. This is the test DEC-019 rests on.
/// </summary>
/// <remarks>
/// The MCP SDK and Microsoft.Extensions.AI wrap a method in unrelated types (see <see cref="CatalogueSeamTests"/>),
/// so "one catalogue" cannot be enforced by the type system — only by starting from one `MethodInfo` list and
/// checking the results agree. A tool that reached `/mcp` and not the chat would be missing from the assistant
/// with no error; one that reached the chat and not `/mcp` would be a capability with no audit filter behind it.
/// </remarks>
public sealed class CatalogueDriftTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Both_surfaces_expose_the_same_tools_in_the_same_order()
    {
        var mcp = TestCatalogue.McpTools.Select(t => t.ProtocolTool.Name).ToList();
        var chat = TestCatalogue.AIFunctions.Select(f => f.Name).ToList();

        Assert.Equal(mcp, chat);

        // Ordered by name, because the catalogue is rendered at position 0 of every chat request and an
        // unstable order changes the prefix bytes — which silently disables prompt caching.
        Assert.Equal(chat.OrderBy(n => n, StringComparer.Ordinal), chat);
    }

    [Fact]
    public void Both_surfaces_describe_the_tools_identically()
    {
        var mismatched = TestCatalogue.McpTools
            .Zip(TestCatalogue.AIFunctions, (m, f) => (Name: m.ProtocolTool.Name, Mcp: m.ProtocolTool.Description, Chat: f.Description))
            .Where(x => !string.Equals(x.Mcp, x.Chat, StringComparison.Ordinal))
            .ToList();

        foreach (var m in mismatched) output.WriteLine($"{m.Name}: mcp={m.Mcp?.Length ?? -1} chars, chat={m.Chat?.Length ?? -1} chars");

        // The description is what the model uses to decide when to call a tool. Two surfaces describing the same
        // capability differently is the drift that makes an assistant behave differently depending on where it
        // is plugged in.
        Assert.Empty(mismatched);
    }

    [Fact]
    public void Both_surfaces_ask_the_model_for_the_same_arguments()
    {
        var rows = TestCatalogue.McpTools
            .Zip(TestCatalogue.AIFunctions, (m, f) => new
            {
                Name = m.ProtocolTool.Name,
                Mcp = Shape(m.ProtocolTool.InputSchema),
                Chat = Shape(f.JsonSchema),
            })
            .ToList();

        var mismatched = rows.Where(r => r.Mcp != r.Chat).ToList();

        foreach (var r in mismatched)
        {
            output.WriteLine($"{r.Name}");
            output.WriteLine($"   mcp : {r.Mcp}");
            output.WriteLine($"   chat: {r.Chat}");
        }

        // Compared by *shape* — the property names and the required set — rather than by raw JSON. The two SDKs
        // are separate schema generators and will differ in incidental ways (title casing, how a nullable is
        // spelled); what must not differ is which arguments the model is asked for and which it must supply.
        // This is also the assertion that catches a service parameter leaking into one surface's schema and not
        // the other's, which is the failure that cost 4× in the prefix measurement.
        Assert.Empty(mismatched);
    }

    [Fact]
    public void No_tool_asks_the_model_for_a_dependency()
    {
        // The generalisation of that 4× failure: a schema property named like one of the tools' service types
        // means the container was not consulted. Cheap, blunt, and it fails loudly rather than costing money
        // quietly.
        string[] dependencies = ["context", "resolver", "factory", "scanner", "metrics", "services", "surface", "currentUser"];

        var offending = TestCatalogue.AIFunctions
            .SelectMany(f => Properties(f.JsonSchema).Select(p => (Tool: f.Name, Property: p)))
            .Where(x => dependencies.Contains(x.Property, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var o in offending) output.WriteLine($"{o.Tool} publishes '{o.Property}'");

        Assert.Empty(offending);
    }

    /// <summary>Property names and the required set — the part of a schema that is a contract with the model.</summary>
    private static string Shape(JsonElement schema)
    {
        var properties = Properties(schema).OrderBy(p => p, StringComparer.Ordinal);

        var required = schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array
            ? req.EnumerateArray().Select(r => r.GetString()!).OrderBy(r => r, StringComparer.Ordinal).ToList()
            : [];

        return $"props[{string.Join(",", properties)}] required[{string.Join(",", required)}]";
    }

    private static IEnumerable<string> Properties(JsonElement schema) =>
        schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
            ? props.EnumerateObject().Select(p => p.Name)
            : [];
}
