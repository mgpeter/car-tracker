using System.Text.Json;
using CarTracker.ModelContextProtocol;
using Microsoft.Extensions.AI;
using ModelContextProtocol;

namespace CarTracker.Chat;

/// <summary>
/// An <see cref="AIFunction"/> wrapped in the same fault policy `/mcp` runs, so one catalogue does not mean two
/// failure behaviours.
/// </summary>
/// <remarks>
/// <para>
/// <c>McpDatabaseFaultFilter</c> is wired onto the MCP <b>server pipeline</b>, so a chat invocation goes nowhere
/// near it. Without this, a locked table produces the model-facing equivalent of a shrug — while `/mcp`, calling
/// the identical method, explains that another session holds the lock and that retrying will not help.
/// </para>
/// <para>
/// The audit half of that pipeline is deliberately not reproduced: <c>AssistantWriteAudit</c> is keyed to an
/// assistant token and a chat write has none. The human who pressed Save is the record, and the row's
/// <c>EntrySource.Chat</c> is the attribution.
/// </para>
/// </remarks>
internal sealed class GuardedTool(AIFunction inner) : AIFunction
{
    public override string Name => inner.Name;

    public override string Description => inner.Description;

    public override JsonElement JsonSchema => inner.JsonSchema;

    public override JsonSerializerOptions JsonSerializerOptions => inner.JsonSerializerOptions;

    public override IReadOnlyDictionary<string, object?> AdditionalProperties => inner.AdditionalProperties;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ToolFaultPolicy.CallBudget);

        try
        {
            return await inner.InvokeAsync(arguments, budget.Token);
        }
        catch (Exception ex) when (ToolFaultPolicy.IsBudgetExpiry(ex, budget, cancellationToken))
        {
            // Returned, not thrown: a tool result the model can read and respond to beats an exception that
            // ends the turn. The loop's job is to keep talking to the owner even when the database will not.
            return ToolFaultPolicy.Explain(Name, sqlState: null, detail: null);
        }
        catch (McpException ex)
        {
            // A deliberate refusal, not a fault: the tools throw this to say "no vehicle matches that plate" or
            // "that category is mirrored". On `/mcp` the SDK turns it into a tool result the model reads and
            // answers; here it would be an *exception*, counted against MaximumConsecutiveErrorsPerRequest — so
            // two honest "no such vehicle" replies in one turn would end the conversation. Same words, same
            // outcome, both surfaces.
            return ex.Message;
        }
        catch (Exception ex) when (ToolFaultPolicy.FindPostgresFault(ex) is { } fault)
        {
            return ToolFaultPolicy.Explain(Name, fault.SqlState, fault.MessageText);
        }
        catch (Exception ex) when (ToolFaultPolicy.FindDataFault(ex) is { } refused)
        {
            // A value the schema will not take — too long, duplicate, out of range. Returned rather than thrown
            // for the same reason as above, and with more to gain: the model can shorten the value and try
            // again, which is what it attempts when told what was wrong and cannot when told "an error
            // occurred while saving the entity changes".
            return ToolFaultPolicy.ExplainData(Name, refused);
        }
    }
}
