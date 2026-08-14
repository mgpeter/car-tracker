using CarTracker.ModelContextProtocol;
using Microsoft.Extensions.AI;

namespace CarTracker.Chat;

/// <summary>
/// The catalogue as the chat sends it: guarded, and with every write tool marked as needing a human.
/// </summary>
internal static class ChatToolset
{
    /// <summary>
    /// Builds the tool list for one request.
    /// </summary>
    /// <param name="services">
    /// <b>The request's scoped provider.</b> The tools resolve a <c>DbContext</c> whose query filter reads the
    /// signed-in owner from <c>ICurrentUserAccessor</c>; handed the root provider they would run with no owner
    /// pinned, and the filter would match nothing — a chat that answers "you have no vehicles" to everyone.
    /// </param>
    public static IList<AITool> For(IServiceProvider services) =>
        [.. CarTrackerToolCatalogue.AIFunctions(services).Select(Wrap)];

    private static AITool Wrap(AIFunction function)
    {
        var guarded = new GuardedTool(function);

        // A write tool is not invoked by the loop at all: FunctionInvokingChatClient replaces the call with a
        // ToolApprovalRequestContent and returns, and the turn resumes when the owner confirms. The set comes
        // from McpToolClassification — the same list the audit filter uses — so a tool cannot be audited on one
        // surface and unconfirmed on the other.
        return McpToolClassification.IsWrite(function.Name)
            ? new ApprovalRequiredAIFunction(guarded)
            : guarded;
    }
}
