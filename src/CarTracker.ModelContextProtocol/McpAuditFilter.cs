using System.Text.Json;
using CarTracker.Domain.Writes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// A call-tool filter that records every successful write-tool call to the audit trail (README §5.1), keyed to
/// the token that made it. Reads are not listed here — they are counted on the token by the auth handler. One
/// filter, so no write tool carries audit plumbing.
/// </summary>
internal static class McpAuditFilter
{
    /// <summary>The write tools (those under the McpWrite scope). Reads are everything else.</summary>
    private static readonly HashSet<string> WriteToolNames =
    [
        "log_fuel_fillup", "add_service", "add_vehicle", "add_task", "complete_task",
        "log_expense", "update_mileage", "mark_check_done", "log_wash", "log_tyre_reading",
        "add_equipment", "add_issue", "add_issue_observation",
        "set_insurance", "set_road_tax", "update_vehicle_profile",
    ];

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
        next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);

            try
            {
                var tool = context.Params?.Name;
                if (tool is not null && WriteToolNames.Contains(tool) && result is { IsError: not true })
                {
                    if (context.Services?.GetService<IAssistantAudit>() is { } audit)
                    {
                        var summary = context.Params?.Arguments is { Count: > 0 } args
                            ? JsonSerializer.Serialize(args)
                            : tool;
                        await audit.RecordWriteAsync(tool, vehicleId: null, summary, cancellationToken);
                    }
                }
            }
            catch
            {
                // Audit must never break a tool call — a write that succeeded is not undone by a failed log.
            }

            return result;
        };
}
