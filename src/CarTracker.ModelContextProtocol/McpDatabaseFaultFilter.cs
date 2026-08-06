using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Npgsql;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// Turns the two database faults an assistant can actually hit into sentences it can act on, instead of the SDK's
/// opaque "An error occurred".
///
/// Both arrive as a cancelled statement, and until the timeouts went on the connection (see the note in the
/// WebApi's <c>Program.cs</c>) neither surfaced at all — the request simply hung until the MCP client gave up.
/// That is a genuinely confusing failure, because it is *per table*: one session holding a lock on
/// <c>tyre_readings</c> makes <c>list_tyre_readings</c> and <c>log_tyre_reading</c> time out while
/// <c>get_reference</c> answers instantly, which reads like the two tyre tools are broken rather than like the
/// database is blocked. Saying so plainly is the whole point of this filter.
/// </summary>
internal static class McpDatabaseFaultFilter
{
    /// <summary>Postgres raises this when <c>lock_timeout</c> expires: another session holds the table lock.</summary>
    private const string LockNotAvailable = "55P03";

    /// <summary>…and this when <c>statement_timeout</c> expires, or the command timeout cancels the query.</summary>
    private const string QueryCanceled = "57014";

    /// <summary>
    /// The ceiling on a whole tool call, and the belt to the connection timeouts' braces.
    ///
    /// <c>lock_timeout</c> alone is not enough, because Aspire's <c>EnrichNpgsqlDbContext</c> installs a retrying
    /// execution strategy and Npgsql classifies a lock timeout (55P03) as *transient* — so EF dutifully retries it
    /// with backoff, turning a 5s database-level failure into a ~90s one. That is worse than the original hang: it
    /// still outlives the MCP client's patience, and now burns the database doing it. Bounding the call here cuts
    /// the retry loop whatever the underlying fault, so the assistant always gets an answer rather than a timeout.
    ///
    /// Well under a typical 60s client timeout, and far above the ~0.5s these tools actually take.
    /// </summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
        next => async (context, cancellationToken) =>
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(CallBudget);

            var tool = context.Params?.Name ?? "the tool";
            try
            {
                return await next(context, budget.Token);
            }
            // Budget expiry is tested FIRST, and deliberately. Cancelling the call makes Npgsql cancel the
            // in-flight statement, so the server answers 57014 "canceling statement due to user request" — i.e.
            // the fault we ourselves caused masks the one we were waiting on. Reporting that verbatim would tell
            // the reader their query was slow, when what actually happened is that it never got to start.
            // A genuine client cancellation is excluded, and must propagate untouched.
            catch (Exception ex) when (IsBudgetExpiry(ex, budget, cancellationToken))
            {
                throw new McpException(Explain(tool, sqlState: null, detail: null));
            }
            catch (Exception ex) when (FindPostgresFault(ex) is { } fault)
            {
                throw new McpException(Explain(tool, fault.SqlState, fault.MessageText));
            }
        };

    private static bool IsBudgetExpiry(Exception ex, CancellationTokenSource budget, CancellationToken client)
    {
        if (!budget.IsCancellationRequested || client.IsCancellationRequested) return false;

        // The cancellation surfaces either as OperationCanceledException or, once Npgsql has round-tripped the
        // cancel request to the server, as a PostgresException carrying 57014.
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is OperationCanceledException) return true;
            if (e is PostgresException { SqlState: QueryCanceled }) return true;
        }

        return false;
    }

    private static string Explain(string tool, string? sqlState, string? detail) => sqlState switch
    {
        LockNotAvailable =>
            $"'{tool}' could not run: another database session is holding a lock on the table it needs, so the "
            + "read/write could not start. This is a database-level block, not a problem with the arguments or the "
            + "data — tools that touch other tables will keep working normally, which is why only some tools fail. "
            + "It usually means an interrupted migration or a SQL client left open mid-transaction. Retrying will "
            + "not help until that session ends.",

        QueryCanceled =>
            $"'{tool}' was stopped after running too long ({detail}). If this repeats, the database is overloaded "
            + "or blocked by another session.",

        _ =>
            $"'{tool}' was stopped after {CallBudget.TotalSeconds:N0} seconds without answering — most likely the "
            + "database is blocked by another session holding a lock on the table it needs. Tools that touch other "
            + "tables should still work. Retrying is unlikely to help until that session ends.",
    };

    /// <summary>
    /// Walks the exception chain: EF wraps provider exceptions, and the retrying execution strategy wraps again
    /// once it gives up, so the <see cref="PostgresException"/> is rarely the outermost one.
    /// </summary>
    private static PostgresException? FindPostgresFault(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException { SqlState: LockNotAvailable or QueryCanceled } postgres)
                return postgres;
        }

        return null;
    }
}
