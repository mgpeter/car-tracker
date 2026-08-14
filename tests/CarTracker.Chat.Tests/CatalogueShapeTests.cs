using System.Text.Json;

namespace CarTracker.Chat.Tests;

/// <summary>
/// Where the catalogue's weight actually sits — offline, so it costs nothing to re-run after a change.
/// </summary>
/// <remarks>
/// The live measurement (<see cref="PrefixMeasurementTests"/>) says how many tokens the whole catalogue is. This
/// says which tools they belong to, which is the part you can act on.
/// </remarks>
public sealed class CatalogueShapeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Report_the_catalogue_shape()
    {
        var rows = TestCatalogue.McpTools
            .Select(t => new
            {
                t.ProtocolTool.Name,
                Description = t.ProtocolTool.Description?.Length ?? 0,
                Schema = t.ProtocolTool.InputSchema.GetRawText().Length,
            })
            .OrderByDescending(r => r.Description + r.Schema)
            .ToList();

        output.WriteLine($"{"tool",-28} {"desc",6} {"schema",8} {"total",8}");
        foreach (var r in rows)
        {
            output.WriteLine($"{r.Name,-28} {r.Description,6} {r.Schema,8} {r.Description + r.Schema,8}");
        }

        output.WriteLine("");
        output.WriteLine($"tools: {rows.Count}");
        output.WriteLine($"description chars: {rows.Sum(r => r.Description):N0}");
        output.WriteLine($"schema chars:      {rows.Sum(r => r.Schema):N0}");
        output.WriteLine($"total chars:       {rows.Sum(r => r.Description + r.Schema):N0}");

        Assert.NotEmpty(rows);
    }

    [Fact]
    public void Show_one_schema_in_full()
    {
        // The single biggest schema, pretty-printed. If the weight is structural — repeated `$defs`, verbose
        // enum wrappers, format strings — this is where it shows.
        var biggest = TestCatalogue.McpTools
            .OrderByDescending(t => t.ProtocolTool.InputSchema.GetRawText().Length)
            .First();

        output.WriteLine($"=== {biggest.ProtocolTool.Name} ===");
        output.WriteLine(JsonSerializer.Serialize(
            biggest.ProtocolTool.InputSchema,
            new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(true);
    }
}
