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
/// <remarks>
/// The list of write tools used to live here as a private field. It moved to <see cref="McpToolClassification"/>
/// when the in-app chat needed the same answer for a different reason (which calls suspend for confirmation),
/// because two copies of it would have let a tool be audited on one surface and unconfirmed on the other.
/// </remarks>
internal static class McpAuditFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
        next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);

            try
            {
                var tool = context.Params?.Name;
                if (tool is not null && McpToolClassification.IsWrite(tool) && result is { IsError: not true })
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
