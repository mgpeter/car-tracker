using System.ComponentModel;
using System.Reflection;
using CarTracker.ModelContextProtocol.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// The assistant's capability, in one place: the `[McpServerTool]` methods, and a way for each surface to wrap
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The single definition is the method.</b> It cannot be the wrapper, because the two SDKs' wrappers are
/// unrelated types — <see cref="McpServerTool"/> descends straight from <see cref="object"/> while
/// <see cref="AIFunction"/> sits under <c>AITool</c>, so neither can hold the other
/// (`CatalogueSeamTests` asserts this, so a future SDK that unifies them fails a test rather than going
/// unnoticed). `/mcp` builds `McpServerTool`s through the SDK's own registration; the chat builds
/// <see cref="AIFunction"/>s here; both start from the same `MethodInfo`, and a drift test compares the results
/// name-for-name and schema-for-schema.
/// </para>
/// <para>
/// <b>Order is by tool name, and it is load-bearing.</b> The catalogue is rendered at position 0 of every chat
/// request, ahead of the system prompt, so an unstable order changes the prefix bytes and silently disables
/// prompt caching — a 10× cost regression whose only symptom is the bill.
/// </para>
/// </remarks>
public static class CarTrackerToolCatalogue
{
    /// <summary>The four `[McpServerToolType]` classes, in the order `AddCarTrackerMcp` registers them.</summary>
    public static IReadOnlyList<Type> ToolTypes { get; } =
        [typeof(VehicleReadTools), typeof(SummaryReadTools), typeof(LogReadTools), typeof(WriteTools)];

    /// <summary>Every tool method, paired with the name the protocol knows it by, ordered by that name.</summary>
    public static IReadOnlyList<(string Name, MethodInfo Method)> Methods { get; } =
        [.. ToolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => (Attribute: m.GetCustomAttribute<McpServerToolAttribute>(), Method: m))
            .Where(x => x.Attribute is not null)
            .Select(x => (Name: x.Attribute!.Name ?? x.Method.Name, x.Method))
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

    /// <summary>
    /// The catalogue as <see cref="AIFunction"/>s, for a chat client — schemas built against
    /// <paramref name="services"/> so that a tool's dependencies stay dependencies.
    /// </summary>
    /// <param name="services">
    /// The container the tools' service parameters resolve from. <b>Pass the request's scoped provider</b>: the
    /// tools take a <c>DbContext</c> whose query filter reads the signed-in owner, and the root provider has no
    /// owner pinned on it.
    /// </param>
    /// <remarks>
    /// <b>The service provider is what keeps the catalogue small, and getting it wrong is silent.</b> A
    /// parameter the factory cannot resolve from DI becomes a *published argument* — so built without one, the
    /// five tools taking a <c>CarTrackerDbContext</c> advertised the DbContext's entire public surface to the
    /// model: ~19,000 characters each, and 66k tokens across the catalogue against 17k built correctly. Nothing
    /// errors. The tools simply become enormous and ask the model for a database.
    /// </remarks>
    public static IReadOnlyList<AIFunction> AIFunctions(IServiceProvider services) =>
        [.. Methods.Select(m => Create(m.Name, m.Method, services))];

    private static AIFunction Create(string name, MethodInfo method, IServiceProvider services) =>
        AIFunctionFactory.Create(method, target: null, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
            ConfigureParameterBinding = parameter => IsService(parameter, services)
                // Bound from the container and hidden from the model — the same treatment `/mcp` gives it.
                ? new AIFunctionFactoryOptions.ParameterBindingOptions
                {
                    ExcludeFromSchema = true,
                    BindParameter = (p, args) => args.Services?.GetService(p.ParameterType)
                        ?? throw new InvalidOperationException(
                            $"The tool '{name}' needs {p.ParameterType.Name} from the container, and the "
                            + "invocation supplied no services. Pass the request's scoped provider."),
                }
                : default,
        });

    /// <summary>
    /// A parameter is a dependency when the container can supply it. Everything else is something the model
    /// fills in.
    /// </summary>
    /// <remarks>
    /// Asked of a real container rather than inferred from the type's shape, because "is this a service?" is a
    /// question about registration, not about naming. <see cref="CancellationToken"/> is excluded explicitly:
    /// no container supplies it and the factory binds it itself, but it must never reach the schema either.
    /// </remarks>
    private static bool IsService(ParameterInfo parameter, IServiceProvider services) =>
        parameter.ParameterType != typeof(CancellationToken)
        && !parameter.ParameterType.IsPrimitive
        && parameter.ParameterType != typeof(string)
        && services.GetService(parameter.ParameterType) is not null;
}
