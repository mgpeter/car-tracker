using System.Text.Json;

namespace CarTracker.Chat.Tests;

/// <summary>
/// <c>offer_to_file</c> asks the model for the proposal and nothing else: its two services and the origin
/// setting come from the container.
/// </summary>
/// <remarks>
/// The tool takes <c>DocumentService</c> and <c>AppOptions</c> as parameters. Either one missing from the
/// container would turn into a published argument, which is the silent failure the drift tests exist for.
/// </remarks>
public sealed class FilingToolSchemaTests
{
    private static string[] Names(JsonElement schema, string property) =>
        schema.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.Object
                ? [.. value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)]
                : [.. value.EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal)]
            : [];

    [Fact]
    public void The_chat_schema_asks_for_the_proposal_only()
    {
        var schema = TestCatalogue.AIFunctions.Single(f => f.Name == "offer_to_file").JsonSchema;

        Assert.Equal(
            ["attachment", "documentDate", "expenseEntryId", "issueId", "serviceRecordId", "title", "type", "vehicle"],
            Names(schema, "properties"));
        Assert.Equal(["title", "type"], Names(schema, "required"));
    }

    [Fact]
    public void The_mcp_schema_asks_for_the_same()
    {
        var tool = TestCatalogue.McpTools.Single(t => t.ProtocolTool.Name == "offer_to_file");

        Assert.Equal(
            ["attachment", "documentDate", "expenseEntryId", "issueId", "serviceRecordId", "title", "type", "vehicle"],
            Names(tool.ProtocolTool.InputSchema, "properties"));
        Assert.Equal(["title", "type"], Names(tool.ProtocolTool.InputSchema, "required"));
    }
}
