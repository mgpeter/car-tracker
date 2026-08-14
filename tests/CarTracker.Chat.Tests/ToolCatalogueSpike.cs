using System.Reflection;
using System.Text.Json;
using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.ModelContextProtocol.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The tool catalogue as the chat will send it: name, description and JSON schema per tool, taken from the same
/// `[McpServerTool]` methods `/mcp` serves.
/// </summary>
/// <remarks>
/// Built through the MCP SDK's own <see cref="McpServerTool.Create(MethodInfo, object?, McpServerToolCreateOptions?)"/>
/// rather than by hand-reading attributes, because the whole point of the design is that there is one derivation
/// of a tool's schema and not two. If this file ever needs its own reflection, that is the finding.
/// </remarks>
internal static class ToolCatalogueSpike
{
    private static readonly Type[] ToolTypes =
        [typeof(VehicleReadTools), typeof(SummaryReadTools), typeof(LogReadTools), typeof(WriteTools)];

    /// <summary>
    /// A container holding the same service *types* the tools take as parameters.
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing, and the most expensive thing to get wrong in this whole feature.</b> The SDK decides
    /// whether a parameter is a service (bound from DI, invisible to the model) or an argument (published in the
    /// tool's JSON schema) by asking a service provider. Built without one, the five tools that take a
    /// <see cref="CarTrackerDbContext"/> published the DbContext's entire public surface as a tool argument —
    /// ~19,000 characters each, and 66k tokens across the catalogue, against ~15k for the same tools built
    /// correctly. Nothing errors; the tools simply become enormous and ask the model for a database.
    /// </remarks>
    private static readonly IServiceProvider Services = BuildServices();

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddCarTrackerDomain();
        // Never connected to: only its *presence in the container* matters, because that is what marks the
        // parameter as a service rather than an argument.
        services.AddDbContext<CarTrackerDbContext>(o => o.UseNpgsql("Host=localhost;Database=none"));
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddSingleton(TimeProvider.System);
        return services.BuildServiceProvider();
    }

    /// <summary>Every tool, ordered by name — the order prompt caching needs to be stable.</summary>
    public static IReadOnlyList<McpServerTool> All { get; } =
        [.. ToolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => McpServerTool.Create(m, options: new McpServerToolCreateOptions { Services = Services }))
            .OrderBy(t => t.ProtocolTool.Name, StringComparer.Ordinal)];

    /// <summary>The catalogue as the JSON an API request would carry, for measurement.</summary>
    public static string AsRequestJson() =>
        JsonSerializer.Serialize(All.Select(t => new
        {
            name = t.ProtocolTool.Name,
            description = t.ProtocolTool.Description,
            input_schema = t.ProtocolTool.InputSchema,
        }));
}
