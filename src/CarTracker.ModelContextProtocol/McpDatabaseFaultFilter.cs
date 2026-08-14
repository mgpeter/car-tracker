using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// Applies <see cref="ToolFaultPolicy"/> to `/mcp`: turns the two database faults an assistant can actually hit
/// into sentences it can act on, instead of the SDK's opaque "An error occurred".
///
/// Both arrive as a cancelled statement, and until the timeouts went on the connection (see the note in the
/// WebApi's <c>Program.cs</c>) neither surfaced at all — the request simply hung until the MCP client gave up.
/// That is a genuinely confusing failure, because it is *per table*: one session holding a lock on
/// <c>tyre_readings</c> makes <c>list_tyre_readings</c> and <c>log_tyre_reading</c> time out while
/// <c>get_reference</c> answers instantly, which reads like the two tyre tools are broken rather than like the
/// database is blocked. Saying so plainly is the whole point.
///
/// The policy itself lives next door because the in-app chat invokes the same tools without going through this
/// pipeline, and one catalogue with two failure behaviours is not one catalogue.
/// </summary>
internal static class McpDatabaseFaultFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
        next => async (context, cancellationToken) =>
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(ToolFaultPolicy.CallBudget);

            var tool = context.Params?.Name ?? "the tool";
            try
            {
                return await next(context, budget.Token);
            }
            catch (Exception ex) when (ToolFaultPolicy.IsBudgetExpiry(ex, budget, cancellationToken))
            {
                throw new McpException(ToolFaultPolicy.Explain(tool, sqlState: null, detail: null));
            }
            catch (Exception ex) when (ToolFaultPolicy.FindPostgresFault(ex) is { } fault)
            {
                throw new McpException(ToolFaultPolicy.Explain(tool, fault.SqlState, fault.MessageText));
            }
        };
}
