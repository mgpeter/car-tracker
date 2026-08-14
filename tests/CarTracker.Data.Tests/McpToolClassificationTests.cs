using System.Reflection;
using CarTracker.ModelContextProtocol;
using CarTracker.ModelContextProtocol.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace CarTracker.Data.Tests;

/// <summary>
/// The tool catalogue and its read/write classification must agree — and the classification must agree with the
/// attribute that actually enforces it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="McpToolClassification.WriteToolNames"/> is a hand-kept list, and three things now read it: the
/// audit filter, the chat's approval-required marking, and the chat's confirm gate. A name that falls out of it
/// is a tool that writes a row with no audit entry and no draft card — which is a silent failure on both
/// surfaces. So the list is checked against the catalogue in both directions, and against
/// <c>[Authorize(Policy = "McpWrite")]</c>, which is what `/mcp` actually gates on.
/// </para>
/// <para>
/// No database, deliberately: this is reflection over attributes and a static set. It lives here because this is
/// where the MCP project is referenced, not because it needs Postgres.
/// </para>
/// </remarks>
public sealed class McpToolClassificationTests
{
    private const string WritePolicy = "McpWrite";

    /// <summary>The four `[McpServerToolType]` classes — the whole catalogue, as registered.</summary>
    private static readonly Type[] ToolTypes =
        [typeof(VehicleReadTools), typeof(SummaryReadTools), typeof(LogReadTools), typeof(WriteTools)];

    private static IEnumerable<(string Name, bool GatedAsWrite)> Catalogue() =>
        from type in ToolTypes
        from method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        let tool = method.GetCustomAttribute<McpServerToolAttribute>()
        where tool is not null
        select (
            tool.Name ?? method.Name,
            // The policy can sit on the method or on the type; `/mcp` honours either, so the test must too.
            method.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == WritePolicy)
                || type.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == WritePolicy));

    [Fact]
    public void Every_tool_carries_a_name()
    {
        // A tool whose name came from the method name rather than the attribute would break both surfaces'
        // classification silently, because the list is written in snake_case.
        Assert.All(Catalogue(), t => Assert.Matches("^[a-z][a-z0-9_]*$", t.Name));
    }

    [Fact]
    public void The_classification_matches_the_policy_that_enforces_it()
    {
        // The list is the declaration; the attribute is the enforcement. This is the assertion that keeps them
        // one fact: a tool gated by McpWrite and missing from the list would be audited nowhere and confirmed
        // never, and a tool in the list but ungated would suspend the chat for a write it does not perform.
        var mismatched = Catalogue()
            .Where(t => McpToolClassification.IsWrite(t.Name) != t.GatedAsWrite)
            .Select(t => $"{t.Name}: listed={McpToolClassification.IsWrite(t.Name)}, gated={t.GatedAsWrite}")
            .ToList();

        Assert.Empty(mismatched);
    }

    [Fact]
    public void No_name_in_the_write_list_is_missing_from_the_catalogue()
    {
        // The other direction: a renamed or removed tool leaves a dead name behind, and a dead name is how a
        // future rename silently stops being classified as a write.
        var names = Catalogue().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(McpToolClassification.WriteToolNames.Except(names));
    }

    [Fact]
    public void Every_tool_is_classified_exactly_once()
    {
        var catalogue = Catalogue().ToList();
        var writes = catalogue.Count(t => McpToolClassification.IsWrite(t.Name));

        // Reads are defined as "everything else", so this asserts the partition rather than a second list.
        Assert.Equal(catalogue.Count, writes + catalogue.Count(t => !McpToolClassification.IsWrite(t.Name)));

        // The figures CLAUDE.md and the MCP spec both quote. They are asserted so that adding a tool without
        // classifying it is a failing test rather than a discovery in production.
        Assert.Equal(49, catalogue.Count);
        Assert.Equal(30, writes);
    }
}
