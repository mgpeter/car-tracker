using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.ModelContextProtocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace CarTracker.Chat.Tests;

/// <summary>
/// Both wrappings of the one catalogue, side by side: what `/mcp` serves and what the chat sends.
/// </summary>
/// <remarks>
/// Deliberately no reflection of its own. Every tool list in this repo comes from
/// <see cref="CarTrackerToolCatalogue"/>; a test project that re-derived one would be the third derivation of a
/// thing the design says has one.
/// </remarks>
internal static class TestCatalogue
{
    /// <summary>
    /// A container holding the same service *types* the tools take as parameters.
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing, and the most expensive thing to get wrong in this whole feature.</b> Both SDKs decide
    /// whether a parameter is a service (bound from DI, invisible to the model) or an argument (published in the
    /// tool's JSON schema) by asking a service provider. Built without one, the five tools that take a
    /// <see cref="CarTrackerDbContext"/> published the DbContext's entire public surface as a tool argument —
    /// ~19,000 characters each, and 66k tokens across the catalogue against ~17k built correctly. Nothing
    /// errors; the tools simply become enormous and ask the model for a database.
    /// </remarks>
    public static IServiceProvider Services { get; } = BuildServices();

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

    /// <summary>What `/mcp` advertises.</summary>
    public static IReadOnlyList<McpServerTool> McpTools { get; } =
        [.. CarTrackerToolCatalogue.Methods
            .Select(m => McpServerTool.Create(m.Method, options: new McpServerToolCreateOptions { Services = Services }))];

    /// <summary>What the chat sends.</summary>
    public static IReadOnlyList<AIFunction> AIFunctions { get; } = CarTrackerToolCatalogue.AIFunctions(Services);
}
