using CarTracker.ModelContextProtocol.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// Wires the in-process MCP server into the host (DEC-004, DEC-014). The WebApi calls both halves; keeping them
/// here means the MCP surface is described in one place and <c>Program.cs</c> stays two lines.
/// </summary>
public static class McpServerRegistration
{
    /// <summary>
    /// Registers the MCP server over Streamable HTTP and discovers the tool types. Call after
    /// <c>AddCarTrackerDomain()</c> — the tools resolve <see cref="Domain.IDerivedMetricsService"/> and the
    /// query/write services from the same container the web API uses.
    /// </summary>
    public static IServiceCollection AddCarTrackerMcp(this IServiceCollection services)
    {
        services
            .AddMcpServer()
            // Stateless: no server-to-client requests (sampling/elicitation), so no session affinity is needed
            // — which is what lets the endpoint sit behind the gateway like every other route.
            .WithHttpTransport(options => options.Stateless = true)
            // Enables [Authorize]/[AllowAnonymous] on tools — the write tools carry [Authorize(Policy="McpWrite")],
            // so a read-only token reaches the read tools but is refused by every write tool.
            .AddAuthorizationFilters()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpAuditFilter.Filter))
            .WithTools<VehicleReadTools>()
            .WithTools<SummaryReadTools>()
            .WithTools<LogReadTools>()
            .WithTools<WriteTools>();

        return services;
    }

    /// <summary>
    /// Maps the Streamable HTTP endpoint at <c>/mcp</c>, gated by the <c>McpRead</c> scope so an unauthenticated
    /// or non-assistant caller is refused. The write tools add the <c>McpWrite</c> gate on top.
    /// </summary>
    public static IEndpointConventionBuilder MapCarTrackerMcp(this IEndpointRouteBuilder endpoints)
        => endpoints.MapMcp("/mcp").RequireAuthorization("McpRead");
}
